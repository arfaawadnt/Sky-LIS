using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Application.Tenancy;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.Identity;

/// <summary>
/// Creates (or completes) a National Technology administrator of the platform tenant (MODULES.md §1): the user, a
/// membership in <see cref="TenantIds.Platform"/> and the role "Platform Administrator" holding every catalogued
/// permission. Idempotent: an existing user, membership or role is reused and the role's keys are brought up to the
/// current catalogue. Used by the DevTools command <c>seed-platform-admin</c> and by the functional tests; the
/// production bootstrap of the first administrator goes through the same code.
/// </summary>
public sealed class PlatformAdministratorSeeder(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IRoleRepository roles,
    IPasswordHasher hasher,
    ICurrentTenantSetter tenantSetter,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const string RoleName = "Platform Administrator";
    public const string RoleNameAr = "مدير المنصة";

    public async Task<PlatformAdministrator> SeedAsync(string username, string password, CancellationToken cancellationToken, string? mobile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var now = clock.UtcNow;

        using (tenantSetter.ForTenant(TenantIds.Platform))
        {
            var user = await users.FindByUsernameAsync(username, cancellationToken);
            var created = user is null;
            if (user is null)
            {
                user = PlatformUser.Register(username, $"{PlatformUser.NormaliseUsername(username)}@platform.lis.local", RoleName, hasher.Hash(password), now, mobile);
                users.Add(user);
            }

            var role = await roles.FindByNameAsync(RoleName, cancellationToken);
            if (role is null)
            {
                role = Role.Create(new LocalizedText(RoleName, RoleNameAr), PermissionCatalog.Keys, now);
                roles.Add(role);
            }
            else if (!role.PermissionKeys.SequenceEqual(PermissionCatalog.Keys.Order(StringComparer.Ordinal)))
            {
                role.SetPermissions(PermissionCatalog.Keys, now);
            }

            var membership = await memberships.FindByPlatformUserAsync(user.Id, cancellationToken);
            if (membership is null)
            {
                membership = TenantMembership.Create(TenantIds.Platform, user.Id, "NT-" + user.Username[..Math.Min(8, user.Username.Length)].ToUpperInvariant(), new LocalizedText(RoleName, RoleNameAr), "en", now);
                memberships.Add(membership);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);   // the role carries its tenant stamp before the membership references it
            if (membership.RoleId != role.Id)
            {
                membership.AssignRole(role, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // The first platform administrator is the protected primary one (one per tenant; it cannot be locked or demoted).
            if (!membership.IsPrimaryAdministrator && !await memberships.PrimaryAdministratorExistsAsync(cancellationToken))
            {
                membership.MarkPrimaryAdministrator();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new PlatformAdministrator(user.Id, user.Username, membership.Id, role.Id, created);
        }
    }
}

public sealed record PlatformAdministrator(Guid UserId, string Username, Guid MembershipId, Guid RoleId, bool UserCreated);
