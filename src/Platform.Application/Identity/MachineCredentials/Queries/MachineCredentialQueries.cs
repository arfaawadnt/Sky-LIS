using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Identity.MachineCredentials.Queries;

/// <summary>The tenant's machine credentials (secrets never included): live and expired by default, revoked on request.</summary>
[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ListMachineCredentialsQuery(bool IncludeRevoked, int? Page, int? PageSize) : IQuery<Paged<MachineCredentialListItem>>;

public sealed class ListMachineCredentialsValidator : AbstractValidator<ListMachineCredentialsQuery>
{
    public ListMachineCredentialsValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListMachineCredentialsHandler(IMachineCredentialRepository credentials, ICurrentTenant tenant, IClock clock) : IQueryHandler<ListMachineCredentialsQuery, Paged<MachineCredentialListItem>>
{
    public Task<Paged<MachineCredentialListItem>> Handle(ListMachineCredentialsQuery query, CancellationToken cancellationToken) =>
        credentials.ListAsync(MachineCredentialIssuance.RequireTenant(tenant), query.IncludeRevoked, clock.UtcNow, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}
