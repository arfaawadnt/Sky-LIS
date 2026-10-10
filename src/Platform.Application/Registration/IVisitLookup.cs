namespace Platform.Application.Registration;

/// <summary>A line of a visit as other modules read it (statuses and kinds are the stored names).</summary>
public sealed record VisitLineView(
    Guid Id,
    Guid ServiceId,
    string ServiceCode,
    string ServiceNameEn,
    string ServiceNameAr,
    string ServiceType,
    string Kind,
    Guid? ParentLineId,
    Guid? TubeId,
    string Status,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    decimal Discount,
    DateTimeOffset? ExpectedResultAtUtc,
    IReadOnlyList<Guid> TestIds,
    Guid? CollectorUserId,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset? CancelledAtUtc);

/// <summary>A tube of a visit (BR-02) with its sample number.</summary>
public sealed record VisitTubeView(Guid Id, string SampleNumber, Guid ClinicalGroupId, Guid SampleTypeId, Guid? SampleConditionId, string UnitCode);

/// <summary>A visit as Billing, Results and Reporting read it: header, totals (MODULES.md §5 definitions), lines and tubes.</summary>
public sealed record VisitView(
    Guid Id,
    string Accession,
    Guid PatientId,
    Guid? OriginalPatientId,
    Guid BranchId,
    string LocationType,
    Guid PayerId,
    Guid ContractTierId,
    Guid? ReferringDoctorId,
    Guid? DiagnosisId,
    Guid? BookingId,
    Guid? BatchId,
    string Status,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    DateTimeOffset RegisteredAtUtc,
    Guid RegisteredByUserId,
    DateTimeOffset? CompletedAtUtc,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    int UtcOffsetMinutes,
    IReadOnlyList<VisitLineView> Lines,
    IReadOnlyList<VisitTubeView> Tubes);

/// <summary>
/// Read port of the Registration module (MODULES.md §1 "cross-module reads") for Billing, Results and Reporting: a visit
/// with its lines and tubes by id or by accession, bounded to the current tenant. Unknown visits are absent. The adapter
/// is <c>Platform.Infrastructure.Persistence.Registration.VisitLookup</c>.
/// </summary>
public interface IVisitLookup
{
    Task<VisitView?> GetAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Exact accession, compared as typed and upper-cased.</summary>
    Task<VisitView?> GetByAccessionAsync(string accession, CancellationToken cancellationToken);

    /// <summary>The known visits among <paramref name="visitIds"/> (at most 500 per call), keyed by id.</summary>
    Task<IReadOnlyDictionary<Guid, VisitView>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken);
}

/// <summary>
/// Whether a visit line has a verified result (CP-10 / CP-26 "verified services cannot be cancelled"). Declared by
/// Registration (MODULES.md §5); the Results module (wave 3) ships the real adapter and deletes the
/// <c>[FallbackAdapter]</c> NoOp that answers "nothing is verified".
/// </summary>
public interface IResultStatusLookup
{
    Task<bool> HasVerifiedResultsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken);
}
