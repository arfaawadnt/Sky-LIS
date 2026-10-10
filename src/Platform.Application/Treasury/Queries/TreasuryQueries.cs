using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Treasury;
using Platform.Domain.Authorization;
using Platform.Domain.Treasury;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Treasury.Queries;

// Registers of the module (intake §14). Queries carry a policy like commands (D-04) and read projections only.

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.View)]
public sealed record ListTreasuriesQuery(bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<TreasuryListItem>>;

public sealed class ListTreasuriesHandler(ITreasuryRepository treasuries) : IQueryHandler<ListTreasuriesQuery, Paged<TreasuryListItem>>
{
    public Task<Paged<TreasuryListItem>> Handle(ListTreasuriesQuery query, CancellationToken cancellationToken) =>
        treasuries.ListAsync(query.IsActive, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

/// <summary><paramref name="Status"/> is the status name; <paramref name="MineOnly"/> narrows to the actor's own requests.</summary>
[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.View)]
public sealed record ListTreasuryAccessRequestsQuery(string? Status, Guid? TreasuryId, bool MineOnly, int? Page, int? PageSize) : IQuery<Paged<TreasuryAccessRequestListItem>>;

public sealed class ListTreasuryAccessRequestsValidator : AbstractValidator<ListTreasuryAccessRequestsQuery>
{
    public ListTreasuryAccessRequestsValidator()
    {
        RuleFor(query => query.Status).IsEnumName(typeof(TreasuryAccessStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListTreasuryAccessRequestsHandler(ITreasuryAccessRequestRepository requests, ICurrentUser user) : IQueryHandler<ListTreasuryAccessRequestsQuery, Paged<TreasuryAccessRequestListItem>>
{
    public Task<Paged<TreasuryAccessRequestListItem>> Handle(ListTreasuryAccessRequestsQuery query, CancellationToken cancellationToken)
    {
        TreasuryAccessStatus? status = string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<TreasuryAccessStatus>(query.Status, ignoreCase: true);
        var filter = new TreasuryAccessRequestFilter(status, query.TreasuryId, query.MineOnly ? user.UserId : null);
        return requests.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.View)]
public sealed record GetTreasuryAccessRequestQuery(Guid RequestId) : IQuery<TreasuryAccessRequestDetail>;

public sealed class GetTreasuryAccessRequestHandler(ITreasuryAccessRequestRepository requests) : IQueryHandler<GetTreasuryAccessRequestQuery, TreasuryAccessRequestDetail>
{
    public async Task<TreasuryAccessRequestDetail> Handle(GetTreasuryAccessRequestQuery query, CancellationToken cancellationToken) =>
        await requests.GetDetailAsync(query.RequestId, cancellationToken)
        ?? throw new DomainException(TreasuryCodes.RequestNotFound, "Treasury access request not found.", DomainFault.NotFound);
}
