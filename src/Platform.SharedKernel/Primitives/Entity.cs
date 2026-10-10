namespace Platform.SharedKernel.Primitives;

/// <summary>
/// A record with an identity that persists through changes. Two entities are equal when they are of the same
/// concrete type and carry the same id; nothing else participates in equality.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    /// <summary>Reserved for object materialisers (EF Core). Domain code always supplies an id.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; private set; }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}

/// <summary>
/// An entity keyed by a UUID version 7 minted in the domain at construction time, never by the database
/// (01 §2.3). Ids therefore exist before the commit, which the outbox relies on.
/// </summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity()
        : base(Guid.CreateVersion7())
    {
    }

    protected Entity(Guid id)
        : base(id)
    {
    }
}
