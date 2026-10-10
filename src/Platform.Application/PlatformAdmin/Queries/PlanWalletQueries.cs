using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// SA-05 plans and client wallets; CP-66 the laboratory's own wallet.

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListPlansQuery(bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<PlanListItem>>, IPlatformAdministrationRequest;

public sealed class ListPlansHandler(IPlanRepository plans, ICurrentTenant currentTenant) : IQueryHandler<ListPlansQuery, Paged<PlanListItem>>
{
    public Task<Paged<PlanListItem>> Handle(ListPlansQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return plans.ListAsync(query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record GetClientWalletQuery(Guid ClientId) : IQuery<WalletDetail>, IPlatformAdministrationRequest;

public sealed class GetClientWalletHandler(IWalletRepository wallets, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<GetClientWalletQuery, WalletDetail>
{
    public async Task<WalletDetail> Handle(GetClientWalletQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return await wallets.GetDetailAsync(query.ClientId, WalletPeriods.MonthStart(clock.UtcNow), cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.WalletNotFound, "The client has no wallet.", DomainFault.NotFound);
    }
}

/// <summary><paramref name="Kind"/>: TopUp|Subscription|Usage.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListClientWalletTransactionsQuery(Guid ClientId, string? Kind, int? Page, int? PageSize) : IQuery<Paged<WalletTransactionItem>>, IPlatformAdministrationRequest;

public sealed class ListClientWalletTransactionsValidator : AbstractValidator<ListClientWalletTransactionsQuery>
{
    public ListClientWalletTransactionsValidator()
    {
        RuleFor(query => query.ClientId).NotEmpty();
        RuleFor(query => query.Kind).IsEnumName(typeof(WalletTransactionKind), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Kind));
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListClientWalletTransactionsHandler(IWalletRepository wallets, ICurrentTenant currentTenant) : IQueryHandler<ListClientWalletTransactionsQuery, Paged<WalletTransactionItem>>
{
    public Task<Paged<WalletTransactionItem>> Handle(ListClientWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return wallets.ListTransactionsAsync(query.ClientId, WalletPeriods.KindOf(query.Kind), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

/// <summary>CP-66: the wallet of the session's laboratory.</summary>
[RequirePermissionPolicy(PermissionCatalog.Wallet, PermissionAction.View)]
public sealed record GetMyWalletQuery : IQuery<WalletDetail>;

public sealed class GetMyWalletHandler(IWalletRepository wallets, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<GetMyWalletQuery, WalletDetail>
{
    public async Task<WalletDetail> Handle(GetMyWalletQuery query, CancellationToken cancellationToken) =>
        await wallets.GetDetailAsync(OwnWallet.TenantOf(currentTenant), WalletPeriods.MonthStart(clock.UtcNow), cancellationToken)
        ?? throw new DomainException(PlatformAdminCodes.WalletNotFound, "This laboratory has no wallet.", DomainFault.NotFound);
}

[RequirePermissionPolicy(PermissionCatalog.Wallet, PermissionAction.View)]
public sealed record ListMyWalletTransactionsQuery(string? Kind, int? Page, int? PageSize) : IQuery<Paged<WalletTransactionItem>>;

public sealed class ListMyWalletTransactionsValidator : AbstractValidator<ListMyWalletTransactionsQuery>
{
    public ListMyWalletTransactionsValidator()
    {
        RuleFor(query => query.Kind).IsEnumName(typeof(WalletTransactionKind), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Kind));
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListMyWalletTransactionsHandler(IWalletRepository wallets, ICurrentTenant currentTenant) : IQueryHandler<ListMyWalletTransactionsQuery, Paged<WalletTransactionItem>>
{
    public Task<Paged<WalletTransactionItem>> Handle(ListMyWalletTransactionsQuery query, CancellationToken cancellationToken) =>
        wallets.ListTransactionsAsync(OwnWallet.TenantOf(currentTenant), WalletPeriods.KindOf(query.Kind), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

internal static class WalletPeriods
{
    public static DateTimeOffset MonthStart(DateTimeOffset nowUtc) => new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public static WalletTransactionKind? KindOf(string? kind) => string.IsNullOrEmpty(kind) ? null : Enum.Parse<WalletTransactionKind>(kind, ignoreCase: true);
}
