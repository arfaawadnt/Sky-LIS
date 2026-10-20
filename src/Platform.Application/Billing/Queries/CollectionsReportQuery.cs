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
/// The collections report printed from the Payments register (change request 2026-10-13, item 2; intake §14,
/// §16 Q20–Q22). The period is whole laboratory-calendar days (default: today) converted with the laboratory's zone
/// (<see cref="ILaboratoryTimeZone"/>); <paramref name="Scope"/> <c>User</c> = the current user's own posted payments and
/// refunds (every branch), <c>Branch</c> = every cashier of the session branch (TENANT-004 without a branch, BIL-044 without
/// the CP-37 right); a period starting before the member's earliest allowed day (<c>ReportDaysLimit</c>) is refused with
/// BIL-045. <paramref name="GroupBy"/> Branch | User | Method (default Method).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record CollectionsReportQuery(DateOnly? FromDay, DateOnly? ToDay, string? Scope, string? GroupBy) : IQuery<CollectionsReportDocument>;

public sealed class CollectionsReportValidator : AbstractValidator<CollectionsReportQuery>
{
    public CollectionsReportValidator()
    {
        RuleFor(query => query.Scope).IsEnumName(typeof(CollectionsReportScope), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Scope));
        RuleFor(query => query.GroupBy).IsEnumName(typeof(CollectionsReportGrouping), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.GroupBy));
        RuleFor(query => query).Must(query => query.FromDay is not { } from || query.ToDay is not { } to || from <= to)
            .OverridePropertyName("toDay").WithMessage("The end of the period must not precede its start.");
    }
}

public sealed class CollectionsReportHandler(
    ICollectionsReportQueries collections,
    ICashDrawerQueries drawer,
    IBillingDirectory directory,
    ILaboratoryTimeZone laboratoryZone,
    ITenantSettings settings,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : IQueryHandler<CollectionsReportQuery, CollectionsReportDocument>
{
    public async Task<CollectionsReportDocument> Handle(CollectionsReportQuery query, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        var scope = Parse(query.Scope, CollectionsReportScope.User);
        var grouping = Parse(query.GroupBy, CollectionsReportGrouping.Method);

        Guid? branch = scope == CollectionsReportScope.Branch ? BranchScope.Require(user) : user.BranchId;
        if (scope == CollectionsReportScope.Branch)
        {
            BillingRights.RequireBranchCollections(privileges);
        }

        var zone = await laboratoryZone.FindAsync(cancellationToken);
        var now = clock.UtcNow;
        var today = CollectionsReport.LocalDay(now, zone);
        var fromDay = query.FromDay ?? today;
        var toDay = query.ToDay ?? today;
        CollectionsReport.RequireWithinLimit(fromDay, privileges.SpecialRights.EarliestReportDay(today));
        var (fromUtc, toUtc) = CollectionsReport.Period(fromDay, toDay, zone);

        var cells = await collections.CellsAsync(
            new CollectionsScope(scope == CollectionsReportScope.User ? actor : null, scope == CollectionsReportScope.Branch ? branch : null, fromUtc, toUtc),
            cancellationToken);
        var (groups, totals) = CollectionsReport.Group(cells, grouping);
        var rows = await NameAsync(groups, grouping, cancellationToken);

        var letterhead = await directory.LetterheadAsync(branch ?? Guid.Empty, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);
        var cashierName = scope == CollectionsReportScope.User ? user.DisplayName ?? await CashierNames.OfAsync(drawer, actor, cancellationToken) : null;

        return new CollectionsReportDocument(
            letterhead,
            scope.ToString(),
            grouping.ToString(),
            fromDay,
            toDay,
            fromUtc,
            toUtc,
            zone?.Id ?? "UTC",
            cashierName,
            rows,
            Row("Total", null, null, totals),
            currency,
            user.DisplayName ?? string.Empty,
            now);
    }

    private async Task<IReadOnlyList<CollectionsReportRow>> NameAsync(IReadOnlyList<CollectionsGroup> groups, CollectionsReportGrouping grouping, CancellationToken cancellationToken)
    {
        switch (grouping)
        {
            case CollectionsReportGrouping.Method:
                return [.. groups.Select(group => Row(group.Key, null, null, group.Figures)).OrderBy(row => Enum.Parse<PaymentMethod>(row.Key))];

            case CollectionsReportGrouping.User:
            {
                var names = await drawer.UserNamesAsync([.. groups.Select(group => Guid.Parse(group.Key))], cancellationToken).ConfigureAwait(false);
                return [.. groups
                    .Select(group => { var name = names.TryGetValue(Guid.Parse(group.Key), out var found) ? found : null; return Row(group.Key, name, name, group.Figures); })
                    .OrderBy(row => row.LabelEn ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Key, StringComparer.Ordinal)];
            }

            default:
            {
                var names = await directory.BranchesAsync([.. groups.Select(group => Guid.Parse(group.Key))], cancellationToken).ConfigureAwait(false);
                return [.. groups
                    .Select(group => { names.TryGetValue(Guid.Parse(group.Key), out var found); return Row(group.Key, found?.NameEn, found?.NameAr, group.Figures); })
                    .OrderBy(row => row.LabelEn ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Key, StringComparer.Ordinal)];
            }
        }
    }

    private static CollectionsReportRow Row(string key, string? labelEn, string? labelAr, CollectionsFigures figures) => new(
        key,
        labelEn,
        labelAr,
        figures.PaymentCount,
        figures.RefundCount,
        View(figures.Cash),
        View(figures.Card),
        View(figures.Pos),
        View(figures.Transfer),
        figures.TotalPayments,
        figures.TotalRefunds,
        figures.Net);

    private static CollectionsMethodView View(MethodFigures figures) => new(figures.Payments, figures.Refunds, figures.Net);

    private static T Parse<T>(string? value, T fallback)
        where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? fallback : Enum.Parse<T>(value.Trim(), ignoreCase: true);
}
