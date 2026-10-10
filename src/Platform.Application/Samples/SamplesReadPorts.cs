namespace Platform.Application.Samples;

/// <summary>
/// The status of a tube for other modules (MODULES.md §1 read port). Results (wave 3) calls it to enforce the Received
/// gate (BR-03) and to show the sample status on the worklist; by tube it always answers with the live sample, so a
/// replacement (BR-18) is followed transparently.
/// </summary>
public interface ISampleStatusLookup
{
    Task<SampleStatusView?> ByTubeAsync(Guid tubeId, CancellationToken cancellationToken);

    /// <summary>The live sample of each tube that has one; tubes without a sample are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, SampleStatusView>> ByTubesAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken);

    Task<SampleStatusView?> BySampleNumberAsync(string sampleNumber, CancellationToken cancellationToken);

    Task<SampleStatusView?> BySampleIdAsync(Guid sampleId, CancellationToken cancellationToken);

    /// <summary>The live samples of a visit.</summary>
    Task<IReadOnlyList<SampleStatusView>> ByVisitAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>BR-03: true only when the tube's live sample is Received.</summary>
    Task<bool> IsResultEntryAllowedAsync(Guid tubeId, CancellationToken cancellationToken);
}

/// <summary>A sample as other modules see it; <see cref="Status"/> is the stored status name.</summary>
public sealed record SampleStatusView(
    Guid SampleId,
    Guid TubeId,
    string SampleNumber,
    Guid VisitId,
    string Status,
    DateTimeOffset? CollectedAtUtc,
    DateTimeOffset? ReceivedAtUtc)
{
    /// <summary>BR-03: results may be recorded only on a Received sample.</summary>
    public bool IsResultEntryAllowed => string.Equals(Status, nameof(Domain.Samples.SampleStatus.Received), StringComparison.Ordinal);
}

/// <summary>
/// Catalogue reasons of kind Rejection (CP-53, built by Catalogue B in wave 1) read by id through the agreed table
/// <c>app.reasons</c>. <see cref="RejectionReasonCheck.CatalogueAvailable"/> is false while that table is not installed;
/// the rejection then keeps the id only.
/// </summary>
public interface IRejectionReasonLookup
{
    Task<RejectionReasonCheck> FindAsync(Guid reasonId, CancellationToken cancellationToken);
}

public sealed record RejectionReasonRow(Guid Id, string Kind, string NameEn, string NameAr, bool IsActive)
{
    public const string RejectionKind = "Rejection";

    public bool IsUsableForRejection => IsActive && string.Equals(Kind, RejectionKind, StringComparison.OrdinalIgnoreCase);
}

public sealed record RejectionReasonCheck(bool CatalogueAvailable, RejectionReasonRow? Reason);

/// <summary>
/// The lines of a tube in Registration's <c>app.visit_lines</c> (MODULES.md §5, wave-2 table contract), for the
/// cancellation policy: a tube is cancelled only when none of its lines is Ordered or Reserved any more.
/// </summary>
public interface IVisitLineLookup
{
    /// <summary>True when a line of the tube other than <paramref name="excludingLineId"/> is Ordered or Reserved. Answers true (keep the sample) when the table is not installed.</summary>
    Task<bool> TubeHasLiveLinesAsync(Guid tubeId, Guid excludingLineId, CancellationToken cancellationToken);
}
