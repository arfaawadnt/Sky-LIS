using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

// Past-tense facts of the Integration module (intake LIS-INT-INT-001 §11); ids and the few values a reader needs.
// The outbox stamps the tenant. Fields may be added, never removed or renamed.

/// <summary>Where an accepted value came from.</summary>
public static class ResultSources
{
    public const string Analyser = "Analyser";
    public const string Pcr = "Pcr";
}

/// <summary>
/// A mapped, Auto-Send result arrived from an instrument through a site agent (WF-07). The Results module lands it on
/// the sample's ordered result for <paramref name="TestId"/> (idempotent on <paramref name="IntakeId"/>), honouring
/// <paramref name="OverwriteReviewed"/> / <paramref name="OverwriteVerified"/>; Automation holds results carrying
/// <paramref name="StopFlags"/>. <paramref name="Value"/> is after slope/intercept and the conversion table;
/// <paramref name="RawValue"/> is what the instrument sent. <paramref name="Source"/> is <see cref="ResultSources"/>.
/// </summary>
public sealed record AnalyserResultReceived(
    Guid AnalyserId,
    string AnalyserCode,
    string SampleNumber,
    Guid? SampleId,
    Guid TestId,
    Guid MappingId,
    string Value,
    string RawValue,
    IReadOnlyList<string> Flags,
    string? Unit,
    Guid? UnitId,
    IReadOnlyList<string> StopFlags,
    DateTimeOffset MeasuredAtUtc,
    Guid IntakeId,
    string Source,
    bool OverwriteReviewed,
    bool OverwriteVerified,
    bool AppendUnregisteredTests,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AnalyserRegistered(Guid AnalyserId, string Code, Guid ClinicalGroupId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AnalyserDeactivated(Guid AnalyserId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AnalyserReactivated(Guid AnalyserId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record AnalyserTestMapped(Guid MappingId, Guid AnalyserId, Guid TestId, string AnalyserCode, bool AutoSend, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Auto Send is the permission to land results (CP-59): every switch is a recorded fact.</summary>
public sealed record AnalyserAutoSendChanged(Guid MappingId, Guid AnalyserId, Guid TestId, string AnalyserCode, bool Enabled, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SiteAgentRegistered(Guid AgentId, string Name, Guid BranchId, Guid MachineCredentialId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The agent must reload its configuration: the heartbeat answers with <paramref name="ConfigVersion"/>.</summary>
public sealed record SiteAgentConfigPushed(Guid AgentId, int ConfigVersion, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The agent now authenticates with another credential (a new client id; the previous one is revoked).</summary>
public sealed record SiteAgentCredentialReplaced(Guid AgentId, Guid MachineCredentialId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SiteAgentDeactivated(Guid AgentId, Guid MachineCredentialId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SiteAgentReactivated(Guid AgentId, Guid MachineCredentialId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PcrKitDefined(Guid KitId, string Name, Guid AnalyserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PcrKitDeactivated(Guid KitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PcrKitReactivated(Guid KitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
