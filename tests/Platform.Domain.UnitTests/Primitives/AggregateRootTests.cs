using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Primitives;

public sealed class AggregateRootTests
{
    private static readonly Guid Creator = Guid.CreateVersion7();
    private static readonly Guid Approver = Guid.CreateVersion7();

    [Fact]
    public void A_transition_records_its_metadata_changes_state_and_raises_one_event()
    {
        var aggregate = TestAggregate.Create();
        aggregate.StampCreatedBy(Creator, FixedClock.Instant);

        aggregate.Approve(Approver, FixedClock.Instant.AddMinutes(5), "  looks right  ");

        Assert.Equal("Approved", aggregate.Status);
        Assert.Equal(new Approval(Approver, FixedClock.Instant.AddMinutes(5), "looks right"), aggregate.Approval);
        var raised = Assert.Single(aggregate.DomainEvents);
        var approved = Assert.IsType<TestApproved>(raised);
        Assert.Equal(aggregate.Id, approved.AggregateId);
        Assert.Equal(7, approved.EventId.Version);
        Assert.Equal(FixedClock.Instant.AddMinutes(5), approved.OccurredAtUtc);
    }

    [Fact]
    public void Clearing_events_empties_the_list_without_touching_state()
    {
        var aggregate = TestAggregate.Create();
        aggregate.Approve(Approver, FixedClock.Instant);

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
        Assert.Equal("Approved", aggregate.Status);
    }

    [Fact]
    public void The_creator_cannot_approve_and_nothing_is_recorded()
    {
        var aggregate = TestAggregate.Create();
        aggregate.StampCreatedBy(Creator, FixedClock.Instant);

        var refusal = Assert.Throws<DomainException>(() => aggregate.Approve(Creator, FixedClock.Instant));

        Assert.Equal(TestAggregate.SodCreatorIsApprover, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
        Assert.Equal("Draft", aggregate.Status);
        Assert.Null(aggregate.Approval);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void A_system_created_record_has_no_creator_so_the_SoD_rule_is_a_no_op()
    {
        var aggregate = TestAggregate.Create();
        aggregate.StampCreatedBy(null, FixedClock.Instant);

        aggregate.Approve(Approver, FixedClock.Instant);

        Assert.Equal("Approved", aggregate.Status);
    }

    [Fact]
    public void A_state_guard_refuses_with_its_code_and_fault()
    {
        var aggregate = TestAggregate.Create();
        aggregate.Approve(Approver, FixedClock.Instant);

        var refusal = Assert.Throws<DomainException>(() => aggregate.Approve(Approver, FixedClock.Instant));

        Assert.Equal(TestAggregate.ApproveRequiresDraft, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
    }

    [Fact]
    public void Audit_stamps_are_written_only_through_the_stamping_interface()
    {
        var aggregate = TestAggregate.Create();

        Assert.Null(aggregate.CreatedByUserId);
        Assert.Equal(default(DateTimeOffset), aggregate.CreatedAtUtc);

        aggregate.StampCreatedBy(Creator, FixedClock.Instant);
        ((IAuditStamped)aggregate).StampModified(new AuditStamp(FixedClock.Instant.AddHours(1), Approver, "Approver"));

        Assert.Equal(Creator, aggregate.CreatedByUserId);
        Assert.Equal("Tester", aggregate.CreatedByDisplayName);
        Assert.Equal(FixedClock.Instant, aggregate.CreatedAtUtc);
        Assert.Equal(Approver, aggregate.ModifiedByUserId);
        Assert.Equal(FixedClock.Instant.AddHours(1), aggregate.ModifiedAtUtc);
    }

    [Fact]
    public void An_invalid_state_transition_exception_is_a_conflict()
    {
        var exception = new InvalidStateTransitionException("TST-020", "test record", "Approved", "submit");

        Assert.Equal(DomainFault.Conflict, exception.Fault);
        Assert.Equal("TST-020", exception.Code);
        Assert.Contains("submit", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Approved", exception.Message, StringComparison.Ordinal);
    }
}
