using Platform.Application.Organization;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Organization;

/// <summary>UAT B-02: a branch's governorate exists and belongs to the branch's country (ORG-017, a field refusal).</summary>
public sealed class BranchGeographyRulesTests
{
    private static readonly Guid Egypt = Guid.CreateVersion7();
    private static readonly Guid Elsewhere = Guid.CreateVersion7();
    private static readonly Guid Alexandria = Guid.CreateVersion7();

    private static readonly Guid Montaza = Guid.CreateVersion7();
    private static readonly Guid Sidi = Guid.CreateVersion7();
    private static readonly Guid Village = Guid.CreateVersion7();

    private sealed class Geography : IBranchGeography
    {
        public int LocalityReads { get; private set; }

        public Task<Guid?> CountryOfGovernorateAsync(Guid governorateId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(governorateId == Alexandria ? Egypt : null);

        // LIS-CR-008 C-04: Montaza (city) of Alexandria → Sidi (district) → Village (area)
        public Task<LocalityRows> LocalityRowsAsync(Guid? cityId, Guid? districtId, Guid? areaId, CancellationToken cancellationToken)
        {
            LocalityReads++;
            return Task.FromResult(new LocalityRows(
                cityId == Montaza ? new LocalityNode(Montaza, Alexandria) : null,
                districtId == Sidi ? new LocalityNode(Sidi, Montaza) : null,
                areaId == Village ? new LocalityNode(Village, Sidi) : null));
        }
    }

    [Fact]
    public async Task A_governorate_of_the_chosen_country_passes()
    {
        await OrganizationGuards.EnsureGovernorateInCountryAsync(new Geography(), Egypt, Alexandria, CancellationToken.None);
    }

    [Fact]
    public async Task No_governorate_needs_no_check()
    {
        await OrganizationGuards.EnsureGovernorateInCountryAsync(new Geography(), Elsewhere, null, CancellationToken.None);
    }

    [Fact]
    public async Task A_governorate_of_another_country_is_ORG_017()
    {
        var refusal = await Assert.ThrowsAsync<DomainException>(() => OrganizationGuards.EnsureGovernorateInCountryAsync(new Geography(), Elsewhere, Alexandria, CancellationToken.None));

        Assert.Equal((OrganizationCodes.GovernorateNotInCountry, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public async Task An_unknown_governorate_is_ORG_017()
    {
        var refusal = await Assert.ThrowsAsync<DomainException>(() => OrganizationGuards.EnsureGovernorateInCountryAsync(new Geography(), Egypt, Guid.CreateVersion7(), CancellationToken.None));

        Assert.Equal(OrganizationCodes.GovernorateNotInCountry, refusal.Code);
    }

    [Fact]
    public async Task A_branch_locality_of_its_governorate_resolves()
    {
        var locality = await OrganizationGuards.ResolveLocalityAsync(new Geography(), Alexandria, Montaza, Sidi, Village, CancellationToken.None);

        Assert.Equal((Alexandria, Montaza, Sidi, Village), (locality.GovernorateId!.Value, locality.CityId!.Value, locality.DistrictId!.Value, locality.AreaId!.Value));
    }

    [Fact]
    public async Task No_locality_reads_nothing()
    {
        var geography = new Geography();
        var locality = await OrganizationGuards.ResolveLocalityAsync(geography, Alexandria, null, Guid.Empty, null, CancellationToken.None);

        Assert.True(locality.IsEmpty);
        Assert.Equal(0, geography.LocalityReads);
    }

    [Fact]
    public async Task A_city_on_a_branch_without_a_governorate_is_GEO_020()
    {
        var refusal = await Assert.ThrowsAsync<DomainException>(() => OrganizationGuards.ResolveLocalityAsync(new Geography(), null, Montaza, null, null, CancellationToken.None));

        Assert.Equal((PlatformAdminCodes.LocalityCityNotInGovernorate, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public async Task An_unknown_area_is_GEO_022()
    {
        var refusal = await Assert.ThrowsAsync<DomainException>(() => OrganizationGuards.ResolveLocalityAsync(new Geography(), Alexandria, Montaza, Sidi, Guid.CreateVersion7(), CancellationToken.None));

        Assert.Equal(PlatformAdminCodes.LocalityAreaNotInDistrict, refusal.Code);
    }
}
