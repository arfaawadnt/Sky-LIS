using System.Text.Json;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>
/// The connector installed in a branch (SRS CP-61): it reaches the branch's instruments over serial, TCP or file
/// shares, authenticates to the cloud with its machine credential (02 §6.3), polls its configuration and pushes
/// heartbeats, logs and results. Replaces LDM's one Windows service per machine with a plaintext configuration (LDM
/// §9 "Service", §13 weaknesses). Its analysers are those whose <see cref="Analyser.SiteAgentId"/> is this agent. The
/// configuration (agent-local settings as a JSON object) carries a version the heartbeat answers with; an increase
/// tells the agent to reload. Heartbeat state is operational and lives outside the aggregate (not field-audited).
/// </summary>
public sealed class SiteAgent : AggregateRoot, ITenantScoped
{
    public const int MaxNameLength = 100;
    public const int MaxConfigLength = 16 * 1024;
    public const string EmptyConfig = "{}";

    /// <summary>No heartbeat for this long and the agent shows Offline (SRS CP-61).</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    private SiteAgent()
    {
        Name = string.Empty;
        ConfigJson = EmptyConfig;
    }

    private SiteAgent(Guid id, string name, Guid branchId, Guid machineCredentialId, Guid serviceUserId, string configJson)
        : base(id)
    {
        Name = name;
        BranchId = branchId;
        MachineCredentialId = machineCredentialId;
        ServiceUserId = serviceUserId;
        ConfigJson = configJson;
        ConfigVersion = 1;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant (INV-INT-007).</summary>
    public string Name { get; private set; }

    public Guid BranchId { get; private set; }

    /// <summary>The live credential the agent authenticates with (identity.machine_credentials).</summary>
    public Guid MachineCredentialId { get; private set; }

    /// <summary>The credential's service user: the agent API recognises the caller by it.</summary>
    public Guid ServiceUserId { get; private set; }

    /// <summary>Agent-local settings (log retention, folder roots …) as a JSON object of at most 16 KB.</summary>
    public string ConfigJson { get; private set; }

    /// <summary>Starts at 1; every configuration change or push increases it.</summary>
    public int ConfigVersion { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>G1 Create (<c>integration.manage</c>), together with its credential (issued by the handler in the same commit).</summary>
    public static SiteAgent Register(string name, Guid branchId, Guid machineCredentialId, Guid serviceUserId, string? configJson, DateTimeOffset nowUtc)
    {
        Require(branchId != Guid.Empty, IntegrationCodes.BranchRequired, "A site agent belongs to a branch.", DomainFault.Validation);
        Require(machineCredentialId != Guid.Empty && serviceUserId != Guid.Empty, IntegrationCodes.AgentInactive, "A site agent needs its credential.", DomainFault.Validation);

        var agent = new SiteAgent(Guid.CreateVersion7(), CleanName(name), branchId, machineCredentialId, serviceUserId, CleanConfig(configJson));
        agent.Raise(new SiteAgentRegistered(agent.Id, agent.Name, branchId, machineCredentialId, nowUtc));
        return agent;
    }

    public void Rename(string name) => Name = CleanName(name);

    /// <summary>Replaces the configuration; a different document increases the version (the agent reloads at its next heartbeat).</summary>
    public void Configure(string? configJson, DateTimeOffset nowUtc)
    {
        var cleaned = CleanConfig(configJson);
        if (string.Equals(cleaned, ConfigJson, StringComparison.Ordinal))
        {
            return;
        }

        ConfigJson = cleaned;
        ConfigVersion++;
        Raise(new SiteAgentConfigPushed(Id, ConfigVersion, nowUtc));
    }

    /// <summary>G3 Push configuration (<c>integration.manage</c>): the version increases so the agent reloads analysers, mappings and kits.</summary>
    public void PushConfiguration(DateTimeOffset nowUtc)
    {
        Require(IsActive, IntegrationCodes.AgentInactive, "The site agent is inactive.", DomainFault.Conflict);
        ConfigVersion++;
        Raise(new SiteAgentConfigPushed(Id, ConfigVersion, nowUtc));
    }

    /// <summary>G2 (when the previous credential is no longer live): the agent now authenticates with a new credential.</summary>
    public void ReplaceCredential(Guid machineCredentialId, Guid serviceUserId, DateTimeOffset nowUtc)
    {
        Require(IsActive, IntegrationCodes.AgentInactive, "The site agent is inactive.", DomainFault.Conflict);
        Require(machineCredentialId != Guid.Empty && serviceUserId != Guid.Empty, IntegrationCodes.AgentInactive, "A site agent needs its credential.", DomainFault.Validation);
        MachineCredentialId = machineCredentialId;
        ServiceUserId = serviceUserId;
        Raise(new SiteAgentCredentialReplaced(Id, machineCredentialId, nowUtc));
    }

    /// <summary>Requires the agent to be active (INT-031) before a secret rotation.</summary>
    public void EnsureActive() => Require(IsActive, IntegrationCodes.AgentInactive, "The site agent is inactive.", DomainFault.Conflict);

    /// <summary>G4 Deactivate (<c>integration.manage</c>); the handler revokes the credential in the same commit. Idempotent.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new SiteAgentDeactivated(Id, MachineCredentialId, nowUtc));
    }

    /// <summary>G5 Reactivate (<c>integration.manage</c>) with a freshly issued credential (the old one stays revoked).</summary>
    public void Reactivate(Guid machineCredentialId, Guid serviceUserId, DateTimeOffset nowUtc)
    {
        Require(!IsActive, IntegrationCodes.AgentInactive, "The site agent is already active.", DomainFault.Conflict);
        Require(machineCredentialId != Guid.Empty && serviceUserId != Guid.Empty, IntegrationCodes.AgentInactive, "A site agent needs its credential.", DomainFault.Validation);
        IsActive = true;
        MachineCredentialId = machineCredentialId;
        ServiceUserId = serviceUserId;
        ConfigVersion++;
        Raise(new SiteAgentReactivated(Id, machineCredentialId, nowUtc));
    }

    /// <summary>Online, Error (a recent heartbeat reporting an error) or Offline (no heartbeat for <see cref="OfflineAfter"/>, or never).</summary>
    public static SiteAgentStatus StatusAt(DateTimeOffset? lastHeartbeatAtUtc, bool reportedError, DateTimeOffset nowUtc)
    {
        if (lastHeartbeatAtUtc is not { } last || nowUtc - last > OfflineAfter)
        {
            return SiteAgentStatus.Offline;
        }

        return reportedError ? SiteAgentStatus.Error : SiteAgentStatus.Online;
    }

    public static string CleanName(string name) =>
        IntegrationGuard.Required(name, MaxNameLength, IntegrationCodes.AgentNameInvalid, "The agent name");

    /// <summary>A JSON object of at most 16 KB, stored compact; blank means <c>{}</c> (INT-018).</summary>
    public static string CleanConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return EmptyConfig;
        }

        Require(configJson.Length <= MaxConfigLength, IntegrationCodes.ConfigurationInvalid, $"The configuration is at most {MaxConfigLength} characters.", DomainFault.Validation);
        try
        {
            using var document = JsonDocument.Parse(configJson, new JsonDocumentOptions { MaxDepth = 16 });
            Require(document.RootElement.ValueKind == JsonValueKind.Object, IntegrationCodes.ConfigurationInvalid, "The configuration must be a JSON object.", DomainFault.Validation);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new DomainException(IntegrationCodes.ConfigurationInvalid, "The configuration is not valid JSON.", DomainFault.Validation);
        }
    }
}
