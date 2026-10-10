namespace Platform.Contracts.Integration;

// The site agent API (intake LIS-INT-INT-001 rows H1, R1, R2): JSON over HTTPS with a machine access token. The agent
// speaks ASTM / HL7 / files to the instruments and translates to these shapes (OQ-08).

/// <summary><paramref name="Status"/> of the agent itself (Online | Error); each analyser reports Connected | Disconnected | Error | Unknown.</summary>
public sealed record AgentHeartbeatRequest(string? Version, string? Status, string? Message, IReadOnlyList<AgentAnalyserStatus>? Analysers);

public sealed record AgentAnalyserStatus(string Code, string Status, string? Message);

/// <summary>The agent reloads <c>GET /api/v1/agents/config</c> when <paramref name="ConfigVersion"/> differs from the one it holds.</summary>
public sealed record AgentHeartbeatResponse(Guid AgentId, int ConfigVersion, DateTimeOffset ServerTimeUtc, IReadOnlyList<string> UnknownAnalyserCodes);

public sealed record AgentLogEntryDto(DateTimeOffset AtUtc, string Level, string Message, string? SampleNumber, string? AnalyserCode);

public sealed record AgentLogBatchRequest(IReadOnlyList<AgentLogEntryDto> Entries);

public sealed record AgentLogBatchResponse(int Accepted);

public sealed record AgentMappingConfig(string AnalyserCode, string? HostCode, Guid TestId, string? TestCode, int ResultSelection, bool Query, string? QueryDetails, bool AutoSend);

public sealed record AgentSampleCodeConfig(Guid SampleTypeId, string Code);

public sealed record AgentAnalyserConfig(
    Guid Id,
    string Code,
    string NameEn,
    string Mode,
    AnalyserConnectionDto Connection,
    bool AutoPrint,
    bool DownloadOrders,
    bool AppendUnregisteredTests,
    bool AppendUnregisteredSamples,
    int PrintCopies,
    IReadOnlyList<AnalyserFlagDto> Flags,
    IReadOnlyList<AgentMappingConfig> Mappings,
    IReadOnlyList<AgentSampleCodeConfig> SampleCodes);

public sealed record AgentPcrKitConfig(Guid Id, string Name, string AnalyserCode, string? SerialLock, PcrRunFileLayoutDto Layout, string ValueColumn, string ResultFolder, string BackupFolder, string FailedFolder);

/// <summary>Everything the agent needs: its own settings (<paramref name="ConfigJson"/>), its active analysers with mappings and sample codes, and their active PCR kits.</summary>
public sealed record AgentConfigView(Guid AgentId, string Name, Guid BranchId, int ConfigVersion, string ConfigJson, IReadOnlyList<AgentAnalyserConfig> Analysers, IReadOnlyList<AgentPcrKitConfig> PcrKits, DateTimeOffset GeneratedAtUtc);

public sealed record AgentOrderTest(Guid TestId, string AnalyserCode, string? HostCode);

/// <summary>A sample to run (download / query mode): identifiers and test codes only, never patient demographics (intake §12).</summary>
public sealed record AgentOrder(string SampleNumber, string? Accession, string? SampleTypeCode, bool Urgent, DateTimeOffset? CollectedAtUtc, DateTimeOffset? ReceivedAtUtc, IReadOnlyList<AgentOrderTest> Tests);

public sealed record AgentOrdersView(string AnalyserCode, DateTimeOffset Since, DateTimeOffset GeneratedAtUtc, IReadOnlyList<AgentOrder> Orders);

public sealed record AgentResultItem(string SampleNumber, string AnalyserTestCode, string Value, IReadOnlyList<string>? Flags, string? Unit, DateTimeOffset? MeasuredAtUtc);

public sealed record AgentResultBatchRequest(string AnalyserCode, IReadOnlyList<AgentResultItem> Results);

/// <summary>Per item: Accepted | Refused | Unmapped | AutoSendOff | UnknownSample; the batch itself is never refused for one item.</summary>
public sealed record AgentResultOutcome(int Index, string SampleNumber, string AnalyserTestCode, string Status, string? ConvertedValue, string? Reason);

public sealed record AgentResultBatchResponse(IReadOnlyList<AgentResultOutcome> Items);

/// <summary>A PCR run file as delimited text (the agent converts instrument workbooks) with the serial the agent read.</summary>
public sealed record AgentPcrRunRequest(string KitName, string? InstrumentSerial, string FileName, string Content);

public sealed record AgentPcrSampleOutcome(string SampleNumber, string? Verdict, string? VerdictText, string Status, string? Reason);

/// <summary><paramref name="Disposition"/>: Backup (move the file to the backup folder) or Failed (move it to the failed folder).</summary>
public sealed record AgentPcrRunResponse(string Disposition, string? RefusalCode, string? Reason, IReadOnlyList<AgentPcrSampleOutcome> Samples);
