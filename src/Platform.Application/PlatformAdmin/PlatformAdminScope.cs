using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// Marks a command or query of the Super Admin portal (SA-03…SA-20). Its handler must start with
/// <see cref="PlatformAdminScope.Require"/>: a client tenant never reaches platform administration, whatever keys its
/// roles hold (INV-PLT-070). Gate: <c>PlatformAdminGateTests</c>.
/// </summary>
public interface IPlatformAdministrationRequest;

/// <summary>The platform-tenant check of every admin handler (PLT-403, 403).</summary>
public static class PlatformAdminScope
{
    public static void Require(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.TenantId is not { } id || !TenantIds.IsPlatform(id))
        {
            throw new DomainException(PlatformAdminCodes.PlatformTenantRequired, "Platform administration is available only in the Super Admin portal.", DomainFault.Refused);
        }
    }
}

/// <summary>Security events of this module (02 §7 ledger); details are categories and ids, never mobiles, codes or passwords.</summary>
public static class PlatformAdminSecurityEvents
{
    public const string TenantProvisioned = "TENANT_PROVISIONED";
    public const string TenantActivated = "TENANT_ACTIVATED";
    public const string TenantDeactivated = "TENANT_DEACTIVATED";
    public const string ClientAdminPasswordReset = "CLIENT_ADMIN_PASSWORD_RESET";
    public const string SessionTerminatedByAdmin = "SESSION_TERMINATED_BY_ADMIN";
    public const string SubscriptionStarted = "SUBSCRIPTION_STARTED";
    public const string SubscriptionVerified = "SUBSCRIPTION_VERIFIED";
    public const string SubscriptionVerifyFailed = "SUBSCRIPTION_VERIFY_FAILED";
    public const string SubscriptionCompleted = "SUBSCRIPTION_COMPLETED";
}

/// <summary>
/// The password rule of every credential this module sets (decision of the user, 2026-10-09; 02 §6.1): minimum length
/// from the platform parameters (<c>passwordMinLength</c>, default 8), maximum 128, no composition rules, must not
/// contain the username. Shape (empty, maximum) is the validators'; this is the setting-dependent part.
/// </summary>
public static class ClientPasswordPolicy
{
    public const int MaxLength = 128;

    /// <summary>The platform tenant's minimum length (SA-19), read in the platform tenant's scope whatever the caller's tenant.</summary>
    public static async Task<int> MinimumLengthAsync(ITenantSettings settings, ICurrentTenantSetter tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tenantScope);
        using (tenantScope.ForTenant(TenantIds.Platform))
        {
            return await settings.GetAsync(SettingKeys.PasswordMinLength, cancellationToken);
        }
    }

    public static void Check(string password, string username, int minimumLength)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < minimumLength || password.Length > MaxLength)
        {
            throw new DomainException(PlatformAdminCodes.PasswordTooShort, $"The password needs {minimumLength} to {MaxLength} characters.", DomainFault.Validation);
        }

        var normalised = PlatformUser.NormaliseUsername(username);
        if (normalised.Length > 0 && password.Contains(normalised, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(PlatformAdminCodes.PasswordContainsUsername, "The password must not contain the username.", DomainFault.Validation);
        }
    }
}

/// <summary>The laboratory's own wallet (CP-66): the tenant of the session, never the platform tenant (which has none).</summary>
internal static class OwnWallet
{
    public static Guid TenantOf(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var id = tenant.TenantId ?? throw new DomainException(Contracts.Common.ProblemCodes.TenantUnresolved, "No tenant is resolved for this request.", DomainFault.Refused);
        return TenantIds.IsPlatform(id)
            ? throw new DomainException(PlatformAdminCodes.WalletNotFound, "The platform has no wallet.", DomainFault.NotFound)
            : id;
    }
}
