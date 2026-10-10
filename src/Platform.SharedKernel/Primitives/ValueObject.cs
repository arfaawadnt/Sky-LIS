namespace Platform.SharedKernel.Primitives;

/// <summary>
/// A value with no identity, compared by content (03 §2). Derived types list their equality components;
/// everything else (equality, hashing, operators) is provided here.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    public bool Equals(ValueObject? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    /// <summary>The components that define this value, in a stable order.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();
}
