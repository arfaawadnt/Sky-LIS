namespace Platform.Domain.Registration;

/// <summary>
/// BR-02 (LDM §2.2) as a pure function: services with the same clinical group, sample type and sample condition share
/// one tube; any difference splits them. A need joins the first <b>live</b> tube of the visit with its key (a tube is live
/// while one of its lines is Ordered); otherwise it opens a new tube, shared by the later needs of the same key. A key
/// whose tube has no live line any more gets a new tube (Samples intake Q15: a cancelled sample is never reopened).
/// <para>
/// Compatible conditions (LIS-CR-015 M4): a need whose condition merges into a target condition P (<see cref="Need.MergesInto"/>)
/// takes the key <c>(group, type, P)</c> when a tube of that key is live on the visit or is needed (by its own key) in the
/// same request; otherwise it keeps its own key. Never the other way: a need of P never joins a tube of a condition that
/// merges into P, and an existing tube is never re-keyed.
/// </para>
/// </summary>
public static class TubePlanner
{
    /// <summary>An existing tube of the visit.</summary>
    public sealed record ExistingTube(Guid TubeId, TubeKey Key, bool IsLive);

    /// <summary>A tube need: its own key and, for a compatible condition, the key of the tube it may join.</summary>
    public sealed record Need(TubeKey Key, TubeKey? MergesInto = null);

    /// <summary>
    /// The assignment of each need, in order: an existing tube id, or the index (0-based) of a new tube. Needs without
    /// a key (<see langword="null"/>) get no tube. <paramref name="Key"/> is the key of the tube the need lands in (its own
    /// key, or the target's when it merged).
    /// </summary>
    public sealed record Assignment(Guid? ExistingTubeId, int? NewTubeIndex, TubeKey? Key = null);

    public sealed record Plan(IReadOnlyList<Assignment?> Assignments, IReadOnlyList<TubeKey> NewTubes);

    public static Plan Assign(IReadOnlyList<ExistingTube> existing, IReadOnlyList<TubeKey?> needs)
    {
        ArgumentNullException.ThrowIfNull(needs);
        return Assign(existing, [.. needs.Select(key => key is null ? null : new Need(key))]);
    }

    public static Plan Assign(IReadOnlyList<ExistingTube> existing, IReadOnlyList<Need?> needs)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(needs);

        var requested = needs.Where(need => need is not null).Select(need => need!.Key).ToHashSet();
        var newTubes = new List<TubeKey>();
        var assignments = new List<Assignment?>(needs.Count);
        foreach (var need in needs)
        {
            if (need is null)
            {
                assignments.Add(null);
                continue;
            }

            var key = EffectiveKey(need, existing, requested);
            var live = existing.FirstOrDefault(tube => tube.IsLive && tube.Key == key);
            if (live is not null)
            {
                assignments.Add(new Assignment(live.TubeId, null, key));
                continue;
            }

            var index = newTubes.IndexOf(key);
            if (index < 0)
            {
                newTubes.Add(key);
                index = newTubes.Count - 1;
            }

            assignments.Add(new Assignment(null, index, key));
        }

        return new Plan(assignments, newTubes);
    }

    /// <summary>How many new tubes (and sample numbers) the needs require.</summary>
    public static int CountNewTubes(IReadOnlyList<ExistingTube> existing, IReadOnlyList<TubeKey?> needs) => Assign(existing, needs).NewTubes.Count;

    /// <summary>How many new tubes (and sample numbers) the needs require, compatible conditions merged.</summary>
    public static int CountNewTubes(IReadOnlyList<ExistingTube> existing, IReadOnlyList<Need?> needs) => Assign(existing, needs).NewTubes.Count;

    private static TubeKey EffectiveKey(Need need, IReadOnlyList<ExistingTube> existing, HashSet<TubeKey> requested)
    {
        if (need.MergesInto is not { } target
            || target == need.Key
            || target.ClinicalGroupId != need.Key.ClinicalGroupId
            || target.SampleTypeId != need.Key.SampleTypeId)
        {
            return need.Key;
        }

        return requested.Contains(target) || existing.Any(tube => tube.IsLive && tube.Key == target) ? target : need.Key;
    }
}
