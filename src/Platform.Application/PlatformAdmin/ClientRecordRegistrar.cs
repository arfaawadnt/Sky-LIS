using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin;

/// <summary>
/// LIS-CR-005 (tooling, not an API): gives an existing tenant that has no client record one, so it gets a laboratory address
/// (sign-in is by address only). Used by DevTools (<c>register-tenant-client</c>, <c>seed-demo-lab</c>) and by the test hosts.
/// The caller picks the country, governorate and plan (they must exist and match; active or not). Contact fields nobody
/// recorded are filled with visible placeholders (<see cref="PlaceholderText"/>; mobiles = the country's dial code + 1 and
/// zeros), to be corrected on SA-04. Nothing else is provisioned: no wallet, no administrator, no catalogue copy, no event.
/// </summary>
public sealed class ClientRecordRegistrar(
    ITenantRepository tenants,
    ICountryRepository countries,
    IGovernorateRepository governorates,
    IPlanRepository plans,
    IUnitOfWork unitOfWork,
    ICurrentTenantSetter tenantScope,
    IClock clock)
{
    public const string PlaceholderText = "Not recorded (registered by tooling)";

    /// <param name="slug">The wanted address; when <paramref name="suggestWhenTaken"/> is true and it is taken, the suggestion rule of provisioning (<c>{stem}</c>, <c>{stem}-2</c> …) picks a free one.</param>
    public async Task<Tenant> RegisterAsync(Guid tenantId, string slug, string nameEn, string nameAr, Guid countryId, Guid governorateId, Guid planId, bool suggestWhenTaken, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || TenantIds.IsPlatform(tenantId))
        {
            throw new DomainException(PlatformAdminCodes.ClientDetailsInvalid, "The platform tenant is not a client.", DomainFault.Refused);
        }

        if (await tenants.GetForUpdateAsync(tenantId, cancellationToken) is not null)
        {
            throw new DomainException(PlatformAdminCodes.ClientDetailsInvalid, $"Tenant {tenantId} already has a client record.", DomainFault.Conflict);
        }

        var country = await countries.GetForUpdateAsync(countryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        var governorate = await governorates.GetForUpdateAsync(governorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        var plan = await plans.GetForUpdateAsync(planId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "Plan not found.", DomainFault.NotFound);

        var chosen = await ChooseSlugAsync(slug, nameEn, tenantId, suggestWhenTaken, cancellationToken);
        var placeholderMobile = country.DialCode + "1" + new string('0', country.MobileLength - 1);
        var profile = ClientProfile.Create(nameEn, nameAr, country, governorate.Id, PlaceholderText, placeholderMobile, null, PlaceholderText, placeholderMobile, null);
        var tenant = Tenant.RegisterExisting(tenantId, profile, country, governorate, plan, chosen, country.Language, clock.UtcNow);
        using (tenantScope.ForTenant(TenantIds.Platform))   // a platform record, written as the Super Admin portal writes it
        {
            tenants.Add(tenant);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return tenant;
    }

    private async Task<string> ChooseSlugAsync(string requested, string nameEn, Guid tenantId, bool suggestWhenTaken, CancellationToken cancellationToken)
    {
        var slug = Tenant.CleanSlug(requested);
        if (!await tenants.SlugExistsAsync(slug, cancellationToken))
        {
            return slug;
        }

        if (!suggestWhenTaken)
        {
            throw new DomainException(PlatformAdminCodes.PortalSlugTaken, "This portal address is already taken.", DomainFault.Conflict);
        }

        var stem = Tenant.SuggestSlug(nameEn, tenantId.ToString("N"));
        for (var attempt = 1; attempt <= TenantProvisioner.MaxSlugAttempts; attempt++)
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
