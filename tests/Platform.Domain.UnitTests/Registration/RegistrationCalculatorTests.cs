using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>The pure rules of the module: BR-13 turnaround clock, BR-02 tube planner, discount arithmetic; and the Booking and Batch aggregates.</summary>
public sealed class RegistrationCalculatorTests
{
    private const int SundayToThursday = 0b0011111;   // bits 0..4 = Sunday..Thursday
    private const int Cairo = 120;

    [Fact]
    public void S_BR13_24_hours_from_Thursday_10_00_on_a_Sunday_to_Thursday_test_ends_Sunday_10_00()
    {
        var thursday10 = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(2));

        var expected = TurnaroundCalculator.ExpectedAt(thursday10.ToUniversalTime(), 24, SundayToThursday, Cairo);

        Assert.Equal(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(2)), expected);
        Assert.Equal(TimeSpan.Zero, expected.Offset);
    }

    [Fact]
    public void S_BR13_every_day_test_adds_the_hours_and_a_start_on_an_off_day_waits_for_the_next_running_day()
    {
        var start = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(start.AddHours(30), TurnaroundCalculator.ExpectedAt(start, 30, TurnaroundCalculator.EveryDay, Cairo));

        var friday09Cairo = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.FromHours(2));
        var expected = TurnaroundCalculator.ExpectedAt(friday09Cairo.ToUniversalTime(), 2, SundayToThursday, Cairo);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.FromHours(2)), expected);
    }

    [Fact]
    public void S_BR13_zero_hours_and_an_empty_mask_are_handled()
    {
        var start = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(start, TurnaroundCalculator.ExpectedAt(start, 0, SundayToThursday, Cairo));
        Assert.Equal(start.AddHours(5), TurnaroundCalculator.ExpectedAt(start, 5, 0, 0));
        Assert.Null(TurnaroundCalculator.Latest([null, null]));
        Assert.Equal(start.AddHours(2), TurnaroundCalculator.Latest([start, null, start.AddHours(2)]));
    }

    [Fact]
    public void S_BR02_planner_joins_live_tubes_opens_one_new_tube_per_key_and_skips_dead_tubes()
    {
        var serum = new TubeKey(Chemistry, Serum, null);
        var blood = new TubeKey(Haematology, WholeBlood, null);
        var live = Guid.CreateVersion7();
        var dead = Guid.CreateVersion7();

        var plan = TubePlanner.Assign(
            [new TubePlanner.ExistingTube(dead, blood, false), new TubePlanner.ExistingTube(live, serum, true)],
            [serum, blood, null, blood, serum]);

        Assert.Equal(live, plan.Assignments[0]!.ExistingTubeId);
        Assert.Equal(0, plan.Assignments[1]!.NewTubeIndex);
        Assert.Null(plan.Assignments[2]);
        Assert.Equal(0, plan.Assignments[3]!.NewTubeIndex);
        Assert.Equal(live, plan.Assignments[4]!.ExistingTubeId);
        Assert.Equal([blood], plan.NewTubes);
        Assert.Equal(1, TubePlanner.CountNewTubes([new TubePlanner.ExistingTube(dead, blood, false)], [blood, blood]));
    }

    [Fact]
    public void Discount_amounts_and_largest_remainder_allocation_add_up_to_the_cent()
    {
        Assert.Equal(33.33m, DiscountAllocator.AmountFor(DiscountKind.Percent, 33.333m, 100m));
        Assert.Equal(50m, DiscountAllocator.AmountFor(DiscountKind.Amount, 80m, 50m));
        Assert.Equal(0m, DiscountAllocator.AmountFor(DiscountKind.Amount, 10m, 0m));

        var allocation = DiscountAllocator.Allocate(10m, [1m, 1m, 1m]);
        Assert.Equal(10m, allocation.Sum());
        Assert.Equal([3.34m, 3.33m, 3.33m], allocation.ToArray());
        Assert.Equal([0m, 0m], DiscountAllocator.Allocate(5m, [0m, 0m]).ToArray());
        Assert.Equal([0m, 5m], DiscountAllocator.Allocate(5m, [0m, 20m]).ToArray());
    }

    [Fact]
    public void S_B1_booking_needs_an_address_a_patient_and_services_and_converts_once()
    {
        var content = new BookingContent(new BookingPatient(null, "Mona Hassan", "Female", "01001234567", 34), " 12 Nile St ", Now.AddDays(1), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), [Guid.CreateVersion7()], null);
        var booking = Booking.Create("BKG-2026-00001", Guid.CreateVersion7(), content, Now);

        Assert.Equal((BookingStatus.Booked, "12 Nile St"), (booking.Status, booking.Address));
        Assert.IsType<BookingCreated>(Assert.Single(booking.DomainEvents));
        Assert.Equal(RegistrationCodes.BookingAddressRequired, Assert.Throws<DomainException>(() => Booking.Create("BKG-2026-00002", Guid.CreateVersion7(), content with { Address = " " }, Now)).Code);
        Assert.Equal(RegistrationCodes.BookingPatientRequired, Assert.Throws<DomainException>(() => Booking.Create("BKG-2026-00003", Guid.CreateVersion7(), content with { Patient = new BookingPatient(null, "Name only", null, null, null) }, Now)).Code);
        Assert.Equal(RegistrationCodes.LinesRequired, Assert.Throws<DomainException>(() => Booking.Create("BKG-2026-00004", Guid.CreateVersion7(), content with { ServiceIds = [] }, Now)).Code);

        var patient = Guid.CreateVersion7();
        booking.Convert(Guid.CreateVersion7(), "ACC-2026-00009", patient, Actor, Now);
        Assert.Equal((BookingStatus.Converted, patient, "ACC-2026-00009"), (booking.Status, booking.PatientId!.Value, booking.Accession));
        Assert.Equal(RegistrationCodes.BookingNotBooked, Assert.Throws<InvalidStateTransitionException>(() => booking.Convert(Guid.CreateVersion7(), "ACC-2026-00010", patient, Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.BookingNotBooked, Assert.Throws<InvalidStateTransitionException>(() => booking.Cancel(Actor, "late", Now)).Code);
    }

    [Fact]
    public void S_B4_a_booking_for_a_file_converts_only_on_that_file_and_cancel_needs_a_reason()
    {
        var file = Guid.CreateVersion7();
        var booking = Booking.Create("BKG-2026-00011", Guid.CreateVersion7(), new BookingContent(new BookingPatient(file, null, null, null, null), "Address", Now, null, Guid.CreateVersion7(), Guid.CreateVersion7(), [Guid.CreateVersion7()], null), Now);

        Assert.Equal(RegistrationCodes.BookingPatientMismatch, Assert.Throws<DomainException>(() => booking.EnsureConvertibleFor(Guid.CreateVersion7())).Code);
        Assert.Equal(RegistrationCodes.CancellationReasonRequired, Assert.Throws<DomainException>(() => booking.Cancel(Actor, " ", Now)).Code);

        booking.Cancel(Actor, "Patient travelled", Now);
        Assert.Equal((BookingStatus.Cancelled, "Patient travelled"), (booking.Status, booking.CancellationReason));
    }

    [Fact]
    public void S_T1_batches_check_their_kind_and_count_members()
    {
        var group = Batch.Register("BTH-2026-00001", Guid.CreateVersion7(), new BatchHeader(BatchKind.Group, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, "Acme staff", null, null, null), 3, Actor, Now);
        Assert.Equal((BatchKind.Group, 3), (group.Kind, group.MemberCount));
        group.AddMembers(2, Actor, Now);
        Assert.Equal(5, group.MemberCount);

        Assert.Equal(RegistrationCodes.BatchGroupNameRequired, Assert.Throws<DomainException>(() => Batch.Register("BTH-2026-00002", Guid.CreateVersion7(), new BatchHeader(BatchKind.Group, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, null, null), 1, Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.SendingLabInvalid, Assert.Throws<DomainException>(() => Batch.Register("BTH-2026-00003", Guid.CreateVersion7(), new BatchHeader(BatchKind.LabToLab, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, null, null), 1, Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.BatchMembersInvalid, Assert.Throws<DomainException>(() => group.AddMembers(0, Actor, Now)).Code);
    }
}
