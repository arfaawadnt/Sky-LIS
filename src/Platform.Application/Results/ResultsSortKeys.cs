using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Results;

/// <summary>
/// The sortable columns of the worklist (CP-18, validation v0.5.0 B-03): the repository orders the whole filtered list
/// before paging. Names sort by the language on screen (<c>patientEn</c>/<c>patientAr</c>, <c>testEn</c>/<c>testAr</c>);
/// <c>status</c> follows the workflow (Ordered → Printed); <c>delay</c> is the time past the expected time (now, or the
/// verification for a signed result). The consumed percentage is a ratio of intervals and is not sortable.
/// </summary>
public static class ResultsSortKeys
{
    public static readonly SortKeys Worklist = new("accession", "patientEn", "patientAr", "testEn", "testAr", "status", "expectedAtUtc", "delay");
}
