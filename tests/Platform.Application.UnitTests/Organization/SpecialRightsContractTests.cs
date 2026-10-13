using Platform.Application.Organization;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Organization;

/// <summary>The contract of the three special rights of 2026-10-13 round-trips, with the defaults a client that predates them still sends.</summary>
public sealed class SpecialRightsContractTests
{
    [Fact]
    public void The_dto_round_trips_the_new_rights()
    {
        var dto = new SpecialRightsDto(15, true, false, true, false, true, false, true, false, "verified", true, 14);

        var domain = OrganizationGuards.ToDomain(dto);
        var back = OrganizationGuards.ToDto(domain);

        Assert.Equal((DemographicsEditLimit.Verified, true, 14), (domain.DemographicsEditUpTo, domain.CanViewBranchCollections, domain.ReportDaysLimit));
        Assert.Equal(("Verified", true, 14), (back.DemographicsEditUpTo, back.CanViewBranchCollections, back.ReportDaysLimit));
    }

    [Fact]
    public void A_client_that_omits_the_new_rights_gets_the_migration_defaults()
    {
        var dto = new SpecialRightsDto(null, true, true, false, false, false, false, false, false);

        var domain = OrganizationGuards.ToDomain(dto);

        Assert.Equal(SpecialRights.DefaultDemographicsEditUpTo, domain.DemographicsEditUpTo);
        Assert.False(domain.CanViewBranchCollections);
        Assert.Null(domain.ReportDaysLimit);
    }

    [Fact]
    public void An_unknown_edit_limit_name_is_refused_with_its_code()
    {
        var dto = new SpecialRightsDto(null, false, false, false, false, false, false, false, false, "Signed");

        var refusal = Assert.Throws<DomainException>(() => OrganizationGuards.ToDomain(dto));

        Assert.Equal(OrganizationCodes.DemographicsEditLimitInvalid, refusal.Code);
    }
}
