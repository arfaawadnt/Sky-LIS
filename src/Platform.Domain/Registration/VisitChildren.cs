using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>
/// A tube of a visit (<c>app.visit_tubes</c>; BR-02): the id Samples keys its sample on, the sample number printed as
/// Code 128 (BR-01: unique, never reused) and the merge key with the names printed on the label (snapshot). A tube is
/// live while one of its lines is Ordered; it is never deleted.
/// </summary>
public sealed class VisitTube : Entity, ITenantScoped
{
    public const int MaxSampleNumberLength = 20;
    public const int MaxUnitCodeLength = 8;
    public const int MaxNameLength = 400;

    private VisitTube()
    {
        SampleNumber = string.Empty;
        UnitCode = string.Empty;
        ClinicalGroupNameEn = string.Empty;
        ClinicalGroupNameAr = string.Empty;
        SampleTypeNameEn = string.Empty;
        SampleTypeNameAr = string.Empty;
    }

    private VisitTube(Guid visitId, string sampleNumber, TubeSpec spec, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        SampleNumber = sampleNumber;
        ClinicalGroupId = spec.ClinicalGroupId;
        SampleTypeId = spec.SampleTypeId;
        SampleConditionId = spec.SampleConditionId;
        UnitCode = Clip(spec.UnitCode, MaxUnitCodeLength);
        ClinicalGroupNameEn = Clip(spec.ClinicalGroupNameEn, MaxNameLength);
        ClinicalGroupNameAr = Clip(spec.ClinicalGroupNameAr, MaxNameLength);
        SampleTypeNameEn = Clip(spec.SampleTypeNameEn, MaxNameLength);
        SampleTypeNameAr = Clip(spec.SampleTypeNameAr, MaxNameLength);
        SampleConditionNameEn = spec.SampleConditionNameEn is null ? null : Clip(spec.SampleConditionNameEn, MaxNameLength);
        SampleConditionNameAr = spec.SampleConditionNameAr is null ? null : Clip(spec.SampleConditionNameAr, MaxNameLength);
        CreatedAtUtc = nowUtc;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    public string SampleNumber { get; private set; }

    public Guid ClinicalGroupId { get; private set; }

    public Guid SampleTypeId { get; private set; }

    public Guid? SampleConditionId { get; private set; }

    /// <summary>The clinical group's label letter (LDM §2.2: C, H …).</summary>
    public string UnitCode { get; private set; }

    public string ClinicalGroupNameEn { get; private set; }

    public string ClinicalGroupNameAr { get; private set; }

    public string SampleTypeNameEn { get; private set; }

    public string SampleTypeNameAr { get; private set; }

    public string? SampleConditionNameEn { get; private set; }

    public string? SampleConditionNameAr { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TubeKey Key => new(ClinicalGroupId, SampleTypeId, SampleConditionId);

    internal static VisitTube Create(Guid visitId, string sampleNumber, TubeSpec spec, DateTimeOffset nowUtc)
    {
        var number = (sampleNumber ?? string.Empty).Trim();
        if (number.Length is 0 or > MaxSampleNumberLength)
        {
            throw new DomainException(RegistrationCodes.HeaderInvalid, "A tube needs a sample number.", DomainFault.Validation);
        }

        return new VisitTube(visitId, number, spec, nowUtc);
    }

    private static string Clip(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max];
    }
}

/// <summary>An answer to a preparation question (<c>app.visit_preparation_answers</c>), with the question as it was asked.</summary>
public sealed class VisitPreparationAnswer : Entity, ITenantScoped
{
    public const int MaxAnswerLength = 400;
    public const int MaxQuestionLength = 400;

    private VisitPreparationAnswer()
    {
        QuestionEn = string.Empty;
        QuestionAr = string.Empty;
        Answer = string.Empty;
    }

