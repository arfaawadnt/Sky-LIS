using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Organization;
using Platform.Application.PlatformAdmin;
using Platform.Application.Tenancy;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Organization;

/// <summary>
/// B5 (intake LIS-INT-ORG-001): stages the MAIN branch of a new laboratory, named after it, in the default currency.
/// Runs inside the provisioning handler's <c>ForTenant(newTenantId)</c> scope and never saves; the handler commits once.
/// </summary>
internal sealed class DefaultBranchProvisioner(IBranchRepository branches, ICurrentTenant tenant, IClock clock) : IDefaultBranchProvisioner
{
    public async Task<Guid> ProvisionAsync(Guid tenantId, LocalizedText name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ProvisioningScope.Ensure(tenant, tenantId);

        var existing = await branches.FindByCodeAsync(Branch.MainCode, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var branch = Branch.Create(Branch.MainCode, name, null, null, null, null, SettingKeys.DefaultCurrency.Default, clock.UtcNow);
        branches.Add(branch);
        return branch.Id;
    }
}

/// <summary>
/// U12: stages the laboratory's primary administrator — the platform user with the given Argon2id hash and recovery
/// mobile, the role "Laboratory Administrator" (every non-platform permission, every special right, cap 100 %), and the
/// membership flagged primary (INV-ORG-010) holding that role with no branch restriction (= every active branch).
/// Runs inside the new tenant's scope; never saves.
/// </summary>
internal sealed class TenantAdministratorProvisioner(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IRoleRepository roles,
    ICurrentTenant tenant,
    IClock clock) : ITenantAdministratorProvisioner
{
    public const string RoleNameEn = "Laboratory Administrator";
    public const string RoleNameAr = "مدير المختبر";
    public const string RoleDescription = "Provisioned with the laboratory: every laboratory permission and special right.";

    /// <summary>The domain of the placeholder e-mail when the username is not an e-mail address; the administrator replaces it on the Users page.</summary>
    public const string PlaceholderEmailDomain = "users.lis.local";

    public Task<Guid> ProvisionAsync(Guid tenantId, string username, string displayName, string mobile, string passwordHash, CancellationToken cancellationToken)
    {
        ProvisioningScope.Ensure(tenant, tenantId);
        var now = clock.UtcNow;

        var normalised = PlatformUser.NormaliseUsername(username);
        var email = normalised.Contains('@', StringComparison.Ordinal) ? normalised : $"{normalised}@{PlaceholderEmailDomain}";
        var user = PlatformUser.Register(normalised, email, displayName, passwordHash, now, mobile);

        var role = Role.CreateForTenant(tenantId, new LocalizedText(RoleNameEn, RoleNameAr), EffectivePrivileges.TenantGrantableKeys, now, RoleDescription, SpecialRights.All);

        var employeeCode = normalised.ToUpperInvariant();
        var membership = TenantMembership.Create(tenantId, user.Id, employeeCode[..Math.Min(employeeCode.Length, TenantMembership.MaxEmployeeCodeLength)], new LocalizedText(displayName, displayName), "en", now);
        membership.MarkPrimaryAdministrator();
        membership.AssignRole(role, now);

        users.Add(user);
        roles.Add(role);
        memberships.Add(membership);
        return Task.FromResult(user.Id);
    }
}

internal static class ProvisioningScope
{
    /// <summary>The provisioning handler must have opened <c>ForTenant(tenantId)</c>: rows are stamped with the current tenant at commit.</summary>
    public static void Ensure(ICurrentTenant tenant, Guid tenantId)
    {
        if (tenantId == Guid.Empty || tenant.TenantId != tenantId)
        {
            throw new InvalidOperationException("Provisioning must run inside ForTenant(tenantId) of the new tenant.");
        }
    }
}
