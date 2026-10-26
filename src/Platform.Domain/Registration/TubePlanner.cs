namespace Platform.Domain.Registration;

/// <summary>
/// BR-02 (LDM §2.2) as a pure function: services with the same clinical group, sample type and sample condition share
/// one tube; any difference splits them. A need joins the first <b>live</b> tube of the visit with its key (a tube is live
/// while one of its lines is Ordered); otherwise it opens a new tube, shared by the later needs of the same key. A key
/// whose tube has no live line any more gets a new tube (Samples intake Q15: a cancelled sample is never reopened).
/// <para>
/// Compatibility groups (LIS-CR-015 M4): needs of the <b>same clinical group</b> whose (sample type, condition) pairs are
/// members of the <b>same</b> compatibility group share one tube. (a) A live tube of that clinical group whose pair is in the
/// group is joined (the best-priority one when several); existing tubes are never re-keyed or renamed. (b) Otherwise the
/// needs of the request open one tube keyed (and so named) after the member with the highest priority (lowest number)
/// among the pairs actually needed. Needs outside any group behave as before.
/// </para>
/// </summary>
public static class TubePlanner
{
    /// <summary>An existing tube of the visit.</summary>
    public sealed record ExistingTube(Guid TubeId, TubeKey Key, bool IsLive);

    /// <summary>A tube need: its own key and, when its pair is in an active compatibility group, that group.</summary>
    public sealed record Need(TubeKey Key, TubeCompatibility? Compatibility = null);

    /// <summary>
    /// The assignment of each need, in order: an existing tube id, or the index (0-based) of a new tube. Needs without
    /// a key (<see langword="null"/>) get no tube. <paramref name="Key"/> is the key of the tube the need lands in (its own
    /// key, or another member's when it shares a compatible tube).
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

        var newTubes = new List<TubeKey>();
        var assignments = new List<Assignment?>(needs.Count);
        foreach (var need in needs)
        {
            if (need is null)
            {
                assignments.Add(null);
                continue;
            }

            var key = EffectiveKey(need, existing, needs);
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

    /// <summary>How many new tubes (and sample numbers) the needs require, compatible samples shared.</summary>
    public static int CountNewTubes(IReadOnlyList<ExistingTube> existing, IReadOnlyList<Need?> needs) => Assign(existing, needs).NewTubes.Count;

    private static TubeKey EffectiveKey(Need need, IReadOnlyList<ExistingTube> existing, IReadOnlyList<Need?> needs)
    {
        if (need.Compatibility is not { } group)
        {
            return need.Key;
        }

        var unit = need.Key.ClinicalGroupId;
        var live = existing
            .Where(tube => tube.IsLive && tube.Key.ClinicalGroupId == unit && group.PriorityOf(tube.Key) is not null)
            .OrderBy(tube => group.PriorityOf(tube.Key))
            .FirstOrDefault();
        if (live is not null)
        {
            return live.Key;
        }

        return needs
            .Where(other => other is not null && other.Key.ClinicalGroupId == unit && other.Compatibility?.GroupId == group.GroupId)
            .Select(other => other!.Key)
            .OrderBy(key => group.PriorityOf(key) ?? int.MaxValue)
            .First();
    }
}

/// <summary>
/// The active compatibility group a tube need's (sample type, condition) pair belongs to (LIS-CR-015 M4): its id, the
/// need's own priority and every available member with its priority (members whose type or condition is inactive are left
/// out by the catalogue read).
/// </summary>
public sealed record TubeCompatibility(Guid GroupId, int Priority, IReadOnlyList<CompatibleTubeMember> Members)
{
    /// <summary>The priority of the member a tube key's pair matches; null when the pair is not in the group.</summary>
    public int? PriorityOf(TubeKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Members.FirstOrDefault(member => member.SampleTypeId == key.SampleTypeId && member.SampleConditionId == key.SampleConditionId)?.Priority;
    }
}

/// <summary>A member of a compatibility group as tube planning sees it.</summary>
public sealed record CompatibleTubeMember(Guid SampleTypeId, Guid? SampleConditionId, int Priority);
