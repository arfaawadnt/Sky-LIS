using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.Organization.Policies;

// Cross-module reactions of the Organization module (03 §7, MODULES.md §3). Each policy runs after the commit that
// raised the event, inside the event's tenant scope, is idempotent on its natural key and commits once.

/// <summary>
/// P1 (change request 2026-10-14): when a clinical group is created, then every active role that manages roles
/// (<see cref="Role.ManagesRoles"/>, which the provisioned Laboratory Administrator holds) receives the five result rights
/// on it for every branch, so the group's results are not invisible to the laboratory's administrators until someone
/// remembers the Result rights tab. Roles without that permission are left alone: their rights stay a deliberate choice.
/// The platform tenant's catalogue is a template (BR-25) and takes no rights. Idempotent: a role that already holds a row
/// for the group in every branch is skipped, and the policy saves nothing when no role changed.
/// </summary>
public sealed class GrantNewClinicalGroupToAdministratorsPolicy(
    IRoleRepository roles,
    IPrincipalCacheInvalidator principals,
    ISecurityEventLog events,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork) : IDomainEventHandler<ClinicalGroupCreated>
{
    public async Task HandleAsync(ClinicalGroupCreated domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.ClinicalGroupId == Guid.Empty || tenant.TenantId is not { } tenantId || TenantIds.IsPlatform(tenantId))
        {
            return;
        }

        var granted = new List<Role>();
        foreach (var role in await roles.ListActiveForUpdateAsync(cancellationToken))
        {
            if (role.ManagesRoles && role.GrantResultRight(ResultRight.Full(domainEvent.ClinicalGroupId), domainEvent.OccurredAtUtc))
            {
                granted.Add(role);
            }
        }

        if (granted.Count == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var role in granted)
        {
            await principals.EvictRoleMembersAsync(role.Id, cancellationToken);
            await events.WriteAsync(
                new SecurityEvent(OrganizationSecurityEvents.PrivilegeMatrixChanged, SecurityEvent.Info, tenantId, null, $"role:{role.Id}:{RoleChangeAspects.ResultRights}:clinical-group:{domainEvent.ClinicalGroupId}"),
                cancellationToken);
        }
    }
}
