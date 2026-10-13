using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Patients;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.Patients;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// Validation v0.5.0 F-08: the patient form opens on the laboratory's country (its client record's), and a mobile typed with
/// the trunk 0 finds a file stored without it (and the other way round) in the register and the visit register.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LaboratoryCountryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_laboratory_country_is_the_country_of_the_client_record()
    {
        var country = await SeedCountryAsync(9);
        var tenant = await ProvisionAsync(country);

        using (var scope = fixture.AppScope(tenant, Guid.CreateVersion7()))
        {
            var found = await scope.ServiceProvider.GetRequiredService<ILaboratoryCountry>().FindAsync(CancellationToken.None);
            Assert.Equal(new LaboratoryCountry(country.IsoCode, 9), found);
        }

        using var outside = fixture.AppScope(null);
        Assert.Null(await outside.ServiceProvider.GetRequiredService<ILaboratoryCountry>().FindAsync(CancellationToken.None));
    }

    [DatabaseFact]
    public async Task A_mobile_typed_with_or_without_the_trunk_0_finds_the_file_stored_either_way()
    {
        var tenant = Guid.CreateVersion7();
        var withoutZero = await RegisterAsync(tenant, "Mobile Ten", "1019199912");
        var withZero = await RegisterAsync(tenant, "Mobile Eleven", "01019199924");

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();

        async Task<IReadOnlyList<Guid>> Search(string text, int? length) =>
            (await repository.SearchAsync(new PatientSearchFilter(PatientSearchTerms.Parse(text, length), null, null, PatientStatus.Active), PageRequest.Normalized(1, 50), CancellationToken.None))
                .Items.Select(row => row.Id).ToList();

        Assert.Equal([withoutZero], await Search("01019199912", 10));
        Assert.Equal([withoutZero], await Search("1019199912", 10));
        Assert.Equal([withZero], await Search("01019199924", 10));
        Assert.Equal([withZero], await Search("1019199924", 10));
        Assert.Empty(await Search("01019199912", null));   // without a laboratory country the number is searched as typed
    }

    private async Task<Guid> RegisterAsync(Guid tenant, string fullName, string mobile)
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var number = await scope.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Persistence.IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
        var patient = Patient.Register(
            number,
            new PatientDemographics(PatientTitle.Mr, fullName, null, PatientGender.Male, new DateOnly(1990, 1, 1), null, null, mobile, null, null, null, null, null, null, null),
            PatientFieldRules.Optional,
            false,
            Now);
        scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(patient);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return patient.Id;
    }

    private async Task<Country> SeedCountryAsync(int mobileLength)
    {
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Countryland " + iso, "أرض " + iso, iso, "+966", "Riyal", "SAR", "ar", "Asia/Riyadh", mobileLength, null, null));
        using var scope = fixture.AppScope(null);
        scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return country;
    }

    private async Task<Guid> ProvisionAsync(Country country)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("C" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 100m, "SAR", 1, 5, 100, 1));
        using (var scope = fixture.AppScope(null))
        {
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var slug = "cty-" + suffix[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "0512345670", null, "Manager", "0512345671", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 14, Now);
        using var provisioning = fixture.AppScope(tenant.Id);
        provisioning.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        await provisioning.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }
}
