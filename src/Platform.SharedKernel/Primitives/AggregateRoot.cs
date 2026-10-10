namespace Platform.SharedKernel.Primitives;

/// <summary>Exposes raised domain events to the persistence layer, which copies them to the outbox in the same transaction and clears them.</summary>
public interface IDomainEventSource
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>
/// The root of a consistency boundary. Every change to the cluster passes through a method on the root, which
/// guards the state machine, applies the rules the domain defined, records transition metadata and raises the
/// events that announce what happened. Setters are private; there are no public constructors on concrete roots
/// (gate: DomainHardeningTests).
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IAuditStamped, IDomainEventSource
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Reserved for object materialisers (EF Core).</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>Events raised since the aggregate was loaded; drained by the outbox interceptor at commit.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Audit stamps are written by the persistence layer from ICurrentUser and IClock (01 §2.3), never by domain
    // code, handlers or request bodies. CreatedByUserId is the anchor for the creator-is-not-approver SoD helper.
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public string? CreatedByDisplayName { get; private set; }

    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedByUserId { get; private set; }

    public string? ModifiedByDisplayName { get; private set; }

    void IAuditStamped.StampCreated(AuditStamp stamp)
    {
        CreatedAtUtc = stamp.AtUtc;
        CreatedByUserId = stamp.UserId;
        CreatedByDisplayName = stamp.DisplayName;
    }

    void IAuditStamped.StampModified(AuditStamp stamp)
    {
        ModifiedAtUtc = stamp.AtUtc;
        ModifiedByUserId = stamp.UserId;
        ModifiedByDisplayName = stamp.DisplayName;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Refuses with a coded <see cref="DomainException"/> when <paramref name="condition"/> is false.</summary>
    protected static void Require(bool condition, string refusalCode, string message, DomainFault fault = DomainFault.Refused)
    {
        if (!condition)
        {
            throw new DomainException(refusalCode, message, fault);
        }
    }

    /// <summary>
    /// Segregation-of-duties helper (03 §5.3): refuses when the acting user is the user recorded as the creator.
    /// Which transitions apply it, and whether it is configurable, is an intake decision per domain.
    /// </summary>
    protected void EnsureActorIsNotCreator(Guid actorUserId, string refusalCode, string message) =>
        EnsureActorIsNot(CreatedByUserId, actorUserId, refusalCode, message);

    /// <summary>
    /// Segregation-of-duties helper for any anchor field (requester, first approver, preparer …): refuses when
    /// the acting user equals the recorded anchor. A missing anchor (system-created record) is a no-op (03 §5.4).
    /// </summary>
    protected static void EnsureActorIsNot(Guid? anchorUserId, Guid actorUserId, string refusalCode, string message)
    {
        if (anchorUserId.HasValue && anchorUserId.Value == actorUserId)
        {
            throw new DomainException(refusalCode, message, DomainFault.Refused);
        }
    }
}

/// <summary>An aggregate root keyed by a UUID version 7 minted in the domain.</summary>
public abstract class AggregateRoot : AggregateRoot<Guid>
{
    protected AggregateRoot()
        : base(Guid.CreateVersion7())
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }
}
