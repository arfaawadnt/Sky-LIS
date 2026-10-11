using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>Countries, governorates and plans (SA-10, SA-11, SA-05; INV-PLT-004, INV-PLT-011, INV-PLT-020).</summary>
public sealed class ReferenceDataTests
{
    [Fact]
    public void A_country_is_created_with_normalised_codes()
    {
        var egypt = Egypt();
        Assert.Equal(("EG", "+20", "EGP", "ar", 10), (egypt.IsoCode, egypt.DialCode, egypt.CurrencyCode, egypt.Language, egypt.MobileLength));
        Assert.True(egypt.IsActive);
    }

    [Theory]
    [InlineData("01012345678", "+201012345678")]
    [InlineData("1012345678", "+201012345678")]
    [InlineData("+20 101 234 5678", "+201012345678")]
    [InlineData("0020-101-234-5678", "+201012345678")]
    [InlineData("(010) 1234 5678", "+201012345678")]
    public void INV_PLT_004_Egyptian_mobiles_normalise_to_E164(string input, string expected)
    {
        Assert.Equal(expected, Egypt().NormaliseMobile(input));
    }

    [Theory]
    [InlineData("0101234567")]          // nine national digits
    [InlineData("010123456789")]        // eleven national digits
    [InlineData("+966512345678")]       // another country's dial code
    [InlineData("0012345678")]          // national number starting with 0
    [InlineData("01012345678x")]        // letters
    [InlineData("")]
    public void INV_PLT_004_a_mobile_of_the_wrong_length_or_country_is_refused_GEO_010(string input)
    {
        var refusal = Assert.Throws<DomainException>(() => Egypt().NormaliseMobile(input));
        Assert.Equal(PlatformAdminCodes.MobileInvalidForCountry, refusal.Code);
    }

