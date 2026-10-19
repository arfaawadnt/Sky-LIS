using Platform.Domain.Patients;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>
/// Change request 2026-10-19 (intake LIS-INT-PAT-001 §18): the second phone (C3) is cleaned like the mobile with its own
/// code, and the picked city / district / area ids (A2) are contact fields — stored as given, empty ids dropped, never part
/// of the identity a result-status lock freezes.
/// </summary>
public sealed class PatientContactTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cairo = Guid.CreateVersion7();
    private static readonly Guid Nasr = Guid.CreateVersion7();
    private static readonly Guid Zone1 = Guid.CreateVersion7();
    private static readonly Guid Block7 = Guid.CreateVersion7();

    private static PatientDemographics Mona(string? secondPhone = null, Guid? cityId = null, Guid? districtId = null, Guid? areaId = null, string? mobile = "0100 123 4567") =>
        new(PatientTitle.Mrs, "Mona Hassan", "منى حسن", PatientGender.Female, new DateOnly(1990, 5, 1), null, null, mobile, null, "EG", null, Cairo, "Cairo", "12 Nile St.", null, cityId, districtId, areaId, secondPhone);

    private static Patient Register(PatientDemographics demographics) => Patient.Register("PAT-2026-00001", demographics, PatientFieldRules.Optional, false, Now);

    [Theory]
    [InlineData("0111 222 3333", "01112223333")]
    [InlineData("+20 (111) 222-3333", "+201112223333")]
    [InlineData("٠١١١٢٢٢٣٣٣٣", "01112223333")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void S_C3_ok_The_second_phone_is_stored_cleaned_like_the_mobile(string? typed, string? stored) =>
        Assert.Equal(stored, Register(Mona(secondPhone: typed)).SecondPhone);

    [Theory]
    [InlineData("1234567")]       // seven digits
    [InlineData("0123456789012345")]   // sixteen digits
    [InlineData("01x12223333")]
    public void S_C3_invalid_A_malformed_second_phone_is_refused_with_its_own_code_PAT_017(string typed)
    {
        var refusal = Assert.Throws<DomainException>(() => Register(Mona(secondPhone: typed)));
        Assert.Equal((PatientCodes.SecondPhoneInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
        Assert.Equal(PatientCodes.SecondPhoneInvalid, Assert.Throws<DomainException>(() => Patient.CleanSecondPhone(typed)).Code);
        Assert.Equal(PatientCodes.MobileInvalid, Assert.Throws<DomainException>(() => Patient.CleanMobile(typed)).Code);   // the mobile keeps PAT-008
    }

    [Fact]
    public void S_C3_optional_The_second_phone_is_never_required_even_when_the_mobile_is()
    {
        var patient = Patient.Register("PAT-2026-00002", Mona(secondPhone: null), new PatientFieldRules(true, false), false, Now);
        Assert.Null(patient.SecondPhone);
        Assert.Equal("01001234567", patient.Mobile);
    }

    [Fact]
    public void S_A2_ok_The_city_district_and_area_ids_are_stored_and_empty_ids_are_dropped()
    {
        var patient = Register(Mona(cityId: Nasr, districtId: Zone1, areaId: Block7));
        Assert.Equal((Cairo, Nasr, Zone1, Block7), (patient.GovernorateId, patient.CityId, patient.DistrictId, patient.AreaId));

        var empty = Register(Mona(cityId: Guid.Empty, districtId: Guid.Empty, areaId: Guid.Empty));
        Assert.Equal(((Guid?)null, (Guid?)null, (Guid?)null), (empty.CityId, empty.DistrictId, empty.AreaId));
    }

    [Fact]
    public void S_A2_contact_The_location_and_the_second_phone_are_contact_fields_not_identity()
    {
        var patient = Register(Mona());

        var changed = Mona(secondPhone: "01112223333", cityId: Nasr, districtId: Zone1, areaId: Block7);
        Assert.False(patient.IdentityChangesWith(changed, PatientFieldRules.Optional, Now));

        patient.Update(changed, PatientFieldRules.Optional, null, Now);
        Assert.Equal(("01112223333", Nasr, Zone1, Block7), (patient.SecondPhone, patient.CityId, patient.DistrictId, patient.AreaId));
        Assert.Contains(patient.DomainEvents, raised => raised is PatientUpdated);   // a contact change is still a change
    }

    [Fact]
    public void S_A2_unchanged_Re_sending_the_same_contact_values_raises_nothing()
    {
        var patient = Register(Mona(secondPhone: "01112223333", cityId: Nasr));
        patient.ClearDomainEvents();

        patient.Update(Mona(secondPhone: "0111-222-3333", cityId: Nasr), PatientFieldRules.Optional, null, Now);
        Assert.Empty(patient.DomainEvents);
    }
}
