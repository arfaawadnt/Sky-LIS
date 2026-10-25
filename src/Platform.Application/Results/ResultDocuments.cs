using Platform.Application.Catalogue;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;

namespace Platform.Application.Results;

/// <summary>Everything a laboratory report needs besides the results: the visit, its names and the print template.</summary>
internal sealed record ReportHeader(
    PatientSummary? Patient,
    ResultVisitInfo? Visit,
    NamedReference? Payer,
    DoctorView? Doctor,
    ReportLetterhead Letterhead,
    ReportTemplateView? Template,
    string Language,
    ReportDesignChoice Design = default,
    IReadOnlyDictionary<Guid, IReadOnlyList<ReportPreviousValue>>? History = null);

/// <summary>The data of the module's documents (MODULES.md §1 "Documents"); rendering is the SPA's print kit.</summary>
internal static class ResultDocuments
{
    /// <summary>
    /// The laboratory report of one report unit (LDM §5.7, §12): live results in report order grouped by profile, hidden
    /// helpers excluded (BR-20), ranges only when the test prints them, culture and paragraphs, comments, the signature
    /// of the last verification, the preliminary marker while a result is unsigned, the QR with the accession.
    /// </summary>
    public static LaboratoryReportDocument Report(string key, IReadOnlyList<Result> unit, ResultReadContext context, ReportHeader header, bool preview, DateTimeOffset nowUtc, string? generatedBy)
    {
        var live = unit.Where(result => result.IsLive).ToList();
        var first = live.Count > 0 ? live[0] : unit[0];
        var group = context.Groups.GetValueOrDefault(first.ClinicalGroupId);
        var printAlone = ReportUnits.ServiceOf(key) is { } serviceId ? context.Services.GetValueOrDefault(serviceId) : null;
        var titleEn = printAlone is not null ? printAlone.ReportName ?? printAlone.NameEn : group?.ReportName ?? group?.NameEn ?? "Laboratory report";
        var titleAr = printAlone?.NameAr ?? group?.NameAr ?? "تقرير المختبر";

        var printable = ResultViews.InReportOrder(live.Where(result => !result.IsHiddenHelper), context).ToList();
        var sections = printable.GroupBy(result => result.ServiceId)
            .Select(section =>
            {
                var service = context.Services.GetValueOrDefault(section.Key);
                var isProfile = service is not null && service.Type != ServiceType.Test && section.Count() > 1;
                return new ReportSection(section.Key, isProfile ? service!.ReportName ?? service.NameEn : null, isProfile ? service!.NameAr : null, isProfile, [.. section.Select(result => Line(result, context, header.History))]);
            })
            .ToList();

        var verified = live.Where(result => result.VerifiedAtUtc is not null).OrderByDescending(result => result.VerifiedAtUtc).FirstOrDefault();
        var reviewed = live.Where(result => result.ReviewedAtUtc is not null).OrderByDescending(result => result.ReviewedAtUtc).FirstOrDefault();
        var reviewerName = reviewed?.ReviewedByUserId is { } reviewer && context.UserNames.TryGetValue(reviewer, out var name) ? name : null;
        var patient = header.Patient;
        var visit = header.Visit;
        var template = header.Template;

        return new LaboratoryReportDocument(
            header.Letterhead,
            template is null ? null : new ReportTemplateInfo(template.TemplateId, template.NameEn, template.Language.ToString(), template.ShowQr, template.LogoFileId, template.SignatureFileId, template.HeaderTextEn, template.HeaderTextAr, template.FooterTextEn, template.FooterTextAr),
            header.Language,
            key,
            titleEn,
            titleAr,
            new ReportPatientBlock(patient?.Number, patient?.FullName, patient?.NameAr, patient?.Gender, patient?.Age.Years, patient?.Age.Months, patient?.Age.Days, patient?.DateOfBirth),
            new ReportVisitBlock(first.Accession, visit?.RegisteredAtUtc ?? first.RegisteredAtUtc, header.Payer?.NameEn, header.Payer?.NameAr, header.Doctor?.NameEn, header.Doctor?.NameAr),
            Samples(live, context),
            sections,
            new ReportSignature(verified?.VerifiedByName, verified?.VerifiedAtUtc, reviewerName, reviewed?.ReviewedAtUtc),
            live.Any(result => !ResultStatuses.IsSigned(result.Status)),
            preview,
            live.Count == 0 ? 0 : live.Max(result => result.PrintCount),
            live.Count == 0 ? 0 : live.Max(result => result.AmendmentCount),
            live.Where(result => result.LastPrintedAtUtc is not null).OrderByDescending(result => result.LastPrintedAtUtc).Select(result => result.LastPrintListNumber).FirstOrDefault(),
            first.Accession,
            nowUtc,
            generatedBy,
            header.Design.Design.ToString(),
            header.Design.Source.ToString());
    }

    private static ReportLine Line(Result result, ResultReadContext context, IReadOnlyDictionary<Guid, IReadOnlyList<ReportPreviousValue>>? history)
    {
        var test = context.Tests.GetValueOrDefault(result.TestId);
        var decimals = result.HasValue ? result.Decimals : test?.Decimals ?? 0;
        var printRange = test?.PrintReference ?? true;
        return new ReportLine(
            result.Id,
            test?.ReportName ?? test?.NameEn ?? result.TestId.ToString("D"),
            test?.NameAr ?? string.Empty,
            (test?.Type ?? result.TestType).ToString(),
            result.Formatted is null && result.Culture is null ? result.TextValue : null,
            result.Unit ?? test?.UnitName,
            printRange ? result.Range?.Display(decimals) : null,
            result.Flag?.ToString(),
            result.IsPanic,
            result.Status.ToString(),
            result.ReportComment,
            result.Formatted?.Paragraphs.Select(paragraph => new FormattedParagraphDto(paragraph.Title, paragraph.Text)).ToList(),
            ResultViews.Culture(result.Culture),
            test?.Methodology,
            history?.GetValueOrDefault(result.Id));
    }

    private static List<ReportSampleLine> Samples(IEnumerable<Result> results, ResultReadContext context) =>
        results.Where(result => result.TubeId is not null)
            .Select(result => result.TubeId!.Value)
            .Distinct()
            .Select(tube => context.Samples.GetValueOrDefault(tube))
            .Where(sample => sample is not null)
            .Select(sample => new ReportSampleLine(sample!.SampleNumber, sample.CollectedAtUtc, sample.ReceivedAtUtc))
            .OrderBy(sample => sample.SampleNumber, StringComparer.Ordinal)
            .ToList();
}
