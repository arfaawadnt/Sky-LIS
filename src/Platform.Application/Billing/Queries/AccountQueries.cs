using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Registration;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Queries;

// Visit and batch accounts, the invoice and the cash desk's print parameters (intake §14). Queries carry a policy like
// commands (D-04) and never commit.

/// <summary>The account of a visit by id or by accession (CP-23 "visit search").</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetVisitAccountQuery(Guid? VisitId, string? Accession) : IQuery<VisitAccountView>;

public sealed class GetVisitAccountValidator : AbstractValidator<GetVisitAccountQuery>
{
    public GetVisitAccountValidator()
    {
        RuleFor(query => query).Must(query => query.VisitId is { } id ? id != Guid.Empty : !string.IsNullOrWhiteSpace(query.Accession))
            .OverridePropertyName("visitId").WithMessage("A visit id or an accession is required.");
        RuleFor(query => query.Accession).MaximumLength(BillingLimits.MaxSearchLength);
    }
}

public sealed class GetVisitAccountHandler(IBillableVisitLookup visits, IVisitAccountRepository accounts, IRefundReasonLookup reasons, IBillingDirectory directory, ITenantSettings settings)
    : IQueryHandler<GetVisitAccountQuery, VisitAccountView>
{
    public async Task<VisitAccountView> Handle(GetVisitAccountQuery query, CancellationToken cancellationToken)
    {
        var visit = (query.VisitId is { } id
            ? await visits.GetAsync(id, cancellationToken)
            : await visits.GetByAccessionAsync(query.Accession!.Trim(), cancellationToken)) ?? throw BillingNotFound.Visit();

        var payments = (await accounts.SearchPaymentsAsync(new PaymentFilter(visit.VisitId, null, null, null, null, null, null), BillingLimits.MaxVisitMovements, cancellationToken))
            .Select(payment => payment with { Accession = visit.Accession })
            .ToList();
        var refunds = await BillingAccessions.FillAsync(
            visits,
            reasons,
            await accounts.SearchRefundsAsync(new RefundFilter(visit.VisitId, null, null, null, null, null), BillingLimits.MaxVisitMovements, cancellationToken),
            cancellationToken);
        var figures = new VisitBalanceFigures(
            visit.PatientShareTotal,
            payments.Where(payment => payment.Status == nameof(PaymentStatus.Posted)).Sum(payment => payment.Amount),
            refunds.Sum(refund => refund.Amount));

        var patients = await directory.PatientsAsync([visit.PatientId], cancellationToken);
        patients.TryGetValue(visit.PatientId, out var patient);
        var payer = await PayerAsync(directory, visit.PayerId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new VisitAccountView(
            visit.VisitId,
            visit.Accession,
            visit.PatientId,
            patient?.Number,
            patient?.Name,
            visit.BranchId,
            visit.PayerId,
            payer?.NameEn,
            payer?.NameAr,
            visit.BatchId,
            visit.BatchNumber,
            visit.BatchKind,
            visit.Status,
            visit.RegisteredAtUtc,
            visit.TotalListPrice,
            visit.DiscountTotal,
            visit.PatientShareTotal,
            visit.PayerShareTotal,
            figures.Paid,
            figures.Refunded,
            figures.Remaining,
            figures.Refundable,
            figures.Status.ToString(),
            visit.BilledThroughClaims,
            currency,
            payments,
            refunds);
    }

    internal static async Task<NamedReference?> PayerAsync(IBillingDirectory directory, Guid? payerId, CancellationToken cancellationToken)
    {
        if (payerId is not { } id)
        {
            return null;
        }

        var payers = await directory.PayersAsync([id], cancellationToken);
        return payers.TryGetValue(id, out var payer) ? payer : null;
    }
}

/// <summary>The visit invoice (document "Receipt / invoice"): billed lines with shares, payments, refunds and the balance.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetInvoiceQuery(Guid VisitId) : IQuery<InvoiceDocument>;

public sealed class GetInvoiceValidator : AbstractValidator<GetInvoiceQuery>
{
    public GetInvoiceValidator() => RuleFor(query => query.VisitId).NotEmpty();
}

public sealed class GetInvoiceHandler(
    IBillableVisitLookup visits,
    IVisitAccountRepository accounts,
    IBillingDirectory directory,
    ITenantSettings settings,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : IQueryHandler<GetInvoiceQuery, InvoiceDocument>
{
    public async Task<InvoiceDocument> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        BillingRights.RequirePrintInvoice(privileges);
        var visit = await visits.GetAsync(query.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var lines = await visits.LinesAsync(visit.VisitId, cancellationToken);
        var services = await directory.ServicesAsync(lines.Select(line => line.ServiceId).Distinct().ToList(), cancellationToken);
        var payments = (await accounts.SearchPaymentsAsync(new PaymentFilter(visit.VisitId, null, null, null, null, null, PaymentStatus.Posted), BillingLimits.MaxVisitMovements, cancellationToken))
            .OrderBy(payment => payment.PaidAtUtc)
            .ToList();
        var refunds = (await accounts.SearchRefundsAsync(new RefundFilter(visit.VisitId, null, null, null, null, null), BillingLimits.MaxVisitMovements, cancellationToken))
            .OrderBy(refund => refund.RefundedAtUtc)
            .ToList();
        var figures = new VisitBalanceFigures(visit.PatientShareTotal, payments.Sum(payment => payment.Amount), refunds.Sum(refund => refund.Amount));

        var patients = await directory.PatientsAsync([visit.PatientId], cancellationToken);
        patients.TryGetValue(visit.PatientId, out var patient);
        var payer = await GetVisitAccountHandler.PayerAsync(directory, visit.PayerId, cancellationToken);
        var letterhead = await directory.LetterheadAsync(visit.BranchId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        var invoiceLines = lines.Select(line =>
        {
            services.TryGetValue(line.ServiceId, out var service);
            return new InvoiceLine(line.LineId, line.ServiceId, service?.Code, service?.NameEn, service?.NameAr, line.Status, line.ListPrice, line.Discount, line.PatientShare, line.PayerShare);
        }).ToList();

        return new InvoiceDocument(
            letterhead,
            visit.VisitId,
            visit.Accession,
            visit.RegisteredAtUtc,
            patient?.Number,
            patient?.Name,
            payer?.NameEn,
            payer?.NameAr,
            invoiceLines,
            visit.TotalListPrice,
            visit.DiscountTotal,
            visit.PatientShareTotal,
            visit.PayerShareTotal,
            payments.Select(payment => new InvoiceMovement(payment.ReceiptNumber, payment.PaidAtUtc, payment.Method, payment.Amount, payment.MethodCode, payment.MethodNameEn, payment.MethodNameAr)).ToList(),
            refunds.Select(refund => new InvoiceMovement(refund.Number, refund.RefundedAtUtc, refund.Method, refund.Amount, refund.MethodCode, refund.MethodNameEn, refund.MethodNameAr)).ToList(),
            figures.Paid,
            figures.Refunded,
            figures.Remaining,
            currency,
            user.DisplayName ?? string.Empty,
            clock.UtcNow);
    }
}

/// <summary>The account of a batch by id or number (CP-27): members in accession order with their shares and balances.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetBatchAccountQuery(Guid? BatchId, string? Number) : IQuery<BatchAccountView>;

public sealed class GetBatchAccountValidator : AbstractValidator<GetBatchAccountQuery>
{
    public GetBatchAccountValidator()
    {
        RuleFor(query => query).Must(query => query.BatchId is { } id ? id != Guid.Empty : !string.IsNullOrWhiteSpace(query.Number))
            .OverridePropertyName("batchId").WithMessage("A batch id or a batch number is required.");
        RuleFor(query => query.Number).MaximumLength(BillingLimits.MaxSearchLength);
    }
}

public sealed class GetBatchAccountHandler(IBillableVisitLookup visits, IVisitBalanceLookup balances, IBillingDirectory directory, ITenantSettings settings)
    : IQueryHandler<GetBatchAccountQuery, BatchAccountView>
{
    public async Task<BatchAccountView> Handle(GetBatchAccountQuery query, CancellationToken cancellationToken)
    {
        var batch = (query.BatchId is { } id
            ? await visits.GetBatchAsync(id, cancellationToken)
            : await visits.GetBatchByNumberAsync(query.Number!.Trim(), cancellationToken)) ?? throw BillingNotFound.Batch();

        var members = await visits.BatchMembersAsync(batch.BatchId, cancellationToken);
        var memberIds = members.Select(member => member.VisitId).ToList();
        var known = await balances.GetManyAsync(memberIds, cancellationToken);
        var patients = await directory.PatientsAsync(members.Select(member => member.PatientId).Distinct().ToList(), cancellationToken);
        var payer = await GetVisitAccountHandler.PayerAsync(directory, batch.PayerId, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        var views = members.Select(member =>
        {
            var figures = known.TryGetValue(member.VisitId, out var balance)
                ? new VisitBalanceFigures(member.PatientShareTotal, balance.Paid, balance.Refunded)
                : new VisitBalanceFigures(member.PatientShareTotal, 0m, 0m);
            patients.TryGetValue(member.PatientId, out var patient);
            return new BatchMemberView(
                member.VisitId,
                member.Accession,
                member.PatientId,
                patient?.Number,
                patient?.Name,
                member.TotalListPrice,
                member.DiscountTotal,
                member.PatientShareTotal,
                member.PayerShareTotal,
                figures.Paid,
                figures.Refunded,
                figures.Remaining,
                figures.Status.ToString());
        }).ToList();

        return new BatchAccountView(
            batch.BatchId,
            batch.Number,
            batch.Kind,
            batch.PayerId,
            payer?.NameEn,
            payer?.NameAr,
            batch.BilledThroughClaims,
            views,
            views.Sum(view => view.TotalListPrice),
            views.Sum(view => view.DiscountTotal),
            views.Sum(view => view.PatientShareTotal),
            views.Sum(view => view.PayerShareTotal),
            views.Sum(view => view.Paid),
            views.Sum(view => view.Refunded),
            views.Sum(view => view.Remaining),
            currency);
    }
}

/// <summary>
/// The cash desk's print parameters (CP-23 "receipt auto-printed per parameter", CP-57 <c>autoPrintReceipt</c>). The pages
/// of the module read them here under <c>billing.view</c>: a cashier holds no <c>settings.view</c>, so reading
/// <c>/api/v1/settings</c> was refused and the page fell back to printing (UAT A-05).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetBillingPrintSettingsQuery : IQuery<BillingPrintSettings>;

public sealed class GetBillingPrintSettingsHandler(ITenantSettings settings) : IQueryHandler<GetBillingPrintSettingsQuery, BillingPrintSettings>
{
    public async Task<BillingPrintSettings> Handle(GetBillingPrintSettingsQuery query, CancellationToken cancellationToken) =>
        new(await settings.GetAsync(SettingKeys.AutoPrintReceipt, cancellationToken));
}

/// <summary>Limits shared by the module's queries.</summary>
internal static class BillingLimits
{
    public const int MaxSearchLength = 40;
    public const int MaxVisitMovements = 500;
    public const int MaxDrawerMovements = 2000;
}
