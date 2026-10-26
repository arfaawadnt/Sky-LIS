using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A sample type (serum, whole blood, urine…) with its conditions (fasting, random, refrigerated…) (SRS CP-43,
/// LDM §2.2 "sample type + condition"). Tubes merge by clinical group, sample type and condition (BR-02), so a
/// condition keeps its id for life: conditions are upserted and deactivated, never deleted.
/// </summary>
public sealed class SampleType : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 20;
    public const int MaxShortNameLength = 20;
    public const int MaxConditions = 50;

    private readonly List<SampleCondition> _conditions = [];

    private SampleType()
    {
        Code = string.Empty;
        ShortName = string.Empty;
        Name = null!;
    }

    private SampleType(Guid id, string code, string shortName, LocalizedText name)
        : base(id)
    {
        Code = code;
        ShortName = shortName;
        Name = name;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant (INV-CAT-001), immutable.</summary>
    public string Code { get; private set; }

    public string ShortName { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// The type is a culture specimen (urine, wound swab, blood culture …): listed on the Microbiology page's *Culture
    /// specimens* tab and offered first by the sample-type picker of a culture service (change request LIS-CR-007, C-11).
    /// A flag only: it restricts nothing.
    /// </summary>
    public bool ForCulture { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Incremented by every change of the conditions, so the root row carries the concurrency check and the modification stamp.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<SampleCondition> Conditions => _conditions.AsReadOnly();

    /// <summary>T4 Create (<c>catalogue.create</c>) with its initial conditions; <paramref name="forCulture"/> marks a culture specimen.</summary>
    public static SampleType Create(string code, string shortName, LocalizedText name, IEnumerable<SampleConditionSpec> conditions, DateTimeOffset nowUtc, bool forCulture = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(conditions);
        var sampleType = new SampleType(Guid.CreateVersion7(), CleanCode(code), CleanShortName(shortName), name) { ForCulture = forCulture };
        sampleType.UpsertConditions(conditions, deactivateOmitted: false);
        sampleType.Raise(new SampleTypeCreated(sampleType.Id, sampleType.Code, nowUtc));
        return sampleType;
    }

    /// <summary>
    /// T5 Edit (<c>catalogue.edit</c>): short name, names and the culture flag; the code is immutable. A null
    /// <paramref name="forCulture"/> keeps the flag (an editor that predates the flag never clears it).
    /// </summary>
    public void Change(string shortName, LocalizedText name, DateTimeOffset nowUtc, bool? forCulture = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var before = (ShortName, Name, ForCulture);
        ShortName = CleanShortName(shortName);
        Name = name;
        ForCulture = forCulture ?? ForCulture;
        if (before != (ShortName, Name, ForCulture))
        {
            Raise(new SampleTypeChanged(Id, Code, nowUtc));
        }
    }

    /// <summary>
    /// T6 Save conditions (<c>catalogue.edit</c>): items with an id update that condition (its code is immutable),
    /// items without one are added, existing conditions that are not listed are deactivated (never deleted, BR-15).
    /// </summary>
    public void ReplaceConditions(IEnumerable<SampleConditionSpec> conditions, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        UpsertConditions(conditions, deactivateOmitted: true);

        // a compatible condition never points at an inactive target (LIS-CR-015 M4): deactivating the target releases its merges
        var released = _conditions.Where(condition => condition.MergesIntoConditionId is { } target && !_conditions.Any(other => other.Id == target && other.IsActive)).ToList();
        foreach (var condition in released)
        {
            condition.MergeInto(null);
        }

        Revision++;
        Raise(new SampleConditionsChanged(Id, Code, _conditions.Count(condition => condition.IsActive), nowUtc));
        if (released.Count > 0)
        {
            Raise(new SampleConditionCompatibilityChanged(Id, Code, _conditions.Count(condition => condition.MergesIntoConditionId is not null), nowUtc));
        }
    }

    /// <summary>
    /// Compatible conditions (LIS-CR-015 M4, <c>catalogue.edit</c>): the full map of the type — each listed condition merges
    /// into the condition named (its tests use that condition's tube when it is in the same visit) or keeps its own sample
    /// (null); unlisted conditions keep their own sample. Rules: a condition of this type, never itself (CAT-281), the
    /// target active (CAT-282) and not merging into another (CAT-283, one level), a target cannot merge (CAT-284), each
    /// condition listed once (CAT-285), unknown conditions CAT-280. Returns false when nothing changed.
    /// </summary>
    public bool SetCompatibleConditions(IEnumerable<SampleConditionPair> pairs, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var map = new Dictionary<Guid, Guid?>();
        foreach (var pair in pairs)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (!_conditions.Any(condition => condition.Id == pair.ConditionId))
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleConditionUnknown, "A listed condition does not belong to this sample type.");
            }

            if (!map.TryAdd(pair.ConditionId, pair.MergesIntoConditionId))
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleConditionListedTwice, "A condition is listed twice.");
            }
        }

        foreach (var (conditionId, targetId) in map)
        {
            if (targetId is not { } target)
            {
                continue;
            }

            var targetCondition = _conditions.FirstOrDefault(condition => condition.Id == target);
            if (targetCondition is null)
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleConditionUnknown, "A condition can merge only into a condition of the same sample type.");
            }

            if (target == conditionId)
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleConditionSelf, "A condition cannot merge into itself.");
            }

            if (!targetCondition!.IsActive)
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleTargetInactive, $"The condition '{targetCondition.Code}' is inactive; a condition can merge only into an active one.");
            }

            if (map.Any(other => other.Value == conditionId))
            {
                var code = _conditions.First(condition => condition.Id == conditionId).Code;
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleSourceIsTarget, $"Other conditions merge into '{code}', so it cannot merge into another condition.");
            }

            if (map.TryGetValue(target, out var targetsTarget) && targetsTarget is not null)
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.CompatibleTargetMerges, $"The condition '{targetCondition.Code}' merges into another condition; merging is one level only.");
            }
        }

        var changed = false;
        foreach (var condition in _conditions)
        {
            var target = map.TryGetValue(condition.Id, out var listed) ? listed : null;
            if (condition.MergesIntoConditionId != target)
            {
                condition.MergeInto(target);
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        Revision++;
        Raise(new SampleConditionCompatibilityChanged(Id, Code, _conditions.Count(condition => condition.MergesIntoConditionId is not null), nowUtc));
        return true;
    }

    /// <summary>True when the condition belongs to this type (INV-CAT-011); <paramref name="mustBeActive"/> adds BR-24.</summary>
    public bool HasCondition(Guid conditionId, bool mustBeActive) =>
        _conditions.Any(condition => condition.Id == conditionId && (!mustBeActive || condition.IsActive));

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new SampleTypeDeactivated(Id, Code, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new SampleTypeReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "sample type");

    private static string CleanShortName(string? shortName)
    {
        var cleaned = (shortName ?? string.Empty).Trim();
        if (cleaned.Length is < 1 or > MaxShortNameLength)
        {
            CatalogueAGuard.Refuse(SampleTypeCodes.ShortNameInvalid, $"The short name is required and holds at most {MaxShortNameLength} characters.");
        }

        return cleaned;
    }

    private void UpsertConditions(IEnumerable<SampleConditionSpec> conditions, bool deactivateOmitted)
    {
        var specs = conditions.ToList();
        if (specs.Count > MaxConditions)
        {
            CatalogueAGuard.Refuse(SampleTypeCodes.TooManyConditions, $"A sample type has at most {MaxConditions} conditions.");
        }

        var listed = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var staged = new List<(SampleCondition? Existing, string Code, SampleConditionSpec Spec)>();
        foreach (var spec in specs)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(spec.Name);
            SampleCondition? existing = null;
            string code;
            if (spec.Id is { } id && id != Guid.Empty)
            {
                existing = _conditions.FirstOrDefault(condition => condition.Id == id);
                if (existing is null)
                {
                    CatalogueAGuard.Refuse(SampleTypeCodes.ConditionNotFound, "A listed condition does not belong to this sample type.");
                }

                if (!listed.Add(id))
                {
                    CatalogueAGuard.Refuse(SampleTypeCodes.ConditionCodeDuplicate, "A condition is listed twice.");
                }

                code = existing!.Code;   // immutable: references and labels keep it
            }
            else
            {
                code = SampleCondition.CleanCode(spec.Code);
                if (_conditions.Any(condition => condition.Code == code))
                {
                    // the codes of existing conditions stay reserved even when they are omitted (they are only deactivated)
                    CatalogueAGuard.Refuse(SampleTypeCodes.ConditionCodeDuplicate, $"The condition code '{code}' already exists in this sample type; edit or reactivate it instead.");
                }
            }

            if (!codes.Add(code))
            {
                CatalogueAGuard.Refuse(SampleTypeCodes.ConditionCodeDuplicate, $"The condition code '{code}' is used twice in this sample type.");
            }

            staged.Add((existing, code, spec));
        }

        if (deactivateOmitted)
        {
            foreach (var omitted in _conditions.Where(condition => !listed.Contains(condition.Id)))
            {
                omitted.Change(omitted.Name, isActive: false);
            }
        }

        foreach (var (existing, code, spec) in staged)
        {
            if (existing is null)
            {
                _conditions.Add(new SampleCondition(Guid.CreateVersion7(), Id, code, spec.Name, spec.IsActive));
            }
            else
            {
                existing.Change(spec.Name, spec.IsActive);
            }
        }
    }
}

