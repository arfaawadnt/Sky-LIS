using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Support;

/// <summary>A clock that returns a fixed instant; tests never read the machine clock (pitfall P-14).</summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public static readonly DateTimeOffset Instant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class TestEntity : Entity
{
    public TestEntity()
    {
    }

    public TestEntity(Guid id)
        : base(id)
    {
    }
}

internal sealed class OtherEntity(Guid id) : Entity(id);

internal sealed record TestApproved(Guid AggregateId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A minimal guarded aggregate: Draft → Approved, creator may not approve (SoD-TST-001).</summary>
internal sealed class TestAggregate : AggregateRoot
{
    public const string SodCreatorIsApprover = "SOD-TST-001";
    public const string ApproveRequiresDraft = "TST-010";

    private TestAggregate()
    {
    }

    public string Status { get; private set; } = "Draft";

    public Approval? Approval { get; private set; }

    public static TestAggregate Create() => new();

    public void Approve(Guid actorUserId, DateTimeOffset atUtc, string? comment = null)
    {
        Require(Status == "Draft", ApproveRequiresDraft, "Only a draft can be approved.", DomainFault.Conflict);
        EnsureActorIsNotCreator(actorUserId, SodCreatorIsApprover, "The creator cannot approve.");

        Approval = new Approval(actorUserId, atUtc, comment);
        Status = "Approved";
        Raise(new TestApproved(Id, actorUserId, atUtc));
    }

    public void StampCreatedBy(Guid? userId, DateTimeOffset atUtc) =>
        ((IAuditStamped)this).StampCreated(new AuditStamp(atUtc, userId, "Tester"));
}

internal sealed class Money(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;

    public string Currency { get; } = currency;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
