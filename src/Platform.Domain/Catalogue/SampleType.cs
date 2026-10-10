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

    public uint RowVersion { get; private set; }

    /// <summary>Incremented by every change of the conditions, so the root row carries the concurrency check and the modification stamp.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<SampleCondition> Conditions => _conditions.AsReadOnly();

    /// <summary>T4 Create (<c>catalogue.create</c>) with its initial conditions.</summary>
    public static SampleType Create(string code, string shortName, LocalizedText name, IEnumerable<SampleConditionSpec> conditions, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(conditions);
        var sampleType = new SampleType(Guid.CreateVersion7(), CleanCode(code), CleanShortName(shortName), name);
        sampleType.UpsertConditions(conditions, deactivateOmitted: false);
        sampleType.Raise(new SampleTypeCreated(sampleType.Id, sampleType.Code, nowUtc));
        return sampleType;
    }

    /// <summary>T5 Edit (<c>catalogue.edit</c>): short name and names; the code is immutable.</summary>
    public void Change(string shortName, LocalizedText name, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var before = (ShortName, Name);
        ShortName = CleanShortName(shortName);
        Name = name;
        if (before != (ShortName, Name))
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
        Revision++;
        Raise(new SampleConditionsChanged(Id, Code, _conditions.Count(condition => condition.IsActive), nowUtc));
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

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "condition");

    internal void Change(LocalizedText name, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        IsActive = isActive;
    }
}
