namespace Platform.SharedKernel.Primitives;

/// <summary>
/// Optional value object (D-07) an aggregate records on a transition the domain designated as an approval gate:
/// who approved, when (server clock) and an optional comment. Whether a transition is a gate, who may approve
/// and what freezes afterwards are intake decisions (03 §6).
/// </summary>
public sealed class Approval : ValueObject
{
    public const int MaxCommentLength = 1000;

    public Approval(Guid actorUserId, DateTimeOffset atUtc, string? comment = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An approval needs an accountable actor.", nameof(actorUserId));
        }

        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (trimmed is { Length: > MaxCommentLength })
        {
            throw new ArgumentException($"The comment exceeds {MaxCommentLength} characters.", nameof(comment));
        }

        ActorUserId = actorUserId;
        AtUtc = atUtc;
        Comment = trimmed;
    }

    /// <summary>Reserved for object materialisers (EF Core owned type).</summary>
    private Approval()
    {
    }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public string? Comment { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ActorUserId;
        yield return AtUtc;
        yield return Comment;
    }
}
