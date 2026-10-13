using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Settings;

/// <summary>SRS CP-57 / SA-19: the parameter catalogue (intake LIS-INT-FND-001, capability 6).</summary>
public sealed class SettingKeysTests
{
    [Fact]
    public void The_catalogue_holds_the_twenty_six_keys_with_unique_lower_camel_names_and_valid_defaults()
    {
        Assert.Equal(26, SettingKeys.All.Count);
        Assert.Equal(SettingKeys.All.Count, SettingKeys.All.Select(descriptor => descriptor.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SettingKeys.All, descriptor =>
        {
            Assert.True(char.IsAsciiLetterLower(descriptor.Key[0]), descriptor.Key);
            Assert.True(descriptor.IsValid(descriptor.DefaultValue), descriptor.Key);
            Assert.Same(descriptor, SettingKeys.Find(descriptor.Key));
        });
    }

    [Fact]
    public void The_defaults_are_the_specified_ones()
    {
        Assert.False(SettingKeys.MandatoryMobile.Default);
        Assert.True(SettingKeys.SimilarNameWarning.Default);
        Assert.Equal(20, SettingKeys.DiscountCapPercent.Default);
        Assert.Equal("ACC-{yyyy}-{nnnnn}", SettingKeys.AccessionFormat.Default);
        Assert.Equal(1, SettingKeys.LabelCopies.Default);
        Assert.Equal("en", SettingKeys.ResultLanguage.Default);
        Assert.True(SettingKeys.AutoReceive.Default);
        Assert.Equal(80, SettingKeys.DelayWarnPercent.Default);
        Assert.Equal(100, SettingKeys.DelayLatePercent.Default);
        Assert.Equal(30, SettingKeys.SessionTimeoutMinutes.Default);
        Assert.Equal(6, SettingKeys.OtpLength.Default);
        Assert.Equal(8, SettingKeys.PasswordMinLength.Default);
        Assert.Equal(14, SettingKeys.TrialDays.Default);
        Assert.Equal("EGP", SettingKeys.DefaultCurrency.Default);
        Assert.Equal("propose", SettingKeys.MasterDataUpdatePolicy.Default);
        Assert.Equal("Africa/Cairo", SettingKeys.TimeZone.Default);
    }

    [Fact]
    public void Values_parse_by_type_and_fall_back_to_the_default_when_malformed_or_out_of_range()
    {
        Assert.True(SettingKeys.MandatoryMobile.Parse("true"));
        Assert.False(SettingKeys.MandatoryMobile.Parse("yes"));          // malformed → default (false)
        Assert.Equal(35, SettingKeys.DiscountCapPercent.Parse(" 35 "));
        Assert.Equal(20, SettingKeys.DiscountCapPercent.Parse("150"));    // out of range → default
        Assert.Equal("ar", SettingKeys.ResultLanguage.Parse("ar"));
        Assert.Equal("en", SettingKeys.ResultLanguage.Parse("fr"));       // outside the closed set → default
        Assert.Equal("propose", SettingKeys.MasterDataUpdatePolicy.Parse(null));
    }

    [Fact]
    public void Resolve_takes_the_tenant_value_else_the_inherited_one_else_the_default_F_01()
    {
        Assert.Equal(5, SettingKeys.SessionTimeoutMinutes.Resolve("5", "10"));
        Assert.Equal(10, SettingKeys.SessionTimeoutMinutes.Resolve(null, "10"));
        Assert.Equal(10, SettingKeys.SessionTimeoutMinutes.Resolve("0", " 10 "));     // out of range → the inherited value
        Assert.Equal(30, SettingKeys.SessionTimeoutMinutes.Resolve("abc", "721"));   // neither valid → the default
        Assert.Equal(30, SettingKeys.SessionTimeoutMinutes.Resolve(null, null));
    }

    [Fact]
    public void Validation_refuses_an_unknown_key_and_an_invalid_value_with_their_codes()
    {
        var unknown = Assert.Throws<DomainException>(() => SettingKeys.Validate("noSuchKey", "1"));
        Assert.Equal(SettingCodes.UnknownKey, unknown.Code);
        Assert.Equal(DomainFault.Validation, unknown.Fault);

        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("labelCopies", "0")).Code);
        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("autoCollect", "maybe")).Code);
        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("masterDataUpdatePolicy", "Push")).Code);
        Assert.Same(SettingKeys.LabelCopies, SettingKeys.Validate("labelCopies", "3"));
    }

    [Fact]
    public void The_time_zone_accepts_an_IANA_zone_this_host_knows_and_nothing_else()
    {
        // Round 2 R2-01: the zone of a laboratory without a client record.
        Assert.Same(SettingKeys.TimeZone, SettingKeys.Validate("timeZone", "Asia/Riyadh"));
        Assert.Equal("Asia/Riyadh", SettingKeys.TimeZone.Parse(" Asia/Riyadh "));
        Assert.Equal(SettingType.String, SettingKeys.TimeZone.Type);
        foreach (var malformed in new[] { "", "   ", new string('A', 65) })
        {
            Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("timeZone", malformed)).Code);
        }

        // LIS-VR-002 round 4: a name the host does not know says so (SET-003), so the field can read "This time zone is not known."
        foreach (var unknown in new[] { "Mars/Olympus", "Cairo", " Mars/Olympus " })
        {
            var refusal = Assert.Throws<DomainException>(() => SettingKeys.Validate("timeZone", unknown));
            Assert.Equal(SettingCodes.UnknownTimeZone, refusal.Code);
            Assert.Equal(DomainFault.Validation, refusal.Fault);
        }

        // only a time-zone setting has that reason
        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("defaultLanguage", "Mars/Olympus")).Code);
        Assert.True(SettingKeys.TimeZone.IsTimeZone);
        Assert.Single(SettingKeys.All, descriptor => descriptor.IsTimeZone);

        Assert.Equal("Africa/Cairo", SettingKeys.TimeZone.Parse("Mars/Olympus"));   // a bad row never breaks a module
    }
}
