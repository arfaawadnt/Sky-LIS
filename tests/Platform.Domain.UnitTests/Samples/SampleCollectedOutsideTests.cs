using Platform.Domain.Samples;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Samples;

/// <summary>A sample collected outside the laboratory arrives Collected (change request LIS-CR-008 C-02; intake LIS-INT-SMP-001 §17).</summary>
public sealed class SampleCollectedOutsideTests
{
    private static readonly Guid Registrar = Guid.CreateVersion7();
    private static readonly Guid MainBranch = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static Sample Ordered() =>
        Sample.Create(Guid.CreateVersion7(), "SMP-2026-00001", Guid.CreateVersion7(), "ACC-2026-00001", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7()], Registrar, Now);

    [Fact]
    public void SMP_C02_S01_an_outside_sample_is_Collected_at_the_typed_time_and_waits_for_its_receipt()
    {
        var sample = Ordered();

        sample.ArriveCollectedOutside(Now.AddHours(-3), autoReceive: false, Registrar, MainBranch, Now);

        Assert.Equal(SampleStatus.Collected, sample.Status);
        Assert.True(sample.CollectedOutside);
        Assert.Equal((Now.AddHours(-3), Registrar, MainBranch), (sample.CollectedAtUtc!.Value, sample.CollectedByUserId!.Value, sample.CollectedBranchId!.Value));
        Assert.False(sample.IsResultEntryAllowed);   // BR-03: still behind the Received gate
        var collected = Assert.IsType<SampleCollected>(Assert.Single(sample.DomainEvents));
        Assert.Equal(Now, collected.OccurredAtUtc);   // the turnaround clock starts at the arrival
        Assert.Equal([SampleAction.Ordered, SampleAction.Collected], sample.Events.Select(step => step.Action));
    }

    [Fact]
    public void SMP_C02_S02_with_auto_receive_it_is_received_at_the_arrival()
    {
        var sample = Ordered();

        sample.ArriveCollectedOutside(null, autoReceive: true, Registrar, MainBranch, Now);

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal((Now, Now), (sample.CollectedAtUtc!.Value, sample.ReceivedAtUtc!.Value));
        Assert.True(sample.IsResultEntryAllowed);
    }

    [Fact]
    public void SMP_C02_S03_a_typed_time_after_the_arrival_is_recorded_as_the_arrival()
    {
        var sample = Ordered();

        sample.ArriveCollectedOutside(Now.AddMinutes(4), autoReceive: false, Registrar, MainBranch, Now);

        Assert.Equal(Now, sample.CollectedAtUtc);
    }

    [Fact]
    public void SMP_C02_S04_only_an_Ordered_sample_arrives_SMP_010()
    {
        var sample = Ordered();
        sample.Collect(Registrar, MainBranch, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => sample.ArriveCollectedOutside(null, false, Registrar, MainBranch, Now));

        Assert.Equal(SamplesCodes.NotOrdered, refusal.Code);
    }

    [Fact]
    public void SMP_C02_S05_a_sample_collected_in_the_laboratory_is_not_outside()
    {
        var sample = Ordered();

        sample.AutoAdvance(autoCollect: true, autoReceive: false, Registrar, MainBranch, Now);

        Assert.False(sample.CollectedOutside);
    }
}
