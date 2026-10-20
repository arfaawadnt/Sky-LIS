using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Registration;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Queries;

// Payments and refunds registers, their CSV exports and their documents (intake §14).

/// <summary>Resolves the visit filter of a register: an explicit id, or the visit of an exact accession (no match = empty register).</summary>
internal static class BillingVisitFilter
{
    public static async Task<(bool Matches, Guid? VisitId)> ResolveAsync(IBillableVisitLookup visits, Guid? visitId, string? accession, CancellationToken cancellationToken)
    {
        if (visitId is { } id)
        {
            return (true, id);
        }

        if (string.IsNullOrWhiteSpace(accession))
        {
            return (true, null);
        }

        var visit = await visits.GetByAccessionAsync(accession.Trim(), cancellationToken).ConfigureAwait(false);
        return visit is null ? (false, null) : (true, visit.VisitId);
    }
}

/// <summary>Shape rules shared by the register filters.</summary>
internal static class RegisterRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string?> accession, Func<T, DateTimeOffset?> from, Func<T, DateTimeOffset?> to, Func<T, string?> method)
    {
        validator.RuleFor(query => accession(query)).MaximumLength(BillingLimits.MaxSearchLength).OverridePropertyName("accession");
        validator.RuleFor(query => method(query)).IsEnumName(typeof(PaymentMethod), caseSensitive: false).When(query => !string.IsNullOrEmpty(method(query))).OverridePropertyName("method");
        validator.RuleFor(query => query).Must(query => from(query) is not { } start || to(query) is not { } end || start < end)
            .OverridePropertyName("to").WithMessage("The end of the period must follow its start.");
    }

    public static void Paging<T>(AbstractValidator<T> validator, Func<T, int?> page, Func<T, int?> pageSize)
    {
        validator.RuleFor(query => page(query)).GreaterThan(0).When(query => page(query) is not null).OverridePropertyName("page");
        validator.RuleFor(query => pageSize(query)).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => pageSize(query) is not null).OverridePropertyName("pageSize");
    }
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record ListPaymentsQuery(Guid? VisitId, string? Accession, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, string? Method, string? Status, int? Page, int? PageSize)
    : IQuery<Paged<PaymentListItem>>;

