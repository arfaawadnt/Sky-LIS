using Platform.Application.Integration;
using Platform.Domain.Integration;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.Integration;

// Operational rows of the Integration module (intake LIS-INT-INT-001 §5): heartbeat state (overwritten), agent log and
// result intake (append-only). Not aggregates and not entities, so the field-change interceptor leaves them alone; they
// are tenant rows (tenant-first key, query filter, FORCE RLS from the creating migration) stamped at commit.

/// <summary><c>app.site_agent_states</c>: one row per agent (<see cref="Id"/> = the agent id), upserted by every heartbeat.</summary>
internal sealed class SiteAgentStateRow : ITenantScoped
{
    public const int MaxVersionLength = 64;
    public const int MaxMessageLength = 500;

    private SiteAgentStateRow()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid Id { get; private set; }

    public string? Version { get; private set; }

    public bool ReportedError { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset LastHeartbeatAtUtc { get; private set; }

    public static SiteAgentStateRow Create(Guid agentId) => new() { Id = agentId };

    public void Record(string? version, bool reportedError, string? message, DateTimeOffset atUtc)
    {
        Version = Cap(version, MaxVersionLength);
        ReportedError = reportedError;
        Message = Cap(message, MaxMessageLength);
        LastHeartbeatAtUtc = atUtc;
    }

    internal static string? Cap(string? value, int maxLength) => value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary><c>app.analyser_connection_states</c>: one row per analyser (<see cref="Id"/> = the analyser id), as its agent last reported it.</summary>
internal sealed class AnalyserConnectionStateRow : ITenantScoped
{
    private AnalyserConnectionStateRow()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid Id { get; private set; }

    public Guid SiteAgentId { get; private set; }

    public AnalyserLinkStatus Status { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public static AnalyserConnectionStateRow Create(Guid analyserId) => new() { Id = analyserId };

    public void Record(Guid siteAgentId, AnalyserLinkStatus status, string? message, DateTimeOffset atUtc)
    {
        SiteAgentId = siteAgentId;
        Status = status;
        Message = SiteAgentStateRow.Cap(message, SiteAgentStateRow.MaxMessageLength);
        LastSeenAtUtc = atUtc;
    }
}

/// <summary><c>app.agent_log_entries</c>: what the agent reported (SRS CP-61 "messages and errors"), append-only.</summary>
internal sealed class AgentLogEntryRow : ITenantScoped
{
    public const int MaxMessageLength = 2000;
    public const int MaxSampleNumberLength = 64;

    private AgentLogEntryRow()
    {
        Message = string.Empty;
    }

    public Guid TenantId { get; private set; }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public AgentLogLevel Level { get; private set; }

    public string Message { get; private set; }

    public string? SampleNumber { get; private set; }

    public string? AnalyserCode { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static AgentLogEntryRow Create(Guid agentId, DateTimeOffset atUtc, AgentLogLevel level, string message, string? sampleNumber, string? analyserCode, DateTimeOffset receivedAtUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentId = agentId,
        AtUtc = atUtc,
        Level = level,
        Message = SiteAgentStateRow.Cap(message, MaxMessageLength) ?? string.Empty,
        SampleNumber = SiteAgentStateRow.Cap(sampleNumber, MaxSampleNumberLength),
        AnalyserCode = SiteAgentStateRow.Cap(analyserCode, Analyser.MaxCodeLength),
        ReceivedAtUtc = receivedAtUtc,
    };
}

/// <summary><c>app.analyser_result_intake</c>: every value an agent relayed and what became of it (WF-07), append-only.</summary>
internal sealed class ResultIntakeRow : ITenantScoped
{
    public const int MaxSampleNumberLength = 64;
    public const int MaxValueLength = 500;
    public const int MaxReasonLength = 500;
    public const int MaxSourceLength = 16;
    public const int MaxFileNameLength = 260;

    private ResultIntakeRow()
    {
        AnalyserCode = string.Empty;
        SampleNumber = string.Empty;
        AnalyserTestCode = string.Empty;
        RawValue = string.Empty;
        Flags = [];
        Source = string.Empty;
    }

    public Guid TenantId { get; private set; }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid? AnalyserId { get; private set; }

    public string AnalyserCode { get; private set; }

    public string SampleNumber { get; private set; }

    public string AnalyserTestCode { get; private set; }

    public Guid? TestId { get; private set; }

    public Guid? MappingId { get; private set; }

    public string RawValue { get; private set; }

    public string? ConvertedValue { get; private set; }

    public string[] Flags { get; private set; }

    public ResultIntakeStatus Status { get; private set; }

    public string? Reason { get; private set; }

    public string Source { get; private set; }

    public string? FileName { get; private set; }

    public DateTimeOffset? MeasuredAtUtc { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public static ResultIntakeRow From(ResultIntakeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ResultIntakeRow
        {
            Id = record.Id,
            AgentId = record.AgentId,
            AnalyserId = record.AnalyserId,
            AnalyserCode = SiteAgentStateRow.Cap(record.AnalyserCode, Analyser.MaxCodeLength) ?? string.Empty,
            SampleNumber = SiteAgentStateRow.Cap(record.SampleNumber, MaxSampleNumberLength) ?? string.Empty,
            AnalyserTestCode = SiteAgentStateRow.Cap(record.AnalyserTestCode, AnalyserTestMapping.MaxCodeLength) ?? string.Empty,
            TestId = record.TestId,
            MappingId = record.MappingId,
            RawValue = SiteAgentStateRow.Cap(record.RawValue, MaxValueLength) ?? string.Empty,
            ConvertedValue = SiteAgentStateRow.Cap(record.ConvertedValue, MaxValueLength),
            Flags = record.Flags.ToArray(),
            Status = record.Status,
            Reason = SiteAgentStateRow.Cap(record.Reason, MaxReasonLength),
            Source = SiteAgentStateRow.Cap(record.Source, MaxSourceLength) ?? string.Empty,
            FileName = SiteAgentStateRow.Cap(record.FileName, MaxFileNameLength),
            MeasuredAtUtc = record.MeasuredAtUtc,
            AtUtc = record.AtUtc,
        };
    }
}
