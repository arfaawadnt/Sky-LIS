using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Samples;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;

namespace Platform.Application.Results;

/// <summary>The catalogue, sample and user data that turn stored results into the views and documents of the module.</summary>
internal sealed record ResultReadContext(
    IReadOnlyDictionary<Guid, CatalogueTestInfo> Tests,
    IReadOnlyDictionary<Guid, CatalogueServiceInfo> Services,
    IReadOnlyDictionary<Guid, CatalogueClinicalGroupInfo> Groups,
    IReadOnlyDictionary<Guid, SampleStatusView> Samples,
    IReadOnlyDictionary<Guid, string> UserNames);

/// <summary>Loads the read context of a set of results through the other modules' read ports (one call per port).</summary>
internal sealed class ResultReadContextLoader(ICatalogueLookup catalogue, ISampleStatusLookup samples, IUserDirectory users)
{
    public async Task<ResultReadContext> LoadAsync(IReadOnlyCollection<Result> results, CancellationToken cancellationToken)
    {
        var tests = (await catalogue.GetTestsAsync([.. results.Select(result => result.TestId).Distinct()], cancellationToken)).ToDictionary(test => test.Id);
        var serviceIds = results.SelectMany(result => new[] { result.ServiceId, result.LineServiceId })
            .Concat(results.Select(result => ReportUnits.ServiceOf(result.ReportUnitKey)).Where(id => id is not null).Select(id => id!.Value))
            .Distinct()
            .ToList();
        var services = (await catalogue.GetServicesAsync(serviceIds, cancellationToken)).ToDictionary(service => service.Id);
        var groups = (await catalogue.GetClinicalGroupsAsync([.. results.Select(result => result.ClinicalGroupId).Distinct()], cancellationToken)).ToDictionary(group => group.Id);
        var tubes = await samples.ByTubesAsync([.. results.Where(result => result.TubeId is not null).Select(result => result.TubeId!.Value).Distinct()], cancellationToken);
        var userIds = results.SelectMany(result => new[] { result.EnteredByUserId, result.ReviewedByUserId, result.VerifiedByUserId, result.DeltaAcknowledgedByUserId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        IReadOnlyDictionary<Guid, Platform.Contracts.Organization.UserNameItem> names = userIds.Count == 0 ? new Dictionary<Guid, Platform.Contracts.Organization.UserNameItem>() : await users.GetNamesAsync(userIds, cancellationToken);
        return new ResultReadContext(
            tests,
            services,
            groups,
            tubes,
            names.ToDictionary(pair => pair.Key, pair => string.IsNullOrWhiteSpace(pair.Value.FullNameEn) ? pair.Value.DisplayName : pair.Value.FullNameEn!));
    }
}

/// <summary>Views of results: grid rows, report units with banners (BR-04) and rights, patient header.</summary>
internal static class ResultViews
{
    /// <summary>Report order: unit by group display order, inside a unit by service sequence, member order and code.</summary>
    public static IEnumerable<Result> InReportOrder(IEnumerable<Result> results, ResultReadContext context) =>
        results.OrderBy(result => result.ServiceSequence)
            .ThenBy(result => result.ServiceId)
            .ThenBy(result => result.MemberOrder)
            .ThenBy(result => context.Tests.GetValueOrDefault(result.TestId)?.Code, StringComparer.Ordinal);

    /// <summary>
    /// One unit per report-unit key of <paramref name="results"/> (the results the user may see). <paramref name="hidden"/>
    /// are the visit's other results, outside the user's result rights: those sharing a key with a visible unit are
    /// counted on it and their clinical groups named, so the page can say what is missing (change request 2026-10-14);
    /// the ones of a key with nothing visible make a hidden unit (counted by the case, never built).
    /// </summary>
    /// <remarks><paramref name="patient"/> resolves the expected range of the rows not entered yet (LIS-CR-007 C-08); null leaves them without.</remarks>
    public static IReadOnlyList<ReportUnitView> Units(IEnumerable<Result> results, IEnumerable<Result> hidden, ResultReadContext context, IUserPrivileges privileges, PatientProfile? patient = null)
    {
        var hiddenByUnit = (hidden ?? []).GroupBy(result => result.ReportUnitKey).ToDictionary(group => group.Key, group => (IReadOnlyList<Result>)[.. group], StringComparer.Ordinal);
        return results
            .GroupBy(result => result.ReportUnitKey)
            .Select(group => Unit(group.Key, [.. group], hiddenByUnit.GetValueOrDefault(group.Key) ?? [], context, privileges, patient))
            .OrderBy(unit => context.Groups.GetValueOrDefault(unit.ClinicalGroupId)?.DisplayOrder ?? int.MaxValue)
            .ThenBy(unit => unit.PrintAloneServiceId is null ? 0 : 1)
            .ThenBy(unit => unit.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static ReportUnitView Unit(string key, IReadOnlyList<Result> results, IReadOnlyList<Result> hidden, ResultReadContext context, IUserPrivileges privileges, PatientProfile? patient = null)
    {
        var live = results.Where(result => result.IsLive).ToList();
        var hiddenGroups = hidden
            .Select(result => result.ClinicalGroupId)
            .Distinct()
            .Select(groupId => context.Groups.TryGetValue(groupId, out var hiddenGroup)
                ? new LookupOption(groupId, hiddenGroup.Code, hiddenGroup.NameEn, hiddenGroup.NameAr)
                : new LookupOption(groupId, null, string.Empty, string.Empty))
            .OrderBy(option => option.Code, StringComparer.Ordinal)
            .ToList();
        var first = results[0];
        var group = context.Groups.GetValueOrDefault(first.ClinicalGroupId);
        var printAlone = ReportUnits.ServiceOf(key);
        var service = printAlone is { } serviceId ? context.Services.GetValueOrDefault(serviceId) : null;
        var status = ResultStatuses.Lowest(live.Select(result => result.Status)) ?? ResultStatus.Cancelled;
        var rights = ResultRights.For(privileges, first.ClinicalGroupId, first.BranchId);
        var samples = results.Where(result => result.TubeId is not null)
            .Select(result => result.TubeId!.Value)
            .Distinct()
            .Select(tube => context.Samples.TryGetValue(tube, out var sample)
                ? new UnitSampleView(tube, sample.SampleNumber, sample.Status, sample.CollectedAtUtc, sample.ReceivedAtUtc)
                : new UnitSampleView(tube, null, null, null, null))
            .OrderBy(sample => sample.SampleNumber, StringComparer.Ordinal)
            .ToList();
        var ordered = InReportOrder(results, context).ToList();
        var banners = ordered.GroupBy(result => result.ServiceId)
            .Select(bannerGroup =>
            {
                var bannerService = context.Services.GetValueOrDefault(bannerGroup.Key);
                var bannerStatus = ResultStatuses.Lowest(bannerGroup.Select(result => result.Status)) ?? ResultStatus.Cancelled;
                return new ServiceBannerView(bannerGroup.Key, bannerService?.Code, bannerService?.NameEn, bannerService?.NameAr, bannerService?.ReportName, bannerStatus.ToString(), bannerGroup.Count());
            })
            .ToList();

        return new ReportUnitView(
            key,
            first.ClinicalGroupId,
            group?.UnitCode,
            group?.NameEn,
            group?.NameAr,
            printAlone,
            service?.NameEn,
            service?.NameAr,
            status.ToString(),
            live.Any(result => result.IsPrinted),
            live.Count == 0 ? 0 : live.Max(result => result.PrintCount),
            live.Any(result => !result.HasValue),
            live.Any(result => result.IsPanic),
            live.Any(result => result.DeltaFlag),
            samples,
            ResultRights.View(rights),
            banners,
            [.. ordered.Select(result => Row(result, context, patient))],
            hidden.Count,
            hiddenGroups);
    }

    public static ResultRowView Row(Result result, ResultReadContext context, PatientProfile? patient = null)
    {
        var test = context.Tests.GetValueOrDefault(result.TestId);
        var decimals = result.HasValue ? result.Decimals : test?.Decimals ?? 0;
        var provisional = ProvisionalRange(result, test, patient);
        var sample = result.TubeId is { } tube ? context.Samples.GetValueOrDefault(tube) : null;
        return new ResultRowView(
            result.Id,
            result.LineId,
            result.ServiceId,
            result.TestId,
            test?.Code,
            test?.NameEn,
            test?.NameAr,
            test?.ReportName,
            (test?.Type ?? result.TestType).ToString(),
            result.IsHiddenHelper,
            (test?.Type ?? result.TestType) == LabTestType.Formula,
            result.TubeId,
            sample?.SampleNumber,
            sample?.Status,
            sample?.IsResultEntryAllowed ?? false,
            result.Status.ToString(),
            result.NumericValue,
            result.TextValue,
            result.Formatted?.Paragraphs.Select(paragraph => new FormattedParagraphDto(paragraph.Title, paragraph.Text)).ToList(),
            Culture(result.Culture),
            result.Unit ?? test?.UnitName,
            decimals,
            ResultCatalogue.RangeView(result.Range ?? provisional, decimals),
            result.Flag?.ToString(),
            result.IsPanic,
            result.DeltaFlag || result.DeltaPercent is not null
                ? new DeltaView(result.DeltaFlag, result.DeltaPercent, result.PreviousValue, result.PreviousResultId, result.DeltaAcknowledgedAtUtc is not null, Name(context, result.DeltaAcknowledgedByUserId), result.DeltaAcknowledgedAtUtc, result.DeltaComment)
                : null,
            result.Source.ToString(),
            result.AnalyserCode,
            result.AnalyserFlags,
            result.EnteredAtUtc is { } entered ? new ActorStampView(result.EnteredByUserId, Name(context, result.EnteredByUserId) ?? (result.Source == ResultSource.Analyser ? result.AnalyserCode : null), entered) : null,
            result.ReviewedAtUtc is { } reviewed ? new ActorStampView(result.ReviewedByUserId, Name(context, result.ReviewedByUserId), reviewed) : null,
            result.VerifiedAtUtc is { } verified ? new ActorStampView(result.VerifiedByUserId, result.VerifiedByName, verified) : null,
            result.VerifiedByName,
            result.PrintCount,
            result.LastPrintedAtUtc,
            result.AmendmentCount,
            result.ReportComment,
            result.InternalNote,
            test is null ? [] : [.. test.ListValues.OrderBy(value => value.DisplayOrder).Select(value => new ListOptionView(value.Value, value.IsNormal, value.IsCritical, value.IsPlaceholder, value.Colour))],
            test is null ? [] : [.. test.Paragraphs.OrderBy(paragraph => paragraph.DisplayOrder).Select(paragraph => new ParagraphTemplateView(paragraph.Title, paragraph.Text))],
            result.ExpectedAtUtc,
            provisional is not null);
    }

    /// <summary>
    /// LIS-CR-007 C-08: the band BR-08 picks for the patient (<see cref="ResultCatalogue.Range"/>, the call entry makes) for a
    /// result with neither a value nor a stored range, so the grid shows the expected range before the first save. Once a value
    /// exists the stored range wins, even when it is empty (a band added later never rewrites what was flagged).
    /// </summary>
    public static ResolvedRange? ProvisionalRange(Result result, CatalogueTestInfo? test, PatientProfile? patient)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Range is null && !result.HasValue && test is not null && patient is not null ? ResultCatalogue.Range(test, patient) : null;
    }

    public static CultureView? Culture(CultureReport? culture) =>
        culture is null
            ? null
            : new CultureView(
                culture.NoGrowth,
                [.. culture.Isolates.Select(isolate => new CultureIsolateView(
                    isolate.OrganismId,
                    isolate.OrganismCode,
                    isolate.OrganismNameEn,
                    isolate.OrganismNameAr,
                    isolate.Quantity,
                    isolate.Comment,
                    [.. isolate.Sensitivities.Select(item => new CultureSensitivityView(item.AntibioticId, item.AntibioticCode, item.Abbreviation, item.NameEn, item.NameAr, item.Interpretation, item.Mic))]))]);

    public static PatientHeaderView Patient(Guid patientId, PatientSummary? patient, IReadOnlyDictionary<Guid, NamedReference> conditions)
    {
        var condition = patient?.PatientConditionId is { } conditionId ? conditions.GetValueOrDefault(conditionId) : null;
        return new PatientHeaderView(
            patientId,
            patient?.Number,
            patient?.FullName,
            patient?.NameAr,
            patient?.Gender,
            patient?.DateOfBirth,
            patient?.Age.Years,
            patient?.Age.Months,
            patient?.Age.Days,
            patient?.PatientConditionId,
            condition?.NameEn,
            condition?.NameAr,
            patient?.IsVip ?? false);
    }

    public static ResultLogItem Log(ResultLogEntry entry) =>
        new(entry.Id, entry.Kind.ToString(), entry.FromStatus?.ToString(), entry.ToStatus?.ToString(), entry.ValueBefore, entry.ValueAfter, entry.Source?.ToString(), entry.AnalyserId, entry.AtUtc, entry.ByUserId, entry.ByName, entry.Detail);

    private static string? Name(ResultReadContext context, Guid? userId) =>
        userId is { } id && context.UserNames.TryGetValue(id, out var name) ? name : null;
}
