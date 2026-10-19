using Platform.Application.Patients;
using Platform.Contracts.Patients;
using Platform.Domain.Patients;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Patients;

/// <summary>
/// Change request 2026-10-19 A2 (intake LIS-INT-PAT-001 §18): a file's city must belong to its governorate, the district to
/// the city and the area to the district (PAT-014/015/016); a picked city writes its English name into the text city; the
/// registers get the three names from the same port.
/// </summary>
public sealed class PatientLocationChainTests
{
    private static readonly Guid Cairo = Guid.CreateVersion7();
    private static readonly Guid Giza = Guid.CreateVersion7();
    private static readonly Guid Nasr = Guid.CreateVersion7();
    private static readonly Guid Dokki = Guid.CreateVersion7();
    private static readonly Guid Zone1 = Guid.CreateVersion7();
    private static readonly Guid Mesaha = Guid.CreateVersion7();
    private static readonly Guid Block7 = Guid.CreateVersion7();

    private static readonly FakeLocations Locations = new(
        new Dictionary<Guid, LocationNode>
        {
            [Nasr] = new(Nasr, Cairo, "Nasr City", "مدينة نصر", true),
            [Dokki] = new(Dokki, Giza, "Dokki", "الدقي", true),
        },
        new Dictionary<Guid, LocationNode>
        {
            [Zone1] = new(Zone1, Nasr, "First Zone", "المنطقة الأولى", true),
            [Mesaha] = new(Mesaha, Dokki, "Mesaha", "المساحة", true),
        },
        new Dictionary<Guid, LocationNode> { [Block7] = new(Block7, Zone1, "Block 7", "بلوك 7", false) });

    private static PatientDemographics Mona(Guid? governorateId, Guid? cityId = null, Guid? districtId = null, Guid? areaId = null, string? city = "typed") =>
        new(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 5, 1), null, null, "01001234567", null, null, null, governorateId, city, null, null, cityId, districtId, areaId);

    [Fact]
    public async Task S_A2_chain_A_full_chain_passes_and_the_city_name_replaces_the_typed_city()
    {
        var resolved = await PatientLocationChain.ResolveAsync(Locations, Mona(Cairo, Nasr, Zone1, Block7), CancellationToken.None);
        Assert.Equal(("Nasr City", Nasr, Zone1, Block7), (resolved.City, resolved.CityId, resolved.DistrictId, resolved.AreaId));

        var cityOnly = await PatientLocationChain.ResolveAsync(Locations, Mona(Cairo, Nasr), CancellationToken.None);
        Assert.Equal("Nasr City", cityOnly.City);
    }

    [Fact]
    public async Task S_A2_free_text_Without_a_city_the_typed_city_stays_and_nothing_is_read()
    {
        var resolved = await PatientLocationChain.ResolveAsync(Locations, Mona(Cairo, city: "Helwan"), CancellationToken.None);
        Assert.Equal(("Helwan", (Guid?)null), (resolved.City, resolved.CityId));

        var emptyIds = await PatientLocationChain.ResolveAsync(Locations, Mona(Cairo, Guid.Empty, Guid.Empty, Guid.Empty, "Helwan"), CancellationToken.None);
        Assert.Equal("Helwan", emptyIds.City);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task S_A2_refused_A_broken_chain_is_refused_with_the_code_of_the_broken_level(Guid? governorateId, Guid? cityId, Guid? districtId, Guid? areaId, string code)
    {
        var refusal = await Assert.ThrowsAsync<DomainException>(() => PatientLocationChain.ResolveAsync(Locations, Mona(governorateId, cityId, districtId, areaId), CancellationToken.None));
        Assert.Equal((code, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    public static TheoryData<Guid?, Guid?, Guid?, Guid?, string> Refusals() => new()
    {
        { Giza, Nasr, null, null, PatientCodes.CityNotInGovernorate },            // the city belongs to Cairo
        { null, Nasr, null, null, PatientCodes.CityNotInGovernorate },            // a city needs its governorate
        { Cairo, Guid.CreateVersion7(), null, null, PatientCodes.CityNotInGovernorate },   // unknown city
        { Cairo, Nasr, Mesaha, null, PatientCodes.DistrictNotInCity },            // the district belongs to Dokki
        { Cairo, Nasr, Guid.CreateVersion7(), null, PatientCodes.DistrictNotInCity },
        { Cairo, null, Zone1, null, PatientCodes.DistrictNotInCity },             // a district needs its city
        { Cairo, Nasr, Zone1, Guid.CreateVersion7(), PatientCodes.AreaNotInDistrict },
        { Giza, Dokki, Mesaha, Block7, PatientCodes.AreaNotInDistrict },          // the area belongs to First Zone
        { Cairo, Nasr, null, Block7, PatientCodes.AreaNotInDistrict },            // an area needs its district
        { Cairo, null, null, Block7, PatientCodes.AreaNotInDistrict },
    };

    [Fact]
    public async Task S_A2_names_The_registers_get_the_names_of_every_level_and_nothing_for_a_file_without_a_location()
    {
        var located = Row(Nasr, Zone1, Block7);
        var cityOnly = Row(Dokki, null, null);
        var none = Row(null, null, null);
        var unknown = Row(Guid.CreateVersion7(), null, null);

        var names = await PatientLocationChain.NamesAsync(Locations, [located, cityOnly, none, unknown], CancellationToken.None);

        Assert.Equal(new PatientLocation(Nasr, "Nasr City", "مدينة نصر", Zone1, "First Zone", "المنطقة الأولى", Block7, "Block 7", "بلوك 7"), names[located.Id]);
        Assert.Equal(new PatientLocation(Dokki, "Dokki", "الدقي", null, null, null, null, null, null), names[cityOnly.Id]);
        Assert.False(names.ContainsKey(none.Id));
        Assert.Equal(new PatientLocation(unknown.CityId, null, null, null, null, null, null, null, null), names[unknown.Id]);   // a deleted row: the id stays, no name
        Assert.Equal([Nasr, Dokki, unknown.CityId!.Value], Locations.LastCityIds);   // one lookup per level for the whole page
    }

    private static PatientRow Row(Guid? cityId, Guid? districtId, Guid? areaId) => new(
        Guid.CreateVersion7(), "PAT-2026-00001", PatientTitle.Mr, "Omar", null, "omar", string.Empty, PatientGender.Male, new DateOnly(1990, 1, 1), null, null, null, null, new DateOnly(2026, 10, 1),
        null, null, null, null, null, Cairo, null, null, null, false, PatientStatus.Active, null, null, null, null, null, DateTimeOffset.UnixEpoch, null, null, null, 1, cityId, districtId, areaId, null);

    private sealed class FakeLocations(IReadOnlyDictionary<Guid, LocationNode> cities, IReadOnlyDictionary<Guid, LocationNode> districts, IReadOnlyDictionary<Guid, LocationNode> areas) : IPatientLocations
    {
        public IReadOnlyList<Guid> LastCityIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<Guid, LocationNode>> CitiesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            LastCityIds = [.. ids];
            return Task.FromResult(Pick(cities, ids));
        }

        public Task<IReadOnlyDictionary<Guid, LocationNode>> DistrictsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(Pick(districts, ids));

        public Task<IReadOnlyDictionary<Guid, LocationNode>> AreasAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(Pick(areas, ids));

        private static IReadOnlyDictionary<Guid, LocationNode> Pick(IReadOnlyDictionary<Guid, LocationNode> source, IReadOnlyCollection<Guid> ids) =>
            ids.Where(source.ContainsKey).Distinct().ToDictionary(id => id, id => source[id]);
    }
}
