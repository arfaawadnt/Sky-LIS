using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization;

/// <summary>The handler-side rules of the module that need facts outside one aggregate (intake LIS-INT-ORG-001 §8–§9).</summary>
public static class OrganizationGuards
{
    /// <summary>The authenticated actor of a command, or AUTH-001.</summary>
    public static Guid Actor(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
    }

    /// <summary>The tenant of the request, or TENANT-000.</summary>
    public static Guid Tenant(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.TenantId ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this request.", DomainFault.Refused);
    }

    /// <summary>
    /// UAT B-02: a branch's governorate exists and belongs to the branch's country (ORG-017). The aggregate already
    /// refuses a governorate without a country (ORG-008); the pair needs the platform geography, so the handler checks it.
    /// </summary>
    public static async Task EnsureGovernorateInCountryAsync(IBranchGeography geography, Guid? countryId, Guid? governorateId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geography);
        if (governorateId is not { } governorate || governorate == Guid.Empty)
        {
            return;
        }

        var owner = await geography.CountryOfGovernorateAsync(governorate, cancellationToken).ConfigureAwait(false);
        if (owner is null || owner != countryId)
        {
            throw new DomainException(OrganizationCodes.GovernorateNotInCountry, "The governorate must belong to the chosen country.", DomainFault.Validation);
        }
    }

    /// <summary>SOD-ORG-001: an administrator does not administer their own access.</summary>
    public static void EnsureNotSelf(Guid actorUserId, Guid targetUserId)
    {
        if (actorUserId == targetUserId)
        {
            throw new DomainException(OrganizationCodes.SodActorIsTarget, "You cannot change your own access; ask another administrator.", DomainFault.Refused);
        }
    }

    /// <summary>INV-ORG-011: nobody grants a permission they do not hold themselves (no privilege escalation through roles or overrides).</summary>
    public static void EnsureGrantable(IUserPrivileges privileges, IEnumerable<string> permissionKeys)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        ArgumentNullException.ThrowIfNull(permissionKeys);
        var beyond = permissionKeys.Where(key => !privileges.Has(key)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (beyond.Count > 0)
        {
            throw new DomainException(OrganizationCodes.GrantBeyondOwnPrivileges, $"You cannot grant a permission you do not hold: {string.Join(", ", beyond)}.", DomainFault.Refused);
        }
    }

    /// <summary>INV-ORG-007: platform-only modules (platform, tenancy) are granted in the platform tenant only.</summary>
    public static void EnsureTenantMayGrant(Guid tenantId, IEnumerable<string> permissionKeys)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);
        if (tenantId == TenantIds.Platform)
        {
            return;
        }

        var platformOnly = permissionKeys.Where(EffectivePrivileges.IsPlatformOnly).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (platformOnly.Count > 0)
        {
            throw new DomainException(OrganizationCodes.PlatformPermissionOutsidePlatform, $"Platform permissions cannot be granted in a laboratory: {string.Join(", ", platformOnly)}.", DomainFault.Refused);
        }
    }

    /// <summary>
    /// INV-ORG-015: credentials are global (D-06), so a tenant administrator changes the credentials or contact data of
    /// a user only when that user is an active member of no other tenant (a cross-tenant takeover otherwise).
    /// </summary>
    public static async Task EnsureSoleTenancyAsync(IMembershipDirectory directory, Guid platformUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (await BelongsToOtherTenantsAsync(directory, platformUserId, tenantId, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.UserBelongsToOtherTenants, "This user also works for other organisations; their credentials cannot be changed here.", DomainFault.Refused);
        }
    }

    public static async Task<bool> BelongsToOtherTenantsAsync(IMembershipDirectory directory, Guid platformUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var memberships = await directory.ListActiveForUserAsync(platformUserId, cancellationToken);
        return memberships.Any(membership => membership.TenantId != tenantId);
    }

    /// <summary>INV-ORG-017: every assigned branch exists in the tenant and is active; the empty list means all active branches.</summary>
    public static async Task<Guid[]> RequireUsableBranchesAsync(IBranchLookup branches, IEnumerable<Guid>? branchIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branches);
        var ids = (branchIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (!await branches.IsActiveAsync(id, cancellationToken))
            {
                throw new DomainException(OrganizationCodes.BranchNotUsable, $"Branch {id} does not exist or is inactive.", DomainFault.Validation);
            }
        }

        return ids;
    }

    public static SpecialRights ToDomain(SpecialRightsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new SpecialRights(dto.DiscountCapPercent, dto.CanPay, dto.CanRefund, dto.CanTransfer, dto.CanCancel, dto.CanModifyVerifiedResult, dto.CanPreviewWithoutVerify, dto.CanPrintInvoice, dto.CanSeeVip);
    }

    public static SpecialRightsDto ToDto(SpecialRights rights)
    {
        ArgumentNullException.ThrowIfNull(rights);
        return new SpecialRightsDto(rights.DiscountCapPercent, rights.CanPay, rights.CanRefund, rights.CanTransfer, rights.CanCancel, rights.CanModifyVerifiedResult, rights.CanPreviewWithoutVerify, rights.CanPrintInvoice, rights.CanSeeVip);
    }

    public static ResultRight ToDomain(ResultRightDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new ResultRight(dto.ClinicalGroupId, dto.BranchId, dto.CanSee, dto.CanReview, dto.CanVerify, dto.CanEdit, dto.CanPrint);
    }

    public static ResultRightDto ToDto(ResultRight right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return new ResultRightDto(right.ClinicalGroupId, right.BranchId, right.CanSee, right.CanReview, right.CanVerify, right.CanEdit, right.CanPrint);
    }
}

/// <summary>
/// The password policy (decision of the user, 2026-10-09, after 02 §6.1): length between the tenant parameter
/// <c>passwordMinLength</c> (default 8) and 128, no composition rules, must not contain the username (ORG-020, ORG-021).
/// </summary>
public static class PasswordRules
{
    public const int MaxLength = SessionPolicy.PasswordMaxLength;

    public static void Ensure(string password, string username, int minLength)
    {
        var value = password ?? string.Empty;
        if (value.Length < minLength || value.Length > MaxLength)
        {
            throw new DomainException(OrganizationCodes.PasswordLength, $"The password must be {minLength} to {MaxLength} characters.", DomainFault.Validation);
        }

        if (!string.IsNullOrEmpty(username) && value.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(OrganizationCodes.PasswordContainsUsername, "The password must not contain the username.", DomainFault.Validation);
        }
    }
}
