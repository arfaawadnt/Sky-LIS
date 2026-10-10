namespace Platform.Application.Catalogue;

/// <summary>
/// BR-25 for catalogue half A: copies every active row of clinical groups, sample types (+ active conditions),
/// units, patient conditions, tests (+ bands, list values, paragraphs, formula, timing) and services (+ members) from
/// <c>sourceTenantId</c> (normally <c>TenantIds.Platform</c>) to <c>targetTenantId</c> with fresh ids and remapped
/// references. Members pointing at rows that were not copied are dropped; a service left without its mandatory
/// members is skipped. Formulas are copied verbatim (they refer to codes). The report template id is cleared unless
/// <c>reportTemplateMap</c> (catalogue B's old → new ids) maps it.
/// <para>
/// The cloner only stages rows: the caller commits once (one transaction for the whole tenant creation) and must run
/// it inside an elevated scope (it reads one tenant and writes another); it refuses otherwise. The result maps every
/// old id to its new id so later cloners (catalogue B, pricing) can remap their references.
/// </para>
/// </summary>
public interface ICatalogueACloner
{
    Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken);

    Task<CatalogueACloneResult> CloneAsync(Guid sourceTenantId, Guid targetTenantId, IReadOnlyDictionary<Guid, Guid>? reportTemplateMap, CancellationToken cancellationToken);
}

/// <summary>Old id → new id per table of catalogue half A.</summary>
public sealed record CatalogueACloneResult(
    IReadOnlyDictionary<Guid, Guid> ClinicalGroups,
    IReadOnlyDictionary<Guid, Guid> SampleTypes,
    IReadOnlyDictionary<Guid, Guid> SampleConditions,
    IReadOnlyDictionary<Guid, Guid> MeasurementUnits,
    IReadOnlyDictionary<Guid, Guid> PatientConditions,
    IReadOnlyDictionary<Guid, Guid> Tests,
    IReadOnlyDictionary<Guid, Guid> Services);
