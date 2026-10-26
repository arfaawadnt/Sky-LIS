using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>The BR-02 merge key of a tube: same clinical group, sample type and sample condition share one tube.</summary>
public sealed record TubeKey(Guid ClinicalGroupId, Guid SampleTypeId, Guid? SampleConditionId);

/// <summary>
/// What a tube is made of, resolved from the catalogue by the order planner: the merge key plus the names printed on
/// labels and the job order (snapshot at tube creation, LDM §2.2 label content). <paramref name="MergesIntoConditionId"/>
/// (LIS-CR-015 M4) is the active condition of the same sample type whose tube this need may join (compatible conditions):
/// the planner uses it only when that tube is live on the visit or needed by the same request.
/// </summary>
public sealed record TubeSpec(
    Guid ClinicalGroupId,
    Guid SampleTypeId,
    Guid? SampleConditionId,
    string UnitCode,
    string ClinicalGroupNameEn,
    string ClinicalGroupNameAr,
    string SampleTypeNameEn,
    string SampleTypeNameAr,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    Guid? MergesIntoConditionId = null)
{
    public TubeKey Key => new(ClinicalGroupId, SampleTypeId, SampleConditionId);

    /// <summary>The key of the tube this need may join instead of its own (same group and type, the target condition); null when none.</summary>
    public TubeKey? MergeKey => MergesIntoConditionId is { } target && target != SampleConditionId ? Key with { SampleConditionId = target } : null;

    /// <summary>This spec as a need of <see cref="TubePlanner"/>.</summary>
    public TubePlanner.Need Need => new(Key, MergeKey);
}

/// <summary>
/// One service resolved for ordering (intake §5 "Order planning"): the catalogue snapshot, the prices of the contract
/// (BR-06), the expected result date (BR-13), the tube it needs (BR-02, <see langword="null"/> for services without a
/// sample) and the tests results are created for. A package carries its member services in <paramref name="Members"/>
/// (kind <see cref="VisitLineKind.PackageMember"/>, prices 0). A formula operand (LIS-CR-015, kind
/// <see cref="VisitLineKind.FormulaOperand"/>, prices 0) names the service of its formula line in
/// <paramref name="FormulaServiceId"/> and whether it shows on the receipt in <paramref name="VisibleOnReceipt"/>.
/// </summary>
public sealed record VisitOrderItem(
    Guid ServiceId,
    string ServiceCode,
    string ServiceNameEn,
    string ServiceNameAr,
    string ServiceType,
    VisitLineKind Kind,
    TubeSpec? Tube,
    IReadOnlyList<Guid> TestIds,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    DateTimeOffset? ExpectedResultAtUtc,
    bool Reserved,
    VisitLineOrigin Origin,
    Guid? AddedByRuleId,
    IReadOnlyList<VisitOrderItem> Members,
    Guid? FormulaServiceId = null,
    bool VisibleOnReceipt = true)
{
    /// <summary>The service ids this item puts on the visit: itself and its package members (INV-REG-002).</summary>
    public IEnumerable<Guid> AllServiceIds => Members.Select(member => member.ServiceId).Prepend(ServiceId);
}

/// <summary>The header of a new visit, validated by <see cref="Visit.Register"/>; the branch is the session's (TENANT-004).</summary>
public sealed record VisitRegistration(
    string Accession,
    Guid PatientId,
    Guid BranchId,
    VisitLocationType LocationType,
    string? TreatmentNo,
    Guid? ReferringDoctorId,
    Guid PayerId,
    Guid ContractTierId,
    string? InsuranceId,
    Guid? DiagnosisId,
    Guid? BookingId,
    Guid? BatchId,
    bool Urgent,
    bool Biohazard,
    bool CollectedOutside,
    int UtcOffsetMinutes,
    Guid? CollectorUserId,
    IReadOnlyList<OutsideSample>? OutsideSamples = null);

/// <summary>
/// A sample the clerk marks "collected outside the laboratory" at registration (LIS-CR-008 C-02): one tube of the dry run
/// (<paramref name="Key"/>) or every tube of a sample type (<paramref name="SampleTypeId"/> without a key), with the time
/// it was collected when the clerk typed one (else the registration time).
/// </summary>
public sealed record OutsideSample(TubeKey? Key, Guid? SampleTypeId, DateTimeOffset? CollectedAtUtc)
{
    /// <summary>The key wins; without one, every tube of the sample type matches.</summary>
    public bool Matches(TubeKey key) =>
        Key is { } own ? own == key : SampleTypeId is { } type && type == key.SampleTypeId;
}

/// <summary>The editable details of a saved visit (V5). "Collected outside" is a property of each tube since LIS-CR-008 and is not edited here.</summary>
public sealed record VisitDetails(string? TreatmentNo, Guid? ReferringDoctorId, string? InsuranceId, bool Urgent, bool Biohazard);

/// <summary>
/// A discount the actor applies (V1/V6): kind and value, the catalogue reason (kind Discount) with its name for the
/// record, and the actor's cap in percent (<c>EffectiveDiscountCap</c>, INV-REG-005).
/// </summary>
public sealed record DiscountRequest(DiscountKind Kind, decimal Value, Guid ReasonId, string Reason, int CapPercent);

/// <summary>A preparation answer with the question as it was asked (snapshot).</summary>
public sealed record PreparationAnswerInput(Guid PreparationId, string QuestionEn, string QuestionAr, string Answer);

/// <summary>A line's prices under a contract (BR-06), used by the contract transfer.</summary>
public sealed record LinePrice(decimal ListPrice, decimal PatientShare, decimal PayerShare);

/// <summary>A Reserved line to release (V3): its tube spec (null when it needs none) and its new expected result date.</summary>
public sealed record LineRelease(Guid LineId, TubeSpec? Tube, DateTimeOffset? ExpectedResultAtUtc);

/// <summary>Sample numbers issued by the handler before the aggregate creates the tubes (P-12 counter, BR-01).</summary>
public sealed class SampleNumberSource
{
    private readonly Queue<string> _numbers;

    public SampleNumberSource(IEnumerable<string> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        _numbers = new Queue<string>(numbers);
    }

    public static SampleNumberSource Empty => new([]);

    public int Remaining => _numbers.Count;

    internal string Next()
    {
        if (_numbers.Count == 0)
        {
            throw new DomainException(RegistrationCodes.HeaderInvalid, "Not enough sample numbers were issued for the tubes of this order.", DomainFault.Unclassified);
        }

        return _numbers.Dequeue();
    }
}
