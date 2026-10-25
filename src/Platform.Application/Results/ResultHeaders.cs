using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Application.Registration;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;
using Platform.Domain.Settings;

namespace Platform.Application.Results;

/// <summary>The header data of a visit for the case view and the documents.</summary>
internal sealed record VisitHeaderData(
    PatientSummary? Patient,
    ResultVisitInfo? Visit,
    VisitHeaderView VisitView,
    PatientHeaderView PatientView,
    NamedReference? Payer,
    DoctorView? Doctor);

/// <summary>Loads the visit, patient, payer, branch, doctor and condition names of a visit's results through the read ports.</summary>
internal sealed class ResultHeaderLoader(IPatientLookup patients, IVisitLookup visits, IResultReferenceLookup references, IDoctorLookup doctors)
{
    public async Task<VisitHeaderData> LoadAsync(Result anyResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anyResult);
        var patient = await patients.GetAsync(anyResult.PatientId, cancellationToken);
        var stored = await visits.GetAsync(anyResult.VisitId, cancellationToken);
        var visit = stored is null ? null : new ResultVisitInfo(stored.Id, stored.Accession, stored.PatientId, stored.BranchId, stored.PayerId, stored.RegisteredAtUtc, stored.Urgent, stored.ReferringDoctorId, stored.ContractTierId);
        var payerId = visit?.PayerId ?? anyResult.PayerId;
        var branchId = visit?.BranchId ?? anyResult.BranchId;
        var payers = payerId is { } payer ? await references.PayersAsync([payer], cancellationToken) : new Dictionary<Guid, NamedReference>();
        var branches = await references.BranchesAsync([branchId], cancellationToken);
        var conditions = patient?.PatientConditionId is { } condition ? await references.PatientConditionsAsync([condition], cancellationToken) : new Dictionary<Guid, NamedReference>();
        var doctor = visit?.DoctorId is { } doctorId ? await doctors.GetAsync(doctorId, cancellationToken) : null;
        var payerName = payerId is { } id ? payers.GetValueOrDefault(id) : null;
        var branch = branches.GetValueOrDefault(branchId);

        var visitView = new VisitHeaderView(
            anyResult.VisitId,
            visit?.Accession ?? anyResult.Accession,
            visit?.RegisteredAtUtc ?? anyResult.RegisteredAtUtc,
            branchId,
            branch?.NameEn,
            branch?.NameAr,
            payerId,
            payerName?.NameEn,
            payerName?.NameAr,
            doctor?.DoctorId,
            doctor?.NameEn,
            doctor?.NameAr,
            visit?.Urgent ?? anyResult.Urgent);
        return new VisitHeaderData(patient, visit, visitView, ResultViews.Patient(anyResult.PatientId, patient, conditions), payerName, doctor);
    }
}

/// <summary>The tenant parameters the result screens and documents read.</summary>
internal static class ResultSettings
{
    public static async Task<ResultSettingsView> ReadAsync(ITenantSettings settings, CancellationToken cancellationToken) =>
        new(
            await settings.GetAsync(SettingKeys.ReviewBeforeVerify, cancellationToken),
            await settings.GetAsync(SettingKeys.PrintUnverified, cancellationToken),
            await settings.GetAsync(SettingKeys.ResultLanguage, cancellationToken));
}

/// <summary>Builds laboratory reports of report units (preview, print, print lists).</summary>
internal sealed class ResultReportBuilder(
    ResultReadContextLoader contexts,
    ResultHeaderLoader headers,
    ICatalogueLookup catalogue,
    IReportTemplateLookup templates,
    IResultReferenceLookup references,
    ITenantSettings settings,
    IResultRepository results,
    ResultVisibility visibility)
{
    public async Task<LaboratoryReportDocument> BuildAsync(string key, IReadOnlyList<Result> unit, bool preview, DateTimeOffset nowUtc, string? generatedBy, CancellationToken cancellationToken)
    {
        var first = unit.FirstOrDefault(result => result.IsLive) ?? unit[0];
        var context = await contexts.LoadAsync(unit, cancellationToken);
        var header = await headers.LoadAsync(first, cancellationToken);
        var group = context.Groups.GetValueOrDefault(first.ClinicalGroupId) ?? await catalogue.GetClinicalGroupAsync(first.ClinicalGroupId, cancellationToken);
        var template = group?.ReportTemplateId is { } templateId ? await templates.GetAsync(templateId, cancellationToken) : null;
        var letterhead = await references.LetterheadAsync(header.VisitView.BranchId, cancellationToken);
        var language = await settings.GetAsync(SettingKeys.ResultLanguage, cancellationToken);
        if (template?.Language is ReportTemplateLanguage.En)
        {
            language = "en";
        }
        else if (template?.Language is ReportTemplateLanguage.Ar)
        {
            language = "ar";
        }

        // LIS-CR-010: contract → template → laboratory; the Cumulative design adds the previous values of each printable line.
        var design = (await ResultReportDesign.LoadAsync(header.Visit?.ContractTierId, references, settings, cancellationToken)).For(template?.Design);
        var history = design.Design == ReportDesign.Cumulative
            ? await ResultCumulativeHistory.LoadAsync(results, visibility, unit.Where(result => result.IsLive && !result.IsHiddenHelper), cancellationToken)
            : null;
        var report = new ReportHeader(header.Patient, header.Visit, header.Payer, header.Doctor, letterhead, template, language, design, history);
        return ResultDocuments.Report(key, unit, context, report, preview, nowUtc, generatedBy);
    }
}
