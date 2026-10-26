using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A compatibility group of samples (change request LIS-CR-015, note M4 as the business owner restated it on 2026-10-26):
/// sample types and sample conditions the laboratory sets "together" because their tests can share one sample. At
/// registration, needs of one processing unit (clinical group) whose (sample type, condition) pairs are members of the same
/// active group share one tube, named after the member ordered first among the pairs present (BR-02 addendum).
/// <para>
/// Members are ordered (priority 1…n = the order given); a member is a sample type with one of its conditions, or the type
/// with no condition (services that name none). Rules: at least <see cref="MinMembers"/> members (CAT-280), at most
/// <see cref="MaxMembers"/> (CAT-287); a newly listed member's type exists and is active (CAT-281) and its condition belongs to
/// that type and is active (CAT-282); a pair once per group (CAT-283). "A pair in at most one active group of the tenant"
/// (CAT-284) needs the other groups, so the handler checks it. A member whose type or condition is deactivated later stays in
/// the group and is ignored by planning.
/// </para>
/// </summary>
public sealed class SampleCompatibilityGroup : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 20;
    public const int MinMembers = 2;
    public const int MaxMembers = 30;

    private readonly List<SampleCompatibilityMember> _members = [];

    private SampleCompatibilityGroup()
    {
        Code = string.Empty;
        Name = null!;
    }

    private SampleCompatibilityGroup(Guid id, string code, LocalizedText name)
        : base(id)
    {
        Code = code;
        Name = name;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Incremented by every change of the members, so the root row carries the concurrency check.</summary>
    public int Revision { get; private set; }

    /// <summary>The members by priority (1 = the name a shared sample takes when that member is present).</summary>
    public IReadOnlyList<SampleCompatibilityMember> Members => _members.OrderBy(member => member.Priority).ToList().AsReadOnly();

    /// <summary>The (sample type, condition) pairs of the group.</summary>
    public IEnumerable<CompatibleSamplePair> Pairs => Members.Select(member => member.Pair);

    /// <summary>Create (<c>catalogue.create</c>); <paramref name="sampleTypes"/> are the types the members name (read only, to check availability).</summary>
    public static SampleCompatibilityGroup Create(string code, LocalizedText name, IReadOnlyList<CompatibleSamplePair> members, IReadOnlyCollection<SampleType> sampleTypes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var group = new SampleCompatibilityGroup(Guid.CreateVersion7(), CleanCode(code), name);
        group.ReplaceMembers(members, sampleTypes);
        group.Raise(new SampleCompatibilityGroupCreated(group.Id, group.Code, group._members.Count, nowUtc));
        return group;
    }

    /// <summary>Edit (<c>catalogue.edit</c>): names, members and their order; the code is immutable. Unchanged members may name a type or condition deactivated since.</summary>
    public void Update(LocalizedText name, IReadOnlyList<CompatibleSamplePair> members, IReadOnlyCollection<SampleType> sampleTypes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ReplaceMembers(members, sampleTypes);   // checks everything before it changes anything
        Name = name;
        Revision++;
        Raise(new SampleCompatibilityGroupChanged(Id, Code, _members.Count, nowUtc));
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new SampleCompatibilityGroupDeactivated(Id, Code, nowUtc));
    }

    /// <summary>The handler checks first that no member is in another active group (CAT-284).</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new SampleCompatibilityGroupReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "compatibility group");

    private void ReplaceMembers(IReadOnlyList<CompatibleSamplePair> members, IReadOnlyCollection<SampleType> sampleTypes)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(sampleTypes);
        if (members.Count < MinMembers)
        {
            CatalogueAGuard.Refuse(SampleCompatibilityCodes.MembersTooFew, $"A compatibility group has at least {MinMembers} members.");
        }

        if (members.Count > MaxMembers)
        {
            CatalogueAGuard.Refuse(SampleCompatibilityCodes.MembersTooMany, $"A compatibility group has at most {MaxMembers} members.");
        }

        var seen = new HashSet<CompatibleSamplePair>();
        foreach (var pair in members)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (!seen.Add(pair))
            {
                CatalogueAGuard.Refuse(SampleCompatibilityCodes.MemberDuplicate, "A sample type and condition is listed twice in the group.");
            }

            if (_members.Any(member => member.Pair == pair))
            {
                continue;   // an unchanged member stays valid even if its type or condition was deactivated since
            }

            var type = sampleTypes.FirstOrDefault(candidate => candidate.Id == pair.SampleTypeId);
            if (type is null || !type.IsActive)
            {
                CatalogueAGuard.Refuse(SampleCompatibilityCodes.MemberSampleTypeUnavailable, "A member's sample type does not exist or is inactive.");
            }

            if (pair.SampleConditionId is { } conditionId && !type!.HasCondition(conditionId, mustBeActive: true))
            {
                CatalogueAGuard.Refuse(SampleCompatibilityCodes.MemberConditionUnavailable, $"A member's condition is not an active condition of the sample type '{type.Code}'.");
            }
        }

        _members.RemoveAll(member => !seen.Contains(member.Pair));
        for (var index = 0; index < members.Count; index++)
        {
            var pair = members[index];
            var existing = _members.FirstOrDefault(member => member.Pair == pair);
            if (existing is null)
            {
                _members.Add(new SampleCompatibilityMember(Guid.CreateVersion7(), Id, pair, index + 1));
            }
            else
            {
                existing.MoveTo(index + 1);
            }
        }
    }
}

/// <summary>A sample type with one of its conditions, or with none (<see cref="SampleConditionId"/> null).</summary>
public sealed record CompatibleSamplePair(Guid SampleTypeId, Guid? SampleConditionId);

/// <summary>A member of a <see cref="SampleCompatibilityGroup"/> in <c>app.sample_compatibility_members</c>, keyed <c>(tenant_id, id)</c>.</summary>
public sealed class SampleCompatibilityMember : Entity, ITenantScoped
{
    private SampleCompatibilityMember()
    {
    }

    internal SampleCompatibilityMember(Guid id, Guid groupId, CompatibleSamplePair pair, int priority)
        : base(id)
    {
        GroupId = groupId;
        SampleTypeId = pair.SampleTypeId;
        SampleConditionId = pair.SampleConditionId;
        Priority = priority;
    }

    public Guid TenantId { get; private set; }

    public Guid GroupId { get; private set; }

    public Guid SampleTypeId { get; private set; }

    /// <summary>Null: the sample type with no condition.</summary>
    public Guid? SampleConditionId { get; private set; }

    /// <summary>1…n, the order the user gives; 1 names the shared sample when present.</summary>
    public int Priority { get; private set; }

    public CompatibleSamplePair Pair => new(SampleTypeId, SampleConditionId);

    internal void MoveTo(int priority) => Priority = priority;
}
