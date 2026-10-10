using Platform.Contracts.Results;

namespace Platform.Application.Results;

// Read ports of the Results module over other modules' tables (MODULES.md §1). Adapters live in
// Platform.Infrastructure/Persistence/Results: same-wave tables (Integration's analysers) are read by raw SQL on the
// agreed names and answer empty while a table is not installed; visits come from Registration's IVisitLookup; earlier-wave records
// (catalogue B lists, payers, branches, patient conditions, the tenant) are read through their entity types.

/// <summary>A visit as the results header needs it, from Registration's <c>IVisitLookup</c>.</summary>
public sealed record ResultVisitInfo(Guid VisitId, string Accession, Guid PatientId, Guid BranchId, Guid? PayerId, DateTimeOffset RegisteredAtUtc, bool Urgent, Guid? DoctorId);

public sealed record AnalyserOption(Guid Id, string Code, string NameEn, string NameAr);

/// <summary>Integration's <c>app.analysers</c> and <c>app.analyser_test_mappings</c> by raw SQL (same wave).</summary>
public interface IResultAnalyserDirectory
{
    /// <summary>The tests mapped (active mappings) on the analyser: the "to analyser" and worklist analyser filters.</summary>
    Task<IReadOnlyList<Guid>> MappedTestIdsAsync(Guid analyserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyserOption>> ListAsync(CancellationToken cancellationToken);
}

public sealed record NamedReference(Guid Id, string? Code, string NameEn, string NameAr);

public sealed record CultureOrganismRef(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

public sealed record CultureAntibioticRef(Guid Id, string Code, string Abbreviation, string NameEn, string NameAr, bool IsActive);

/// <summary>Reference names and lists for entry and documents (catalogue B, pricing, organization, patients, platform).</summary>
public interface IResultReferenceLookup
{
    Task<IReadOnlyList<CommentTemplateOption>> CommentTemplatesAsync(string? search, Guid? clinicalGroupId, int take, CancellationToken cancellationToken);

    Task<MicrobiologyLookups> MicrobiologyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, CultureOrganismRef>> OrganismsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, CultureAntibioticRef>> AntibioticsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The codes of the active sensitivity values (S, I, R …); empty when the catalogue has none (any code is then accepted).</summary>
    Task<IReadOnlySet<string>> SensitivityCodesAsync(CancellationToken cancellationToken);

    /// <summary>Active clinical groups in display order (worklist filter).</summary>
    Task<IReadOnlyList<LookupOption>> ClinicalGroupsAsync(CancellationToken cancellationToken);

    /// <summary>Active tests of a group (or all), code or name starting with / containing <paramref name="search"/>, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<LookupOption>> TestsAsync(Guid? clinicalGroupId, string? search, int take, CancellationToken cancellationToken);

    /// <summary>Active payers by name (worklist filter).</summary>
    Task<IReadOnlyList<LookupOption>> ActivePayersAsync(CancellationToken cancellationToken);

    Task<ReportLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, NamedReference>> BranchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, NamedReference>> PatientConditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// The result figures of a branch for the dashboard (CP-05; MODULES.md §1 read port): pending results registered in the
/// window, the unverified backlog, the delayed tests (expected time passed, not verified) and the verifications in the
/// window. <paramref name="branchId"/> null = every branch.
/// </summary>
public interface IResultKpiLookup
{
    Task<ResultKpis> ForBranchAsync(Guid? branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
