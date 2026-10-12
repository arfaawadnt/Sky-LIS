using Platform.Application.Catalogue;
using Platform.Domain.Results;

namespace Platform.Application.Results;

/// <summary>
/// BR-11 re-check (validation A-02, 2026-10-11): before review, delta acknowledgement and verification, every open
/// numeric result of a test with a delta limit is compared again with the patient's latest verified previous result of
/// the same test (other visit, inside the test's delta window), so a previous result verified after the value was
/// entered still raises the flag. <paramref name="staged"/> are results of the same command already signed in memory
/// (bulk verification) and not saved yet: the latest of them counts too. Stages changes only; the command commits once.
/// </summary>
internal static class ResultDeltaRecheck
{
    /// <summary>Re-checks <paramref name="targets"/>; returns the ids whose stored delta changed.</summary>
    public static async Task<IReadOnlySet<Guid>> RunAsync(
        IResultRepository results,
        ICatalogueLookup catalogue,
        IReadOnlyCollection<Result> targets,
        IEnumerable<Result> staged,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(staged);
        var open = targets.Where(IsOpenNumeric).ToList();
        var changed = new HashSet<Guid>();
        if (open.Count == 0)
        {
            return changed;
        }

        var tests = (await catalogue.GetTestsAsync([.. open.Select(result => result.TestId).Distinct()], cancellationToken)).ToDictionary(test => test.Id);
        var signed = staged.Where(result => ResultStatuses.IsSigned(result.Status) && result.NumericValue is not null && result.VerifiedAtUtc is not null).ToList();
        foreach (var result in open)
        {
            if (!tests.TryGetValue(result.TestId, out var test))
            {
                continue;
            }

            var threshold = ResultCatalogue.Rules(test).DeltaPercent;
            if (threshold is null)
            {
                continue;
            }

            var since = ResultCatalogue.DeltaSince(test, nowUtc);
            var previous = await results.PreviousVerifiedAsync(result.PatientId, result.TestId, result.VisitId, since, cancellationToken);
            var inMemory = signed
                .Where(other => other.PatientId == result.PatientId && other.TestId == result.TestId && other.VisitId != result.VisitId && (since is null || other.VerifiedAtUtc >= since))
                .MaxBy(other => other.VerifiedAtUtc);
            if (inMemory is not null && (previous is null || inMemory.VerifiedAtUtc > previous.VerifiedAtUtc))
            {
                previous = new PreviousResultValue(inMemory.Id, inMemory.NumericValue!.Value, inMemory.VerifiedAtUtc!.Value);
            }

            if (result.RecheckDelta(previous, threshold))
            {
                changed.Add(result.Id);
            }
        }

        return changed;
    }

    private static bool IsOpenNumeric(Result result) =>
        result.Status is ResultStatus.Complete or ResultStatus.Reviewed && result.NumericValue is not null;
}
