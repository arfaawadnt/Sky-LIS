using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>The behaviour switches of an analyser (CP-58; LDM manual §13.2).</summary>
public sealed record AnalyserSwitches(
    bool AutoPrint,
    bool DownloadOrders,
    bool AppendUnregisteredTests,
    bool AppendUnregisteredSamples,
    bool OverwriteReviewed,
    bool OverwriteVerified);

/// <summary>A flag code the instrument may send and what it does.</summary>
public sealed record AnalyserFlagSpec(string Code, FlagAction Action, string? Description);

/// <summary>The editable data of an analyser (everything but the code).</summary>
public sealed record AnalyserDefinition(
    LocalizedText Name,
    Guid ClinicalGroupId,
    Guid? DefaultSampleTypeId,
    Guid? AnalyserModelId,
    AnalyserMode Mode,
    AnalyserConnection Connection,
    AnalyserSwitches Switches,
    int PrintCopies,
    IReadOnlyList<AnalyserFlagSpec> Flags,
    Guid? SiteAgentId);

/// <summary>A value as the agent relayed it, after the handler found the sample.</summary>
public sealed record IncomingAnalyserResult(string SampleNumber, Guid? SampleId, string AnalyserTestCode, string RawValue, IReadOnlyList<string> Flags, string? Unit, DateTimeOffset MeasuredAtUtc);

/// <summary>What became of one incoming value; <see cref="ConvertedValue"/> is recorded in the intake log whatever the status.</summary>
public sealed record AnalyserResultOutcome(ResultIntakeStatus Status, string? ConvertedValue, IReadOnlyList<string> StopFlags, string? Reason)
{
    public bool Landed => Status == ResultIntakeStatus.Accepted;
}

