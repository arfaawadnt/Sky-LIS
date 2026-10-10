using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Samples;

namespace Platform.Application.Samples;

/// <summary>Filters of the sample registers (intake §14). Dates bound the time the sample was ordered; <see cref="Search"/> matches the start of the sample number or the accession.</summary>
public sealed record SampleFilter(
    Guid? VisitId,
    string? Accession,
    SampleStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? BranchId,
    Guid? ClinicalGroupId,
    string? Search);

/// <summary>Write side plus the projections of the module's registers and documents. Every read is bounded to the current tenant.</summary>
public interface ISampleRepository : IRepository<Sample>
{
    /// <summary>The tube ids among <paramref name="tubeIds"/> that already have a sample of any generation (the policy's idempotency, INV-SMP-001).</summary>
    Task<IReadOnlySet<Guid>> ExistingTubeIdsAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken);

    /// <summary>The live (not Replaced) sample of a tube, tracked with its timeline.</summary>
    Task<Sample?> GetLiveByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken);

    /// <summary>The sample carrying a barcode (any generation), tracked with its timeline; <paramref name="sampleNumber"/> is already in its stored form.</summary>
    Task<Sample?> GetByNumberForUpdateAsync(string sampleNumber, CancellationToken cancellationToken);

    /// <summary>The live samples of a visit, tracked with their timelines.</summary>
    Task<IReadOnlyList<Sample>> ListLiveByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken);

    Task<Paged<SampleListItem>> ListAsync(SampleFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Samples currently Rejected whose rejection happened in the window, newest first (the dashboard's "rejected today").</summary>
    Task<IReadOnlyList<SampleListItem>> ListRejectedAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? branchId, CancellationToken cancellationToken);

    Task<Guid?> FindIdByNumberAsync(string sampleNumber, CancellationToken cancellationToken);

    Task<SampleDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The timeline rows of a sample, oldest first, with names; <c>SincePreviousSeconds</c> is left empty (the query computes it).</summary>
    Task<IReadOnlyList<SampleTimelineStep>> GetTimelineStepsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The label of a sample; <paramref name="labelCopies"/> is the tenant parameter (the services' label counts may raise it).</summary>
    Task<SampleLabelDocument?> GetLabelAsync(Guid id, int labelCopies, CancellationToken cancellationToken);

    /// <summary>The labels of every live, uncancelled sample of a visit, in unit and number order.</summary>
    Task<IReadOnlyList<SampleLabelDocument>> GetVisitLabelsAsync(Guid visitId, int labelCopies, CancellationToken cancellationToken);
}
