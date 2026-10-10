namespace Platform.Domain.Registration;

/// <summary>
/// BR-02 (LDM §2.2) as a pure function: services with the same clinical group, sample type and sample condition share
/// one tube; any difference splits them. A need joins the first <b>live</b> tube of the visit with its key (a tube is live
/// while one of its lines is Ordered); otherwise it opens a new tube, shared by the later needs of the same key. A key
/// whose tube has no live line any more gets a new tube (Samples intake Q15: a cancelled sample is never reopened).
/// </summary>
public static class TubePlanner
{
    /// <summary>An existing tube of the visit.</summary>
    public sealed record ExistingTube(Guid TubeId, TubeKey Key, bool IsLive);

    /// <summary>
    /// The assignment of each need, in order: an existing tube id, or the index (0-based) of a new tube. Needs without
    /// a key (<see langword="null"/>) get no tube.
    /// </summary>
    public sealed record Assignment(Guid? ExistingTubeId, int? NewTubeIndex);

    public sealed record Plan(IReadOnlyList<Assignment?> Assignments, IReadOnlyList<TubeKey> NewTubes);

    public static Plan Assign(IReadOnlyList<ExistingTube> existing, IReadOnlyList<TubeKey?> needs)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(needs);

        var newTubes = new List<TubeKey>();
        var assignments = new List<Assignment?>(needs.Count);
        foreach (var key in needs)
        {
            if (key is null)
            {
                assignments.Add(null);
                continue;
            }

            var live = existing.FirstOrDefault(tube => tube.IsLive && tube.Key == key);
            if (live is not null)
            {
                assignments.Add(new Assignment(live.TubeId, null));
                continue;
            }

            var index = newTubes.IndexOf(key);
            if (index < 0)
            {
                newTubes.Add(key);
                index = newTubes.Count - 1;
            }

            assignments.Add(new Assignment(null, index));
        }

        return new Plan(assignments, newTubes);
    }

    /// <summary>How many new tubes (and sample numbers) the needs require.</summary>
    public static int CountNewTubes(IReadOnlyList<ExistingTube> existing, IReadOnlyList<TubeKey?> needs) => Assign(existing, needs).NewTubes.Count;
}