/// <summary>
/// A connected instrument of the laboratory (SRS CP-58; LDM §9, manual §13.1–§13.3): its code, name and clinical
/// group, the communication mode and the physical link the site agent uses, the behaviour switches, the flags it may
/// send (Stop holds the result in auto-verification), the site agent that hosts it and its activation. It is the gate
/// every incoming value passes (<see cref="AcceptResult"/>): only an active analyser, an active mapping and Auto Send
/// let a value land (INV-INT-004), and only then is <see cref="AnalyserResultReceived"/> raised.
/// </summary>
public sealed class Analyser : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MaxPrintCopies = 10;
    public const int MaxFlags = 50;
    public const int MaxFlagCodeLength = 32;
    public const int MaxFlagDescriptionLength = 200;

    private readonly List<AnalyserFlag> _flags = [];

    private Analyser()
    {
        Code = string.Empty;
        Name = null!;
        Connection = null!;
    }

    private Analyser(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
        Connection = null!;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Upper-case, unique per tenant (INV-INT-001); the agent's configuration names the analyser by it.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public Guid ClinicalGroupId { get; private set; }

    public Guid? DefaultSampleTypeId { get; private set; }

    /// <summary>The vendor/model of the platform library (SA-15), when chosen.</summary>
    public Guid? AnalyserModelId { get; private set; }

    public AnalyserMode Mode { get; private set; }

    public AnalyserConnection Connection { get; private set; }

    /// <summary>Print automatically after auto-verification (LDM "Auto Print").</summary>
    public bool AutoPrint { get; private set; }

    /// <summary>The agent downloads the orders of registered samples (LDM "Load"); always on in Download mode.</summary>
    public bool DownloadOrders { get; private set; }

    public bool AppendUnregisteredTests { get; private set; }

    public bool AppendUnregisteredSamples { get; private set; }

    /// <summary>A re-run may overwrite a reviewed result (LDM "Update R Result").</summary>
    public bool OverwriteReviewed { get; private set; }

    /// <summary>A re-run may overwrite a verified result (LDM "Update V Result").</summary>
    public bool OverwriteVerified { get; private set; }

    public int PrintCopies { get; private set; }

    public IReadOnlyList<AnalyserFlag> Flags => _flags;

    /// <summary>The agent hosting the instrument; the agent's analyser list is derived from this field (single source).</summary>
    public Guid? SiteAgentId { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>A1 Create (<c>integration.edit</c>). Code uniqueness and the referenced ids are checked by the handler.</summary>
    public static Analyser Create(string code, AnalyserDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var analyser = new Analyser(Guid.CreateVersion7(), CleanCode(code));
        analyser.Apply(definition);
        analyser.Raise(new AnalyserRegistered(analyser.Id, analyser.Code, analyser.ClinicalGroupId, nowUtc));
        return analyser;
    }

    /// <summary>A2 Update (<c>integration.edit</c>): every field but the code, in both states.</summary>
    public void Update(AnalyserDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Apply(definition);
    }

    /// <summary>A3 Deactivate (<c>integration.manage</c>). Idempotent; the agent stops relaying, incoming values are refused.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new AnalyserDeactivated(Id, Code, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new AnalyserReactivated(Id, Code, nowUtc));
    }

    /// <summary>
    /// R1/R2 one incoming value through <paramref name="mapping"/>: refused while the analyser is inactive, Unmapped when
    /// the mapping is inactive, AutoSendOff without Auto Send (INV-INT-004, nothing raised); otherwise the mapping's
    /// slope/intercept and conversion table apply and <see cref="AnalyserResultReceived"/> carries the value, the
    /// flags and the Stop flags to the Results and Automation modules.
    /// </summary>
    public AnalyserResultOutcome AcceptResult(AnalyserTestMapping mapping, IncomingAnalyserResult incoming, Guid intakeId, string source, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(incoming);
        Require(mapping.AnalyserId == Id, IntegrationCodes.MappingOfAnotherAnalyser, "The mapping belongs to another analyser.", DomainFault.Validation);

        var flags = (incoming.Flags ?? []).Where(flag => !string.IsNullOrWhiteSpace(flag)).Select(flag => flag.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var stops = StopFlagsOf(flags);
        if (!IsActive)
        {
            return new AnalyserResultOutcome(ResultIntakeStatus.Refused, null, stops, "The analyser is inactive.");
        }

        if (!mapping.IsActive)
        {
            return new AnalyserResultOutcome(ResultIntakeStatus.Unmapped, null, stops, "The mapping is inactive.");
        }

        var converted = mapping.Transform(incoming.RawValue);
        if (!mapping.AutoSend)
        {
            return new AnalyserResultOutcome(ResultIntakeStatus.AutoSendOff, converted, stops, "Auto Send is off for this analyser code.");
        }

        Raise(new AnalyserResultReceived(
            Id,
            Code,
            incoming.SampleNumber,
            incoming.SampleId,
            mapping.TestId,
            mapping.Id,
            converted,
            incoming.RawValue,
            flags,
            string.IsNullOrWhiteSpace(incoming.Unit) ? null : incoming.Unit.Trim(),
            mapping.UnitId,
            stops,
            incoming.MeasuredAtUtc,
            intakeId,
            source,
            OverwriteReviewed,
            OverwriteVerified,
            AppendUnregisteredTests,
            nowUtc));
        return new AnalyserResultOutcome(ResultIntakeStatus.Accepted, converted, stops, null);
    }

    /// <summary>The received flags whose configured action is Stop (case-insensitive codes).</summary>
    public IReadOnlyList<string> StopFlagsOf(IEnumerable<string> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var stops = _flags.Where(flag => flag.Action == FlagAction.Stop).Select(flag => flag.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return flags.Where(flag => stops.Contains(flag.Trim())).Select(flag => flag.Trim()).Distinct(StringComparer.Ordinal).ToList();
    }

    public static string CleanCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 1 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            IntegrationCodes.CodeInvalid,
            $"The analyser code must be 1 to {MaxCodeLength} characters: letters, digits, '-' or '_'.",
            DomainFault.Validation);
        return value;
    }

    private void Apply(AnalyserDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition.Name);
        ArgumentNullException.ThrowIfNull(definition.Connection);
        ArgumentNullException.ThrowIfNull(definition.Switches);
        Require(definition.ClinicalGroupId != Guid.Empty, IntegrationCodes.ClinicalGroupRequired, "An analyser belongs to a clinical group.", DomainFault.Validation);
        Require(Enum.IsDefined(definition.Mode), IntegrationCodes.ModeConnectionMismatch, "Unknown mode.", DomainFault.Validation);
        Require(
            (definition.Mode == AnalyserMode.SharedFolder) == (definition.Connection.Kind == ConnectionKind.File),
            IntegrationCodes.ModeConnectionMismatch,
            "A shared-folder analyser uses a file link, and a file link is used only in shared-folder mode.",
            DomainFault.Validation);
        Require(definition.PrintCopies is >= 0 and <= MaxPrintCopies, IntegrationCodes.PrintCopiesInvalid, $"Print copies must be 0 to {MaxPrintCopies}.", DomainFault.Validation);
        var flags = CleanFlags(definition.Flags ?? []);

        Name = definition.Name;
        ClinicalGroupId = definition.ClinicalGroupId;
        DefaultSampleTypeId = definition.DefaultSampleTypeId == Guid.Empty ? null : definition.DefaultSampleTypeId;
        AnalyserModelId = definition.AnalyserModelId == Guid.Empty ? null : definition.AnalyserModelId;
        Mode = definition.Mode;
        Connection = definition.Connection;
        AutoPrint = definition.Switches.AutoPrint;
        DownloadOrders = definition.Switches.DownloadOrders || definition.Mode == AnalyserMode.Download;
        AppendUnregisteredTests = definition.Switches.AppendUnregisteredTests;
        AppendUnregisteredSamples = definition.Switches.AppendUnregisteredSamples;
        OverwriteReviewed = definition.Switches.OverwriteReviewed;
        OverwriteVerified = definition.Switches.OverwriteVerified;
        PrintCopies = definition.PrintCopies;
        SiteAgentId = definition.SiteAgentId == Guid.Empty ? null : definition.SiteAgentId;
        ReplaceFlags(flags);
    }

    private void ReplaceFlags(IReadOnlyList<AnalyserFlagSpec> flags)
    {
        foreach (var stale in _flags.Where(existing => !flags.Any(flag => string.Equals(flag.Code, existing.Code, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _flags.Remove(stale);
        }

        foreach (var flag in flags)
        {
            var existing = _flags.FirstOrDefault(row => string.Equals(row.Code, flag.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _flags.Add(new AnalyserFlag(flag.Code, flag.Action, flag.Description));
            }
            else
            {
                existing.Change(flag.Code, flag.Action, flag.Description);
            }
        }
    }

    private static List<AnalyserFlagSpec> CleanFlags(IReadOnlyList<AnalyserFlagSpec> flags)
    {
        Require(flags.Count <= MaxFlags, IntegrationCodes.FlagsInvalid, $"At most {MaxFlags} flags.", DomainFault.Validation);
        var cleaned = new List<AnalyserFlagSpec>(flags.Count);
        foreach (var flag in flags)
        {
            Require(flag is not null, IntegrationCodes.FlagsInvalid, "A flag is required.", DomainFault.Validation);
            var code = (flag!.Code ?? string.Empty).Trim();
            Require(code.Length is >= 1 and <= MaxFlagCodeLength && !code.Any(char.IsControl), IntegrationCodes.FlagsInvalid, $"A flag code is 1 to {MaxFlagCodeLength} characters.", DomainFault.Validation);
            Require(Enum.IsDefined(flag.Action), IntegrationCodes.FlagsInvalid, "Unknown flag action.", DomainFault.Validation);
            Require(!cleaned.Any(other => string.Equals(other.Code, code, StringComparison.OrdinalIgnoreCase)), IntegrationCodes.FlagsInvalid, $"The flag '{code}' appears twice.", DomainFault.Validation);
            var description = string.IsNullOrWhiteSpace(flag.Description) ? null : flag.Description.Trim();
            Require(description is null || description.Length <= MaxFlagDescriptionLength, IntegrationCodes.FlagsInvalid, $"A flag description is at most {MaxFlagDescriptionLength} characters.", DomainFault.Validation);
            cleaned.Add(new AnalyserFlagSpec(code, flag.Action, description));
        }

        return cleaned;
    }
}

/// <summary>One flag of an analyser (owned row of <c>app.analyser_flags</c>).</summary>
public sealed class AnalyserFlag : Entity
{
    private AnalyserFlag()
    {
        Code = string.Empty;
    }

    internal AnalyserFlag(string code, FlagAction action, string? description)
        : base(Guid.CreateVersion7())
    {
        Code = code;
        Action = action;
        Description = description;
    }

    public string Code { get; private set; }

    public FlagAction Action { get; private set; }

    public string? Description { get; private set; }

    internal void Change(string code, FlagAction action, string? description)
    {
        Code = code;
        Action = action;
        Description = description;
    }
}
