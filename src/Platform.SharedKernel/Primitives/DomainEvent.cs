namespace Platform.SharedKernel.Primitives;

/// <summary>
/// A past-tense fact an aggregate announces (03 §2). Events carry ids and the few values a reader needs,
/// never the whole record (01 §8, DomainHardeningTests).
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique per occurrence; the outbox and consumers use it for idempotency.</summary>
    Guid EventId { get; }

    /// <summary>When the fact occurred, taken from <c>IClock</c> by the aggregate method that raised it.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>Base record for domain events. Derived events are sealed records named in the past tense.</summary>
/// <param name="OccurredAtUtc">The server time the aggregate method received from <c>IClock</c>.</param>
public abstract record DomainEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}
