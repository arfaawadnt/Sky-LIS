using Platform.Domain.Patients;
using Platform.Domain.UnitTests.Support;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>
/// Change request 2026-10-13 item 1 (intake LIS-INT-PAT-001 §17): which fields are the <b>identity</b> a result-status lock
/// protects (PAT-032) and which are contact fields that stay free. <see cref="Patient.IdentityChangesWith"/> is what the update
/// handler asks before it reads the results.
/// </summary>
public sealed class PatientIdentityChangeTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;   // 2026-10-09
    private static readonly Guid Condition = Guid.CreateVersion7();

    private static PatientDemographics Mona(
        PatientTitle title = PatientTitle.Mrs,
        string fullName = "Mona Hassan",
        string? nameAr = "منى حسن",
        PatientGender gender = PatientGender.Female,
        DateOnly? dateOfBirth = null,
        PatientAge? age = null,
        Guid? conditionId = null,
        string? mobile = "01001234567",
        string? nationalId = "29005010101234",
        string? nationality = "EG",
        string? email = null,
        Guid? governorateId = null,
        string? city = "Cairo",
        string? address = null,
        string? comments = null) =>
        new(title, fullName, nameAr, gender, dateOfBirth ?? (age is null ? new DateOnly(1990, 5, 1) : null), age, conditionId, mobile, nationalId, nationality, email, governorateId, city, address, comments);

    private static Patient Registered(PatientDemographics? demographics = null) => Patient.Register("PAT-2026-00001", demographics ?? Mona(), PatientFieldRules.Optional, false, Now);

    [Fact]
    public void S_LOCK_same_values_are_no_identity_change_even_when_spelled_with_extra_spaces_or_lower_case_id()
    {
        var patient = Registered();
        Assert.False(patient.IdentityChangesWith(Mona(), PatientFieldRules.Optional, Now));
        Assert.False(patient.IdentityChangesWith(Mona(fullName: "  Mona   Hassan ", nationalId: "2900-5010-101234"), PatientFieldRules.Optional, Now));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("fullName")]
    [InlineData("nameAr")]
    [InlineData("gender")]
    [InlineData("dateOfBirth")]
    [InlineData("condition")]
    [InlineData("nationalId")]
    [InlineData("nationality")]
    public void S_LOCK_each_identity_field_counts(string field)
    {
        var patient = Registered();
        var changed = field switch
        {
            "title" => Mona(title: PatientTitle.Dr),
            "fullName" => Mona(fullName: "Mona Hassan Ali"),
            "nameAr" => Mona(nameAr: null),
            "gender" => Mona(gender: PatientGender.Male),
            "dateOfBirth" => Mona(dateOfBirth: new DateOnly(1991, 5, 1)),
            "condition" => Mona(conditionId: Condition),
            "nationalId" => Mona(nationalId: "29005010109999"),
            _ => Mona(nationality: "SA"),
        };

        Assert.True(patient.IdentityChangesWith(changed, PatientFieldRules.Optional, Now));
    }

    [Fact]
    public void S_LOCK_contact_fields_and_the_vip_flag_are_not_identity()
    {
        var patient = Registered();
        var contact = Mona(mobile: "01112223333", email: "mona@example.test", governorateId: Guid.CreateVersion7(), city: "Giza", address: "12 Nile St.", comments: "called");
        Assert.False(patient.IdentityChangesWith(contact, PatientFieldRules.Optional, Now));

        patient.Update(contact, PatientFieldRules.Optional, isVip: true, Now);   // the VIP flag is governed by patients.viewConfidential, not by the lock
        Assert.Equal(("01112223333", "Giza", true), (patient.Mobile, patient.City, patient.IsVip));
    }

    [Fact]
    public void S_LOCK_an_age_restated_as_today_s_age_is_no_change_but_a_different_age_or_a_switch_to_a_date_is()
    {
        var byAge = Registered(Mona(age: new PatientAge(30, 0, 0)));
        Assert.False(byAge.IdentityChangesWith(Mona(age: byAge.AgeOn(new DateOnly(2026, 10, 9))), PatientFieldRules.Optional, Now));
        Assert.True(byAge.IdentityChangesWith(Mona(age: new PatientAge(31, 0, 0)), PatientFieldRules.Optional, Now));
        Assert.True(byAge.IdentityChangesWith(Mona(dateOfBirth: new DateOnly(1996, 10, 9)), PatientFieldRules.Optional, Now));

        var byDate = Registered();
        Assert.True(byDate.IdentityChangesWith(Mona(age: byDate.AgeOn(new DateOnly(2026, 10, 9))), PatientFieldRules.Optional, Now));   // the known date would be dropped for an estimate
    }
}
