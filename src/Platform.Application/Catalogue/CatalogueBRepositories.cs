using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>The fourteen lists of Catalogue B, as the generic activation and export requests name them.</summary>
public enum CatalogueBList
{
    Preparations = 0,
    CommentTemplates = 1,
    Doctors = 2,
    Diagnoses = 3,
    SendingLabs = 4,
    ShortcutSets = 5,
    TestRules = 6,
    Reasons = 7,
    ReportTemplates = 8,
    DrugTypes = 9,
    CommercialNames = 10,
    Antibiotics = 11,
    Organisms = 12,
    SensitivityValues = 13,
}

/// <summary>The filter every register shares: free-text search (code / name / text, case-insensitive) and the active flag (null = both).</summary>
public sealed record CatalogueBFilter(string? Search, bool? IsActive);

// Write side plus the projections each register needs (ADR-0010 carried forward). Every read is bounded to the current
// tenant. Uniqueness probes compare codes exactly (codes are stored upper-case) and names case-insensitively.

public interface IPreparationRepository : IRepository<Preparation>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<PreparationListItem>> ListAsync(CatalogueBFilter filter, PreparationAnswerType? answerType, PageRequest page, CancellationToken cancellationToken);

    Task<PreparationListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICommentTemplateRepository : IRepository<CommentTemplate>
{
    Task<bool> ShortNameExistsAsync(string shortName, CancellationToken cancellationToken);

    Task<Paged<CommentTemplateListItem>> ListAsync(CatalogueBFilter filter, Guid? clinicalGroupId, PageRequest page, CancellationToken cancellationToken);

    Task<CommentTemplateListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IDoctorRepository : IRepository<Doctor>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary><paramref name="branchId"/>: doctors offered in that branch (assigned to it or to every branch).</summary>
    Task<Paged<DoctorListItem>> ListAsync(CatalogueBFilter filter, Guid? branchId, PageRequest page, CancellationToken cancellationToken);

    Task<DoctorListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IDiagnosisRepository : IRepository<Diagnosis>
{
    Task<bool> IcdCodeExistsAsync(string icdCode, CancellationToken cancellationToken);

    Task<Paged<DiagnosisListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<DiagnosisListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISendingLabRepository : IRepository<SendingLab>
{
    Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<SendingLabListItem>> ListAsync(CatalogueBFilter filter, Guid? payerId, PageRequest page, CancellationToken cancellationToken);

    Task<SendingLabListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IShortcutSetRepository : IRepository<ShortcutSet>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<ShortcutSetListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<ShortcutSetListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITestRuleRepository : IRepository<TestRule>
{
    Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every active rule of the tenant, untracked (the rule validation and the order-rules read port work on the whole set).</summary>
    Task<IReadOnlyList<TestRule>> ActiveRulesAsync(CancellationToken cancellationToken);

    Task<Paged<TestRuleListItem>> ListAsync(CatalogueBFilter filter, Guid? clinicalGroupId, PageRequest page, CancellationToken cancellationToken);

    Task<TestRuleListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IReasonRepository : IRepository<Reason>
{
    Task<bool> NameExistsAsync(ReasonKind kind, string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<ReasonListItem>> ListAsync(CatalogueBFilter filter, ReasonKind? kind, PageRequest page, CancellationToken cancellationToken);

    Task<ReasonListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IReportTemplateRepository : IRepository<ReportTemplate>
{
    Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<ReportTemplateListItem>> ListAsync(CatalogueBFilter filter, ReportTemplateType? type, PageRequest page, CancellationToken cancellationToken);

    Task<ReportTemplateListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IDrugTypeRepository : IRepository<DrugType>
{
    Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<DrugTypeListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<DrugTypeListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICommercialNameRepository : IRepository<CommercialName>
{
    Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<CommercialNameListItem>> ListAsync(CatalogueBFilter filter, Guid? drugTypeId, PageRequest page, CancellationToken cancellationToken);

    Task<CommercialNameListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAntibioticRepository : IRepository<Antibiotic>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<AntibioticListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<AntibioticListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface IOrganismRepository : IRepository<Organism>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<OrganismListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<OrganismListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISensitivityValueRepository : IRepository<SensitivityValue>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<SensitivityValueListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<SensitivityValueListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Loads any Catalogue B record for the generic activation commands (T3/T4), tracked, in the current tenant.</summary>
public interface ICatalogueBRecords
{
    Task<CatalogueBRecord?> GetForUpdateAsync(CatalogueBList list, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The other modules' records Catalogue B links to by id (same wave: read by SQL on the agreed tables, never through
/// their types): services and clinical groups (Catalogue A), branches (Organization), payers and contract tiers (Pricing).
/// Every probe is bounded to the current tenant.
/// </summary>
public interface ICatalogueBReferences
{
    /// <summary>The ids that are not services of the tenant (any status).</summary>
    Task<IReadOnlyList<Guid>> MissingServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    /// <summary>Code, names and active flag of the given services; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, LinkedServiceView>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    Task<bool> ClinicalGroupExistsAsync(Guid clinicalGroupId, CancellationToken cancellationToken);

    /// <summary>The ids that are not branches of the tenant.</summary>
    Task<IReadOnlyList<Guid>> MissingBranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken);

    Task<bool> PayerExistsAsync(Guid payerId, CancellationToken cancellationToken);

    /// <summary>INV-CTB-008: the tier exists and belongs to the payer.</summary>
    Task<bool> ContractTierOfPayerExistsAsync(Guid contractTierId, Guid payerId, CancellationToken cancellationToken);
}
