using Platform.Domain.Catalogue;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>LIS-CR-007 C-09 (Catalogue-B intake, change request 2026-10-24): the generated doctor code <c>DR-nnn</c>.</summary>
public sealed class DoctorCodeTests
{
    [Theory]
    [InlineData(1, "DR-001")]
    [InlineData(42, "DR-042")]
    [InlineData(999, "DR-999")]
    [InlineData(1000, "DR-1000")]
    public void C09_a_generated_code_is_DR_with_at_least_three_digits_and_no_year(long sequence, string expected) =>
        Assert.Equal(expected, Doctor.GeneratedCode(sequence));

    [Fact]
    public void C09_a_generated_code_is_a_valid_doctor_code_kept_as_is()
    {
        var doctor = Doctor.Create(Doctor.GeneratedCode(7), new LocalizedText("Dr. Amal", "د. أمل"), null, null, null, 0m, null, FixedClock.Instant);

        Assert.Equal("DR-007", doctor.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void C09_a_counter_value_below_one_is_not_a_code(long sequence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Doctor.GeneratedCode(sequence));

    [Fact]
    public void The_short_format_pads_to_the_width_and_never_cuts_digits()
    {
        Assert.Equal(("X-00005", "X-123456"), (ReferenceNumberFormat.Short("X", 5, 5), ReferenceNumberFormat.Short("X", 123456, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceNumberFormat.Short("X", 1, ReferenceNumberFormat.MaxShortWidth + 1));
    }
}
