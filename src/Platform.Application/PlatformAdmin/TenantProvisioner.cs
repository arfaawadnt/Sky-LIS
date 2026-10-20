using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>What provisioning needs, whichever door it came through (SA-04 manual, CP-02 wizard).</summary>
public sealed record ProvisionTenantSpec(
    string NameEn,
    string NameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    Guid? PlanId,
    string? PortalSlug,
    TenantSource Source,
    string? Language,
    string AdminUsername,
    string AdminPassword);

/// <summary>A provisioning prepared in memory: the aggregates and the administrator's data, nothing staged yet.</summary>
public sealed record PreparedTenant(Tenant Tenant, Wallet Wallet, string AdminUsername, string AdminDisplayName, string AdminMobile, string PasswordHash);

/// <summary>
/// Tenant provisioning (WF-01, intake §11) in four steps a handler calls around its single flush:
/// <see cref="PrepareAsync"/> checks everything that can be checked before writing (place, mobiles, plan, slug,
/// username, password policy) and builds the aggregates; inside <see cref="ITenantProvisioningScope"/> (one transaction in
/// the new tenant's scope) <see cref="StageAsync"/> stages the client, its wallet, the default branch, the administrator
/// and catalogue A's copy (BR-25), the handler flushes once, and <see cref="CopyAfterFlushAsync"/> runs catalogue B's
/// set-based copy on the flushed rows; the scope commits. <see cref="AnnounceAsync"/> then sends the portal address to
/// the manager and writes the security event.
/// </summary>
public sealed class TenantProvisioner(
    ITenantRepository tenants,
    IWalletRepository wallets,
    ICountryRepository countries,
    IGovernorateRepository governorates,
    IPlanRepository plans,
    IPlatformUserRepository users,
    IDefaultBranchProvisioner branches,
    ITenantAdministratorProvisioner administrators,
    IMasterDataCloner masterData,
    IPasswordHasher hasher,
    ITenantSettings settings,
    ICurrentTenantSetter tenantScope,
    IClock clock)
{
    public const int MaxSlugAttempts = 50;

    public async Task<PreparedTenant> PrepareAsync(ProvisionTenantSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var now = clock.UtcNow;

        var country = await countries.GetForUpdateAsync(spec.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        var governorate = await governorates.GetForUpdateAsync(spec.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        var plan = spec.PlanId is { } planId
            ? await plans.GetForUpdateAsync(planId, cancellationToken) ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "Plan not found.", DomainFault.NotFound)
            : await plans.FindDefaultForSubscriptionAsync(cancellationToken) ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "No active plan is available for new subscriptions.", DomainFault.NotFound);

        var profile = ClientProfile.Create(spec.NameEn, spec.NameAr, country, governorate.Id, spec.Address, spec.Mobile, spec.LicenceNo, spec.ManagerName, spec.ManagerMobile, spec.Email);

        var username = NormaliseUsername(spec.AdminUsername);
        if (await users.UsernameExistsAsync(username, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.UsernameTaken, "This username is already used.", DomainFault.Conflict);
        }

        ClientPasswordPolicy.Check(spec.AdminPassword, username, await ClientPasswordPolicy.MinimumLengthAsync(settings, tenantScope, cancellationToken));

        var slug = await ChooseSlugAsync(spec.PortalSlug, spec.NameEn, username, cancellationToken);
        int trialDays;
        using (tenantScope.ForTenant(TenantIds.Platform))
        {
            trialDays = await settings.GetAsync(SettingKeys.TrialDays, cancellationToken);   // the platform default (SA-19)
        }

        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, spec.Source, spec.Language ?? country.Language, trialDays, now);
        var wallet = Wallet.Open(tenant.Id, country.CurrencyCode, tenant.TrialEndsAtUtc ?? now.AddMonths(1));
        return new PreparedTenant(tenant, wallet, username, profile.ManagerName, profile.ManagerMobile, hasher.Hash(spec.AdminPassword));
    }

    /// <summary>Stages every row of the new tenant. Call inside <see cref="ITenantProvisioningScope"/>, then flush once.</summary>
    public async Task StageAsync(PreparedTenant prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        tenants.Add(prepared.Tenant);
        wallets.Add(prepared.Wallet);

        await branches.ProvisionAsync(prepared.Tenant.Id, new LocalizedText(prepared.Tenant.Name.En, prepared.Tenant.Name.Ar), cancellationToken);
        var administratorId = await administrators.ProvisionAsync(prepared.Tenant.Id, prepared.AdminUsername, prepared.AdminDisplayName, prepared.AdminMobile, prepared.PasswordHash, cancellationToken);
        prepared.Tenant.AttachAdministrator(administratorId, prepared.AdminUsername);

        await masterData.StageAsync(TenantIds.Platform, prepared.Tenant.Id, cancellationToken);
    }

    /// <summary>The set-based half of the master-data copy, after the handler's flush and before the scope commits.</summary>
    public Task CopyAfterFlushAsync(PreparedTenant prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return masterData.ApplyAsync(TenantIds.Platform, prepared.Tenant.Id, cancellationToken);
    }

    /// <summary>After the commit: the portal address to the manager (template <c>portal-url</c>) and the security event.</summary>
    public static async Task AnnounceAsync(PreparedTenant prepared, string portalBaseUrl, INotificationSender sender, ISecurityEventLog events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(events);
        var tenant = prepared.Tenant;
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["labName"] = tenant.Name.For(tenant.PreferredLanguage),
            ["url"] = tenant.PortalUrl(portalBaseUrl),
            ["username"] = prepared.AdminUsername,
        };
        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, tenant.ManagerMobile, NotificationTemplateCodes.PortalUrl, tenant.PreferredLanguage, placeholders), cancellationToken);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.TenantProvisioned, SecurityEvent.Info, Detail: $"tenant:{tenant.Id} source:{tenant.Source}"), cancellationToken);
    }

    /// <summary>The username of a new administrator (CLI-042 when the shape is wrong).</summary>
    public static string NormaliseUsername(string username)
    {
        try
        {
            return PlatformUser.NormaliseUsername(username);
        }
        catch (DomainException exception)
        {
            throw new DomainException(PlatformAdminCodes.UsernameInvalid, exception.Message, DomainFault.Validation, exception);
        }
    }

    private async Task<string> ChooseSlugAsync(string? requested, string nameEn, string username, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var slug = Tenant.CleanSlug(requested);
            if (await tenants.SlugExistsAsync(slug, cancellationToken))
            {
                throw new DomainException(PlatformAdminCodes.PortalSlugTaken, "This portal address is already taken.", DomainFault.Conflict);
            }

            return slug;
        }

        var stem = Tenant.SuggestSlug(nameEn, username);
        for (var attempt = 1; attempt <= MaxSlugAttempts; attempt++)
        {
            var candidate = attempt == 1 ? stem : $"{stem}-{attempt}";
            if (!await tenants.SlugExistsAsync(candidate, cancellationToken))
            {
                return Tenant.CleanSlug(candidate);
            }
        }

        throw new DomainException(PlatformAdminCodes.PortalSlugTaken, "No free portal address could be derived from the name; choose one.", DomainFault.Conflict);
    }
}