    private VisitPreparationAnswer(Guid visitId, PreparationAnswerInput input, Guid actorUserId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        PreparationId = input.PreparationId;
        QuestionEn = Clip(input.QuestionEn, MaxQuestionLength);
        QuestionAr = Clip(input.QuestionAr, MaxQuestionLength);
        Answer = input.Answer;
        AnsweredAtUtc = nowUtc;
        AnsweredByUserId = actorUserId;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid PreparationId { get; private set; }

    public string QuestionEn { get; private set; }

    public string QuestionAr { get; private set; }

    public string Answer { get; private set; }

    public DateTimeOffset AnsweredAtUtc { get; private set; }

    public Guid AnsweredByUserId { get; private set; }

    internal static VisitPreparationAnswer Create(Guid visitId, PreparationAnswerInput input, Guid actorUserId, DateTimeOffset nowUtc) =>
        new(visitId, input, actorUserId, nowUtc);

    /// <summary>A new answer to the same question replaces the text; the field history keeps the old one.</summary>
    internal void Reanswer(string answer, Guid actorUserId, DateTimeOffset nowUtc)
    {
        Answer = answer;
        AnsweredAtUtc = nowUtc;
        AnsweredByUserId = actorUserId;
    }

    private static string Clip(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max];
    }
}

/// <summary>
/// A logged money transfer (<c>app.visit_transfers</c>; CP-25 "every transfer is logged", LDM "Pateint Transfer"
/// report): a contract transfer with the visit totals before and after, or a share shift of one line with the line's
/// values before and after. Always with reason, actor and time (BR-15); never changed.
/// </summary>
public sealed class VisitTransfer : Entity, ITenantScoped
{
    public const int MaxReasonLength = 1000;

    private VisitTransfer()
    {
        Reason = string.Empty;
    }

    private VisitTransfer(
        Guid visitId,
        VisitTransferKind kind,
        Guid? lineId,
        Guid? fromPayerId,
        Guid? fromContractTierId,
        Guid? toPayerId,
        Guid? toContractTierId,
        decimal patientShareBefore,
        decimal patientShareAfter,
        decimal payerShareBefore,
        decimal payerShareAfter,
        Guid? reasonId,
        string reason,
        Guid actorUserId,
        DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        Kind = kind;
        LineId = lineId;
        FromPayerId = fromPayerId;
        FromContractTierId = fromContractTierId;
        ToPayerId = toPayerId;
        ToContractTierId = toContractTierId;
        PatientShareBefore = patientShareBefore;
        PatientShareAfter = patientShareAfter;
        PayerShareBefore = payerShareBefore;
        PayerShareAfter = payerShareAfter;
        ReasonId = reasonId;
        Reason = reason;
        ActorUserId = actorUserId;
        AtUtc = nowUtc;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    public VisitTransferKind Kind { get; private set; }

    public Guid? LineId { get; private set; }

    public Guid? FromPayerId { get; private set; }

    public Guid? FromContractTierId { get; private set; }

    public Guid? ToPayerId { get; private set; }

    public Guid? ToContractTierId { get; private set; }

    public decimal PatientShareBefore { get; private set; }

    public decimal PatientShareAfter { get; private set; }

    public decimal PayerShareBefore { get; private set; }

    public decimal PayerShareAfter { get; private set; }

    public Guid? ReasonId { get; private set; }

    public string Reason { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    internal static VisitTransfer Contract(Guid visitId, Guid fromPayerId, Guid fromTierId, Guid toPayerId, Guid toTierId, decimal patientBefore, decimal patientAfter, decimal payerBefore, decimal payerAfter, Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc) =>
        new(visitId, VisitTransferKind.Contract, null, fromPayerId, fromTierId, toPayerId, toTierId, patientBefore, patientAfter, payerBefore, payerAfter, reasonId, reason, actorUserId, nowUtc);

    internal static VisitTransfer Shares(Guid visitId, Guid lineId, Guid payerId, Guid tierId, decimal patientBefore, decimal patientAfter, decimal payerBefore, decimal payerAfter, Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc) =>
        new(visitId, VisitTransferKind.LineShares, lineId, payerId, tierId, payerId, tierId, patientBefore, patientAfter, payerBefore, payerAfter, reasonId, reason, actorUserId, nowUtc);
}
