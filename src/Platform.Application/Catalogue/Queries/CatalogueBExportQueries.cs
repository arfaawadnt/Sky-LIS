using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Contracts.Catalogue;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Catalogue.Queries;

// CSV exports of the Catalogue B registers (intake LIS-INT-CTB-001 §14, catalogue.export): the register's filters,
// every column, both languages.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportPreparationsQuery(string? Search, bool? IsActive, string? AnswerType, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportPreparationsValidator : AbstractValidator<ExportPreparationsQuery>
{
    public ExportPreparationsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.AnswerType).OptionalEnumName<ExportPreparationsQuery, PreparationAnswerType>();
    }
}

public sealed class ExportPreparationsHandler(IPreparationRepository preparations, IClock clock) : IQueryHandler<ExportPreparationsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportPreparationsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var type = CatalogueBRules.ParseOptional<PreparationAnswerType>(query.AnswerType);
        var rows = await CatalogueBCsv.AllAsync(page => preparations.ListAsync(filter, type, page, cancellationToken));
        return CatalogueBCsv.Build(
            "preparations",
            ["Code", "Question (EN)", "Question (AR)", "Answer type", "Options", "Gender", "Services", "Active"],
            rows,
            row => [row.Code, row.QuestionEn, row.QuestionAr, CatalogueBCsv.Label(language, "answerType", row.AnswerType), string.Join(" | ", row.Options), CatalogueBCsv.Label(language, "gender", row.Gender), row.Services.Count == 0 ? CatalogueBCsv.Text(language, "All services") : CatalogueBCsv.Services(row.Services), CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportCommentTemplatesQuery(string? Search, bool? IsActive, Guid? ClinicalGroupId, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportCommentTemplatesValidator : AbstractValidator<ExportCommentTemplatesQuery>
{
    public ExportCommentTemplatesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportCommentTemplatesHandler(ICommentTemplateRepository templates, IClock clock) : IQueryHandler<ExportCommentTemplatesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportCommentTemplatesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => templates.ListAsync(filter, query.ClinicalGroupId, page, cancellationToken));
        return CatalogueBCsv.Build(
            "comment-library",
            ["Short name", "Text (EN)", "Text (AR)", "Clinical group (EN)", "Clinical group (AR)", "Active"],
            rows,
            row => [row.ShortName, row.TextEn, row.TextAr, row.ClinicalGroupNameEn ?? CatalogueBCsv.Text(language, "All groups"), row.ClinicalGroupNameAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportDoctorsQuery(string? Search, bool? IsActive, Guid? BranchId, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportDoctorsValidator : AbstractValidator<ExportDoctorsQuery>
{
    public ExportDoctorsValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportDoctorsHandler(IDoctorRepository doctors, IBranchLookup branches, IClock clock) : IQueryHandler<ExportDoctorsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportDoctorsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => doctors.ListAsync(filter, query.BranchId, page, cancellationToken));
        // the branches as their codes, like the grid (UAT B-09), read through the Organization port
        var branchCodes = await branches.GetManyAsync(rows.SelectMany(row => row.BranchIds).Distinct().ToList(), cancellationToken);
        return CatalogueBCsv.Build(
            "doctors",
            ["Code", "Name (EN)", "Name (AR)", "Specialty", "Occupation", "Branches", "Share %", "Phone", "Active"],
            rows,
            row => [row.Code, row.NameEn, row.NameAr, row.Specialty, row.Occupation, row.BranchIds.Count == 0 ? CatalogueBCsv.Text(language, "All branches") : string.Join(" | ", row.BranchIds.Select(id => branchCodes.TryGetValue(id, out var branch) ? branch.Code : id.ToString())), CatalogueBCsv.Number(row.SharePercent), row.Phone, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportDiagnosesQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportDiagnosesValidator : AbstractValidator<ExportDiagnosesQuery>
{
    public ExportDiagnosesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportDiagnosesHandler(IDiagnosisRepository diagnoses, IClock clock) : IQueryHandler<ExportDiagnosesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportDiagnosesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => diagnoses.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build(
            "diagnoses",
            ["ICD code", "Name (EN)", "Name (AR)", "Services", "Active"],
            rows,
            row => [row.IcdCode, row.NameEn, row.NameAr, CatalogueBCsv.Services(row.Services), CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportSendingLabsQuery(string? Search, bool? IsActive, Guid? PayerId, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportSendingLabsValidator : AbstractValidator<ExportSendingLabsQuery>
{
    public ExportSendingLabsValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportSendingLabsHandler(ISendingLabRepository labs, IClock clock) : IQueryHandler<ExportSendingLabsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportSendingLabsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => labs.ListAsync(filter, query.PayerId, page, cancellationToken));
        return CatalogueBCsv.Build(
            "sending-labs",
            ["Name (EN)", "Name (AR)", "Payer id", "Contract tier id", "Courier (EN)", "Courier (AR)", "Phone", "Doctor (EN)", "Doctor (AR)", "Active"],
            rows,
            row => [row.NameEn, row.NameAr, row.PayerId.ToString(), row.ContractTierId.ToString(), row.CourierEn, row.CourierAr, row.Phone, row.DoctorNameEn, row.DoctorNameAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportShortcutSetsQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportShortcutSetsValidator : AbstractValidator<ExportShortcutSetsQuery>
{
    public ExportShortcutSetsValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportShortcutSetsHandler(IShortcutSetRepository sets, IClock clock) : IQueryHandler<ExportShortcutSetsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportShortcutSetsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => sets.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build(
            "shortcut-sets",
            ["Code", "Name (EN)", "Name (AR)", "Services", "Active"],
            rows,
            row => [row.Code, row.NameEn, row.NameAr, CatalogueBCsv.Services(row.Services), CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportTestRulesQuery(string? Search, bool? IsActive, Guid? ClinicalGroupId, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportTestRulesValidator : AbstractValidator<ExportTestRulesQuery>
{
    public ExportTestRulesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportTestRulesHandler(ITestRuleRepository rules, IClock clock) : IQueryHandler<ExportTestRulesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportTestRulesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => rules.ListAsync(filter, query.ClinicalGroupId, page, cancellationToken));
        return CatalogueBCsv.Build(
            "test-rules",
            ["Name (EN)", "Name (AR)", "Clinical group (EN)", "Triggers", "Added services", "Active"],
            rows,
            row => [row.NameEn, row.NameAr, row.ClinicalGroupNameEn, CatalogueBCsv.Services(row.Triggers), CatalogueBCsv.Services(row.Added), CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportReasonsQuery(string? Search, bool? IsActive, string? Kind, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportReasonsValidator : AbstractValidator<ExportReasonsQuery>
{
    public ExportReasonsValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Kind).OptionalEnumName<ExportReasonsQuery, ReasonKind>();
    }
}

public sealed class ExportReasonsHandler(IReasonRepository reasons, IClock clock) : IQueryHandler<ExportReasonsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportReasonsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var kind = CatalogueBRules.ParseOptional<ReasonKind>(query.Kind);
        var rows = await CatalogueBCsv.AllAsync(page => reasons.ListAsync(filter, kind, page, cancellationToken));
        return CatalogueBCsv.Build(
            "reasons",
            ["Kind", "Name (EN)", "Name (AR)", "Active"],
            rows,
            row => [CatalogueBCsv.Label(language, "reasonKind", row.Kind), row.NameEn, row.NameAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportReportTemplatesQuery(string? Search, bool? IsActive, string? Type, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportReportTemplatesValidator : AbstractValidator<ExportReportTemplatesQuery>
{
    public ExportReportTemplatesValidator()
    {
        RuleFor(query => query.Search).SearchText();
        RuleFor(query => query.Type).OptionalEnumName<ExportReportTemplatesQuery, ReportTemplateType>();
    }
}

public sealed class ExportReportTemplatesHandler(IReportTemplateRepository templates, IClock clock) : IQueryHandler<ExportReportTemplatesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportReportTemplatesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var type = CatalogueBRules.ParseOptional<ReportTemplateType>(query.Type);
        var rows = await CatalogueBCsv.AllAsync(page => templates.ListAsync(filter, type, page, cancellationToken));
        return CatalogueBCsv.Build(
            "report-templates",
            ["Name (EN)", "Name (AR)", "Type", "Logo", "Signature", "QR", "Language", "Header (EN)", "Header (AR)", "Footer (EN)", "Footer (AR)", "Active"],
            rows,
            row => [row.NameEn, row.NameAr, CatalogueBCsv.Label(language, "templateType", row.Type), CatalogueBCsv.Flag(row.LogoFileId is not null, language), CatalogueBCsv.Flag(row.SignatureFileId is not null, language), CatalogueBCsv.Flag(row.ShowQr, language), CatalogueBCsv.Label(language, "templateLanguage", row.Language), row.HeaderTextEn, row.HeaderTextAr, row.FooterTextEn, row.FooterTextAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportDrugTypesQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportDrugTypesValidator : AbstractValidator<ExportDrugTypesQuery>
{
    public ExportDrugTypesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportDrugTypesHandler(IDrugTypeRepository drugTypes, IClock clock) : IQueryHandler<ExportDrugTypesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportDrugTypesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => drugTypes.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build("drug-types", ["Name (EN)", "Name (AR)", "Active"], rows, row => [row.NameEn, row.NameAr, CatalogueBCsv.Flag(row.IsActive, language)], clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportCommercialNamesQuery(string? Search, bool? IsActive, Guid? DrugTypeId, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportCommercialNamesValidator : AbstractValidator<ExportCommercialNamesQuery>
{
    public ExportCommercialNamesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportCommercialNamesHandler(ICommercialNameRepository commercialNames, IClock clock) : IQueryHandler<ExportCommercialNamesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportCommercialNamesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => commercialNames.ListAsync(filter, query.DrugTypeId, page, cancellationToken));
        return CatalogueBCsv.Build(
            "commercial-names",
            ["Name (EN)", "Name (AR)", "Drug type (EN)", "Drug type (AR)", "Active"],
            rows,
            row => [row.NameEn, row.NameAr, row.DrugTypeNameEn, row.DrugTypeNameAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportAntibioticsQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportAntibioticsValidator : AbstractValidator<ExportAntibioticsQuery>
{
    public ExportAntibioticsValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportAntibioticsHandler(IAntibioticRepository antibiotics, IClock clock) : IQueryHandler<ExportAntibioticsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportAntibioticsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => antibiotics.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build(
            "antibiotics",
            ["Code", "Name (EN)", "Name (AR)", "Abbreviation", "Commercial name (EN)", "Commercial name (AR)", "Active"],
            rows,
            row => [row.Code, row.NameEn, row.NameAr, row.Abbreviation, row.CommercialNameEn, row.CommercialNameAr, CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportOrganismsQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportOrganismsValidator : AbstractValidator<ExportOrganismsQuery>
{
    public ExportOrganismsValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportOrganismsHandler(IOrganismRepository organisms, IClock clock) : IQueryHandler<ExportOrganismsQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportOrganismsQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => organisms.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build("organisms", ["Code", "Name (EN)", "Name (AR)", "Active"], rows, row => [row.Code, row.NameEn, row.NameAr, CatalogueBCsv.Flag(row.IsActive, language)], clock.UtcNow, language);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Export)]
public sealed record ExportSensitivityValuesQuery(string? Search, bool? IsActive, string? Language = null) : IQuery<CatalogueBExportFile>;

public sealed class ExportSensitivityValuesValidator : AbstractValidator<ExportSensitivityValuesQuery>
{
    public ExportSensitivityValuesValidator() => RuleFor(query => query.Search).SearchText();
}

public sealed class ExportSensitivityValuesHandler(ISensitivityValueRepository values, IClock clock) : IQueryHandler<ExportSensitivityValuesQuery, CatalogueBExportFile>
{
    public async Task<CatalogueBExportFile> Handle(ExportSensitivityValuesQuery query, CancellationToken cancellationToken)
    {
        var filter = new CatalogueBFilter(query.Search, query.IsActive);
        var language = CatalogueBCsv.LanguageOf(query.Language);
        var rows = await CatalogueBCsv.AllAsync(page => values.ListAsync(filter, page, cancellationToken));
        return CatalogueBCsv.Build(
            "sensitivity-values",
            ["Code", "Name (EN)", "Name (AR)", "Order", "Active"],
            rows,
            row => [row.Code, row.NameEn, row.NameAr, row.DisplayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture), CatalogueBCsv.Flag(row.IsActive, language)],
            clock.UtcNow, language);
    }
}
