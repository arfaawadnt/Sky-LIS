using Platform.Domain.Tenancy;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Tenancy;

/// <summary>The results grid density preference of a member (results page, 2026-10-14), beside the language.</summary>
public sealed class MembershipPreferencesTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid User = Guid.CreateVersion7();

    private static TenantMembership Member() => TenantMembership.Create(Tenant, User, "E-1", new LocalizedText("Mona Adel", "منى عادل"), "en", FixedClock.Instant);

    [Fact]
    public void A_new_member_has_no_density_preference_and_may_choose_one()
    {
        var member = Member();

        Assert.Null(member.PreferredResultsDensity);
        member.SetPreferredResultsDensity(" Compact ");
        Assert.Equal("compact", member.PreferredResultsDensity);
        member.SetPreferredResultsDensity("comfortable");
        Assert.Equal("comfortable", member.PreferredResultsDensity);
    }

    [Fact]
    public void An_empty_value_returns_to_the_default()
    {
        var member = Member();
        member.SetPreferredResultsDensity("compact");

        member.SetPreferredResultsDensity(null);
        Assert.Null(member.PreferredResultsDensity);
        member.SetPreferredResultsDensity("compact");
        member.SetPreferredResultsDensity("  ");
        Assert.Null(member.PreferredResultsDensity);
    }

    [Fact]
    public void An_unknown_density_is_refused_with_its_code()
    {
        var member = Member();

        var refusal = Assert.Throws<DomainException>(() => member.SetPreferredResultsDensity("dense"));

        Assert.Equal(TenancyCodes.ResultsDensityInvalid, refusal.Code);
        Assert.Null(member.PreferredResultsDensity);
    }
}
