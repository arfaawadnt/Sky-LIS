using Platform.Domain.Patients;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>Acceptance scenarios of intake LIS-INT-PAT-001 §16 for the patient file: one test per transition, refused precondition and invariant (03 §10).</summary>
public sealed class PatientTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;   // 2026-10-09
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static PatientDemographics Mona(
        DateOnly? dateOfBirth = null,
        PatientAge? age = null,
        string? mobile = "0100 123 4567",
        string? nationalId = null,
        PatientGender gender = PatientGender.Female,
        string fullName = "  Mona   Hassan  ",
        string? email = null) =>
        new(PatientTitle.Mrs, fullName, "منى حسن", gender, dateOfBirth ?? (age is null ? new DateOnly(1990, 5, 1) : null), age, null, mobile, nationalId, "EG", email, null, "Cairo", "12 Nile St.", null);

    private static Patient Register(PatientDemographics? demographics = null, string number = "PAT-2026-00001", bool vip = false) =>
        Patient.Register(number, demographics ?? Mona(), PatientFieldRules.Optional, vip, Now);

    [Fact]
    public void S_T1_ok_Register_records_an_active_file_with_cleaned_fields_and_raises_PatientRegistered()
    {
        var patient = Register();

        Assert.Equal(PatientStatus.Active, patient.Status);
        Assert.Equal("PAT-2026-00001", patient.Number);
        Assert.Equal("Mona Hassan", patient.FullName);
        Assert.Equal("mona hassan", patient.NameKey);
        Assert.Equal("مني حسن", patient.NameArKey);
        Assert.Equal("01001234567", patient.Mobile);
        Assert.Equal(Today, patient.RegisteredOn);
        Assert.Equal(new DateOnly(1990, 5, 1), patient.DateOfBirth);
        Assert.False(patient.DateOfBirthIsEstimated);
        Assert.Null(patient.AgeYears);
        Assert.Equal(new PatientAge(36, 5, 8), patient.AgeOn(Today));
        var raised = Assert.IsType<PatientRegistered>(Assert.Single(patient.DomainEvents));
        Assert.Equal((patient.Id, "PAT-2026-00001", PatientGender.Female), (raised.PatientId, raised.Number, raised.Gender));
    }

    [Fact]
    public void S_T1_age_A_stated_age_is_kept_with_its_day_and_the_shown_age_grows_with_time()
    {
        var patient = Register(Mona(age: new PatientAge(34, 2, 0)));

        Assert.Null(patient.DateOfBirth);
        Assert.True(patient.DateOfBirthIsEstimated);
        Assert.Equal(new PatientAge(34, 2, 0), patient.StatedAge);
        Assert.Equal(Today, patient.AgeRecordedOn);
        Assert.Equal(new DateOnly(1992, 8, 9), patient.EstimatedDateOfBirth);
        Assert.Equal(new PatientAge(34, 2, 0), patient.AgeOn(Today));
        Assert.Equal(new PatientAge(35, 2, 0), patient.AgeOn(Today.AddYears(1)));
    }

    [Fact]
    public void S_T1_age_local_day_The_age_and_the_registration_are_dated_by_the_laboratory_day_not_the_UTC_one()
    {
        // Validation v0.5.0 R-11: created at 01:57 in Cairo (UTC+3 in October) on 12 Oct = 22:57 UTC on 11 Oct.
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var atNight = new DateTimeOffset(2026, 10, 11, 22, 57, 0, TimeSpan.Zero);

        var local = Patient.Register("PAT-2026-00014", Mona(age: new PatientAge(52, 0, 0)), PatientFieldRules.Optional, false, atNight, cairo);
        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)), (local.AgeRecordedOn!.Value, local.RegisteredOn));
        Assert.Equal(new DateOnly(1974, 10, 12), local.EstimatedDateOfBirth);

        // No zone known: the UTC day, as before; one minute before local midnight stays on the previous day.
        Assert.Equal(new DateOnly(2026, 10, 11), Patient.Register("PAT-2026-00015", Mona(age: new PatientAge(52, 0, 0)), PatientFieldRules.Optional, false, atNight).AgeRecordedOn);
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("Lab+3", TimeSpan.FromHours(3), "Lab+3", "Lab+3");
        Assert.Equal(new DateOnly(2026, 10, 11), Patient.LocalDate(new DateTimeOffset(2026, 10, 11, 20, 59, 0, TimeSpan.Zero), plusThree));
        Assert.Equal(new DateOnly(2026, 10, 12), Patient.LocalDate(new DateTimeOffset(2026, 10, 11, 21, 0, 0, TimeSpan.Zero), plusThree));

        // A date of birth of the local today is not "in the future" although the UTC day is still the day before.
        var newborn = Patient.Register("PAT-2026-00016", Mona(dateOfBirth: new DateOnly(2026, 10, 12)), PatientFieldRules.Optional, false, atNight, cairo);
        Assert.Equal(PatientAge.Zero, newborn.AgeOn(new DateOnly(2026, 10, 12)));

        // T2 re-states the age on the laboratory's day too.
        local.Update(Mona(age: new PatientAge(53, 0, 0)), PatientFieldRules.Optional, null, atNight.AddDays(1), cairo);
        Assert.Equal(new DateOnly(2026, 10, 13), local.AgeRecordedOn);
    }

    [Fact]
    public void S_T1_age_The_date_of_birth_wins_when_both_are_given()
    {
        var patient = Register(new PatientDemographics(PatientTitle.Mr, "Ali", null, PatientGender.Male, new DateOnly(2000, 1, 1), new PatientAge(3, 0, 0), null, null, null, null, null, null, null, null, null));

        Assert.Equal(new DateOnly(2000, 1, 1), patient.DateOfBirth);
        Assert.Null(patient.StatedAge);
    }

    [Fact]
    public void S_T1_noage_Register_is_refused_without_a_date_of_birth_or_an_age_PAT_007()
    {
        var demographics = new PatientDemographics(PatientTitle.Mr, "Ali", null, PatientGender.Male, null, null, null, null, null, null, null, null, null, null, null);

        AssertRefused(PatientCodes.AgeOrDateOfBirthRequired, () => Patient.Register("PAT-2026-00001", demographics, PatientFieldRules.Optional, false, Now));
    }

    [Theory]
    [InlineData(2026, 10, 10)]
    [InlineData(1899, 12, 31)]
    public void S_T1_future_Register_is_refused_with_an_impossible_date_of_birth_PAT_005(int year, int month, int day) =>
        AssertRefused(PatientCodes.DateOfBirthInvalid, () => Register(Mona(dateOfBirth: new DateOnly(year, month, day))));

    [Theory]
    [InlineData(151, 0, 0)]
    [InlineData(10, 12, 0)]
    [InlineData(10, 0, 32)]
    [InlineData(-1, 0, 0)]
    public void S_T1_age_Register_is_refused_with_an_impossible_age_PAT_006(int years, int months, int days) =>
        AssertRefused(PatientCodes.AgeInvalid, () => Register(Mona(age: new PatientAge(years, months, days))));

    [Theory]
    [InlineData("12ab")]
    [InlineData("1234567")]
    [InlineData("1234567890123456")]
    [InlineData("++201001234567")]
    public void S_T1_mobile_Register_is_refused_with_a_malformed_mobile_PAT_008(string mobile) =>
        AssertRefused(PatientCodes.MobileInvalid, () => Register(Mona(mobile: mobile)));

    [Theory]
    [InlineData("+20 100-123-4567", "+201001234567")]
    [InlineData("٠١٠٠١٢٣٤٥٦٧", "01001234567")]
    [InlineData("(010) 0123.4567", "01001234567")]
    public void Mobiles_are_stored_normalised(string typed, string stored) => Assert.Equal(stored, Register(Mona(mobile: typed)).Mobile);

    [Fact]
    public void S_T1_mandatory_Register_honours_the_mandatory_mobile_and_national_id_settings_PAT_009_PAT_011()
    {
        AssertRefused(PatientCodes.MobileRequired, () => Patient.Register("PAT-2026-00001", Mona(mobile: null), new PatientFieldRules(true, false), false, Now));
        AssertRefused(PatientCodes.NationalIdRequired, () => Patient.Register("PAT-2026-00001", Mona(), new PatientFieldRules(false, true), false, Now));

        var patient = Patient.Register("PAT-2026-00001", Mona(nationalId: " 2900-501 0101234 "), new PatientFieldRules(true, true), false, Now);
        Assert.Equal("29005010101234", patient.NationalId);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("ABC$1234")]
    public void S_T1_Register_is_refused_with_a_malformed_national_id_PAT_010(string nationalId) =>
        AssertRefused(PatientCodes.NationalIdInvalid, () => Register(Mona(nationalId: nationalId)));

    [Theory]
    [InlineData("mona@")]
    [InlineData("mona@example")]
    [InlineData("mo na@example.com")]
    [InlineData("a@b@example.com")]
    public void S_T1_Register_is_refused_with_a_malformed_email_PAT_012(string email) =>
        AssertRefused(PatientCodes.EmailInvalid, () => Register(Mona(email: email)));

    [Fact]
    public void S_T1_Register_is_refused_without_a_name_PAT_001_and_with_an_overlong_text_PAT_013()
    {
        AssertRefused(PatientCodes.NameInvalid, () => Register(Mona(fullName: "   ")));
        var tooLongCity = Mona() with { City = new string('c', Patient.MaxCityLength + 1) };
        AssertRefused(PatientCodes.TextTooLong, () => Register(tooLongCity));
    }

    [Fact]
    public void Check_runs_the_same_rules_without_creating_a_file()
    {
        Patient.Check(Mona(), PatientFieldRules.Optional, Now);
        AssertRefused(PatientCodes.MobileRequired, () => Patient.Check(Mona(mobile: null), new PatientFieldRules(true, false), Now));
    }

    [Fact]
    public void Titles_propose_a_gender_only_when_they_imply_one()
    {
        Assert.Equal(PatientGender.Male, PatientTitles.DefaultGender(PatientTitle.Mr));
        Assert.Equal(PatientGender.Male, PatientTitles.DefaultGender(PatientTitle.Master));
        Assert.Equal(PatientGender.Female, PatientTitles.DefaultGender(PatientTitle.Mrs));
        Assert.Equal(PatientGender.Female, PatientTitles.DefaultGender(PatientTitle.Miss));
        Assert.Equal(PatientGender.Female, PatientTitles.DefaultGender(PatientTitle.Ms));
        Assert.Null(PatientTitles.DefaultGender(PatientTitle.Dr));
        Assert.Null(PatientTitles.DefaultGender(PatientTitle.Baby));
        Assert.Null(PatientTitles.DefaultGender(PatientTitle.Prof));
    }

    [Fact]
    public void S_T2_ok_Update_changes_the_fields_keeps_the_number_and_raises_PatientUpdated()
    {
        var patient = Register();
        patient.ClearDomainEvents();

        patient.Update(Mona(mobile: "01112223333") with { Address = "5 Tahrir Sq." }, PatientFieldRules.Optional, null, Now.AddDays(1));

        Assert.Equal("PAT-2026-00001", patient.Number);
        Assert.Equal("01112223333", patient.Mobile);
        Assert.Equal("5 Tahrir Sq.", patient.Address);
        var raised = Assert.IsType<PatientUpdated>(Assert.Single(patient.DomainEvents));
        Assert.Equal(patient.Id, raised.PatientId);
    }

    [Fact]
    public void S_T2_Update_without_a_change_raises_nothing_and_a_restated_age_keeps_its_original_day()
    {
        var patient = Register(Mona(age: new PatientAge(30, 0, 0)));
        patient.ClearDomainEvents();
        var later = Now.AddDays(40);

        // The form shows the age computed for the day of the edit and sends it back unchanged.
        patient.Update(Mona(age: patient.AgeOn(DateOnly.FromDateTime(later.UtcDateTime))), PatientFieldRules.Optional, null, later);

        Assert.Empty(patient.DomainEvents);
        Assert.Equal(Today, patient.AgeRecordedOn);
        Assert.Equal(new PatientAge(30, 0, 0), patient.StatedAge);
    }

    [Fact]
    public void S_T2_Update_changes_the_VIP_flag_only_when_asked()
    {
        var patient = Register(vip: true);
        patient.ClearDomainEvents();

        patient.Update(Mona(), PatientFieldRules.Optional, null, Now);
        Assert.True(patient.IsVip);
        Assert.Empty(patient.DomainEvents);

        patient.Update(Mona(), PatientFieldRules.Optional, false, Now);
        Assert.False(patient.IsVip);
        Assert.IsType<PatientUpdated>(Assert.Single(patient.DomainEvents));
    }

    [Fact]
    public void S_T2_merged_Update_of_a_merged_file_is_refused_PAT_020()
    {
        var survivor = Register(number: "PAT-2026-00001");
        var duplicate = Register(number: "PAT-2026-00002");
        duplicate.MergeInto(survivor, Actor, "same person", false, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => duplicate.Update(Mona(), PatientFieldRules.Optional, null, Now));
        Assert.Equal(PatientCodes.PatientMerged, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
    }

    [Fact]
    public void S_T3_ok_Merge_freezes_the_duplicate_pointing_to_the_survivor_and_raises_PatientsMerged()
    {
        var survivor = Register(number: "PAT-2026-00001");
        var duplicate = Register(number: "PAT-2026-00002");
        duplicate.ClearDomainEvents();

        duplicate.MergeInto(survivor, Actor, "  registered twice  ", false, Now.AddHours(1));

        Assert.Equal(PatientStatus.Merged, duplicate.Status);
        Assert.Equal(survivor.Id, duplicate.MergedIntoPatientId);
        Assert.Equal(Actor, duplicate.MergedByUserId);
        Assert.Equal(Now.AddHours(1), duplicate.MergedAtUtc);
        Assert.Equal("registered twice", duplicate.MergeReason);
        Assert.Equal(PatientStatus.Active, survivor.Status);
        Assert.Equal("PAT-2026-00001", survivor.Number);
        var raised = Assert.IsType<PatientsMerged>(Assert.Single(duplicate.DomainEvents));
        Assert.Equal((survivor.Id, "PAT-2026-00001", duplicate.Id, "PAT-2026-00002", Actor, "registered twice"), (raised.SurvivorId, raised.SurvivorNumber, raised.MergedId, raised.MergedNumber, raised.ActorUserId, raised.Reason));
    }

    [Fact]
    public void S_T3_self_Merge_into_itself_is_refused_PAT_021()
    {
        var patient = Register();
        AssertRefused(PatientCodes.MergeWithItself, () => patient.MergeInto(patient, Actor, "reason", false, Now));
        Assert.Equal(PatientStatus.Active, patient.Status);
    }

    [Fact]
    public void S_T3_survivor_Merge_into_a_merged_file_is_refused_PAT_022()
    {
        var first = Register(number: "PAT-2026-00001");
        var second = Register(number: "PAT-2026-00002");
        var third = Register(number: "PAT-2026-00003");
        second.MergeInto(first, Actor, "duplicate", false, Now);

        AssertRefused(PatientCodes.SurvivorNotActive, () => third.MergeInto(second, Actor, "duplicate", false, Now));
    }

    [Fact]
    public void S_T3_twice_A_merged_file_cannot_be_merged_again_PAT_020()
    {
        var first = Register(number: "PAT-2026-00001");
        var second = Register(number: "PAT-2026-00002");
        var third = Register(number: "PAT-2026-00003");
        second.MergeInto(first, Actor, "duplicate", false, Now);

        Assert.Equal(PatientCodes.PatientMerged, Assert.Throws<InvalidStateTransitionException>(() => second.MergeInto(third, Actor, "again", false, Now)).Code);
    }

    [Fact]
    public void S_T3_gender_Different_genders_need_the_confirmation_PAT_100()
    {
        var survivor = Register(number: "PAT-2026-00001");
        var duplicate = Register(Mona(gender: PatientGender.Male), number: "PAT-2026-00002");

        var refusal = Assert.Throws<DomainException>(() => duplicate.MergeInto(survivor, Actor, "same person, wrong sex", false, Now));
        Assert.Equal((PatientCodes.GenderMismatchNotConfirmed, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Equal(PatientStatus.Active, duplicate.Status);

        duplicate.MergeInto(survivor, Actor, "same person, wrong sex", true, Now);
        Assert.Equal(PatientStatus.Merged, duplicate.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void S_T3_reason_Merge_without_a_reason_is_refused_PAT_024(string reason)
    {
        var survivor = Register(number: "PAT-2026-00001");
        var duplicate = Register(number: "PAT-2026-00002");

        AssertRefused(PatientCodes.MergeReasonRequired, () => duplicate.MergeInto(survivor, Actor, reason, false, Now));
        Assert.Null(duplicate.MergedIntoPatientId);
    }

    private static void AssertRefused(string code, Action action)
    {
        var refusal = Assert.ThrowsAny<DomainException>(action);
        Assert.Equal(code, refusal.Code);
    }
}
