using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Account;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Queries;

// Ticket registers (intake LIS-INT-ACC-001 §14): the laboratory's own (support.view) and the platform desk's
// (support.manage, platform tenant only). The repository scopes every read to the current tenant (INV-SUP-004).

/// <summary>CP-67: the laboratory's tickets. <paramref name="Status"/> and <paramref name="Priority"/> are names; <paramref name="Mine"/> narrows to the actor's own.</summary>
[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.View)]
public sealed record ListSupportTicketsQuery(string? Status, string? Priority, string? Search, bool Mine, int? Page, int? PageSize) : IQuery<Paged<SupportTicketListItem>>;

public sealed class ListSupportTicketsValidator : AbstractValidator<ListSupportTicketsQuery>
{
    public ListSupportTicketsValidator()
    {
        RuleFor(query => query.Status).IsEnumName(typeof(SupportTicketStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Priority).IsEnumName(typeof(SupportTicketPriority), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Priority));
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListSupportTicketsHandler(ISupportTicketRepository tickets, ICurrentUser user, ICurrentTenant tenant) : IQueryHandler<ListSupportTicketsQuery, Paged<SupportTicketListItem>>
{
    public Task<Paged<SupportTicketListItem>> Handle(ListSupportTicketsQuery query, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireLaboratory(tenant);
        var filter = new SupportTicketFilter(
            SupportTicketFilters.Status(query.Status),
            SupportTicketFilters.Priority(query.Priority),
            query.Search,
            query.Mine ? AccountActor.Require(user) : null,
            null);
        return tickets.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.View)]
public sealed record GetSupportTicketQuery(Guid TicketId) : IQuery<SupportTicketDetail>;

public sealed class GetSupportTicketHandler(ISupportTicketRepository tickets, ICurrentTenant tenant) : IQueryHandler<GetSupportTicketQuery, SupportTicketDetail>
{
    public async Task<SupportTicketDetail> Handle(GetSupportTicketQuery query, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireLaboratory(tenant);
        return await tickets.GetDetailAsync(query.TicketId, cancellationToken)
            ?? throw new DomainException(AccountCodes.TicketNotFound, "Support ticket not found.", DomainFault.NotFound);
    }
}

/// <summary>The platform desk: every laboratory's tickets, optionally one laboratory's (<paramref name="TenantId"/>).</summary>
[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record ListDeskTicketsQuery(Guid? TenantId, string? Status, string? Priority, string? Search, int? Page, int? PageSize) : IQuery<Paged<SupportTicketListItem>>;

public sealed class ListDeskTicketsValidator : AbstractValidator<ListDeskTicketsQuery>
{
    public ListDeskTicketsValidator()
    {
        RuleFor(query => query.Status).IsEnumName(typeof(SupportTicketStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Priority).IsEnumName(typeof(SupportTicketPriority), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Priority));
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListDeskTicketsHandler(ISupportTicketRepository tickets, ICurrentTenant tenant) : IQueryHandler<ListDeskTicketsQuery, Paged<SupportTicketListItem>>
{
    public Task<Paged<SupportTicketListItem>> Handle(ListDeskTicketsQuery query, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        var filter = new SupportTicketFilter(SupportTicketFilters.Status(query.Status), SupportTicketFilters.Priority(query.Priority), query.Search, null, query.TenantId);
        return tickets.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record GetDeskTicketQuery(Guid TicketId) : IQuery<SupportTicketDetail>;

public sealed class GetDeskTicketHandler(ISupportTicketRepository tickets, ICurrentTenant tenant) : IQueryHandler<GetDeskTicketQuery, SupportTicketDetail>
{
    public async Task<SupportTicketDetail> Handle(GetDeskTicketQuery query, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        return await tickets.GetDetailAsync(query.TicketId, cancellationToken)
            ?? throw new DomainException(AccountCodes.TicketNotFound, "Support ticket not found.", DomainFault.NotFound);
    }
}

internal static class SupportTicketFilters
{
    public static SupportTicketStatus? Status(string? name) => string.IsNullOrEmpty(name) ? null : Enum.Parse<SupportTicketStatus>(name, ignoreCase: true);

    public static SupportTicketPriority? Priority(string? name) => string.IsNullOrEmpty(name) ? null : Enum.Parse<SupportTicketPriority>(name, ignoreCase: true);
}
