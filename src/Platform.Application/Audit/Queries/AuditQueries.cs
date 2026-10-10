using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;

namespace Platform.Application.Audit.Queries;

/// <summary>The tenant's audit log (SRS CP-68; SA-09 is the same query under the platform tenant). Read-only; <c>audit.view</c>.</summary>
[RequirePermissionPolicy(PermissionCatalog.Audit, PermissionAction.View)]
public sealed record ListAuditEntriesQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? UserId, string? EntityType, string? EntityId, int? Page, int? PageSize) : IQuery<Paged<AuditEntryItem>>;

public sealed class ListAuditEntriesQueryValidator : AbstractValidator<ListAuditEntriesQuery>
{
    public ListAuditEntriesQueryValidator()
    {
        RuleFor(query => query.EntityType).MaximumLength(200);
        RuleFor(query => query.EntityId).MaximumLength(200);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query).Must(query => query.From is null || query.To is null || query.From <= query.To).WithMessage("'from' must not be after 'to'.");
    }
}

public sealed class ListAuditEntriesQueryHandler(IAuditReader reader) : IQueryHandler<ListAuditEntriesQuery, Paged<AuditEntryItem>>
{
    public Task<Paged<AuditEntryItem>> Handle(ListAuditEntriesQuery query, CancellationToken cancellationToken) =>
        reader.ListAsync(new AuditEntryFilter(query.From, query.To, query.UserId, query.EntityType, query.EntityId), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

/// <summary>Operators' check of the tamper-evident ledger (01 §4.8 "VerifyChain endpoint").</summary>
[RequirePermissionPolicy(PermissionCatalog.Audit, PermissionAction.View)]
public sealed record VerifyAuditChainQuery : IQuery<ChainVerificationResult>;

public sealed class VerifyAuditChainQueryHandler(IAuditReader reader) : IQueryHandler<VerifyAuditChainQuery, ChainVerificationResult>
{
    public Task<ChainVerificationResult> Handle(VerifyAuditChainQuery query, CancellationToken cancellationToken) => reader.VerifyChainAsync(cancellationToken);
}
