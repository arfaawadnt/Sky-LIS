using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// Validation 2026-10-11 C-03: user mobiles in E.164 by the country rules, and lookups that match every spelling a number
/// may have been stored under before the rule (no data migration: old rows are normalised on their next save).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IdentityMobileNumbersTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_national_or_international_mobile_is_stored_in_E164_by_the_country_rules()
    {
        await SeedCountryAsync("+20", 10);
        var national = NewEgyptianNational();
        using var scope = fixture.AppScope(null);
        var mobiles = scope.ServiceProvider.GetRequiredService<IUserMobileNumbers>();

        Assert.Equal("+20" + national, await mobiles.CanonicalAsync("0" + national, TenantIds.Platform, CancellationToken.None));
        Assert.Equal("+20" + national, await mobiles.CanonicalAsync(national, null, CancellationToken.None));
        Assert.Equal("+20" + national, await mobiles.CanonicalAsync("+20 " + national[..3] + " " + national[3..], null, CancellationToken.None));
        Assert.Equal("+20" + national, await mobiles.CanonicalAsync("0020" + national, null, CancellationToken.None));
        Assert.Null(await mobiles.CanonicalAsync("  ", null, CancellationToken.None));

        var shortNumber = await Assert.ThrowsAsync<DomainException>(() => mobiles.CanonicalAsync("+20" + national[..8], null, CancellationToken.None));
        Assert.Equal(IdentityCodes.MobileInvalid, shortNumber.Code);
        Assert.Contains("+20", shortNumber.Message, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_laboratory_user_mobile_follows_the_laboratory_country()
    {
        var saudi = await SeedCountryAsync("+966", 9);
        var tenantId = await ProvisionAsync(saudi);
        using var scope = fixture.AppScope(tenantId);
        var mobiles = scope.ServiceProvider.GetRequiredService<IUserMobileNumbers>();

        Assert.Equal("+966512345678", await mobiles.CanonicalAsync("0512345678", tenantId, CancellationToken.None));
        Assert.Equal("+966512345678", await mobiles.CanonicalAsync("512345678", tenantId, CancellationToken.None));
        var wrongLength = await Assert.ThrowsAsync<DomainException>(() => mobiles.CanonicalAsync("05123456", tenantId, CancellationToken.None));
        Assert.Equal(IdentityCodes.MobileInvalid, wrongLength.Code);
    }

    [DatabaseFact]
    public async Task Uniqueness_and_lookups_match_every_spelling_of_a_number()
    {
        await SeedCountryAsync("+20", 10);
        var national = NewEgyptianNational();
        Guid legacyId;
        using (var scope = fixture.AppScope(null))
        {
            // a row saved before the rule: the national form as typed
            var legacy = PlatformUser.Register($"mob.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Legacy Mobile", "hash", Now, "0" + national);
            Assert.Equal("0" + national, legacy.Mobile);
            scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(legacy);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            legacyId = legacy.Id;
        }

        using var read = fixture.AppScope(null);
        var mobiles = read.ServiceProvider.GetRequiredService<IUserMobileNumbers>();
        Assert.True(await mobiles.IsTakenAsync("+20" + national, null, CancellationToken.None));
        Assert.True(await mobiles.IsTakenAsync(national, null, CancellationToken.None));
        Assert.True(await mobiles.IsTakenAsync("0020 " + national, null, CancellationToken.None));
        Assert.False(await mobiles.IsTakenAsync("+20" + national, legacyId, CancellationToken.None));   // the user's own number
        Assert.False(await mobiles.IsTakenAsync("+20" + NewEgyptianNational(), null, CancellationToken.None));
        Assert.False(await mobiles.IsTakenAsync("not a number", null, CancellationToken.None));

        var spellings = await mobiles.SpellingsAsync("+20" + national, CancellationToken.None);
        var holders = await read.ServiceProvider.GetRequiredService<IPlatformUserRepository>().ListByMobileSpellingsAsync(spellings, CancellationToken.None);
        Assert.Equal(legacyId, Assert.Single(holders).Id);
    }

    internal static string NewEgyptianNational() =>
        "10" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private async Task<Country> SeedCountryAsync(string dialCode, int mobileLength)
    {
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Mobileland " + iso, "أرض " + iso, iso, dialCode, "Pound", "EGP", "ar", "Africa/Cairo", mobileLength, null, null));
        using var scope = fixture.AppScope(null);
        scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return country;
    }

    private async Task<Guid> ProvisionAsync(Country country)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("M" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        using (var scope = fixture.AppScope(null))
        {
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var slug = "mob-" + suffix[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "0512345670", null, "Manager", "0512345671", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 14, Now);
        using var provisioning = fixture.AppScope(tenant.Id, registerTenant: false);
        provisioning.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        await provisioning.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }
}