/// <summary>A condition as an editor sends it: <see cref="Id"/> is null for a new one.</summary>
public sealed record SampleConditionSpec(Guid? Id, string Code, LocalizedText Name, bool IsActive);

/// <summary>One entry of the compatibility map (LIS-CR-015 M4): <see cref="MergesIntoConditionId"/> null = its own sample.</summary>
public sealed record SampleConditionPair(Guid ConditionId, Guid? MergesIntoConditionId);

/// <summary>
/// A condition of a sample type (fasting, random, refrigerated…). Child of <see cref="SampleType"/>, keyed
/// <c>(tenant_id, id)</c> in <c>app.sample_conditions</c>; referenced by id from services, tests and tubes.
/// </summary>
public sealed class SampleCondition : Entity, ITenantScoped
{
    public const int MaxCodeLength = 20;

    private SampleCondition()
    {
        Code = string.Empty;
        Name = null!;
    }

    internal SampleCondition(Guid id, Guid sampleTypeId, string code, LocalizedText name, bool isActive)
        : base(id)
    {
        SampleTypeId = sampleTypeId;
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    public Guid TenantId { get; private set; }

    public Guid SampleTypeId { get; private set; }

    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Compatible condition (LIS-CR-015 M4): the active condition of the same sample type whose tube this condition's tests
    /// join when that tube is in the same visit (one level; set through <see cref="SampleType.SetCompatibleConditions"/>).
    /// </summary>
    public Guid? MergesIntoConditionId { get; private set; }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "condition");

    internal void MergeInto(Guid? targetConditionId) => MergesIntoConditionId = targetConditionId;

    internal void Change(LocalizedText name, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        IsActive = isActive;
    }
}
