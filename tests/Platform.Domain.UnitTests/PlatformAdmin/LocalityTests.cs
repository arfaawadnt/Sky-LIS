using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-008 C-04 (intake LIS-INT-PLT-001 §22, ORG-001 §17, ACC-001 §20): the locality of a laboratory, a wizard request
/// and a branch — city → district → area under the governorate, each optional, checked as a chain (GEO-020…022) — and the
/// address line of the public card and the clients register.
/// </summary>
public sealed class LocalityTests
{
    private static (Governorate Cairo, City Nasr, District Zone1, Area Block7) Chain()
    {
        var cairo = Cairo(Egypt());
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        var zone1 = District.Create(nasr, "First Zone", "المنطقة الأولى");
        var block7 = Area.Create(zone1, "Block 7", "بلوك 7");
        return (cairo, nasr, zone1, block7);
    }

    private static LocalityRows Rows(City? city, District? district, Area? area) => new(
        city is null ? null : new LocalityNode(city.Id, city.GovernorateId),
        district is null ? null : new LocalityNode(district.Id, district.CityId),
        area is null ? null : new LocalityNode(area.Id, area.DistrictId));

    [Fact]
    public void S_LCY_01_A_full_chain_under_the_governorate_resolves()
    {
        var (cairo, nasr, zone1, block7) = Chain();

        var locality = Locality.Resolve(cairo.Id, nasr.Id, zone1.Id, block7.Id, Rows(nasr, zone1, block7));

        Assert.Equal((cairo.Id, nasr.Id, zone1.Id, block7.Id), (locality.GovernorateId, locality.CityId, locality.DistrictId, locality.AreaId));
        Assert.False(locality.IsEmpty);
    }

    [Fact]
    public void S_LCY_02_Each_level_is_optional_from_the_bottom()
    {
        var (cairo, nasr, zone1, _) = Chain();

        var cityOnly = Locality.Resolve(cairo.Id, nasr.Id, null, null, Rows(nasr, null, null));
        Assert.Equal((nasr.Id, (Guid?)null), (cityOnly.CityId!.Value, cityOnly.DistrictId));
        Assert.Null(Locality.Resolve(cairo.Id, nasr.Id, zone1.Id, Guid.Empty, Rows(nasr, zone1, null)).AreaId);
        Assert.True(Locality.Resolve(cairo.Id, Guid.Empty, null, null, LocalityRows.None).IsEmpty);
        Assert.True(Locality.Resolve(null, null, null, null, LocalityRows.None).IsEmpty);
    }

    [Fact]
    public void S_LCY_03_A_city_of_another_governorate_or_without_one_is_GEO_020()
    {
        var (_, nasr, _, _) = Chain();
        var giza = Governorate.Create(Egypt(), "Giza", "الجيزة", null, null);

        var other = Assert.Throws<DomainException>(() => Locality.Resolve(giza.Id, nasr.Id, null, null, Rows(nasr, null, null)));
        var none = Assert.Throws<DomainException>(() => Locality.Resolve(null, nasr.Id, null, null, Rows(nasr, null, null)));
        var unknown = Assert.Throws<DomainException>(() => Locality.Resolve(nasr.GovernorateId, Guid.CreateVersion7(), null, null, LocalityRows.None));

        Assert.Equal((PlatformAdminCodes.LocalityCityNotInGovernorate, DomainFault.Validation), (other.Code, other.Fault));
        Assert.Equal(PlatformAdminCodes.LocalityCityNotInGovernorate, none.Code);
        Assert.Equal(PlatformAdminCodes.LocalityCityNotInGovernorate, unknown.Code);
    }

    [Fact]
    public void S_LCY_04_A_district_of_another_city_or_without_its_city_is_GEO_021()
    {
        var (cairo, nasr, zone1, _) = Chain();
        var heliopolis = City.Create(cairo, "Heliopolis", "مصر الجديدة");

        var other = Assert.Throws<DomainException>(() => Locality.Resolve(cairo.Id, heliopolis.Id, zone1.Id, null, Rows(heliopolis, zone1, null)));
        var orphan = Assert.Throws<DomainException>(() => Locality.Resolve(cairo.Id, null, zone1.Id, null, LocalityRows.None));

        Assert.Equal(PlatformAdminCodes.LocalityDistrictNotInCity, other.Code);
        Assert.Equal(PlatformAdminCodes.LocalityDistrictNotInCity, orphan.Code);
        Assert.NotEqual(nasr.Id, heliopolis.Id);
    }

    [Fact]
    public void S_LCY_05_An_area_of_another_district_or_without_its_district_is_GEO_022()
    {
        var (cairo, nasr, zone1, block7) = Chain();
        var zone2 = District.Create(nasr, "Second Zone", "المنطقة الثانية");

        var other = Assert.Throws<DomainException>(() => Locality.Resolve(cairo.Id, nasr.Id, zone2.Id, block7.Id, Rows(nasr, zone2, block7)));
        var orphan = Assert.Throws<DomainException>(() => Locality.Resolve(cairo.Id, nasr.Id, null, block7.Id, Rows(nasr, null, block7)));

        Assert.Equal(PlatformAdminCodes.LocalityAreaNotInDistrict, other.Code);
        Assert.Equal(PlatformAdminCodes.LocalityAreaNotInDistrict, orphan.Code);
        Assert.Equal(zone1.CityId, zone2.CityId);
    }

