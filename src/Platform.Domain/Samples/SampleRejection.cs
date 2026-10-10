using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Samples;

/// <summary>A catalogue reason of kind Rejection as the handler resolved it; the names are a snapshot (names may change later).</summary>
public sealed record RejectionReason(Guid ReasonId, string? NameEn, string? NameAr);

/// <summary>
/// The last rejection of a sample (T4): the catalogue reason (id and the names as of the rejection), the free note,
/// when, by whom and where. Kept after a reinstatement or a replacement (BR-15); the timeline keeps every rejection.
/// </summary>
public sealed class SampleRejection : ValueObject
{
    public const int MaxReasonNameLength = 400;

    public SampleRejection(RejectionReason reason, string? note, DateTimeOffset atUtc, Guid byUserId, Guid branchId)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ReasonId = reason.ReasonId;
        ReasonNameEn = Clip(reason.NameEn);
        ReasonNameAr = Clip(reason.NameAr);
        Note = note;
        AtUtc = atUtc;
        ByUserId = byUserId;
        BranchId = branchId;
    }

    /// <summary>Reserved for object materialisers (EF Core owned type).</summary>
    private SampleRejection()
    {
    }

    public Guid ReasonId { get; private set; }

    public string? ReasonNameEn { get; private set; }

    public string? ReasonNameAr { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public Guid ByUserId { get; private set; }

    public Guid BranchId { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ReasonId;
        yield return ReasonNameEn;
        yield return ReasonNameAr;
        yield return Note;
        yield return AtUtc;
        yield return ByUserId;
        yield return BranchId;
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxReasonNameLength ? trimmed : trimmed[..MaxReasonNameLength];
    }
}