    [Fact]
    public void C_03_a_client_profile_names_the_mobile_it_refuses_GEO_010_and_GEO_014()
    {
        var egypt = Egypt();
        var laboratory = Assert.Throws<DomainException>(() => ClientProfile.Create("Lab", "معمل", egypt, Guid.CreateVersion7(), "Street", "0233351111", null, "Dr. Mona", "01012345678", null));
        Assert.Equal(PlatformAdminCodes.MobileInvalidForCountry, laboratory.Code);
        Assert.StartsWith("The laboratory mobile", laboratory.Message, StringComparison.Ordinal);

        var manager = Assert.Throws<DomainException>(() => ClientProfile.Create("Lab", "معمل", egypt, Guid.CreateVersion7(), "Street", "01012345678", null, "Dr. Mona", "+2010111100", null));
        Assert.Equal(PlatformAdminCodes.ManagerMobileInvalidForCountry, manager.Code);
        Assert.StartsWith("The manager's mobile", manager.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kuwaiti_mobiles_have_eight_digits()
    {
        Assert.Equal("+96551234567", Kuwait().NormaliseMobile("51234567"));
        Assert.Throws<DomainException>(() => Kuwait().NormaliseMobile("0512345678"));
    }

    [Theory]
    [InlineData("EGY", "+20", "EGP", "ar", 10, PlatformAdminCodes.IsoCodeInvalid)]
    [InlineData("EG", "20", "EGP", "ar", 10, PlatformAdminCodes.DialCodeInvalid)]
    [InlineData("EG", "+012", "EGP", "ar", 10, PlatformAdminCodes.DialCodeInvalid)]
    [InlineData("EG", "+20", "EG", "ar", 10, PlatformAdminCodes.CurrencyInvalid)]
    [InlineData("EG", "+20", "EGP", "fr", 10, PlatformAdminCodes.LanguageInvalid)]
    [InlineData("EG", "+20", "EGP", "ar", 5, PlatformAdminCodes.MobileLengthInvalid)]
    public void Invalid_country_data_is_refused_with_its_code(string iso, string dial, string currencyCode, string language, int mobileLength, string code)
    {
        var refusal = Assert.Throws<DomainException>(() => Country.Create(new CountryData("Egypt", "مصر", iso, dial, "Pound", currencyCode, language, "Africa/Cairo", mobileLength, null, null)));
        Assert.Equal(code, refusal.Code);
    }

    [Theory]
    [InlineData("Mars/Base")]               // validation 2026-10-11 C-08: was saved
    [InlineData("Egypt Standard Time")]     // a Windows name, not IANA
    [InlineData("Cairo")]
    [InlineData("   ")]
    public void C_08_a_time_zone_that_is_not_an_IANA_name_is_refused_GEO_007(string timeZone)
    {
        var refusal = Assert.Throws<DomainException>(() => Country.Create(new CountryData("Egypt", "مصر", "EG", "+20", "Pound", "EGP", "ar", timeZone, 10, null, null)));
        Assert.Equal(PlatformAdminCodes.TimeZoneInvalid, refusal.Code);
        Assert.Throws<DomainException>(() => Egypt().Update(new CountryData("Egypt", "مصر", "EG", "+20", "Pound", "EGP", "ar", timeZone, 10, null, null)));
    }

    [Theory]
    [InlineData("Africa/Cairo", "Africa/Cairo")]
    [InlineData("africa/cairo", "Africa/Cairo")]
    [InlineData("Asia/Kuwait", "Asia/Kuwait")]
    [InlineData("America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("UTC", "UTC")]
    public void C_08_IANA_time_zones_are_accepted_in_their_canonical_spelling(string timeZone, string stored)
    {
        Assert.Equal(stored, Country.Create(new CountryData("Egypt", "مصر", "EG", "+20", "Pound", "EGP", "ar", timeZone, 10, null, null)).TimeZone);
    }

    [Fact]
    public void Update_keeps_the_ISO_code()
    {
        var egypt = Egypt();
        egypt.Update(new CountryData("Arab Republic of Egypt", "جمهورية مصر العربية", "XX", "+20", "Pound", "EGP", "en", "Africa/Cairo", 10, null, "note"));
        Assert.Equal(("EG", "Arab Republic of Egypt", "en", "note"), (egypt.IsoCode, egypt.Name.En, egypt.Language, egypt.Remarks));
    }

    [Fact]
    public void A_governorate_needs_an_active_country_GEO_011()
    {
        var egypt = Egypt();
        egypt.Deactivate();
        Assert.Equal(PlatformAdminCodes.CountryInactive, Assert.Throws<DomainException>(() => Governorate.Create(egypt, "Cairo", "القاهرة", null, null)).Code);
    }

    [Fact]
    public void A_governorate_is_edited_but_never_moves()
    {
        var egypt = Egypt();
        var cairo = Cairo(egypt);
        cairo.Update(" Cairo ", "القاهرة", null, "capital");
        Assert.Equal((egypt.Id, "Cairo", null, "capital"), (cairo.CountryId, cairo.Name.En, cairo.District, cairo.Remarks));
        Assert.Equal(PlatformAdminCodes.GeographyTextInvalid, Assert.Throws<DomainException>(() => cairo.Update("", "القاهرة", null, null)).Code);
    }

    [Fact]
    public void A_plan_code_is_upper_case_and_limits_are_checked_PLN_001_PLN_002()
    {
        var plan = Starter();
        Assert.Equal(("STARTER", "EGP", 1500m), (plan.Code, plan.CurrencyCode, plan.MonthlyFee));

        Assert.Equal(PlatformAdminCodes.PlanCodeInvalid, Assert.Throws<DomainException>(() => Plan.CleanCode("x")).Code);
        var noBranch = new PlanTerms(new LocalizedText("Zero", "صفر"), 0m, "EGP", 0, 1, 0, 0);
        Assert.Equal(PlatformAdminCodes.PlanLimitsInvalid, Assert.Throws<DomainException>(() => Plan.Create("ZERO", noBranch)).Code);
        var negativeFee = new PlanTerms(new LocalizedText("Neg", "سالب"), -1m, "EGP", 1, 1, 0, 0);
        Assert.Equal(PlatformAdminCodes.PlanLimitsInvalid, Assert.Throws<DomainException>(() => Plan.Create("NEG", negativeFee)).Code);
    }

    [Fact]
    public void A_plan_fee_is_rounded_to_two_decimals()
    {
        var plan = Starter();
        plan.Update(new PlanTerms(new LocalizedText("Starter", "الأساسية"), 1499.995m, "egp", 2, 6, 600, 2));
        Assert.Equal((1500.00m, 2, 6), (plan.MonthlyFee, plan.MaxBranches, plan.MaxUsers));
    }
}
