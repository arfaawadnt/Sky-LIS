using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Patients;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.Patients;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Primitives;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of change request 2026-10-19 A2 / C3 (intakes LIS-INT-PLT-001 §17, LIS-INT-PAT-001 §18): the three
/// saas location tables with their per-parent unique names (UNIQUE-409), the pick lists filtered by parent and by
/// activity, the Patients read port over them, and the new patient columns (second phone, location ids) through the
/// repository and the similar pre-filter. Platform rows carry no tenant, so no invisibility scenario applies to them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LocationsModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_English_name_is_unique_per_parent_case_insensitively_UNIQUE_409()
    {
        var (_, governorate, nasr, zone1) = await SeedChainAsync();

        var duplicateCity = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(scope =>
            scope.ServiceProvider.GetRequiredService<ICityRepository>().Add(City.Create(governorate, nasr.Name.En.ToUpperInvariant(), "مكرر"))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateCity.Code);

        var duplicateDistrict = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(scope =>
            scope.ServiceProvider.GetRequiredService<IDistrictRepository>().Add(District.Create(nasr, " first zone ", "مكرر"))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateDistrict.Code);

        var duplicateArea = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(scope =>
            scope.ServiceProvider.GetRequiredService<IAreaRepository>().Add(Area.Create(zone1, "BLOCK 7", "مكرر"))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateArea.Code);

        // the same name under another parent is fine
        var other = City.Create(governorate, "Maadi", "المعادي");
        await SaveAsync(scope => scope.ServiceProvider.GetRequiredService<ICityRepository>().Add(other));
        await SaveAsync(scope => scope.ServiceProvider.GetRequiredService<IDistrictRepository>().Add(District.Create(other, "First Zone", "المنطقة الأولى")));
    }

    [DatabaseFact]
    public async Task The_pick_lists_filter_by_parent_and_hide_inactive_rows_and_the_admin_lists_search_both_names()
    {
        var (country, governorate, nasr, zone1) = await SeedChainAsync();
        var otherGovernorate = Governorate.Create(country, "Giza " + Guid.CreateVersion7().ToString("N")[..6], "الجيزة", null, null);
        var dokki = City.Create(otherGovernorate, "Dokki", "الدقي");
        var sleeping = City.Create(governorate, "Sleeping City", "مدينة نائمة");
        sleeping.Deactivate(hasActiveDistricts: false);
        await SaveAsync(scope =>
        {
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(otherGovernorate);
            scope.ServiceProvider.GetRequiredService<ICityRepository>().Add(dokki);
            scope.ServiceProvider.GetRequiredService<ICityRepository>().Add(sleeping);
        });

        using var scope = fixture.AppScope(Guid.CreateVersion7(), Guid.CreateVersion7());   // a laboratory's scope: the platform rows are readable
        var cities = scope.ServiceProvider.GetRequiredService<ICityRepository>();
        var districts = scope.ServiceProvider.GetRequiredService<IDistrictRepository>();
        var areas = scope.ServiceProvider.GetRequiredService<IAreaRepository>();

        var ofGovernorate = await cities.ListPublicAsync(governorate.Id, CancellationToken.None);
        Assert.Equal([nasr.Id], ofGovernorate.Select(city => city.Id));   // Dokki is Giza's, the sleeping city is inactive
        Assert.Equal((governorate.Id, "Nasr City", "مدينة نصر"), (ofGovernorate[0].GovernorateId, ofGovernorate[0].NameEn, ofGovernorate[0].NameAr));
        Assert.Contains(await cities.ListPublicAsync(null, CancellationToken.None), city => city.Id == dokki.Id);   // unfiltered: every governorate
        Assert.Equal([zone1.Id], (await districts.ListPublicAsync(nasr.Id, CancellationToken.None)).Select(district => district.Id));
        Assert.Empty(await districts.ListPublicAsync(dokki.Id, CancellationToken.None));
        Assert.Single(await areas.ListPublicAsync(zone1.Id, CancellationToken.None));
        Assert.Empty(await areas.ListPublicAsync(Guid.CreateVersion7(), CancellationToken.None));

        // the Locations drawer: every row of the parent (inactive included), searched in both scripts, escaped wildcards
        var all = await cities.ListAsync(governorate.Id, null, null, CancellationToken.None);
        Assert.Equal([nasr.Id, sleeping.Id], all.Select(city => city.Id));   // by English name
        Assert.Equal([sleeping.Id], (await cities.ListAsync(governorate.Id, null, false, CancellationToken.None)).Select(city => city.Id));
        Assert.Equal([nasr.Id], (await cities.ListAsync(governorate.Id, "نصر", null, CancellationToken.None)).Select(city => city.Id));
        Assert.Empty(await cities.ListAsync(governorate.Id, "%", null, CancellationToken.None));
        Assert.True(await cities.NameExistsAsync(governorate.Id, "NASR CITY", null, CancellationToken.None));
        Assert.False(await cities.NameExistsAsync(governorate.Id, "Nasr City", nasr.Id, CancellationToken.None));
        Assert.True(await cities.HasActiveDistrictsAsync(nasr.Id, CancellationToken.None));
        Assert.True(await districts.HasActiveAreasAsync(zone1.Id, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_patients_read_port_answers_the_chain_and_a_file_keeps_its_location_and_second_phone()
    {
        var (_, governorate, nasr, zone1) = await SeedChainAsync();
        Guid block7Id;
        using (var platform = fixture.AppScope(null))
        {
            block7Id = Assert.Single(await platform.ServiceProvider.GetRequiredService<IAreaRepository>().ListPublicAsync(zone1.Id, CancellationToken.None)).Id;
        }

        var tenant = Guid.CreateVersion7();

        Guid patientId;
        using (var scope = fixture.AppScope(tenant, Guid.CreateVersion7()))
        {
            var locations = scope.ServiceProvider.GetRequiredService<IPatientLocations>();
            var city = Assert.Single(await locations.CitiesAsync([nasr.Id, Guid.CreateVersion7()], CancellationToken.None)).Value;
            Assert.Equal(new LocationNode(nasr.Id, governorate.Id, "Nasr City", "مدينة نصر", true), city);
            Assert.Equal(nasr.Id, Assert.Single(await locations.DistrictsAsync([zone1.Id], CancellationToken.None)).Value.ParentId);
            Assert.Equal(zone1.Id, Assert.Single(await locations.AreasAsync([block7Id], CancellationToken.None)).Value.ParentId);
            Assert.Empty(await locations.CitiesAsync([], CancellationToken.None));

            var demographics = await PatientLocationChain.ResolveAsync(
                locations,
                new PatientDemographics(PatientTitle.Mr, "Located Person", null, PatientGender.Male, new DateOnly(1990, 1, 1), null, null, "01000000001", null, null, null, governorate.Id, "typed", null, null, nasr.Id, zone1.Id, block7Id, "0111 222 3333"),
                CancellationToken.None);
            var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
            var patient = Patient.Register(number, demographics, PatientFieldRules.Optional, false, Now);
            scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(patient);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            patientId = patient.Id;
        }

        using var reader = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = reader.ServiceProvider.GetRequiredService<IPatientRepository>();
        var row = (await repository.GetRowAsync(patientId, CancellationToken.None))!;
        Assert.Equal(("Nasr City", nasr.Id, zone1.Id, block7Id, "01112223333"), (row.City, row.CityId, row.DistrictId, row.AreaId, row.SecondPhone));

        var names = await PatientLocationChain.NamesAsync(reader.ServiceProvider.GetRequiredService<IPatientLocations>(), [row], CancellationToken.None);
        Assert.Equal(("Nasr City", "First Zone", "Block 7"), (names[patientId].CityNameEn, names[patientId].DistrictNameEn, names[patientId].AreaNameEn));

        // C3: the second phone takes part in the similar pre-filter and in the register's digit search
        Assert.Equal([patientId], (await repository.FindSimilarCandidatesAsync([], ["01112223333"], 10, CancellationToken.None)).Select(found => found.Id));
        Assert.Equal([patientId], (await repository.FindSimilarCandidatesAsync([], ["01000000001"], 10, CancellationToken.None)).Select(found => found.Id));
        var searched = await repository.SearchAsync(new PatientSearchFilter(PatientSearchTerms.Parse("2223333"), null, null, PatientStatus.Active), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal([patientId], searched.Items.Select(found => found.Id));
    }

    private async Task<(Country Country, Governorate Governorate, City Nasr, District Zone1)> SeedChainAsync()
    {
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Testland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Cairo " + Guid.CreateVersion7().ToString("N")[..6], "القاهرة", null, null);
        var nasr = City.Create(governorate, "Nasr City", "مدينة نصر");
        var zone1 = District.Create(nasr, "First Zone", "المنطقة الأولى");
        var block7 = Area.Create(zone1, "Block 7", "بلوك 7");
        await SaveAsync(scope =>
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<ICityRepository>().Add(nasr);
            scope.ServiceProvider.GetRequiredService<IDistrictRepository>().Add(zone1);
            scope.ServiceProvider.GetRequiredService<IAreaRepository>().Add(block7);
        });
        return (country, governorate, nasr, zone1);
    }

    private async Task SaveAsync(Action<IServiceScope> stage)
    {
        using var scope = fixture.AppScope(null);
        stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

}
