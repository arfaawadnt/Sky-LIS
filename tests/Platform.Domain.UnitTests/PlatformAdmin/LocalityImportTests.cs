using Platform.Domain.PlatformAdmin;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-008 C-04 (decision D-2; intake LIS-INT-PLT-001 §22): the locality import of one governorate plans the new cities,
/// districts and areas, matches existing names case-insensitively within their parent (idempotent), and refuses lines without a
/// city, areas without a district, over-long names and rows under an inactive parent.
/// </summary>
public sealed class LocalityImportTests
{
    private static LocalityImportRow Row(int line, string? city, string? district = null, string? area = null, string? cityAr = null) =>
        new(line, city, cityAr, district, null, area, null);

    [Fact]
    public void S_IMP_01_A_new_hierarchy_is_planned_once_per_name()
    {
        var cairo = Cairo(Egypt());

        var plan = LocalityImport.Plan(cairo, [], [], [], [
            Row(2, "Nasr City", "First Zone", "Block 7", "مدينة نصر"),
            Row(3, "nasr city ", "First Zone", "Block 8"),
            Row(4, "Heliopolis"),
        ]);

        Assert.True(plan.IsClean);
        Assert.Equal(new LocalityImportCount(2, 0, 0), plan.Cities);
        Assert.Equal(new LocalityImportCount(1, 0, 0), plan.Districts);
        Assert.Equal(new LocalityImportCount(2, 0, 0), plan.Areas);
        var nasr = plan.NewCities[0];
        Assert.Equal(("Nasr City", "مدينة نصر", cairo.Id), (nasr.Name.En, nasr.Name.Ar, nasr.GovernorateId));
        Assert.Equal("Heliopolis", plan.NewCities[1].Name.Ar);   // a blank Arabic name takes the English one
        Assert.All(plan.NewAreas, area => Assert.Equal(plan.NewDistricts[0].Id, area.DistrictId));
    }

    [Fact]
    public void S_IMP_02_Importing_the_same_file_again_creates_nothing()
    {
        var cairo = Cairo(Egypt());
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        var zone = District.Create(nasr, "First Zone", "المنطقة الأولى");
        var block = Area.Create(zone, "Block 7", "بلوك 7");

        var plan = LocalityImport.Plan(cairo, [nasr], [zone], [block], [Row(2, "NASR CITY", "first zone", "block 7"), Row(3, "Nasr City")]);

        Assert.True(plan.IsClean);
        Assert.Equal((new LocalityImportCount(0, 1, 0), new LocalityImportCount(0, 1, 0), new LocalityImportCount(0, 1, 0)), (plan.Cities, plan.Districts, plan.Areas));
        Assert.Empty(plan.NewCities);
    }

    [Fact]
    public void S_IMP_03_An_Arabic_only_line_takes_the_Arabic_name_for_both_languages()
    {
        var plan = LocalityImport.Plan(Cairo(Egypt()), [], [], [], [new LocalityImportRow(2, null, "مدينة نصر", null, "الحي الأول", null, null)]);

        Assert.Equal(("مدينة نصر", "مدينة نصر"), (plan.NewCities[0].Name.En, plan.NewCities[0].Name.Ar));
        Assert.Equal("الحي الأول", plan.NewDistricts[0].Name.En);
    }

    [Fact]
    public void S_IMP_04_Lines_without_a_city_areas_without_a_district_and_long_names_are_refused_GEO_025()
    {
        var plan = LocalityImport.Plan(Cairo(Egypt()), [], [], [], [
            Row(2, null, "Zone"),
            Row(3, "Nasr City", null, "Block 7"),
            Row(4, "Nasr City", new string('x', LocationRules.MaxNameLength + 1)),
            Row(5, "Maadi"),
        ]);

        Assert.False(plan.IsClean);
        Assert.Equal([(2, LocalityLevel.City), (3, LocalityLevel.Area), (4, LocalityLevel.District)], plan.Refusals.Select(refusal => (refusal.Line, refusal.Level)));
        Assert.All(plan.Refusals, refusal => Assert.Equal(PlatformAdminCodes.LocalityImportRowInvalid, refusal.Code));
        Assert.Equal((1, 1, 1), (plan.Cities.Refused, plan.Districts.Refused, plan.Areas.Refused));
    }

    [Fact]
    public void S_IMP_05_A_new_row_under_an_inactive_parent_is_refused_GEO_015()
    {
        var cairo = Cairo(Egypt());
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        nasr.Deactivate(hasActiveDistricts: false);

        var plan = LocalityImport.Plan(cairo, [nasr], [], [], [Row(2, "Nasr City"), Row(3, "Nasr City", "First Zone")]);

        var refusal = Assert.Single(plan.Refusals);
        Assert.Equal((3, LocalityLevel.District, PlatformAdminCodes.LocationParentInactive), (refusal.Line, refusal.Level, refusal.Code));
        Assert.Equal(1, plan.Cities.Existing);   // the inactive city is matched, never reactivated
    }

    [Fact]
    public void S_IMP_06_Cities_of_another_governorate_are_not_matched()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var giza = Governorate.Create(egypt, "Giza", "الجيزة", null, null);
        var gizaCity = City.Create(giza, "Nasr City", "مدينة نصر");

        var plan = LocalityImport.Plan(cairo, [gizaCity], [], [], [Row(2, "Nasr City")]);

        Assert.Equal(new LocalityImportCount(1, 0, 0), plan.Cities);
    }
}
