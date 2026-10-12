using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Integration;

namespace Platform.Application.Integration;

// Write sides and projections of the module (intake LIS-INT-INT-001 §14). Every read is bounded to the current tenant
// (query filter + FORCE RLS). Names of other modules' records are joined by the handlers through the read ports below.

public sealed record AnalyserFilter(string? Search, bool? IsActive, Guid? ClinicalGroupId, Guid? SiteAgentId, ListSort Sort = default);

public interface IAnalyserRepository : IRepository<Analyser>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>The analyser with this code (tracked), for the agent API.</summary>
    Task<Analyser?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>The analysers hosted by the agent (tracked), active or not.</summary>
    Task<IReadOnlyList<Analyser>> ListForAgentAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>Clinical group and connection state are filled by the handler; <paramref name="nowUtc"/> decides when a link state is stale.</summary>
    Task<Paged<AnalyserListItem>> ListAsync(AnalyserFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<AnalyserDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Number of analysers per agent.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByAgentAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken);
}

public interface IAnalyserTestMappingRepository : IRepository<AnalyserTestMapping>
{
    Task<bool> CodeExistsAsync(Guid analyserId, string analyserCode, Guid? exceptMappingId, CancellationToken cancellationToken);

    /// <summary>The mapping of that analyser (tracked); another analyser's id reads as absent.</summary>
    Task<AnalyserTestMapping?> GetInAnalyserAsync(Guid analyserId, Guid mappingId, CancellationToken cancellationToken);

    /// <summary>The mappings of the analyser carrying these codes (tracked, any state), by code (exact).</summary>
    Task<IReadOnlyDictionary<string, AnalyserTestMapping>> FindByCodesAsync(Guid analyserId, IReadOnlyCollection<string> analyserCodes, CancellationToken cancellationToken);

    /// <summary>Test and unit names are filled by the handler.</summary>
    Task<Paged<MappingListItem>> ListAsync(Guid analyserId, string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Active mappings of the analysers, for the agent configuration, the orders and the code sheet.</summary>
    Task<IReadOnlyList<MappingListItem>> ListActiveAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken);
}

public interface IAnalyserSampleCodeRepository : IRepository<AnalyserSampleCode>
{
    Task<bool> ExistsAsync(Guid analyserId, Guid sampleTypeId, CancellationToken cancellationToken);

    Task<AnalyserSampleCode?> GetInAnalyserAsync(Guid analyserId, Guid sampleCodeId, CancellationToken cancellationToken);

    /// <summary>Sample type names are filled by the handler.</summary>
    Task<IReadOnlyList<SampleCodeItem>> ListAsync(IReadOnlyCollection<Guid> analyserIds, bool activeOnly, CancellationToken cancellationToken);
}

public sealed record SiteAgentFilter(string? Search, Guid? BranchId, bool? IsActive, ListSort Sort = default);

/// <summary>A site agent as registers show it before the handler adds names, credential and heartbeat state.</summary>
public sealed record SiteAgentRow(Guid Id, string Name, Guid BranchId, Guid MachineCredentialId, string ConfigJson, int ConfigVersion, bool IsActive);

public interface ISiteAgentRepository : IRepository<SiteAgent>
{
    Task<bool> NameExistsAsync(string name, Guid? exceptAgentId, CancellationToken cancellationToken);

    /// <summary>The agent whose credential's service user is <paramref name="serviceUserId"/>: how the agent API recognises its caller.</summary>
    Task<SiteAgent?> FindByServiceUserAsync(Guid serviceUserId, CancellationToken cancellationToken);

    Task<Paged<SiteAgentRow>> ListAsync(SiteAgentFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken);
}

public sealed record PcrKitFilter(string? Search, bool? IsActive, Guid? AnalyserId, ListSort Sort = default);

public interface IPcrKitRepository : IRepository<PcrKit>
{
    Task<bool> NameExistsAsync(string name, Guid? exceptKitId, CancellationToken cancellationToken);

    /// <summary>The kit with this name whose instrument is one of <paramref name="analyserIds"/>.</summary>
    Task<PcrKit?> FindByNameAsync(string name, IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken);

    /// <summary>Active kits of the instruments (untracked), for the agent configuration.</summary>
    Task<IReadOnlyList<PcrKit>> ListActiveAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken);

    Task<Paged<PcrKitListItem>> ListAsync(PcrKitFilter filter, PageRequest page, CancellationToken cancellationToken);
}

// ---- Read ports over other modules' tables (MODULES.md §1; adapters use raw SQL on the agreed tables) ----

public enum ReferenceKind
{
    ClinicalGroup = 0,
    SampleType = 1,
    Test = 2,
    Unit = 3,
    Branch = 4,
}