    [Fact]
    public void S_LCY_06_Inactive_rows_are_accepted_a_record_keeps_its_place_after_a_deactivation()
    {
        var (cairo, nasr, zone1, block7) = Chain();
        block7.Deactivate();

        Assert.Equal(block7.Id, Locality.Resolve(cairo.Id, nasr.Id, zone1.Id, block7.Id, Rows(nasr, zone1, block7)).AreaId);
    }

    [Fact]
    public void S_LCY_10_A_laboratory_is_placed_under_its_governorate_and_cleared_with_None()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        var tenant = Tenant.Provision(Profile(egypt, cairo), egypt, cairo, Starter(), "al-noor", TenantSource.Manual, "en", 14, Now);

        tenant.Locate(Locality.Resolve(cairo.Id, nasr.Id, null, null, Rows(nasr, null, null)));
        Assert.Equal((nasr.Id, (Guid?)null, (Guid?)null), (tenant.CityId!.Value, tenant.DistrictId, tenant.AreaId));

        tenant.Locate(Locality.None);
        Assert.Null(tenant.CityId);
    }

    [Fact]
    public void S_LCY_11_A_laboratory_refuses_a_locality_checked_against_another_governorate_GEO_020()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var giza = Governorate.Create(egypt, "Giza", "الجيزة", null, null);
        var dokki = City.Create(giza, "Dokki", "الدقي");
        var tenant = Tenant.Provision(Profile(egypt, cairo), egypt, cairo, Starter(), "al-noor", TenantSource.Manual, "en", 14, Now);

        var refusal = Assert.Throws<DomainException>(() => tenant.Locate(Locality.Resolve(giza.Id, dokki.Id, null, null, Rows(dokki, null, null))));

        Assert.Equal(PlatformAdminCodes.LocalityCityNotInGovernorate, refusal.Code);
        Assert.Null(tenant.CityId);
    }

    [Fact]
    public void S_LCY_12_A_wizard_request_takes_its_locality_while_pending_only()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        var nasr = City.Create(cairo, "Nasr City", "مدينة نصر");
        var request = SubscriptionRequest.Start(Profile(egypt, cairo), egypt, cairo, "en", Now);

        request.Locate(Locality.Resolve(cairo.Id, nasr.Id, null, null, Rows(nasr, null, null)));
        Assert.Equal(nasr.Id, request.CityId);

        request.MarkVerified(Now);
        Assert.Equal(PlatformAdminCodes.SubscriptionNotPending, Assert.Throws<InvalidStateTransitionException>(() => request.Locate(Locality.None)).Code);
    }

    [Fact]
    public void S_LCY_20_A_branch_is_placed_under_its_governorate_only()
    {
        var (cairo, nasr, zone1, _) = Chain();
        var placed = Branch.Create("NASR", new LocalizedText("Nasr", "نصر"), cairo.CountryId, cairo.Id, null, null, "EGP", FixedClock.Instant);
        placed.Locate(Locality.Resolve(cairo.Id, nasr.Id, zone1.Id, null, Rows(nasr, zone1, null)));
        Assert.Equal((nasr.Id, zone1.Id), (placed.CityId!.Value, placed.DistrictId!.Value));

        var noGovernorate = Branch.Create("FREE", new LocalizedText("Free", "حر"), null, null, null, null, "EGP", FixedClock.Instant);
        var refusal = Assert.Throws<DomainException>(() => noGovernorate.Locate(Locality.Resolve(cairo.Id, nasr.Id, null, null, Rows(nasr, null, null))));
        Assert.Equal(PlatformAdminCodes.LocalityCityNotInGovernorate, refusal.Code);

        noGovernorate.Locate(Locality.None);   // nothing picked: always fine
        Assert.Null(noGovernorate.CityId);
    }

    [Fact]
    public void S_LCY_30_The_address_line_reads_finest_first_and_only_with_a_city()
    {
        var full = AddressLine.Of(new LocalityNames("Block 7", "بلوك 7", "First Zone", "المنطقة الأولى", "Nasr City", "مدينة نصر", "Cairo", "القاهرة"));
        var cityOnly = AddressLine.Of(new LocalityNames(null, null, null, null, "Nasr City", "مدينة نصر", "Cairo", "القاهرة"));
        var none = AddressLine.Of(new LocalityNames(null, null, null, null, null, null, "Cairo", "القاهرة"));

        Assert.Equal(("Block 7, First Zone, Nasr City, Cairo", "بلوك 7، المنطقة الأولى، مدينة نصر، القاهرة"), full);
        Assert.Equal(("Nasr City, Cairo", "مدينة نصر، القاهرة"), cityOnly);
        Assert.Equal(((string?)null, (string?)null), none);
    }
}
