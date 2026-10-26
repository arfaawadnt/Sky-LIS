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

/// <summary>LIS-CR-014 T2: the column chooser's list views saved on the membership (TEN-015, 4 KB).</summary>
public sealed class MembershipListPreferencesTests
{
    private static TenantMembership Member() => TenantMembership.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "E-2", new LocalizedText("Omar Said", "عمر سعيد"), "en", FixedClock.Instant);

    [Fact]
    public void A_member_hides_columns_per_list_and_an_empty_set_returns_the_list_to_its_defaults()
    {
        var member = Member();
        Assert.Null(member.ListPreferences);

        member.SetListPreference("visits", ["payer", " branch ", "payer"]);
        member.SetListPreference("tracker", ["timeLeft"]);

        Assert.Equal(["payer", "branch"], member.HiddenColumnsOf("visits"));
        Assert.Equal(["timeLeft"], member.HiddenColumnsOf("tracker"));
        Assert.Equal("""{"tracker":{"hidden":["timeLeft"]},"visits":{"hidden":["payer","branch"]}}""", member.ListPreferences);

        member.SetListPreference("visits", []);
        Assert.Empty(member.HiddenColumnsOf("visits"));
        member.SetListPreference("tracker", []);
        Assert.Null(member.ListPreferences);
    }

    [Theory]
    [InlineData("", "payer")]
    [InlineData("visits list", "payer")]
    [InlineData("visits", "")]
    [InlineData("visits", "<script>")]
    [InlineData("-visits", "payer")]
    public void A_key_that_is_not_an_identifier_is_refused_with_TEN_015(string listKey, string column)
    {
        var member = Member();

        var refusal = Assert.Throws<DomainException>(() => member.SetListPreference(listKey, [column]));

        Assert.Equal(TenancyCodes.ListPreferencesInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
        Assert.Null(member.ListPreferences);
    }

    [Fact]
    public void Too_many_columns_or_a_document_beyond_4_KB_is_refused_and_the_saved_view_stays()
    {
        var member = Member();
        member.SetListPreference("visits", ["payer"]);

        var tooMany = Assert.Throws<DomainException>(() => member.SetListPreference("tracker", Enumerable.Range(0, ListPreferenceDocument.MaxHiddenColumns + 1).Select(i => $"c{i}")));
        Assert.Equal(TenancyCodes.ListPreferencesInvalid, tooMany.Code);

        var longKey = new string('k', ListPreferenceDocument.MaxKeyLength - 3);
        var tooLarge = Assert.Throws<DomainException>(() =>
        {
            for (var list = 0; list < 10; list++)
            {
                member.SetListPreference($"list{list}", Enumerable.Range(0, 10).Select(i => $"{longKey}{i:D2}"));
            }
        });
        Assert.Equal(TenancyCodes.ListPreferencesInvalid, tooLarge.Code);
        Assert.True(member.ListPreferences!.Length <= ListPreferenceDocument.MaxLength);
        Assert.Equal(["payer"], member.HiddenColumnsOf("visits"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"visits":{"hidden":"payer"},"bad key":{"hidden":["x"]},"tracker":{"hidden":[1,"timeLeft","no way"]}}""")]
    public void A_stored_value_that_is_not_a_document_reads_tolerantly(string stored)
    {
        var lists = ListPreferenceDocument.Parse(stored);

        Assert.DoesNotContain("visits", lists.Keys);
        Assert.DoesNotContain("bad key", lists.Keys);
        if (lists.TryGetValue("tracker", out var tracker))
        {
            Assert.Equal(["timeLeft"], tracker);
        }

        Assert.Equal("""{"visits":{"hidden":["payer"]}}""", ListPreferenceDocument.With(stored, "visits", ["payer"])?.Replace("""{"tracker":{"hidden":["timeLeft"]},""", "{", StringComparison.Ordinal));
    }
}
