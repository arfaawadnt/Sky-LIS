using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Patients;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Queries;

/// <summary>
/// The cash ledger (SRS CP-70, change request LIS-CR-011 point C-15; intake §18) and its printed twin, the A4 "Daily cash
/// sheet". The branch is the session branch (TENANT-004); <paramref name="Scope"/> <c>User</c> (default) = the current user's own
/// drawer, <c>Branch</c> = one cashier of the branch (<paramref name="CashierUserId"/>) or every cashier (null), which needs the CP-37
/// right "view branch collections" (BIL-044). Whole laboratory days (default today, one day when only the start is given; BIL-030
/// reversed, BIL-033 longer than 31 days),
/// never before the member's earliest report day (BIL-045); at most <see cref="CashLedger.MaxLines"/> lines (BIL-064).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record CashLedgerQuery(DateOnly? FromDay, DateOnly? ToDay, string? Scope, Guid? CashierUserId) : IQuery<CashLedgerView>;

public sealed class CashLedgerValidator : AbstractValidator<CashLedgerQuery>
{
    public CashLedgerValidator()
    {
        RuleFor(query => query.Scope).IsEnumName(typeof(CollectionsReportScope), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Scope));
        RuleFor(query => query.CashierUserId).NotEqual(Guid.Empty).When(query => query.CashierUserId is not null);
        RuleFor(query => query).Must(query => query.FromDay is not { } from || query.ToDay is not { } to || from <= to)
            .OverridePropertyName("toDay").WithMessage("The end of the period must not precede its start.");
    }
}

public sealed class CashLedgerHandler(
    ICashLedgerReader ledger,
    ICashDrawerQueries drawer,
    IBillableVisitLookup visits,
    IBillingDirectory directory,
    ILaboratoryTimeZone laboratoryZone,
    ITenantSettings settings,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : IQueryHandler<CashLedgerQuery, CashLedgerView>
{
    public async Task<CashLedgerView> Handle(CashLedgerQuery query, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        var branch = BranchScope.Require(user);
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? CollectionsReportScope.User : Enum.Parse<CollectionsReportScope>(query.Scope.Trim(), ignoreCase: true);
        if (scope == CollectionsReportScope.Branch)
        {
            BillingRights.RequireBranchCollections(privileges);
        }

        Guid? cashier = scope == CollectionsReportScope.User ? actor : query.CashierUserId;

        var zone = await laboratoryZone.FindAsync(cancellationToken);
        var now = clock.UtcNow;
        var today = CollectionsReport.LocalDay(now, zone);
        var fromDay = query.FromDay ?? today;
        var toDay = query.ToDay ?? fromDay;   // one day when only the start is given
        CollectionsReport.RequireWithinLimit(fromDay, privileges.SpecialRights.EarliestReportDay(today));
        var (fromUtc, toUtc) = CollectionsReport.Period(fromDay, toDay, zone);
        CashLedger.RequirePeriodLength(fromDay, toDay);

        var lines = await ledger.LinesAsync(new CashLedgerScope(branch, cashier, fromUtc, toUtc), CashLedger.MaxLines + 1, cancellationToken);
        CashLedger.RequireWithinLimit(lines.Count);
        var opening = await ledger.OpeningAsync(branch, cashier, fromUtc, cancellationToken);
        var (rows, summary) = CashLedger.Build(opening, lines);
        var window = CashLedger.CloseWindow(fromUtc, toUtc, opening, lines, now);

        var accessions = await visits.GetManyAsync([.. lines.Where(line => line.VisitId is not null).Select(line => line.VisitId!.Value).Distinct()], cancellationToken);
        var nameIds = lines.Select(line => line.CashierUserId).Append(actor);
        if (cashier is { } chosen)
        {
            nameIds = nameIds.Append(chosen);
        }

        var names = await drawer.UserNamesAsync([.. nameIds.Distinct()], cancellationToken);
        var cashiers = scope == CollectionsReportScope.Branch ? await drawer.CashiersAsync(branch, fromUtc, toUtc, cancellationToken) : [];
        var letterhead = await directory.LetterheadAsync(branch, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new CashLedgerView(
            letterhead,
            branch,
            scope.ToString(),
            cashier,
            cashier is { } id && names.TryGetValue(id, out var cashierName) ? cashierName : null,
            fromDay,
            toDay,
            fromUtc,
            toUtc,
            zone?.Id ?? "UTC",
            View(summary, opening),
            [.. rows.Select(row => View(row, accessions, names))],
            window is { } close ? new CashLedgerCloseWindow(close.FromUtc, close.ToUtc) : null,
            cashiers,
            currency,
            user.DisplayName ?? (names.TryGetValue(actor, out var printedBy) ? printedBy : string.Empty),
            now);
    }

    private static CashLedgerSummaryView View(CashLedgerSummary summary, CashLedgerOpening opening) => new(
        summary.OpeningBalance,
        opening.ClosingNumber,
        opening.ClosedThroughUtc,
        View(summary.Receipts),
        summary.ReceiptCount,
        View(summary.Refunds),
        summary.RefundCount,
        summary.VoidedCount,
        summary.VoidedAmount,
        summary.CashIn,
        summary.CashOut,
        summary.EntryCount,
        summary.CountDifferences,
        summary.ClosingCount,
        summary.ClosingBalance,
        summary.NetCash);

    private static CashLedgerMethodAmounts View(MethodAmounts amounts) => new(amounts.Cash, amounts.Card, amounts.Pos, amounts.Transfer, amounts.Total);

    private static CashLedgerRowView View(CashLedgerRow row, IReadOnlyDictionary<Guid, BillableVisit> accessions, IReadOnlyDictionary<Guid, string> names)
    {
        var line = row.Line;
        var accession = line.VisitId is { } visitId && accessions.TryGetValue(visitId, out var visit) ? visit.Accession : null;
        return new CashLedgerRowView(
            line.Kind.ToString(),
            line.Id,
            line.Number,
            line.AtUtc,
            line.VisitId,
            accession,
            line.Method?.ToString(),
            line.MethodCode,
            line.Category,
            line.Text,
            line.Amount,
            line.IsVoided ? "Voided" : "Posted",
            row.MoneyIn,
            row.MoneyOut,
            row.Balance,
            line.CashierUserId,
            names.TryGetValue(line.CashierUserId, out var name) ? name : null,
            line.Expected,
            line.Counted,
            line.Difference);
    }
}
