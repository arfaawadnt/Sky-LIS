using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Organization;

/// <summary>
/// The three special rights added by the change request of 2026-10-13: the demographics edit limit by test status
/// (item 1), the branch collections view (item 2) and the report days limit (item 3).
/// </summary>
public sealed class SpecialRightsLimitsTests
{
    [Fact]
    public void None_locks_everything_and_All_opens_everything()
    {
        Assert.Equal(DemographicsEditLimit.Ordered, SpecialRights.None.DemographicsEditUpTo);
        Assert.False(SpecialRights.None.CanViewBranchCollections);
        Assert.Equal(0, SpecialRights.None.ReportDaysLimit);

        Assert.Equal(DemographicsEditLimit.Printed, SpecialRights.All.DemographicsEditUpTo);
        Assert.True(SpecialRights.All.CanViewBranchCollections);
        Assert.Null(SpecialRights.All.ReportDaysLimit);
    }

    [Fact]
    public void The_short_constructor_keeps_the_values_existing_roles_received_from_the_migration()
    {
        var rights = new SpecialRights(null, true, false, false, false, false, false, false, false);

        Assert.Equal(SpecialRights.DefaultDemographicsEditUpTo, rights.DemographicsEditUpTo);
        Assert.Equal(DemographicsEditLimit.Complete, rights.DemographicsEditUpTo);
        Assert.False(rights.CanViewBranchCollections);
        Assert.Null(rights.ReportDaysLimit);
    }

    [Theory]
    [InlineData(DemographicsEditLimit.Ordered, DemographicsEditLimit.Ordered, true)]
    [InlineData(DemographicsEditLimit.Ordered, DemographicsEditLimit.Complete, false)]
    [InlineData(DemographicsEditLimit.Complete, DemographicsEditLimit.Complete, true)]
    [InlineData(DemographicsEditLimit.Complete, DemographicsEditLimit.Reviewed, false)]
    [InlineData(DemographicsEditLimit.Verified, DemographicsEditLimit.Verified, true)]
    [InlineData(DemographicsEditLimit.Verified, DemographicsEditLimit.Printed, false)]
    [InlineData(DemographicsEditLimit.Printed, DemographicsEditLimit.Printed, true)]
    public void Demographics_may_change_while_the_highest_result_status_is_within_the_limit(DemographicsEditLimit limit, DemographicsEditLimit highest, bool allowed)
    {
        var rights = new SpecialRights(null, false, false, false, false, false, false, false, false, limit, false, null);

        Assert.Equal(allowed, rights.AllowsDemographicsEditAt(highest));
    }

    [Fact]
    public void The_report_days_limit_bounds_the_earliest_day()
    {
        var today = new DateOnly(2026, 10, 13);

        Assert.Equal(today, new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Complete, false, 0).EarliestReportDay(today));
        Assert.Equal(new DateOnly(2026, 10, 6), new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Complete, false, 7).EarliestReportDay(today));
        Assert.Null(SpecialRights.All.EarliestReportDay(today));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public void A_report_days_limit_outside_the_range_is_refused(int days)
    {
        var refusal = Assert.Throws<DomainException>(() => new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Complete, false, days));

        Assert.Equal(OrganizationCodes.ReportDaysLimitInvalid, refusal.Code);
    }

    [Fact]
    public void An_unknown_edit_limit_is_refused()
    {
        var refusal = Assert.Throws<DomainException>(() => new SpecialRights(null, false, false, false, false, false, false, false, false, (DemographicsEditLimit)99, false, null));

        Assert.Equal(OrganizationCodes.DemographicsEditLimitInvalid, refusal.Code);
    }

    [Fact]
    public void Equality_and_copies_include_the_new_rights()
    {
        var a = new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Verified, true, 30);
        var same = new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Verified, true, 30);
        var other = new SpecialRights(null, false, false, false, false, false, false, false, false, DemographicsEditLimit.Verified, true, 31);

        Assert.Equal(a, same);
        Assert.NotEqual(a, other);
        Assert.Equal(a, a.Copy());
        Assert.NotSame(a, a.Copy());
    }
}
