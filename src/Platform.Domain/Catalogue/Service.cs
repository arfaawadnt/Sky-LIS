using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>The kinds of orderable item (SRS CP-40, CP-55; LDM §3.1). Fixed at creation (assumption Q4).</summary>
public enum ServiceType
{
    /// <summary>One test ordered on its own.</summary>
    Test = 0,

    /// <summary>Several tests ordered and reported together (LDM profile).</summary>
    Profile = 1,

    /// <summary>Several services at one package price (LDM panel / mega).</summary>
    Package = 2,

    /// <summary>A culture: tests of which exactly one is of type Culture (LDM §3.6).</summary>
    Culture = 3,

    /// <summary>A free-text report (histology, cytology) carried by one formatted-text test (LDM §3.7).</summary>
    TextReport = 4,

    /// <summary>A chargeable non-test service: no sample, no result, not on the job order (CP-55).</summary>
    Additional = 5,
}

public enum ServiceMemberKind
{
    Test = 0,
    Service = 1,
}

/// <summary>The editable data of a service (everything but the immutable code and type, the members and the sequence).</summary>
public sealed record ServiceDefinition(
    string ShortName,
    int? ShortcutNo,
    LocalizedText Name,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string> Aliases,
    string? Precaution,
    GenderRestriction Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum);

/// <summary>
/// A member as the handler resolved it: the aggregate cannot read other aggregates, so the handler supplies the
/// member's type (and has already checked that the member exists and, when newly selected, is active: CAT-027).
/// </summary>
public sealed record ResolvedServiceMember(ServiceMemberKind Kind, Guid MemberId, int Order, bool InSum, bool Visible, LabTestType? TestType, ServiceType? ServiceType);

