using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Samples;

/// <summary>
/// The sortable columns of the sample registers (CP-14 collection list, CP-16 rejected samples; validation v0.5.0
/// B-03): the repository orders the whole filtered list before paging; <c>status</c> follows the workflow
/// (Ordered → Collected → Received → Rejected → Cancelled → Replaced).
/// </summary>
public static class SamplesSortKeys
{
    public static readonly SortKeys Samples = new("sampleNumber", "accession", "status", "rejectedAtUtc");
}
