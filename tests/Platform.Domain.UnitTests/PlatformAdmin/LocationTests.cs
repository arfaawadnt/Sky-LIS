using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// The location hierarchy under a governorate (change request 2026-10-19, A2; intake LIS-INT-PLT-001 §17): cities,
/// districts and areas are created under an active parent only, renamed but never moved, deactivated only without
/// active children, reactivated only under an active parent.
/// </summary>
public sealed class LocationTests
{
    private static (Governorate Cairo, City Nasr, District Zone1, Area Block7) Chain()
    {
        var cairo = Cairo(Egypt());
        var nasr = City.Create(cairo, " Nasr City ", "مدينة نصر");
        var zone1 = District.Create(nasr, "First Zone", "المنطقة الأولى");
        var block7 = Area.Create(zone1, "Block 7", "بلوك 7");
        return (cairo, nasr, zone1, block7);
    }

    [Fact]
    public void S_LOC_create_A_city_a_district_and_an_area_are_created_active_under_their_parent_with_trimmed_names()
    {
        var (cairo, nasr, zone1, block7) = Chain();

        Assert.Equal((cairo.Id, "Nasr City", "مدينة نصر", true), (nasr.GovernorateId, nasr.Name.En, nasr.Name.Ar, nasr.IsActive));
        Assert.Equal((nasr.Id, "First Zone", true), (zone1.CityId, zone1.Name.En, zone1.IsActive));
        Assert.Equal((zone1.Id, "Block 7", true), (block7.DistrictId, block7.Name.En, block7.IsActive));
        Assert.Empty(nasr.DomainEvents);   // master data: no events, like the governorate
    }

    [Fact]
    public void S_LOC_parent_inactive_Nothing_is_created_under_an_inactive_parent_GEO_015()
    {
        var (cairo, nasr, zone1, _) = Chain();
        cairo.Deactivate();
        Assert.Equal(PlatformAdminCodes.LocationParentInactive, Assert.Throws<DomainException>(() => City.Create(cairo, "Maadi", "المعادي")).Code);

        nasr.Deactivate(hasActiveDistricts: false);
        Assert.Equal(PlatformAdminCodes.LocationParentInactive, Assert.Throws<DomainException>(() => District.Create(nasr, "Second Zone", "المنطقة الثانية")).Code);

        zone1.Deactivate(hasActiveAreas: false);
        var refusal = Assert.Throws<DomainException>(() => Area.Create(zone1, "Block 8", "بلوك 8"));
        Assert.Equal((PlatformAdminCodes.LocationParentInactive, DomainFault.Conflict), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData("", "مدينة")]
    [InlineData("Maadi", "")]
    [InlineData("   ", "المعادي")]
    public void S_LOC_name_Both_names_are_required_GEO_006(string en, string ar)
    {
        var cairo = Cairo(Egypt());
        Assert.Equal(PlatformAdminCodes.GeographyTextInvalid, Assert.Throws<DomainException>(() => City.Create(cairo, en, ar)).Code);
    }

    [Fact]
    public void S_LOC_name_A_name_longer_than_100_characters_is_refused_GEO_006()
    {
        var cairo = Cairo(Egypt());
        var tooLong = new string('x', LocationRules.MaxNameLength + 1);
        Assert.Equal(PlatformAdminCodes.GeographyTextInvalid, Assert.Throws<DomainException>(() => City.Create(cairo, tooLong, "مدينة")).Code);
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        Assert.Equal(PlatformAdminCodes.GeographyTextInvalid, Assert.Throws<DomainException>(() => nasr.Rename("Nasr", tooLong)).Code);
        Assert.Equal("Nasr City", nasr.Name.En);   // a refused rename changes nothing
    }

    [Fact]
    public void S_LOC_rename_A_row_is_renamed_but_never_moves()
    {
        var (cairo, nasr, zone1, block7) = Chain();
        nasr.Rename(" Madinet Nasr ", "مدينة نصر");
        zone1.Rename("Zone 1", "المنطقة 1");
        block7.Rename("Block Seven", "بلوك سبعة");
        Assert.Equal((cairo.Id, "Madinet Nasr"), (nasr.GovernorateId, nasr.Name.En));
        Assert.Equal((nasr.Id, "Zone 1"), (zone1.CityId, zone1.Name.En));
        Assert.Equal((zone1.Id, "Block Seven"), (block7.DistrictId, block7.Name.En));
    }

    [Fact]
    public void S_LOC_deactivate_A_parent_with_active_children_is_not_deactivated_GEO_112()
    {
        var (_, nasr, zone1, block7) = Chain();

        var city = Assert.Throws<DomainException>(() => nasr.Deactivate(hasActiveDistricts: true));
        Assert.Equal((PlatformAdminCodes.LocationHasActiveChildren, DomainFault.Conflict), (city.Code, city.Fault));
        Assert.True(nasr.IsActive);
        Assert.Equal(PlatformAdminCodes.LocationHasActiveChildren, Assert.Throws<DomainException>(() => zone1.Deactivate(hasActiveAreas: true)).Code);

        block7.Deactivate();
        zone1.Deactivate(hasActiveAreas: false);
        nasr.Deactivate(hasActiveDistricts: false);
        Assert.False(block7.IsActive);
        Assert.False(zone1.IsActive);
        Assert.False(nasr.IsActive);
    }

    [Fact]
    public void S_LOC_reactivate_A_row_comes_back_only_under_an_active_parent_GEO_015()
    {
        var (cairo, nasr, zone1, block7) = Chain();
        block7.Deactivate();
        zone1.Deactivate(hasActiveAreas: false);
        nasr.Deactivate(hasActiveDistricts: false);

        Assert.Equal(PlatformAdminCodes.LocationParentInactive, Assert.Throws<DomainException>(() => zone1.Reactivate(nasr)).Code);
        Assert.Equal(PlatformAdminCodes.LocationParentInactive, Assert.Throws<DomainException>(() => block7.Reactivate(zone1)).Code);

        nasr.Reactivate(cairo);
        zone1.Reactivate(nasr);
        block7.Reactivate(zone1);
        Assert.True(nasr.IsActive && zone1.IsActive && block7.IsActive);

        cairo.Deactivate();
        nasr.Deactivate(hasActiveDistricts: false);
        Assert.Equal(PlatformAdminCodes.LocationParentInactive, Assert.Throws<DomainException>(() => nasr.Reactivate(cairo)).Code);
    }
}