/// <summary>
/// An orderable item (SRS CP-40, CP-42, CP-55): tests, profiles, packages, cultures, text reports and additional
/// services. Its members are tests or (for packages) other services, each with an order, an in-sum flag (profile
/// sum, LDM §3.5) and a visibility flag. The sequence is the global report order (CP-42).
/// </summary>
public sealed class Service : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MaxShortNameLength = 50;
    public const int MaxReportNameLength = 200;
    public const int MaxReferenceCodeLength = 20;
    public const int MaxHeaderLength = 200;
    public const int MaxPrecautionLength = 1000;
    public const int MaxAliases = 20;
    public const int MaxAliasLength = 100;
    public const int MaxShortcutNo = 999_999;
    public const int MaxTurnaroundHours = 8760;
    public const int MaxLabelCount = 20;
    public const int MaxMembers = 200;
    public const decimal MaxProfileSum = 1_000_000m;

    private readonly List<ServiceMember> _members = [];

    private Service()
    {
        Code = string.Empty;
        ShortName = string.Empty;
        Name = null!;
        Aliases = [];
    }

    private Service(Guid id, string code, ServiceType type, int sequence)
        : base(id)
    {
        Code = code;
        Type = type;
        Sequence = sequence;
        ShortName = string.Empty;
        Name = null!;
        Aliases = [];
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant (INV-CAT-001), immutable.</summary>
    public string Code { get; private set; }

    public ServiceType Type { get; private set; }

    public string ShortName { get; private set; }

    /// <summary>Typing shortcut at reception (LDM §3.8), unique per tenant when set (INV-CAT-005).</summary>
    public int? ShortcutNo { get; private set; }

    public LocalizedText Name { get; private set; }

    public string? ReportName { get; private set; }

    public string? Loinc { get; private set; }

    public string? Cpt { get; private set; }

    /// <summary>Required except for additional services (CAT-041).</summary>
    public Guid? ClinicalGroupId { get; private set; }

    public Guid? SampleTypeId { get; private set; }

    public Guid? SampleConditionId { get; private set; }

    public string? Header { get; private set; }

    public string[] Aliases { get; private set; }

    public string? Precaution { get; private set; }

    public GenderRestriction Gender { get; private set; }

    public int TurnaroundHours { get; private set; }

    public int LabelCount { get; private set; }

    public bool PrintAlone { get; private set; }

    public bool IsOrderable { get; private set; }

    public bool ShowOnWeb { get; private set; }

    /// <summary>Binding total of the in-sum members of a profile (e.g. a differential = 100), checked by Results.</summary>
    public decimal? ProfileSum { get; private set; }

    /// <summary>Global report order (CP-42), &gt; 0 and unique per tenant (INV-CAT-004).</summary>
    public int Sequence { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Incremented by every member change, so the root row carries the concurrency check and the modification stamp.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<ServiceMember> Members => _members.AsReadOnly();

    public bool CarriesSample => Type is ServiceType.Test or ServiceType.Profile or ServiceType.Culture or ServiceType.TextReport;

    /// <summary>T9 Create (<c>catalogue.create</c>) with its members; the sequence is the next free one (handler).</summary>
    public static Service Create(string code, ServiceType type, ServiceDefinition definition, int sequence, IReadOnlyList<ResolvedServiceMember> members, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(members);
        if (!Enum.IsDefined(type))
        {
            CatalogueAGuard.Refuse(CatalogueACodes.TextInvalid, "Unknown service type.");
        }

        var service = new Service(Guid.CreateVersion7(), CleanCode(code), type, CleanSequence(sequence));
        service.Apply(definition);
        service.SetMembers(members);
        service.CheckProfileSum();
        service.Raise(new ServiceCreated(service.Id, service.Code, service.Type, service.Sequence, nowUtc));
        return service;
    }

    /// <summary>T10 Edit (<c>catalogue.edit</c>): everything but code, type, members and sequence.</summary>
    public void Change(ServiceDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Apply(definition);
        CheckProfileSum();
        Raise(new ServiceChanged(Id, Code, nowUtc));
    }

    /// <summary>T11 Replace members (<c>catalogue.edit</c>): the member rules of the type (INV-CAT-003).</summary>
    public void ReplaceMembers(IReadOnlyList<ResolvedServiceMember> members, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(members);
        SetMembers(members);
        CheckProfileSum();
        Revision++;
        Raise(new ServiceMembersChanged(Id, Code, _members.Count, nowUtc));
    }

    /// <summary>T12 part of the bulk sequence save; uniqueness across the tenant is the handler's check.</summary>
    public void Resequence(int sequence, DateTimeOffset nowUtc)
    {
        var cleaned = CleanSequence(sequence);
        if (cleaned == Sequence)
        {
            return;
        }

        Sequence = cleaned;
        Raise(new ServiceResequenced(Id, Code, Sequence, nowUtc));
    }

    public bool HasMember(ServiceMemberKind kind, Guid memberId) => _members.Any(member => member.Kind == kind && member.MemberId == memberId);

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new ServiceDeactivated(Id, Code, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new ServiceReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "service");

    public static int CleanSequence(int sequence)
    {
        if (sequence <= 0)
        {
            CatalogueAGuard.Refuse(ServiceCodes.SequenceInvalid, "The sequence must be greater than zero.");
        }

        return sequence;
    }

    /// <summary>
    /// T12 (change request 2026-10-19): the numbers of one bulk sequence save must be distinct among themselves before the
    /// tenant-wide check (INV-CAT-004); a number sent for two services is refused with CAT-033 and nothing is applied.
    /// </summary>
    public static void EnsureDistinctSequences(IEnumerable<int> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        var seen = new HashSet<int>();
        foreach (var sequence in sequences)
        {
            if (!seen.Add(sequence))
            {
                CatalogueAGuard.Refuse(ServiceCodes.SequenceRepeated, $"The sequence {sequence} is given to more than one of the listed services.");
            }
        }
    }

    private void Apply(ServiceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition.Name);
        var additional = Type == ServiceType.Additional;

        if (!additional && (definition.ClinicalGroupId is null || definition.ClinicalGroupId == Guid.Empty))
        {
            CatalogueAGuard.Refuse(ServiceCodes.ClinicalGroupRequired, "A clinical group is required for every service except additional services.");
        }

        var sampleTypeId = definition.SampleTypeId == Guid.Empty ? null : definition.SampleTypeId;
        var conditionId = definition.SampleConditionId == Guid.Empty ? null : definition.SampleConditionId;
        if (!CarriesSample && (sampleTypeId is not null || conditionId is not null))
        {
            CatalogueAGuard.Refuse(ServiceCodes.SampleNotAllowed, $"A {Type} service carries no sample; its members bring their own.");
        }

        if (CarriesSample && sampleTypeId is null)
        {
            CatalogueAGuard.Refuse(ServiceCodes.SampleRequired, "A sample type is required: tubes are generated from it (BR-02).");
        }

        if (conditionId is not null && sampleTypeId is null)
        {
            CatalogueAGuard.Refuse(ServiceCodes.ConditionMismatch, "A sample condition needs its sample type.");
        }

        if (definition.ShortcutNo is { } shortcut && (shortcut < 1 || shortcut > MaxShortcutNo))
        {
            CatalogueAGuard.Refuse(ServiceCodes.ShortcutInvalid, $"The shortcut number must be between 1 and {MaxShortcutNo}.");
        }

        if (definition.ProfileSum is { } sum && (Type != ServiceType.Profile || sum <= 0 || sum > MaxProfileSum))
        {
            CatalogueAGuard.Refuse(ServiceCodes.ProfileSumInvalid, "A profile sum is a positive total and only profiles carry one.");
        }

        if (!Enum.IsDefined(definition.Gender))
        {
            CatalogueAGuard.Refuse(CatalogueACodes.TextInvalid, "Unknown gender restriction.");
        }

        ShortName = CatalogueAGuard.Required(definition.ShortName, MaxShortNameLength, "The short name");
        ShortcutNo = definition.ShortcutNo;
        Name = definition.Name;
        ReportName = CatalogueAGuard.Optional(definition.ReportName, MaxReportNameLength, "The report name");
        Loinc = CatalogueAGuard.ReferenceCode(definition.Loinc, MaxReferenceCodeLength, "The LOINC code");
        Cpt = CatalogueAGuard.ReferenceCode(definition.Cpt, MaxReferenceCodeLength, "The CPT code");
        ClinicalGroupId = definition.ClinicalGroupId == Guid.Empty ? null : definition.ClinicalGroupId;
        SampleTypeId = sampleTypeId;
        SampleConditionId = conditionId;
        Header = CatalogueAGuard.Optional(definition.Header, MaxHeaderLength, "The header");
        Aliases = CleanAliases(definition.Aliases);
        Precaution = CatalogueAGuard.Optional(definition.Precaution, MaxPrecautionLength, "The precaution");
        Gender = definition.Gender;
        TurnaroundHours = additional ? 0 : CatalogueAGuard.Range(definition.TurnaroundHours, 0, MaxTurnaroundHours, "The turnaround (hours)");
        LabelCount = additional ? 0 : CatalogueAGuard.Range(definition.LabelCount, 0, MaxLabelCount, "The number of labels");
        PrintAlone = !additional && definition.PrintAlone;
        IsOrderable = definition.IsOrderable;
        ShowOnWeb = definition.ShowOnWeb;
        ProfileSum = definition.ProfileSum;
    }

    private void SetMembers(IReadOnlyList<ResolvedServiceMember> members)
    {
        ValidateMembers(members);

        _members.Clear();
        foreach (var member in members.OrderBy(member => member.Order))
        {
            _members.Add(new ServiceMember(Guid.CreateVersion7(), Id, member.Kind, member.MemberId, member.Order, member.Kind == ServiceMemberKind.Test && member.InSum, member.Visible));
        }
    }

    private void ValidateMembers(IReadOnlyList<ResolvedServiceMember> members)
    {
        if (members.Count > MaxMembers)
        {
            CatalogueAGuard.Refuse(ServiceCodes.MembersInvalidForType, $"A service has at most {MaxMembers} members.");
        }

        var tests = members.Count(member => member.Kind == ServiceMemberKind.Test);
        var services = members.Count - tests;
        switch (Type)
        {
            case ServiceType.Additional when members.Count > 0:
                CatalogueAGuard.Refuse(ServiceCodes.MembersInvalidForType, "An additional service has no members.");
                break;
            case ServiceType.Test when members.Count != 1:
            case ServiceType.TextReport when members.Count != 1:
                CatalogueAGuard.Refuse(ServiceCodes.MembersInvalidForType, $"A {Type} service carries exactly one test.");
                break;
            case ServiceType.Profile or ServiceType.Culture or ServiceType.Package when members.Count == 0:
                CatalogueAGuard.Refuse(ServiceCodes.MembersInvalidForType, $"A {Type} service needs at least one member.");
                break;
        }

        if (Type == ServiceType.Package ? tests > 0 : services > 0)
        {
            CatalogueAGuard.Refuse(
                ServiceCodes.MemberKindNotAllowed,
                Type == ServiceType.Package ? "A package contains services, not tests." : $"A {Type} service contains tests, not services.");
        }

        if (members.Any(member => member.Kind == ServiceMemberKind.Service && member.MemberId == Id))
        {
            CatalogueAGuard.Refuse(ServiceCodes.SelfInclusion, "A package cannot include itself.");
        }

        if (members.Any(member => member.Kind == ServiceMemberKind.Service && member.ServiceType is ServiceType.Package or ServiceType.Additional))
        {
            CatalogueAGuard.Refuse(ServiceCodes.NestedPackage, "A package contains laboratory services only: no packages and no additional services.");
        }

        if (members.GroupBy(member => (member.Kind, member.MemberId)).Any(group => group.Count() > 1))
        {
            CatalogueAGuard.Refuse(ServiceCodes.DuplicateMember, "A member is listed twice.");
        }

        if (members.Any(member => member.Order <= 0) || members.Select(member => member.Order).Distinct().Count() != members.Count)
        {
            CatalogueAGuard.Refuse(ServiceCodes.MemberOrderInvalid, "Member orders are positive and unique within the service.");
        }

        if (Type == ServiceType.Culture && members.Count(member => member.TestType == LabTestType.Culture) != 1)
        {
            CatalogueAGuard.Refuse(ServiceCodes.MemberTestTypeMismatch, "A culture service carries exactly one test of type Culture (LDM §3.6).");
        }

        if (Type == ServiceType.TextReport && members.Any(member => member.TestType != LabTestType.FormattedText))
        {
            CatalogueAGuard.Refuse(ServiceCodes.MemberTestTypeMismatch, "A text report carries one formatted-text test (LDM §3.7).");
        }
    }

    private void CheckProfileSum()
    {
        if (ProfileSum is not null && !_members.Any(member => member.InSum))
        {
            CatalogueAGuard.Refuse(ServiceCodes.ProfileSumInvalid, "A profile sum needs at least one member counted in the sum.");
        }
    }

    private static string[] CleanAliases(IReadOnlyList<string>? aliases)
    {
        var cleaned = (aliases ?? []).Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias.Trim()).ToArray();
        if (cleaned.Length > MaxAliases
            || cleaned.Any(alias => alias.Length > MaxAliasLength)
            || cleaned.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cleaned.Length)
        {
            CatalogueAGuard.Refuse(ServiceCodes.AliasesInvalid, $"Up to {MaxAliases} distinct aliases of at most {MaxAliasLength} characters.");
        }

        return cleaned;
    }
}

/// <summary>
/// A member of a service: a test (test, profile, culture, text report) or a service (package). Child of
/// <see cref="Service"/> in <c>app.service_members</c>; the member is referenced by kind and id (no foreign key:
/// it points at one of two tables; the handler checks it).
/// </summary>
public sealed class ServiceMember : Entity, ITenantScoped
{
    private ServiceMember()
    {
    }

    internal ServiceMember(Guid id, Guid serviceId, ServiceMemberKind kind, Guid memberId, int order, bool inSum, bool visible)
        : base(id)
    {
        ServiceId = serviceId;
        Kind = kind;
        MemberId = memberId;
        Order = order;
        InSum = inSum;
        Visible = visible;
    }

    public Guid TenantId { get; private set; }

    public Guid ServiceId { get; private set; }

    public ServiceMemberKind Kind { get; private set; }

    public Guid MemberId { get; private set; }

    public int Order { get; private set; }

    /// <summary>Counted in the profile sum (test members only).</summary>
    public bool InSum { get; private set; }

    /// <summary>Printed on the report (LDM §3.5 per-row "Visible").</summary>
    public bool Visible { get; private set; }
}