public sealed class ListPaymentsValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsValidator()
    {
        RegisterRules.Apply(this, query => query.Accession, query => query.FromUtc, query => query.ToUtc, query => query.Method);
        RegisterRules.Paging(this, query => query.Page, query => query.PageSize);
        RuleFor(query => query.Status).IsEnumName(typeof(PaymentStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
    }
}

public sealed class ListPaymentsHandler(IVisitAccountRepository accounts, IBillableVisitLookup visits) : IQueryHandler<ListPaymentsQuery, Paged<PaymentListItem>>
{
    public async Task<Paged<PaymentListItem>> Handle(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var (matches, visitId) = await BillingVisitFilter.ResolveAsync(visits, query.VisitId, query.Accession, cancellationToken);
        if (!matches)
        {
            return Paged<PaymentListItem>.Empty(page);
        }

        var filter = new PaymentFilter(visitId, query.FromUtc, query.ToUtc, query.BranchId, query.CashierUserId, BillingNames.OptionalMethod(query.Method), BillingNames.OptionalStatus(query.Status));
        var result = await accounts.ListPaymentsAsync(filter, page, cancellationToken);
        return result with { Items = await BillingAccessions.FillAsync(visits, result.Items, cancellationToken) };
    }
}

/// <summary>CSV of the payments register (<c>billing.export</c>, at most 10,000 rows: BIL-050).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Export)]
public sealed record ExportPaymentsQuery(Guid? VisitId, string? Accession, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, string? Method, string? Status, string? TimeZone = null)
    : IQuery<BillingExportFile>;

public sealed class ExportPaymentsValidator : AbstractValidator<ExportPaymentsQuery>
{
    public ExportPaymentsValidator()
    {
        RegisterRules.Apply(this, query => query.Accession, query => query.FromUtc, query => query.ToUtc, query => query.Method);
        RuleFor(query => query.Status).IsEnumName(typeof(PaymentStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.TimeZone).Must(BillingCsv.IsKnownZone).WithMessage("The time zone must be an IANA time zone name.");
    }
}

public sealed class ExportPaymentsHandler(IVisitAccountRepository accounts, IBillableVisitLookup visits, IClock clock) : IQueryHandler<ExportPaymentsQuery, BillingExportFile>
{
    private static readonly string[] Header = ["Receipt", "Paid at (UTC)", "Accession", "Amount", "Method", "Method code", "Reference", "Cashier", "Status", "Void reason", "Voided by", "Voided at (UTC)"];

    public async Task<BillingExportFile> Handle(ExportPaymentsQuery query, CancellationToken cancellationToken)
    {
        var (matches, visitId) = await BillingVisitFilter.ResolveAsync(visits, query.VisitId, query.Accession, cancellationToken);
        IReadOnlyList<PaymentListItem> rows = [];
        if (matches)
        {
            var filter = new PaymentFilter(visitId, query.FromUtc, query.ToUtc, query.BranchId, query.CashierUserId, BillingNames.OptionalMethod(query.Method), BillingNames.OptionalStatus(query.Status));
            rows = await accounts.SearchPaymentsAsync(filter, BillingCsv.MaxRows + 1, cancellationToken);
            BillingCsv.RequireWithinLimit(rows.Count);
            rows = await BillingAccessions.FillAsync(visits, rows, cancellationToken);
        }

        return BillingCsv.Build("payments", clock.UtcNow, Header, rows.Select(row => (IReadOnlyList<object?>)
        [
            row.ReceiptNumber, row.PaidAtUtc, row.Accession, row.Amount, row.Method, row.MethodCode, row.Reference, row.CashierName, row.Status,
            row.VoidReason, row.VoidedByName, row.VoidedAtUtc,
        ]), BillingCsv.Zone(query.TimeZone));
    }
}

/// <summary>The receipt of one payment (A5), with the visit's figures as of printing (intake Q14).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetPaymentReceiptQuery(Guid PaymentId) : IQuery<PaymentReceiptDocument>;

public sealed class GetPaymentReceiptValidator : AbstractValidator<GetPaymentReceiptQuery>
{
    public GetPaymentReceiptValidator() => RuleFor(query => query.PaymentId).NotEmpty();
}

public sealed class GetPaymentReceiptHandler(
    IVisitAccountRepository accounts,
    IBillableVisitLookup visits,
    IVisitBalanceLookup balances,
    IBillingDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetPaymentReceiptQuery, PaymentReceiptDocument>
{
    public async Task<PaymentReceiptDocument> Handle(GetPaymentReceiptQuery query, CancellationToken cancellationToken)
    {
        var payment = await accounts.GetPaymentAsync(query.PaymentId, cancellationToken) ?? throw BillingNotFound.Payment();
        var visit = await visits.GetAsync(payment.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var balance = await balances.GetAsync(visit.VisitId, cancellationToken);
        var patients = await directory.PatientsAsync([visit.PatientId], cancellationToken);
        patients.TryGetValue(visit.PatientId, out var patient);
        var letterhead = await directory.LetterheadAsync(payment.BranchId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new PaymentReceiptDocument(
            letterhead,
            payment.Id,
            payment.ReceiptNumber,
            payment.Status,
            payment.PaidAtUtc,
            visit.VisitId,
            visit.Accession,
            patient?.Number,
            patient?.Name,
            payment.Amount,
            payment.Method,
            payment.Reference,
            payment.CashierName,
            visit.PatientShareTotal,
            balance?.Paid ?? 0m,
            balance?.Refunded ?? 0m,
            balance?.Remaining ?? visit.PatientShareTotal,
            payment.VoidReason,
            payment.VoidedAtUtc,
            currency,
            user.DisplayName ?? string.Empty,
            clock.UtcNow,
            payment.MethodCode,
            payment.MethodNameEn,
            payment.MethodNameAr);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record ListRefundsQuery(Guid? VisitId, string? Accession, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, string? Method, int? Page, int? PageSize)
    : IQuery<Paged<RefundListItem>>;

public sealed class ListRefundsValidator : AbstractValidator<ListRefundsQuery>
{
    public ListRefundsValidator()
    {
        RegisterRules.Apply(this, query => query.Accession, query => query.FromUtc, query => query.ToUtc, query => query.Method);
        RegisterRules.Paging(this, query => query.Page, query => query.PageSize);
    }
}

public sealed class ListRefundsHandler(IVisitAccountRepository accounts, IBillableVisitLookup visits, IRefundReasonLookup reasons) : IQueryHandler<ListRefundsQuery, Paged<RefundListItem>>
{
    public async Task<Paged<RefundListItem>> Handle(ListRefundsQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var (matches, visitId) = await BillingVisitFilter.ResolveAsync(visits, query.VisitId, query.Accession, cancellationToken);
        if (!matches)
        {
            return Paged<RefundListItem>.Empty(page);
        }

        var filter = new RefundFilter(visitId, query.FromUtc, query.ToUtc, query.BranchId, query.CashierUserId, BillingNames.OptionalMethod(query.Method));
        var result = await accounts.ListRefundsAsync(filter, page, cancellationToken);
        return result with { Items = await BillingAccessions.FillAsync(visits, reasons, result.Items, cancellationToken) };
    }
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Export)]
public sealed record ExportRefundsQuery(Guid? VisitId, string? Accession, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, string? Method, string? TimeZone = null)
    : IQuery<BillingExportFile>;

public sealed class ExportRefundsValidator : AbstractValidator<ExportRefundsQuery>
{
    public ExportRefundsValidator()
    {
        RegisterRules.Apply(this, query => query.Accession, query => query.FromUtc, query => query.ToUtc, query => query.Method);
        RuleFor(query => query.TimeZone).Must(BillingCsv.IsKnownZone).WithMessage("The time zone must be an IANA time zone name.");
    }
}

public sealed class ExportRefundsHandler(IVisitAccountRepository accounts, IBillableVisitLookup visits, IRefundReasonLookup reasons, IClock clock) : IQueryHandler<ExportRefundsQuery, BillingExportFile>
{
    private static readonly string[] Header = ["Number", "Refunded at (UTC)", "Accession", "Amount", "Method", "Method code", "Reference", "Reason", "Reason (Arabic)", "Note", "Cashier"];

    public async Task<BillingExportFile> Handle(ExportRefundsQuery query, CancellationToken cancellationToken)
    {
        var (matches, visitId) = await BillingVisitFilter.ResolveAsync(visits, query.VisitId, query.Accession, cancellationToken);
        IReadOnlyList<RefundListItem> rows = [];
        if (matches)
        {
            var filter = new RefundFilter(visitId, query.FromUtc, query.ToUtc, query.BranchId, query.CashierUserId, BillingNames.OptionalMethod(query.Method));
            rows = await accounts.SearchRefundsAsync(filter, BillingCsv.MaxRows + 1, cancellationToken);
            BillingCsv.RequireWithinLimit(rows.Count);
            rows = await BillingAccessions.FillAsync(visits, reasons, rows, cancellationToken);
        }

        return BillingCsv.Build("refunds", clock.UtcNow, Header, rows.Select(row => (IReadOnlyList<object?>)
        [
            row.Number, row.RefundedAtUtc, row.Accession, row.Amount, row.Method, row.MethodCode, row.Reference, row.ReasonNameEn, row.ReasonNameAr, row.Note, row.CashierName,
        ]), BillingCsv.Zone(query.TimeZone));
    }
}

/// <summary>The refund receipt (A5) with signature boxes rendered by the SPA (LDM §10.8: "for patient signature").</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetRefundReceiptQuery(Guid RefundId) : IQuery<RefundReceiptDocument>;

public sealed class GetRefundReceiptValidator : AbstractValidator<GetRefundReceiptQuery>
{
    public GetRefundReceiptValidator() => RuleFor(query => query.RefundId).NotEmpty();
}

public sealed class GetRefundReceiptHandler(
    IVisitAccountRepository accounts,
    IBillableVisitLookup visits,
    IRefundReasonLookup reasons,
    IBillingDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetRefundReceiptQuery, RefundReceiptDocument>
{
    public async Task<RefundReceiptDocument> Handle(GetRefundReceiptQuery query, CancellationToken cancellationToken)
    {
        var refund = await accounts.GetRefundAsync(query.RefundId, cancellationToken) ?? throw BillingNotFound.Refund();
        var visit = await visits.GetAsync(refund.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var names = await reasons.GetManyAsync([refund.ReasonId], cancellationToken);
        names.TryGetValue(refund.ReasonId, out var reason);
        var patients = await directory.PatientsAsync([visit.PatientId], cancellationToken);
        patients.TryGetValue(visit.PatientId, out var patient);
        var letterhead = await directory.LetterheadAsync(refund.BranchId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new RefundReceiptDocument(
            letterhead,
            refund.Id,
            refund.Number,
            refund.RefundedAtUtc,
            visit.VisitId,
            visit.Accession,
            patient?.Number,
            patient?.Name,
            refund.Amount,
            refund.Method,
            refund.Reference,
            reason?.NameEn,
            reason?.NameAr,
            refund.Note,
            refund.CashierName,
            currency,
            user.DisplayName ?? string.Empty,
            clock.UtcNow,
            refund.MethodCode,
            refund.MethodNameEn,
            refund.MethodNameAr);
    }
}
