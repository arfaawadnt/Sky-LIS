using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Queries;

// Cash closing (CP-30, BR-16): the preview of a drawer, the cashiers of the session branch, the closings register, its
// export and the closing sheet. The drawer is the session branch + a cashier (or all) since LIS-CR-004 (2026-10-20).

/// <summary>The figures of the session branch's drawer before it is closed (same scope rules as the close: TENANT-004, BIL-030/031/033).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record PreviewCashClosingQuery(Guid? CashierUserId, DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<CashClosingPreview>;

public sealed class PreviewCashClosingValidator : AbstractValidator<PreviewCashClosingQuery>
{
    public PreviewCashClosingValidator()
    {
        RuleFor(query => query.CashierUserId).NotEqual(Guid.Empty).When(query => query.CashierUserId is not null);
        RuleFor(query => query.FromUtc).NotEmpty();
        RuleFor(query => query.ToUtc).NotEmpty();
    }
}

public sealed class PreviewCashClosingHandler(
    ICashDrawerQueries drawer,
    ICashClosingRepository closings,
    IBillableVisitLookup visits,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock) : IQueryHandler<PreviewCashClosingQuery, CashClosingPreview>
{
    public async Task<CashClosingPreview> Handle(PreviewCashClosingQuery query, CancellationToken cancellationToken)
    {
        var branch = BranchScope.Require(user);
        CashClosing.RequireScope(branch, query.FromUtc, query.ToUtc, clock.UtcNow);

        var scope = new DrawerScope(branch, query.CashierUserId, query.FromUtc, query.ToUtc);
        var totals = await drawer.TotalsAsync(scope, cancellationToken);
        var movements = await drawer.MovementsAsync(scope, BillingLimits.MaxDrawerMovements + 1, cancellationToken);
        var truncated = movements.Count > BillingLimits.MaxDrawerMovements;
        var shown = await BillingAccessions.FillAsync(visits, truncated ? movements.Take(BillingLimits.MaxDrawerMovements).ToList() : movements, cancellationToken);
        var overlaps = await closings.OverlapsAsync(branch, query.CashierUserId, query.FromUtc, query.ToUtc, cancellationToken);
        var cashierName = await CashierNames.OfAsync(drawer, query.CashierUserId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new CashClosingPreview(
            branch,
            query.CashierUserId,
            cashierName,
            query.FromUtc,
            query.ToUtc,
            BillingViews.Totals(totals),
            shown,
            truncated,
            overlaps,
            currency);
    }
}

/// <summary>The cashiers who moved money in the session branch in a period (the closing page's cashier filter).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record ListDrawerCashiersQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<IReadOnlyList<DrawerCashier>>;

public sealed class ListDrawerCashiersValidator : AbstractValidator<ListDrawerCashiersQuery>
{
    public ListDrawerCashiersValidator()
    {
        RuleFor(query => query).Must(query => query.FromUtc < query.ToUtc && query.ToUtc - query.FromUtc <= TimeSpan.FromDays(CashClosing.MaxPeriodDays))
            .OverridePropertyName("toUtc").WithMessage("The period must start before it ends and cover at most 31 days.");
    }
}

public sealed class ListDrawerCashiersHandler(ICashDrawerQueries drawer, ICurrentUser user) : IQueryHandler<ListDrawerCashiersQuery, IReadOnlyList<DrawerCashier>>
{
    public Task<IReadOnlyList<DrawerCashier>> Handle(ListDrawerCashiersQuery query, CancellationToken cancellationToken) =>
        drawer.CashiersAsync(BranchScope.Require(user), query.FromUtc, query.ToUtc, cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record ListCashClosingsQuery(Guid? BranchId, Guid? CashierUserId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int? Page, int? PageSize) : IQuery<Paged<CashClosingListItem>>;

public sealed class ListCashClosingsValidator : AbstractValidator<ListCashClosingsQuery>
{
    public ListCashClosingsValidator()
    {
        RuleFor(query => query).Must(query => query.FromUtc is not { } start || query.ToUtc is not { } end || start < end)
            .OverridePropertyName("toUtc").WithMessage("The end of the period must follow its start.");
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListCashClosingsHandler(ICashClosingRepository closings) : IQueryHandler<ListCashClosingsQuery, Paged<CashClosingListItem>>
{
    public Task<Paged<CashClosingListItem>> Handle(ListCashClosingsQuery query, CancellationToken cancellationToken) =>
        closings.ListAsync(new CashClosingFilter(query.BranchId, query.CashierUserId, query.FromUtc, query.ToUtc), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Export)]
public sealed record ExportCashClosingsQuery(Guid? BranchId, Guid? CashierUserId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string? TimeZone = null) : IQuery<BillingExportFile>;

public sealed class ExportCashClosingsValidator : AbstractValidator<ExportCashClosingsQuery>
{
    public ExportCashClosingsValidator()
    {
        RuleFor(query => query).Must(query => query.FromUtc is not { } start || query.ToUtc is not { } end || start < end)
            .OverridePropertyName("toUtc").WithMessage("The end of the period must follow its start.");
        RuleFor(query => query.TimeZone).Must(BillingCsv.IsKnownZone).WithMessage("The time zone must be an IANA time zone name.");
    }
}

public sealed class ExportCashClosingsHandler(ICashClosingRepository closings, IClock clock) : IQueryHandler<ExportCashClosingsQuery, BillingExportFile>
{
    private static readonly string[] Header =
        ["Number", "Cashier", "From (UTC)", "To (UTC)", "Payments", "Refunds", "Expected cash", "Counted cash", "Difference", "Closed by", "Closed at (UTC)"];

    public async Task<BillingExportFile> Handle(ExportCashClosingsQuery query, CancellationToken cancellationToken)
    {
        var rows = await closings.SearchAsync(new CashClosingFilter(query.BranchId, query.CashierUserId, query.FromUtc, query.ToUtc), BillingCsv.MaxRows + 1, cancellationToken);
        BillingCsv.RequireWithinLimit(rows.Count);
        return BillingCsv.Build("cash-closings", clock.UtcNow, Header, rows.Select(row => (IReadOnlyList<object?>)
        [
            row.Number, row.CashierName ?? "*", row.PeriodFromUtc, row.PeriodToUtc, row.TotalPayments, row.TotalRefunds, row.ExpectedCash, row.CountedCash, row.Difference,
            row.ClosedByName, row.ClosedAtUtc,
        ]), BillingCsv.Zone(query.TimeZone));
    }
}

/// <summary>The cash-closing sheet (A4) with the movements of the period and the handover signatures rendered by the SPA.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetCashClosingSheetQuery(Guid ClosingId) : IQuery<CashClosingSheetDocument>;

public sealed class GetCashClosingSheetValidator : AbstractValidator<GetCashClosingSheetQuery>
{
    public GetCashClosingSheetValidator() => RuleFor(query => query.ClosingId).NotEmpty();
}

public sealed class GetCashClosingSheetHandler(
    ICashClosingRepository closings,
    ICashDrawerQueries drawer,
    IBillableVisitLookup visits,
    IBillingDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetCashClosingSheetQuery, CashClosingSheetDocument>
{
    public async Task<CashClosingSheetDocument> Handle(GetCashClosingSheetQuery query, CancellationToken cancellationToken)
    {
        var record = await closings.GetRecordAsync(query.ClosingId, cancellationToken) ?? throw BillingNotFound.Closing();
        var item = record.Item;
        var movements = await drawer.MovementsAsync(new DrawerScope(item.BranchId, item.CashierUserId, item.PeriodFromUtc, item.PeriodToUtc, record.LegacyTreasuryId), BillingLimits.MaxDrawerMovements + 1, cancellationToken);
        var truncated = movements.Count > BillingLimits.MaxDrawerMovements;
        var shown = await BillingAccessions.FillAsync(visits, truncated ? movements.Take(BillingLimits.MaxDrawerMovements).ToList() : movements, cancellationToken);
        var letterhead = await directory.LetterheadAsync(record.BranchId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new CashClosingSheetDocument(
            letterhead,
            item.Id,
            item.Number,
            item.CashierUserId,
            item.CashierName,
            item.PeriodFromUtc,
            item.PeriodToUtc,
            BillingViews.Totals(record.Totals),
            item.CountedCash,
            item.Difference,
            record.Note,
            item.ClosedByName,
            item.ClosedAtUtc,
            shown,
            truncated,
            currency,
            user.DisplayName ?? string.Empty,
            clock.UtcNow);
    }
}

internal static class CashierNames
{
    public static async Task<string?> OfAsync(ICashDrawerQueries drawer, Guid? cashierUserId, CancellationToken cancellationToken)
    {
        if (cashierUserId is not { } id)
        {
            return null;
        }

        var names = await drawer.UserNamesAsync([id], cancellationToken).ConfigureAwait(false);
        return names.TryGetValue(id, out var name) ? name : null;
    }
}
