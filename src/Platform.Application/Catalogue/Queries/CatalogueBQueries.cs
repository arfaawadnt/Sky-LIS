using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue.Queries;

// Registers of Catalogue B (intake LIS-INT-CTB-001 §14). Queries carry a policy like commands (D-04) and read
// projections only. Every list: free-text search, active flag (null = both), the list's own filter, paging.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListPreparationsQuery(string? Search, bool? IsActive, string? AnswerType, int? Page, int? PageSize) : IQuery<Paged<PreparationListItem>>;

public sealed class ListPreparationsValidator : AbstractValidator<ListPreparationsQuery>
{
    public ListPreparationsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.AnswerType).OptionalEnumName<ListPreparationsQuery, PreparationAnswerType>();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListPreparationsHandler(IPreparationRepository preparations) : IQueryHandler<ListPreparationsQuery, Paged<PreparationListItem>>
{
    public Task<Paged<PreparationListItem>> Handle(ListPreparationsQuery query, CancellationToken cancellationToken) =>
        preparations.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), CatalogueBRules.ParseOptional<PreparationAnswerType>(query.AnswerType), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetPreparationQuery(Guid PreparationId) : IQuery<PreparationListItem>;

public sealed class GetPreparationHandler(IPreparationRepository preparations) : IQueryHandler<GetPreparationQuery, PreparationListItem>
{
    public async Task<PreparationListItem> Handle(GetPreparationQuery query, CancellationToken cancellationToken) =>
        await preparations.GetListItemAsync(query.PreparationId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.PreparationNotFound, "Preparation");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListCommentTemplatesQuery(string? Search, bool? IsActive, Guid? ClinicalGroupId, int? Page, int? PageSize) : IQuery<Paged<CommentTemplateListItem>>;

public sealed class ListCommentTemplatesValidator : AbstractValidator<ListCommentTemplatesQuery>
{
    public ListCommentTemplatesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListCommentTemplatesHandler(ICommentTemplateRepository templates) : IQueryHandler<ListCommentTemplatesQuery, Paged<CommentTemplateListItem>>
{
    public Task<Paged<CommentTemplateListItem>> Handle(ListCommentTemplatesQuery query, CancellationToken cancellationToken) =>
        templates.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), query.ClinicalGroupId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetCommentTemplateQuery(Guid CommentTemplateId) : IQuery<CommentTemplateListItem>;

public sealed class GetCommentTemplateHandler(ICommentTemplateRepository templates) : IQueryHandler<GetCommentTemplateQuery, CommentTemplateListItem>
{
    public async Task<CommentTemplateListItem> Handle(GetCommentTemplateQuery query, CancellationToken cancellationToken) =>
        await templates.GetListItemAsync(query.CommentTemplateId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.CommentTemplateNotFound, "Comment");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListDoctorsQuery(string? Search, bool? IsActive, Guid? BranchId, int? Page, int? PageSize) : IQuery<Paged<DoctorListItem>>;

public sealed class ListDoctorsValidator : AbstractValidator<ListDoctorsQuery>
{
    public ListDoctorsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListDoctorsHandler(IDoctorRepository doctors) : IQueryHandler<ListDoctorsQuery, Paged<DoctorListItem>>
{
    public Task<Paged<DoctorListItem>> Handle(ListDoctorsQuery query, CancellationToken cancellationToken) =>
        doctors.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), query.BranchId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetDoctorQuery(Guid DoctorId) : IQuery<DoctorListItem>;

public sealed class GetDoctorHandler(IDoctorRepository doctors) : IQueryHandler<GetDoctorQuery, DoctorListItem>
{
    public async Task<DoctorListItem> Handle(GetDoctorQuery query, CancellationToken cancellationToken) =>
        await doctors.GetListItemAsync(query.DoctorId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DoctorNotFound, "Doctor");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListDiagnosesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<DiagnosisListItem>>;

public sealed class ListDiagnosesValidator : AbstractValidator<ListDiagnosesQuery>
{
    public ListDiagnosesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListDiagnosesHandler(IDiagnosisRepository diagnoses) : IQueryHandler<ListDiagnosesQuery, Paged<DiagnosisListItem>>
{
    public Task<Paged<DiagnosisListItem>> Handle(ListDiagnosesQuery query, CancellationToken cancellationToken) =>
        diagnoses.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetDiagnosisQuery(Guid DiagnosisId) : IQuery<DiagnosisListItem>;

public sealed class GetDiagnosisHandler(IDiagnosisRepository diagnoses) : IQueryHandler<GetDiagnosisQuery, DiagnosisListItem>
{
    public async Task<DiagnosisListItem> Handle(GetDiagnosisQuery query, CancellationToken cancellationToken) =>
        await diagnoses.GetListItemAsync(query.DiagnosisId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DiagnosisNotFound, "Diagnosis");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListSendingLabsQuery(string? Search, bool? IsActive, Guid? PayerId, int? Page, int? PageSize) : IQuery<Paged<SendingLabListItem>>;

public sealed class ListSendingLabsValidator : AbstractValidator<ListSendingLabsQuery>
{
    public ListSendingLabsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListSendingLabsHandler(ISendingLabRepository labs) : IQueryHandler<ListSendingLabsQuery, Paged<SendingLabListItem>>
{
    public Task<Paged<SendingLabListItem>> Handle(ListSendingLabsQuery query, CancellationToken cancellationToken) =>
        labs.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), query.PayerId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetSendingLabQuery(Guid SendingLabId) : IQuery<SendingLabListItem>;

public sealed class GetSendingLabHandler(ISendingLabRepository labs) : IQueryHandler<GetSendingLabQuery, SendingLabListItem>
{
    public async Task<SendingLabListItem> Handle(GetSendingLabQuery query, CancellationToken cancellationToken) =>
        await labs.GetListItemAsync(query.SendingLabId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.SendingLabNotFound, "Sending lab");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListShortcutSetsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<ShortcutSetListItem>>;

public sealed class ListShortcutSetsValidator : AbstractValidator<ListShortcutSetsQuery>
{
    public ListShortcutSetsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListShortcutSetsHandler(IShortcutSetRepository sets) : IQueryHandler<ListShortcutSetsQuery, Paged<ShortcutSetListItem>>
{
    public Task<Paged<ShortcutSetListItem>> Handle(ListShortcutSetsQuery query, CancellationToken cancellationToken) =>
        sets.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetShortcutSetQuery(Guid ShortcutSetId) : IQuery<ShortcutSetListItem>;

public sealed class GetShortcutSetHandler(IShortcutSetRepository sets) : IQueryHandler<GetShortcutSetQuery, ShortcutSetListItem>
{
    public async Task<ShortcutSetListItem> Handle(GetShortcutSetQuery query, CancellationToken cancellationToken) =>
        await sets.GetListItemAsync(query.ShortcutSetId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ShortcutSetNotFound, "Shortcut set");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListTestRulesQuery(string? Search, bool? IsActive, Guid? ClinicalGroupId, int? Page, int? PageSize) : IQuery<Paged<TestRuleListItem>>;

public sealed class ListTestRulesValidator : AbstractValidator<ListTestRulesQuery>
{
    public ListTestRulesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListTestRulesHandler(ITestRuleRepository rules) : IQueryHandler<ListTestRulesQuery, Paged<TestRuleListItem>>
{
    public Task<Paged<TestRuleListItem>> Handle(ListTestRulesQuery query, CancellationToken cancellationToken) =>
        rules.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), query.ClinicalGroupId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetTestRuleQuery(Guid TestRuleId) : IQuery<TestRuleListItem>;

public sealed class GetTestRuleHandler(ITestRuleRepository rules) : IQueryHandler<GetTestRuleQuery, TestRuleListItem>
{
    public async Task<TestRuleListItem> Handle(GetTestRuleQuery query, CancellationToken cancellationToken) =>
        await rules.GetListItemAsync(query.TestRuleId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.TestRuleNotFound, "Test rule");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListReasonsQuery(string? Search, bool? IsActive, string? Kind, int? Page, int? PageSize) : IQuery<Paged<ReasonListItem>>;

public sealed class ListReasonsValidator : AbstractValidator<ListReasonsQuery>
{
    public ListReasonsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Kind).OptionalEnumName<ListReasonsQuery, ReasonKind>();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListReasonsHandler(IReasonRepository reasons) : IQueryHandler<ListReasonsQuery, Paged<ReasonListItem>>
{
    public Task<Paged<ReasonListItem>> Handle(ListReasonsQuery query, CancellationToken cancellationToken) =>
        reasons.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), CatalogueBRules.ParseOptional<ReasonKind>(query.Kind), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetReasonQuery(Guid ReasonId) : IQuery<ReasonListItem>;

public sealed class GetReasonHandler(IReasonRepository reasons) : IQueryHandler<GetReasonQuery, ReasonListItem>
{
    public async Task<ReasonListItem> Handle(GetReasonQuery query, CancellationToken cancellationToken) =>
        await reasons.GetListItemAsync(query.ReasonId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReasonNotFound, "Reason");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListReportTemplatesQuery(string? Search, bool? IsActive, string? Type, int? Page, int? PageSize) : IQuery<Paged<ReportTemplateListItem>>;

public sealed class ListReportTemplatesValidator : AbstractValidator<ListReportTemplatesQuery>
{
    public ListReportTemplatesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Type).OptionalEnumName<ListReportTemplatesQuery, ReportTemplateType>();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListReportTemplatesHandler(IReportTemplateRepository templates) : IQueryHandler<ListReportTemplatesQuery, Paged<ReportTemplateListItem>>
{
    public Task<Paged<ReportTemplateListItem>> Handle(ListReportTemplatesQuery query, CancellationToken cancellationToken) =>
        templates.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), CatalogueBRules.ParseOptional<ReportTemplateType>(query.Type), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetReportTemplateQuery(Guid ReportTemplateId) : IQuery<ReportTemplateListItem>;

public sealed class GetReportTemplateHandler(IReportTemplateRepository templates) : IQueryHandler<GetReportTemplateQuery, ReportTemplateListItem>
{
    public async Task<ReportTemplateListItem> Handle(GetReportTemplateQuery query, CancellationToken cancellationToken) =>
        await templates.GetListItemAsync(query.ReportTemplateId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ReportTemplateNotFound, "Report template");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListDrugTypesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<DrugTypeListItem>>;

public sealed class ListDrugTypesValidator : AbstractValidator<ListDrugTypesQuery>
{
    public ListDrugTypesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListDrugTypesHandler(IDrugTypeRepository drugTypes) : IQueryHandler<ListDrugTypesQuery, Paged<DrugTypeListItem>>
{
    public Task<Paged<DrugTypeListItem>> Handle(ListDrugTypesQuery query, CancellationToken cancellationToken) =>
        drugTypes.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetDrugTypeQuery(Guid DrugTypeId) : IQuery<DrugTypeListItem>;

public sealed class GetDrugTypeHandler(IDrugTypeRepository drugTypes) : IQueryHandler<GetDrugTypeQuery, DrugTypeListItem>
{
    public async Task<DrugTypeListItem> Handle(GetDrugTypeQuery query, CancellationToken cancellationToken) =>
        await drugTypes.GetListItemAsync(query.DrugTypeId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DrugTypeNotFound, "Drug type");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListCommercialNamesQuery(string? Search, bool? IsActive, Guid? DrugTypeId, int? Page, int? PageSize) : IQuery<Paged<CommercialNameListItem>>;

public sealed class ListCommercialNamesValidator : AbstractValidator<ListCommercialNamesQuery>
{
    public ListCommercialNamesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListCommercialNamesHandler(ICommercialNameRepository commercialNames) : IQueryHandler<ListCommercialNamesQuery, Paged<CommercialNameListItem>>
{
    public Task<Paged<CommercialNameListItem>> Handle(ListCommercialNamesQuery query, CancellationToken cancellationToken) =>
        commercialNames.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), query.DrugTypeId, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetCommercialNameQuery(Guid CommercialNameId) : IQuery<CommercialNameListItem>;

public sealed class GetCommercialNameHandler(ICommercialNameRepository commercialNames) : IQueryHandler<GetCommercialNameQuery, CommercialNameListItem>
{
    public async Task<CommercialNameListItem> Handle(GetCommercialNameQuery query, CancellationToken cancellationToken) =>
        await commercialNames.GetListItemAsync(query.CommercialNameId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.CommercialNameNotFound, "Commercial name");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListAntibioticsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<AntibioticListItem>>;

public sealed class ListAntibioticsValidator : AbstractValidator<ListAntibioticsQuery>
{
    public ListAntibioticsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListAntibioticsHandler(IAntibioticRepository antibiotics) : IQueryHandler<ListAntibioticsQuery, Paged<AntibioticListItem>>
{
    public Task<Paged<AntibioticListItem>> Handle(ListAntibioticsQuery query, CancellationToken cancellationToken) =>
        antibiotics.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetAntibioticQuery(Guid AntibioticId) : IQuery<AntibioticListItem>;

public sealed class GetAntibioticHandler(IAntibioticRepository antibiotics) : IQueryHandler<GetAntibioticQuery, AntibioticListItem>
{
    public async Task<AntibioticListItem> Handle(GetAntibioticQuery query, CancellationToken cancellationToken) =>
        await antibiotics.GetListItemAsync(query.AntibioticId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.AntibioticNotFound, "Antibiotic");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListOrganismsQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<OrganismListItem>>;

public sealed class ListOrganismsValidator : AbstractValidator<ListOrganismsQuery>
{
    public ListOrganismsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListOrganismsHandler(IOrganismRepository organisms) : IQueryHandler<ListOrganismsQuery, Paged<OrganismListItem>>
{
    public Task<Paged<OrganismListItem>> Handle(ListOrganismsQuery query, CancellationToken cancellationToken) =>
        organisms.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetOrganismQuery(Guid OrganismId) : IQuery<OrganismListItem>;

public sealed class GetOrganismHandler(IOrganismRepository organisms) : IQueryHandler<GetOrganismQuery, OrganismListItem>
{
    public async Task<OrganismListItem> Handle(GetOrganismQuery query, CancellationToken cancellationToken) =>
        await organisms.GetListItemAsync(query.OrganismId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.OrganismNotFound, "Organism");
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ListSensitivityValuesQuery(string? Search, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<SensitivityValueListItem>>;

public sealed class ListSensitivityValuesValidator : AbstractValidator<ListSensitivityValuesQuery>
{
    public ListSensitivityValuesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
    }
}

public sealed class ListSensitivityValuesHandler(ISensitivityValueRepository values) : IQueryHandler<ListSensitivityValuesQuery, Paged<SensitivityValueListItem>>
{
    public Task<Paged<SensitivityValueListItem>> Handle(ListSensitivityValuesQuery query, CancellationToken cancellationToken) =>
        values.ListAsync(new CatalogueBFilter(query.Search, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record GetSensitivityValueQuery(Guid SensitivityValueId) : IQuery<SensitivityValueListItem>;

public sealed class GetSensitivityValueHandler(ISensitivityValueRepository values) : IQueryHandler<GetSensitivityValueQuery, SensitivityValueListItem>
{
    public async Task<SensitivityValueListItem> Handle(GetSensitivityValueQuery query, CancellationToken cancellationToken) =>
        await values.GetListItemAsync(query.SensitivityValueId, cancellationToken) ?? throw CatalogueBRules.NotFound(CatalogueBCodes.SensitivityValueNotFound, "Sensitivity value");
}

/// <summary>"Check rule" (CP-52 validate, T5): the candidate's errors and warnings against the tenant's catalogue and the other active rules. Saves nothing.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.View)]
public sealed record ValidateTestRuleQuery(Guid? RuleId, IReadOnlyList<Guid> TriggerServiceIds, IReadOnlyList<Guid> AddedServiceIds) : IQuery<TestRuleValidationResult>;

public sealed class ValidateTestRuleValidator : AbstractValidator<ValidateTestRuleQuery>
{
    public ValidateTestRuleValidator()
    {
        RuleFor(query => query.TriggerServiceIds).NotNull().Must(ids => ids is null || ids.Count <= CatalogueBGuard.MaxLinks);
        RuleFor(query => query.AddedServiceIds).NotNull().Must(ids => ids is null || ids.Count <= CatalogueBGuard.MaxLinks);
    }
}

public sealed class ValidateTestRuleHandler(ITestRuleRepository rules, ICatalogueBReferences references) : IQueryHandler<ValidateTestRuleQuery, TestRuleValidationResult>
{
    public async Task<TestRuleValidationResult> Handle(ValidateTestRuleQuery query, CancellationToken cancellationToken)
    {
        var triggers = query.TriggerServiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var added = query.AddedServiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var others = await rules.ActiveRulesAsync(cancellationToken);

        var findings = TestRuleEngine.Analyze(query.RuleId, triggers, added, others).ToList();
        var linked = triggers.Concat(added).Distinct().ToList();
        var known = await references.ServicesAsync(linked, cancellationToken);
        findings.AddRange(linked.Where(id => !known.ContainsKey(id)).Select(id => new TestRuleFinding(CatalogueBCodes.ServiceUnknown, true, id, null)));
        findings.AddRange(linked.Where(id => known.TryGetValue(id, out var service) && !service.IsActive).Select(id => new TestRuleFinding(CatalogueBCodes.TestRuleInactiveService, false, id, null)));

        var issues = findings
            .OrderByDescending(finding => finding.IsError)
            .Select(finding => new TestRuleIssue(finding.Code, finding.IsError ? "Error" : "Warning", TestRuleEngine.Describe(finding.Code), finding.ServiceId, finding.OtherRuleId))
            .ToList();
        return new TestRuleValidationResult(!findings.Any(finding => finding.IsError), issues);
    }
}
