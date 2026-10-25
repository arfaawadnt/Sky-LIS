using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>
/// "Collected outside the laboratory" per sample (change request LIS-CR-008, reviewer point C-02; intake LIS-INT-REG-001
/// §20): the flag lives on the tube, the visit derives it, and the old visit-level field still marks every tube.
/// </summary>
public sealed class VisitCollectedOutsideTests
{
    private static Visit RegisterWith(VisitRegistration header, params VisitOrderItem[] items) =>
        Visit.Register(header, items, [], null, Numbers(10), Actor, Now);

    private static VisitTube TubeOf(Visit visit, Guid sampleTypeId) => visit.Tubes.Single(tube => tube.SampleTypeId == sampleTypeId);

    [Fact]
    public void REG_C02_S01_a_tube_marked_by_its_key_is_outside_and_the_others_are_not()
    {
        var serum = ChemistrySerum();
        var header = Header() with { OutsideSamples = [new OutsideSample(serum.Key, null, Now.AddHours(-2))] };

        var visit = RegisterWith(header, Test("GLU", serum), Test("CBC", HaematologyBlood()));

        var outside = TubeOf(visit, Serum);
        Assert.True(outside.CollectedOutside);
        Assert.Equal(Now.AddHours(-2), outside.CollectedOutsideAtUtc);
        Assert.False(TubeOf(visit, WholeBlood).CollectedOutside);
        Assert.True(visit.AnyCollectedOutside);
        Assert.False(visit.CollectedOutside);   // the history column belongs to the old visit-level field only
    }

    [Fact]
    public void REG_C02_S02_a_sample_type_entry_marks_every_tube_of_that_type()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, Serum, null)] };

        var visit = RegisterWith(header, Test("GLU", ChemistrySerum()), Test("FBS", ChemistrySerum(Fasting)), Test("CBC", HaematologyBlood()));

        Assert.Equal(2, visit.Tubes.Count(tube => tube.CollectedOutside));
        Assert.All(visit.Tubes.Where(tube => tube.SampleTypeId == Serum), tube => Assert.Null(tube.CollectedOutsideAtUtc));
        Assert.False(TubeOf(visit, WholeBlood).CollectedOutside);
    }

    [Fact]
    public void REG_C02_S03_the_old_visit_level_field_marks_every_tube_and_is_kept_as_history()
    {
        var visit = RegisterWith(Header() with { CollectedOutside = true }, Test("GLU", ChemistrySerum()), Test("CBC", HaematologyBlood()));

        Assert.All(visit.Tubes, tube => Assert.True(tube.CollectedOutside));
        Assert.True(visit.AnyCollectedOutside);
        Assert.True(visit.CollectedOutside);
    }

    [Fact]
    public void REG_C02_S04_per_sample_entries_win_over_the_old_field()
    {
        var header = Header() with { CollectedOutside = true, OutsideSamples = [new OutsideSample(null, WholeBlood, null)] };

        var visit = RegisterWith(header, Test("GLU", ChemistrySerum()), Test("CBC", HaematologyBlood()));

        Assert.False(TubeOf(visit, Serum).CollectedOutside);
        Assert.True(TubeOf(visit, WholeBlood).CollectedOutside);
        Assert.False(visit.CollectedOutside);
    }

    [Fact]
    public void REG_C02_S05_without_any_mark_no_tube_is_outside()
    {
        var visit = RegisterWith(Header(), Test("GLU", ChemistrySerum()));

        Assert.False(visit.AnyCollectedOutside);
        Assert.False(Assert.Single(visit.Tubes).CollectedOutside);
    }

    [Fact]
    public void REG_C02_S06_the_event_carries_the_flag_and_the_typed_time_per_tube()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, Serum, Now.AddMinutes(-45))] };

        var visit = RegisterWith(header, Test("GLU", ChemistrySerum()), Test("CBC", HaematologyBlood()));

        var registered = Assert.Single(visit.DomainEvents.OfType<VisitRegistered>());
        var serumTube = registered.Tubes.Single(tube => tube.SampleTypeId == Serum);
        var bloodTube = registered.Tubes.Single(tube => tube.SampleTypeId == WholeBlood);
        Assert.True(serumTube.CollectedOutside);
        Assert.Equal(Now.AddMinutes(-45), serumTube.CollectedAtUtc);
        Assert.False(bloodTube.CollectedOutside);
        Assert.Null(bloodTube.CollectedAtUtc);
    }

    [Fact]
    public void REG_C02_S07_an_entry_that_matches_no_tube_is_refused_REG_040()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, WholeBlood, null)] };

        var refusal = Assert.Throws<DomainException>(() => RegisterWith(header, Test("GLU", ChemistrySerum())));

        Assert.Equal(RegistrationCodes.OutsideSampleUnknown, refusal.Code);
    }

    [Fact]
    public void REG_C02_S08_an_entry_without_key_or_type_is_refused_REG_040()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, null, null)] };

        Assert.Equal(RegistrationCodes.OutsideSampleUnknown, Assert.Throws<DomainException>(() => RegisterWith(header, Test("GLU", ChemistrySerum()))).Code);
    }

    [Theory]
    [InlineData(10)]          // ten minutes ahead: beyond the clock skew
    [InlineData(-31 * 1440)]  // 31 days back
    public void REG_C02_S09_a_collection_time_out_of_range_is_refused_REG_041(int minutesFromNow)
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, Serum, Now.AddMinutes(minutesFromNow))] };

        Assert.Equal(RegistrationCodes.OutsideCollectedAtInvalid, Assert.Throws<DomainException>(() => RegisterWith(header, Test("GLU", ChemistrySerum()))).Code);
    }

    [Fact]
    public void REG_C02_S10_a_time_within_the_clock_skew_is_kept_as_now()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, Serum, Now.AddMinutes(3))] };

        var visit = RegisterWith(header, Test("GLU", ChemistrySerum()));

        Assert.Equal(Now, Assert.Single(visit.Tubes).CollectedOutsideAtUtc);
    }

    [Fact]
    public void REG_C02_S11_a_reserved_service_has_no_tube_to_mark()
    {
        var header = Header() with { OutsideSamples = [new OutsideSample(null, Serum, null)] };

        Assert.Equal(RegistrationCodes.OutsideSampleUnknown, Assert.Throws<DomainException>(() => RegisterWith(header, Test("GLU", ChemistrySerum(), reserved: true), Additional("X"))).Code);
    }

    [Fact]
    public void REG_C02_S12_tubes_added_to_a_saved_visit_are_collected_in_the_laboratory()
    {
        var visit = RegisterWith(Header() with { CollectedOutside = true }, Test("GLU", ChemistrySerum()));

        visit.AddLines([Test("CBC", HaematologyBlood())], [], Numbers(1, 50), Actor, Now);

        Assert.True(TubeOf(visit, Serum).CollectedOutside);
        Assert.False(TubeOf(visit, WholeBlood).CollectedOutside);
    }

    [Fact]
    public void REG_C02_S13_editing_the_details_never_changes_the_tubes()
    {
        var visit = RegisterWith(Header() with { OutsideSamples = [new OutsideSample(null, Serum, null)] }, Test("GLU", ChemistrySerum()));

        visit.UpdateDetails(new VisitDetails(null, null, null, true, false));

        Assert.True(visit.AnyCollectedOutside);
    }
}
