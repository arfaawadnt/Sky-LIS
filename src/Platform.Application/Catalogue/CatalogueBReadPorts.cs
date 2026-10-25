using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

// Read ports Catalogue B exposes to the modules of later waves (MODULES.md §1). Adapters live in
// Platform.Infrastructure/Persistence/Catalogue and are registered by convention. Every answer is bounded to the
// current tenant and, unless stated otherwise, considers active records only.

/// <summary>A service a test rule adds to an order, with the rule that added it (for the "added by rule" hint).</summary>
public sealed record RuleAddedService(Guid RuleId, string RuleNameEn, string RuleNameAr, Guid ServiceId);

/// <summary>A diagnosis and the services it adds, in order.</summary>
public sealed record DiagnosisServices(Guid DiagnosisId, string IcdCode, string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds);

/// <summary>A preparation question to ask; <paramref name="ServiceIds"/> empty = asked for every service.</summary>
public sealed record PreparationQuestion(Guid PreparationId, string Code, string QuestionEn, string QuestionAr, PreparationAnswerType AnswerType, IReadOnlyList<string> Options, PreparationGender Gender, IReadOnlyList<Guid> ServiceIds);

/// <summary>A shortcut set and its services in the order they are added.</summary>
public sealed record ShortcutSetServices(Guid ShortcutSetId, string Code, string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds);

public sealed record ReasonOption(Guid ReasonId, ReasonKind Kind, string NameEn, string NameAr);

/// <summary>Order aids for Registration (CP-06, CP-09, CP-12): test rules, diagnoses, preparations, shortcut sets and reasons.</summary>
public interface IOrderRulesLookup
{
    /// <summary>The services the active test rules add when <paramref name="orderedServiceIds"/> are ordered (all triggers present; chained; never a service already present).</summary>
    Task<IReadOnlyList<RuleAddedService>> ServicesAddedByRulesAsync(IReadOnlyCollection<Guid> orderedServiceIds, CancellationToken cancellationToken);

    /// <summary>An active diagnosis and its services, or <see langword="null"/>.</summary>
    Task<DiagnosisServices?> DiagnosisAsync(Guid diagnosisId, CancellationToken cancellationToken);

    /// <summary>The active preparation questions for an order: linked to one of the services (or to all) and matching the patient's gender (null = not filtered).</summary>
    Task<IReadOnlyList<PreparationQuestion>> PreparationsForAsync(IReadOnlyCollection<Guid> serviceIds, PreparationGender? patientGender, CancellationToken cancellationToken);

    /// <summary>The active shortcut set typed at registration (code compared case-insensitively), or <see langword="null"/>.</summary>
    Task<ShortcutSetServices?> ShortcutSetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>The active reasons of a kind, by English name.</summary>
    Task<IReadOnlyList<ReasonOption>> ReasonsAsync(ReasonKind kind, CancellationToken cancellationToken);

    /// <summary>The reason when it exists, is active and is of <paramref name="kind"/>; otherwise <see langword="null"/> (the caller refuses with its own code).</summary>
    Task<ReasonOption?> ReasonAsync(Guid reasonId, ReasonKind kind, CancellationToken cancellationToken);
}

public sealed record ReportTemplateView(
    Guid TemplateId,
    string NameEn,
    string NameAr,
    ReportTemplateType Type,
    ReportTemplateLanguage Language,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    bool ShowQr,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr,
    bool IsActive,
    ReportDesign? Design = null);

/// <summary>Print templates for Results, Billing and Reporting (CP-19, CP-54).</summary>
public interface IReportTemplateLookup
{
    /// <summary>The template whatever its status (a clinical group keeps printing with the template it references), or <see langword="null"/>.</summary>
    Task<ReportTemplateView?> GetAsync(Guid templateId, CancellationToken cancellationToken);

    /// <summary>The active templates of a type, by English name.</summary>
    Task<IReadOnlyList<ReportTemplateView>> ActiveOfTypeAsync(ReportTemplateType type, CancellationToken cancellationToken);
}

/// <summary><paramref name="BranchIds"/> empty = offered in every branch.</summary>
public sealed record DoctorView(Guid DoctorId, string Code, string NameEn, string NameAr, string? Specialty, decimal SharePercent, IReadOnlyList<Guid> BranchIds, bool IsActive);

/// <summary>Referring doctors for Registration (CP-06 doctor) and Reporting (doctor income with share %).</summary>
public interface IDoctorLookup
{
    /// <summary>The doctor whatever the status (history keeps its doctor), or <see langword="null"/>.</summary>
    Task<DoctorView?> GetAsync(Guid doctorId, CancellationToken cancellationToken);

    /// <summary>The doctors among <paramref name="doctorIds"/> (any status); unknown ids are absent.</summary>
    Task<IReadOnlyList<DoctorView>> GetManyAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken);

    /// <summary>Reception may pick the doctor in the branch: active and unrestricted or assigned to it.</summary>
    Task<bool> IsSelectableAsync(Guid doctorId, Guid branchId, CancellationToken cancellationToken);
}

/// <summary>What one clone copied: rows per list, and links to services or clinical groups dropped because the target has no record with the same code.</summary>
public sealed record CatalogueBCloneSummary(IReadOnlyDictionary<string, int> RowsCopied, int ServiceLinksDropped, int ClinicalGroupLinksDropped);

/// <summary>
/// Copies the global Catalogue B templates (the platform tenant's active rows) to a newly provisioned tenant with
/// fresh ids (BR-25): preparations, comment library, diagnoses, shortcut sets, test rules, reasons, report templates
/// and the microbiology masters. Doctors and sending labs are laboratory-specific and are not copied; report-template
/// images are copied into the target tenant's file store with new file ids (intake A10), so the clone must run in the
/// target tenant's scope. Service and clinical-group ids are remapped by <b>code</b> to the target tenant's Catalogue A
/// rows, so Catalogue A's cloner must have run first. Runs inside the provisioning's elevated scope (it reads one
/// tenant and writes another) and writes immediately with set-based SQL in the ambient transaction.
/// </summary>
public interface ICatalogueBCloner
{
    Task<CatalogueBCloneSummary> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken);
}
