using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Billing.Queries;

// The payment methods set-up list (CP-30b, change request 2026-10-19) and the pick list of the cash desk / registration page.

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record ListPaymentMethodsQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<PaymentMethodListItem>>;

public sealed class ListPaymentMethodsValidator : AbstractValidator<ListPaymentMethodsQuery>
{
    public ListPaymentMethodsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RegisterRules.Paging(this, query => query.Page, query => query.PageSize);
        RuleFor(query => query.Sort).SortKeyOf(BillingSortKeys.PaymentMethods);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListPaymentMethodsHandler(IPaymentMethodRepository methods) : IQueryHandler<ListPaymentMethodsQuery, Paged<PaymentMethodListItem>>
{
    public Task<Paged<PaymentMethodListItem>> Handle(ListPaymentMethodsQuery query, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return methods.ListAsync(search, query.IsActive, ListSort.Of(query.Sort, query.Direction), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record GetPaymentMethodQuery(Guid MethodId) : IQuery<PaymentMethodListItem>;

public sealed class GetPaymentMethodValidator : AbstractValidator<GetPaymentMethodQuery>
{
    public GetPaymentMethodValidator() => RuleFor(query => query.MethodId).NotEmpty();
}

public sealed class GetPaymentMethodHandler(IPaymentMethodRepository methods) : IQueryHandler<GetPaymentMethodQuery, PaymentMethodListItem>
{
    public async Task<PaymentMethodListItem> Handle(GetPaymentMethodQuery query, CancellationToken cancellationToken) =>
        await methods.GetListItemAsync(query.MethodId, cancellationToken) ?? throw BillingNotFound.Method();
}

/// <summary>
/// The active methods in pick-list order, read with <c>billing.view</c>: the registration page
/// already needs that key to capture a payment (BR-26), so the reception role holds it wherever it pays.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.View)]
public sealed record ListPaymentMethodOptionsQuery : IQuery<IReadOnlyList<PaymentMethodOption>>;

public sealed class ListPaymentMethodOptionsHandler(IPaymentMethodRepository methods) : IQueryHandler<ListPaymentMethodOptionsQuery, IReadOnlyList<PaymentMethodOption>>
{
    public Task<IReadOnlyList<PaymentMethodOption>> Handle(ListPaymentMethodOptionsQuery query, CancellationToken cancellationToken) =>
        methods.OptionsAsync(cancellationToken);
}

/// <summary>
/// CSV of the set-up list under the grid's filter (<c>billing.export</c>, like the registers' exports): every method of
/// the tenant that matches the search and the status filter, in pick-list order. The list is small by nature (a cap of
/// <see cref="PageRequest.MaxPageSize"/> rows, BIL-050 beyond it).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Export)]
public sealed record ExportPaymentMethodsQuery(string? Search, bool? IsActive) : IQuery<BillingExportFile>;

public sealed class ExportPaymentMethodsValidator : AbstractValidator<ExportPaymentMethodsQuery>
{
    public ExportPaymentMethodsValidator() => RuleFor(query => query.Search).MaximumLength(100);
}

public sealed class ExportPaymentMethodsHandler(IPaymentMethodRepository methods, IClock clock) : IQueryHandler<ExportPaymentMethodsQuery, BillingExportFile>
{
    private static readonly string[] Header = ["Code", "Name", "Name (Arabic)", "Kind", "Requires reference", "Sequence", "Active"];

    public async Task<BillingExportFile> Handle(ExportPaymentMethodsQuery query, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var page = await methods.ListAsync(search, query.IsActive, ListSort.Of(null, null), PageRequest.Normalized(1, PageRequest.MaxPageSize), cancellationToken);
        if (page.TotalCount > PageRequest.MaxPageSize)
        {
            throw new DomainException(BillingCodes.ExportTooLarge, $"The export is limited to {PageRequest.MaxPageSize} rows; narrow the filter.", DomainFault.Validation);
        }

        return BillingCsv.Build("payment-methods", clock.UtcNow, Header, page.Items.Select(row => (IReadOnlyList<object?>)
        [
            row.Code, row.NameEn, row.NameAr, row.Kind, row.RequiresReference ? "Yes" : "No", row.Sequence, row.IsActive ? "Yes" : "No",
        ]));
    }
}
