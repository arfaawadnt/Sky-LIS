using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Services;
using Platform.Application.Patients;
using Platform.Application.Samples;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results;

/// <summary>The outcome of an entry: the results that changed (formulas included), the refusals, and the first refusal (rethrown by single-result commands).</summary>
internal sealed record EntryOutcome(IReadOnlyList<Guid> Changed, IReadOnlyList<ResultRefusal> Refused, DomainException? FirstRefusal);

/// <summary>
/// The shared body of T2 (enter), T6 (amend) and T2b (formulas) for the entry commands: loads the visits of the
/// results with their catalogue tests, patient and sample gates; resolves the range for the patient (BR-08) and the
/// previous value (BR-11); builds culture and formatted-text values from the catalogue lists; lets the aggregate decide;
/// then recomputes the formulas of every visit touched. It stages changes only: the command commits once.
/// </summary>
internal sealed class ResultEntryEngine(
    IResultRepository results,
    ICatalogueLookup catalogue,
    IPatientLookup patients,
    ISampleStatusLookup samples,
    IResultReferenceLookup references,
    IFormulaEvaluator evaluator,
    IClock clock)
{
    /// <summary>The loaded working set of one entry: the visits' results (tracked), their tests, patients and sample gates.</summary>
    internal sealed record WorkingSet(
        IReadOnlyDictionary<Guid, Result> ById,
        IReadOnlyDictionary<Guid, IReadOnlyList<Result>> ByVisit,
        IReadOnlyDictionary<Guid, CatalogueTestInfo> Tests,
        IReadOnlyDictionary<Guid, PatientProfile> Patients,
        IReadOnlyDictionary<Guid, SampleStatusView> Samples);

    public async Task<WorkingSet> LoadAsync(IReadOnlyCollection<Guid> resultIds, CancellationToken cancellationToken)
    {
        var targets = await results.ListForUpdateAsync(resultIds, cancellationToken);
        var byVisit = new Dictionary<Guid, IReadOnlyList<Result>>();
        foreach (var visitId in targets.Select(result => result.VisitId).Distinct())
        {
            byVisit[visitId] = await results.ListByVisitForUpdateAsync(visitId, cancellationToken);
        }

        var all = byVisit.Values.SelectMany(list => list).Concat(targets).DistinctBy(result => result.Id).ToList();
        var tests = (await catalogue.GetTestsAsync([.. all.Select(result => result.TestId).Distinct()], cancellationToken)).ToDictionary(test => test.Id);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var summaries = await patients.GetManyAsync([.. all.Select(result => result.PatientId).Distinct()], cancellationToken);
        var profiles = all.Select(result => result.PatientId).Distinct().ToDictionary(id => id, id => PatientProfile.From(summaries.GetValueOrDefault(id), today));
        var tubes = all.Where(result => result.TubeId is not null).Select(result => result.TubeId!.Value).Distinct().ToList();
        var gates = await samples.ByTubesAsync(tubes, cancellationToken);
        return new WorkingSet(all.ToDictionary(result => result.Id), byVisit, tests, profiles, gates);
    }

    /// <summary>T2 for every item; refusals are collected per result, the rest stays staged.</summary>
    public async Task<EntryOutcome> EnterAsync(IReadOnlyList<ResultValueItem> items, Func<Result, ResultActor> actorOf, Guid actorUserId, string? actorName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var set = await LoadAsync([.. items.Select(item => item.ResultId).Distinct()], cancellationToken);
        var now = clock.UtcNow;
        var changed = new List<Guid>();
        var refused = new List<ResultRefusal>();
        DomainException? first = null;

        foreach (var item in items)
        {
            try
            {
                var result = set.ById.GetValueOrDefault(item.ResultId) ?? throw ResultsNotFound.Result();
                var test = TestOf(set, result);
                var rules = ResultCatalogue.Rules(test);
                var range = ResultCatalogue.Range(test, set.Patients[result.PatientId]);
                var input = await BuildInputAsync(result, test.Type, item.NumericValue, item.TextValue, item.Paragraphs, item.Culture, cancellationToken);
                var previous = await PreviousAsync(result, test, rules, now, cancellationToken);
                if (result.Enter(input, rules, range, previous, actorOf(result), IsReceived(set, result), now) && !changed.Contains(result.Id))
                {
                    changed.Add(result.Id);
                }
            }
            catch (DomainException exception)
            {
                first ??= exception;
                refused.Add(new ResultRefusal(item.ResultId, exception.Code, exception.Message));
            }
        }

        changed.AddRange((await RecomputeAsync(set, ResultSource.Manual, actorUserId, actorName, now, cancellationToken)).Where(id => !changed.Contains(id)));
        return new EntryOutcome(changed, refused, first);
    }

    /// <summary>T6 for one result, then the visit's formulas.</summary>
    public async Task AmendAsync(Guid resultId, ResultValueItem value, string reason, Func<Result, ResultActor> actorOf, Guid actorUserId, string? actorName, CancellationToken cancellationToken)
    {
        var set = await LoadAsync([resultId], cancellationToken);
        var result = set.ById.GetValueOrDefault(resultId) ?? throw ResultsNotFound.Result();
        var test = TestOf(set, result);
        var now = clock.UtcNow;
        var rules = ResultCatalogue.Rules(test);
        var range = ResultCatalogue.Range(test, set.Patients[result.PatientId]);
        var input = await BuildInputAsync(result, test.Type, value.NumericValue, value.TextValue, value.Paragraphs, value.Culture, cancellationToken);
        var previous = await PreviousAsync(result, test, rules, now, cancellationToken);
        result.Amend(input, rules, range, previous, actorOf(result), IsReceived(set, result), reason, now);
        await RecomputeAsync(set, ResultSource.Manual, actorUserId, actorName, now, cancellationToken);
    }

    /// <summary>T2b for every visit of the working set.</summary>
    public async Task<IReadOnlyList<Guid>> RecomputeAsync(WorkingSet set, ResultSource source, Guid? actorUserId, string? actorName, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var changed = new List<Guid>();
        foreach (var (_, visitResults) in set.ByVisit)
        {
            if (visitResults.Count == 0)
            {
                continue;
            }

            var profile = set.Patients.GetValueOrDefault(visitResults[0].PatientId) ?? PatientProfile.Unknown;
            changed.AddRange(await FormulaRecalculator.RecomputeAsync(
                visitResults,
                set.Tests,
                evaluator,
                profile,
                (formula, test) => PreviousAsync(formula, test, ResultCatalogue.Rules(test), nowUtc, cancellationToken),
                source,
                actorUserId,
                actorName,
                nowUtc));
        }

        return changed;
    }

    public Task<PreviousResultValue?> PreviousAsync(Result result, CatalogueTestInfo test, ResultTestRules rules, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        rules.DeltaPercent is null
            ? Task.FromResult<PreviousResultValue?>(null)
            : results.PreviousVerifiedAsync(result.PatientId, result.TestId, result.VisitId, ResultCatalogue.DeltaSince(test, nowUtc), cancellationToken);

    /// <summary>
    /// The value of a request for the test type. A culture names catalogue organisms and antibiotics (RES-040, RES-041):
    /// newly recorded ones must be active, ones already on the result stay valid; interpretations must be catalogue
    /// sensitivity codes when the catalogue has any (RES-042).
    /// </summary>
    public async Task<ResultValueInput> BuildInputAsync(Result result, LabTestType type, decimal? numeric, string? text, IReadOnlyList<FormattedParagraphDto>? paragraphs, CultureEntryDto? culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        switch (type)
        {
            case LabTestType.FormattedText:
                return new ResultValueInput(null, null, paragraphs is null ? null : FormattedReport.Create(paragraphs.Select(paragraph => new FormattedParagraph(paragraph.Title, paragraph.Text))), null);
            case LabTestType.Culture:
                return new ResultValueInput(null, null, null, culture is null ? null : await BuildCultureAsync(result, culture, cancellationToken));
            default:
                return new ResultValueInput(numeric, text, null, null);
        }
    }

    private async Task<CultureReport> BuildCultureAsync(Result result, CultureEntryDto entry, CancellationToken cancellationToken)
    {
        var isolates = entry.Isolates ?? [];
        if (entry.NoGrowth && isolates.Count > 0)
        {
            throw new DomainException(ResultsCodes.CultureInvalid, "A culture with no growth has no organism.", DomainFault.Validation);
        }

        if (entry.NoGrowth || isolates.Count == 0)
        {
            return CultureReport.Create(entry.NoGrowth, []);
        }

        var organisms = await references.OrganismsAsync([.. isolates.Select(isolate => isolate.OrganismId).Distinct()], cancellationToken);
        var antibiotics = await references.AntibioticsAsync([.. isolates.SelectMany(isolate => isolate.Sensitivities ?? []).Select(item => item.AntibioticId).Distinct()], cancellationToken);
        var codes = await references.SensitivityCodesAsync(cancellationToken);
        var knownOrganisms = result.Culture?.Isolates.Select(isolate => isolate.OrganismId).ToHashSet() ?? [];
        var knownAntibiotics = result.Culture?.Isolates.SelectMany(isolate => isolate.Sensitivities).Select(item => item.AntibioticId).ToHashSet() ?? [];

        var built = new List<CultureIsolate>(isolates.Count);
        foreach (var isolate in isolates)
        {
            if (!organisms.TryGetValue(isolate.OrganismId, out var organism) || (!organism.IsActive && !knownOrganisms.Contains(organism.Id)))
            {
                throw new DomainException(ResultsCodes.OrganismUnknown, "The organism is not an active organism of the catalogue.", DomainFault.Validation);
            }

            var sensitivities = new List<CultureSensitivity>();
            foreach (var item in isolate.Sensitivities ?? [])
            {
                if (!antibiotics.TryGetValue(item.AntibioticId, out var antibiotic) || (!antibiotic.IsActive && !knownAntibiotics.Contains(antibiotic.Id)))
                {
                    throw new DomainException(ResultsCodes.AntibioticUnknown, "The antibiotic is not an active antibiotic of the catalogue.", DomainFault.Validation);
                }

                var interpretation = (item.Interpretation ?? string.Empty).Trim().ToUpperInvariant();
                if (codes.Count > 0 && !codes.Contains(interpretation))
                {
                    throw new DomainException(ResultsCodes.SensitivityValueUnknown, $"'{interpretation}' is not a sensitivity value of the catalogue.", DomainFault.Validation);
                }

                sensitivities.Add(new CultureSensitivity(antibiotic.Id, antibiotic.Code, antibiotic.Abbreviation, antibiotic.NameEn, antibiotic.NameAr, interpretation, item.Mic));
            }

            built.Add(new CultureIsolate(organism.Id, organism.Code, organism.NameEn, organism.NameAr, isolate.Quantity, isolate.Comment, sensitivities));
        }

        return CultureReport.Create(false, built);
    }

    private static CatalogueTestInfo TestOf(WorkingSet set, Result result) =>
        set.Tests.GetValueOrDefault(result.TestId)
        ?? throw new DomainException(ResultsCodes.ReferenceRequired, "The test of this result is no longer in the catalogue.", DomainFault.Conflict);

    public static bool IsReceived(WorkingSet set, Result result) =>
        result.TubeId is { } tube && set.Samples.TryGetValue(tube, out var sample) && sample.IsResultEntryAllowed;
}