public sealed record ReferenceName(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

/// <summary>
/// Catalogue (<c>app.clinical_groups</c>, <c>app.sample_types</c>, <c>app.tests</c>, <c>app.units</c>), organization
/// (<c>app.branches</c>) and the platform library (<c>saas.analyser_models</c>). An id the table does not hold, or a
/// table that does not exist yet, is simply absent from the answer: the handler refuses it (fail closed).
/// </summary>
public interface IIntegrationReferenceLookup
{
    Task<IReadOnlyDictionary<Guid, ReferenceName>> FindAsync(ReferenceKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> AnalyserModelExistsAsync(Guid analyserModelId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalyserModelItem>> ListAnalyserModelsAsync(string? search, CancellationToken cancellationToken);
}

/// <summary>A sample of the Samples module (<c>app.samples</c>) as the intake needs it.</summary>
public sealed record SampleRef(Guid SampleId, string SampleNumber, string Status);

/// <summary>One (sample, test) pair to download (registration <c>app.visits</c>/<c>app.visit_lines</c>, catalogue <c>app.service_members</c>).</summary>
public sealed record OrderRow(string SampleNumber, string? Accession, Guid SampleTypeId, bool Urgent, DateTimeOffset? CollectedAtUtc, DateTimeOffset? ReceivedAtUtc, Guid TestId);

public interface IIntegrationSampleLookup
{
    /// <summary>The samples carrying these numbers (exact); unknown numbers are absent; an absent table answers nothing.</summary>
    Task<IReadOnlyDictionary<string, SampleRef>> FindByNumbersAsync(IReadOnlyCollection<string> sampleNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Samples of the branch's visits in status Collected or Received, changed since <paramref name="sinceUtc"/>, with
    /// their Ordered lines' tests among <paramref name="testIds"/>; at most <paramref name="limit"/> samples; empty when a
    /// table does not exist yet.
    /// </summary>
    Task<IReadOnlyList<OrderRow>> ListOrdersAsync(Guid branchId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, int limit, CancellationToken cancellationToken);
}

// ---- Operational stores of the module (not aggregates, not field-audited; staged here, committed by the handler) ----

public sealed record AnalyserLinkReport(Guid AnalyserId, AnalyserLinkStatus Status, string? Message);

public sealed record AgentHeartbeat(Guid AgentId, string? Version, bool ReportedError, string? Message, DateTimeOffset AtUtc, IReadOnlyList<AnalyserLinkReport> Analysers);

public sealed record AgentState(Guid AgentId, string? Version, bool ReportedError, string? Message, DateTimeOffset? LastHeartbeatAtUtc);

public sealed record AnalyserLinkState(Guid AnalyserId, AnalyserLinkStatus Status, string? Message, DateTimeOffset? LastSeenAtUtc);

public interface IAgentTelemetryStore
{
    /// <summary>Upserts the agent's state and its analysers' link states.</summary>
    Task RecordHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, AgentState>> AgentStatesAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, AnalyserLinkState>> LinkStatesAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken);
}

public sealed record AgentLogRecord(Guid AgentId, DateTimeOffset AtUtc, AgentLogLevel Level, string Message, string? SampleNumber, string? AnalyserCode);

/// <summary><paramref name="MinLevel"/> shows that level and above.</summary>
public sealed record AgentLogFilter(Guid AgentId, DateTimeOffset? From, DateTimeOffset? To, AgentLogLevel? MinLevel, string? Search);

public interface IAgentLogStore
{
    void Append(IEnumerable<AgentLogRecord> entries, DateTimeOffset receivedAtUtc);

    Task<Paged<AgentLogItem>> ListAsync(AgentLogFilter filter, PageRequest page, CancellationToken cancellationToken);
}

public sealed record ResultIntakeRecord(
    Guid Id,
    Guid AgentId,
    Guid? AnalyserId,
    string AnalyserCode,
    string SampleNumber,
    string AnalyserTestCode,
    Guid? TestId,
    Guid? MappingId,
    string RawValue,
    string? ConvertedValue,
    IReadOnlyList<string> Flags,
    ResultIntakeStatus Status,
    string? Reason,
    string Source,
    string? FileName,
    DateTimeOffset? MeasuredAtUtc,
    DateTimeOffset AtUtc);

public sealed record ResultIntakeFilter(DateTimeOffset? From, DateTimeOffset? To, ResultIntakeStatus? Status, Guid? AnalyserId, Guid? AgentId, string? Search);

public interface IResultIntakeLog
{
    void Append(ResultIntakeRecord record);

    /// <summary>Agent names are filled by the handler.</summary>
    Task<Paged<IntakeLogItem>> ListAsync(ResultIntakeFilter filter, PageRequest page, CancellationToken cancellationToken);
}
