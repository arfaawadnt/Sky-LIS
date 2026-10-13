using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of Catalogue B (intake LIS-INT-CTB-001 §11): ids and the few values a reader needs. The outbox
// stamps the tenant. Edits raise no event; the field-change audit records them.

public sealed record PreparationCreated(Guid PreparationId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PreparationDeactivated(Guid PreparationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PreparationReactivated(Guid PreparationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommentTemplateCreated(Guid CommentTemplateId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommentTemplateDeactivated(Guid CommentTemplateId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommentTemplateReactivated(Guid CommentTemplateId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DoctorCreated(Guid DoctorId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DoctorDeactivated(Guid DoctorId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DoctorReactivated(Guid DoctorId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DiagnosisCreated(Guid DiagnosisId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DiagnosisDeactivated(Guid DiagnosisId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DiagnosisReactivated(Guid DiagnosisId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SendingLabCreated(Guid SendingLabId, Guid PayerId, Guid ContractTierId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SendingLabDeactivated(Guid SendingLabId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SendingLabReactivated(Guid SendingLabId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ShortcutSetCreated(Guid ShortcutSetId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ShortcutSetDeactivated(Guid ShortcutSetId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ShortcutSetReactivated(Guid ShortcutSetId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TestRuleCreated(Guid TestRuleId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TestRuleDeactivated(Guid TestRuleId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TestRuleReactivated(Guid TestRuleId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReasonCreated(Guid ReasonId, ReasonKind Kind, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReasonDeactivated(Guid ReasonId, ReasonKind Kind, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReasonReactivated(Guid ReasonId, ReasonKind Kind, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReportTemplateCreated(Guid ReportTemplateId, ReportTemplateType Type, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReportTemplateDeactivated(Guid ReportTemplateId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReportTemplateReactivated(Guid ReportTemplateId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A logo or signature image was attached (<paramref name="FileId"/>) or removed (null).</summary>
public sealed record ReportTemplateImageChanged(Guid ReportTemplateId, ReportTemplateImage Image, Guid? FileId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DrugTypeCreated(Guid DrugTypeId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DrugTypeDeactivated(Guid DrugTypeId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DrugTypeReactivated(Guid DrugTypeId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommercialNameCreated(Guid CommercialNameId, Guid DrugTypeId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommercialNameDeactivated(Guid CommercialNameId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CommercialNameReactivated(Guid CommercialNameId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AntibioticCreated(Guid AntibioticId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AntibioticDeactivated(Guid AntibioticId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AntibioticReactivated(Guid AntibioticId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record OrganismCreated(Guid OrganismId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record OrganismDeactivated(Guid OrganismId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record OrganismReactivated(Guid OrganismId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SensitivityValueCreated(Guid SensitivityValueId, string Code, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SensitivityValueDeactivated(Guid SensitivityValueId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SensitivityValueReactivated(Guid SensitivityValueId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
