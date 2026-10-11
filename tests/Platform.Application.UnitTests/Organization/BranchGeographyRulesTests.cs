using Platform.Application.Organization;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Organization;

/// <summary>UAT B-02: a branch's governorate exists and belongs to the branch's country (ORG-017, a field refusal).</summary>
public sealed class BranchGeographyRulesTests
{
    private static readonly Guid Egypt = Guid.CreateVersion7();
    private static readonly Guid Elsewhere = Guid.CreateVersion7();
    private static readonly Guid Alexandria = Guid.CreateVersion7();

    private sealed class Geography : IBranchGeography
    {
        public Task<Guid?> CountryOfGovernorateAsync(Guid governorateId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(governorateId == Alexandria ? Egypt : null);
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
}
