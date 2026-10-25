using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;
using Platform.Domain.Settings;

namespace Platform.Application.Results;

/// <summary>
/// The report design of a visit's reports (LIS-CR-010, C-14; intake RES §18): the payer contract's override (the visit's
/// tier), else the report template's override (the unit's clinical group), else the laboratory parameter
/// <c>reportDesign</c> (<see cref="ReportDesigns.Resolve"/>). The contract and the parameter are read once per visit.
/// </summary>
internal sealed class ResultReportDesign
{
    private readonly ReportDesign? _contract;
    private readonly ReportDesign _laboratory;

    private ResultReportDesign(ReportDesign? contract, ReportDesign laboratory)
    {
        _contract = contract;
        _laboratory = laboratory;
    }

    public static async Task<ResultReportDesign> LoadAsync(Guid? contractTierId, IResultReferenceLookup references, ITenantSettings settings, CancellationToken cancellationToken)
    {
        var contract = contractTierId is { } tierId && tierId != Guid.Empty ? await references.ContractReportDesignAsync(tierId, cancellationToken) : null;
        var laboratory = ReportDesigns.FromSettingValue(await settings.GetAsync(SettingKeys.ReportDesign, cancellationToken));
        return new ResultReportDesign(contract, laboratory);
    }

    /// <summary>The design of a unit printing with a template whose override is <paramref name="templateDesign"/>.</summary>
    public ReportDesignChoice For(ReportDesign? templateDesign) => ReportDesigns.Resolve(_contract, templateDesign, _laboratory);
}

/// <summary>
/// The previous values the Cumulative design prints beside each line (LIS-CR-010): through the results history reader
/// (<see cref="IResultRepository.ReadPreviousAsync"/>), the patient's last <see cref="ReportDesigns.CumulativeHistory"/>
/// signed (Verified / Printed) values of the same test in earlier visits that the user may see, newest first. Cultures and
/// formatted texts have no cumulative column.
/// </summary>
internal static class ResultCumulativeHistory
{
    /// <summary>How many of the patient's other results of a test are read to find the previous signed values.</summary>
    public const int ReadWindow = 20;

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReportPreviousValue>>> LoadAsync(IResultRepository results, ResultVisibility visibility, IEnumerable<Result> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(visibility);
        var history = new Dictionary<Guid, IReadOnlyList<ReportPreviousValue>>();
        foreach (var line in lines.Where(HasCumulativeColumn))
        {
            var previous = await results.ReadPreviousAsync(line.PatientId, line.TestId, line.Id, ReadWindow, cancellationToken);
            history[line.Id] = Pick(line, previous, visibility);
        }

        return history;
    }

    /// <summary>The earlier visits' signed values the user may see, newest first, at most three.</summary>
    public static IReadOnlyList<ReportPreviousValue> Pick(Result line, IEnumerable<Result> previous, ResultVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(line);
        return [.. previous
            .Where(row => row.IsLive && row.VisitId != line.VisitId && row.RegisteredAtUtc < line.RegisteredAtUtc)
            .Where(row => ResultStatuses.IsSigned(row.Status) && row.HasValue && visibility.CanSee(row.ClinicalGroupId, row.BranchId))
            .OrderByDescending(row => row.RegisteredAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(ReportDesigns.CumulativeHistory)
            .Select(row => new ReportPreviousValue(row.Accession, row.RegisteredAtUtc, row.TextValue ?? row.DescribeValue(), row.Unit, row.Flag?.ToString()))];
    }

    private static bool HasCumulativeColumn(Result result) => result.TestType is not (Domain.Catalogue.LabTestType.Culture or Domain.Catalogue.LabTestType.FormattedText);
}
