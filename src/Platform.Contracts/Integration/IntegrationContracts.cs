using Platform.Contracts.Common;

namespace Platform.Contracts.Integration;

// Request and response shapes of the Integration module's administration API (SRS CP-58…CP-62; intake LIS-INT-INT-001).
// Enum values travel as their names: modes Unidirectional | Bidirectional | Download | SharedFolder; connection kinds
// Serial | Tcp | File; TCP roles Client | Server; flag actions Record | Stop; link statuses Unknown | Connected |
// Disconnected | Error; agent statuses Online | Offline | Error; log levels Debug | Info | Warning | Error; intake
// statuses Accepted | Refused | Unmapped | AutoSendOff | UnknownSample; verdicts Detected | NotDetected | Presumptive | Invalid.

public sealed record AnalyserFlagDto(string Code, string Action, string? Description);

public sealed record AnalyserConnectionDto(string Kind, string? SerialSettings, string? SerialPort, string? Host, int? Port, string? TcpRole, string? FolderPath);

/// <summary>Create (all fields) or update (<paramref name="Code"/> is ignored: the code is immutable) an analyser.</summary>
public sealed record SaveAnalyserRequest(
    string Code,
    string NameEn,
    string NameAr,
    Guid ClinicalGroupId,
    Guid? DefaultSampleTypeId,
    Guid? AnalyserModelId,
    string Mode,
    AnalyserConnectionDto Connection,
    bool AutoPrint,
    bool DownloadOrders,
    bool AppendUnregisteredTests,
    bool AppendUnregisteredSamples,
    bool OverwriteReviewed,
    bool OverwriteVerified,
    int PrintCopies,
    IReadOnlyList<AnalyserFlagDto>? Flags,
    Guid? SiteAgentId);

public sealed record AnalyserListItem(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    Guid ClinicalGroupId,
    string? ClinicalGroupNameEn,
    string? ClinicalGroupNameAr,
    string Mode,
    string ConnectionKind,
    Guid? SiteAgentId,
    string? SiteAgentName,
    string LinkStatus,
    DateTimeOffset? LastSeenAtUtc,
    int MappingCount,
    bool IsActive);

public sealed record AnalyserDetail(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    Guid ClinicalGroupId,
    Guid? DefaultSampleTypeId,
    Guid? AnalyserModelId,
    string Mode,
    AnalyserConnectionDto Connection,
    bool AutoPrint,
    bool DownloadOrders,
    bool AppendUnregisteredTests,
    bool AppendUnregisteredSamples,
    bool OverwriteReviewed,
    bool OverwriteVerified,
    int PrintCopies,
    IReadOnlyList<AnalyserFlagDto> Flags,
    Guid? SiteAgentId,
    bool IsActive);

/// <summary>"Test connection" (CP-58): the last state the hosting agent reported; <paramref name="LinkStatus"/> is Unknown when it is older than five minutes.</summary>
public sealed record AnalyserConnectionState(
    Guid AnalyserId,
    Guid? SiteAgentId,
    string? SiteAgentName,
    string AgentStatus,
    DateTimeOffset? AgentLastHeartbeatAtUtc,
    string LinkStatus,
    string? Message,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset CheckedAtUtc);

/// <summary>A vendor/model of the platform library (SA-15, <c>saas.analyser_models</c>).</summary>
public sealed record AnalyserModelItem(Guid Id, string Vendor, string Model, string? Protocol, string? PhysicalLink);

public sealed record ConversionDto(string FromValue, string ToValue);

public sealed record RangeOverrideDto(decimal? Low, decimal? High, string? Text);

public sealed record SaveMappingRequest(
    Guid TestId,
    string AnalyserCode,
    string? HostCode,
    int ResultSelection,
    decimal Slope,
    decimal Intercept,
    Guid? UnitId,
    bool Query,
    string? QueryDetails,
    bool AutoSend,
    IReadOnlyList<ConversionDto>? Conversions,
    RangeOverrideDto? RangeOverride,
    string? Methodology);

public sealed record SetAutoSendRequest(bool Enabled);

public sealed record ReplaceConversionsRequest(IReadOnlyList<ConversionDto> Conversions);

public sealed record MappingListItem(
    Guid Id,
    Guid AnalyserId,
    Guid TestId,
    string? TestCode,
    string? TestNameEn,
    string? TestNameAr,
    string AnalyserCode,
    string? HostCode,
    int ResultSelection,
    decimal Slope,
    decimal Intercept,
    Guid? UnitId,
    string? UnitCode,
    bool Query,
    string? QueryDetails,
    bool AutoSend,
    IReadOnlyList<ConversionDto> Conversions,
    RangeOverrideDto? RangeOverride,
    string? Methodology,
    bool IsActive);

/// <summary>The printable analyser code sheet (SRS documents: "Analyser code sheet — mapped codes per analyser").</summary>
public sealed record AnalyserCodeSheetDocument(
    Guid AnalyserId,
    string AnalyserCode,
    string AnalyserNameEn,
    string AnalyserNameAr,
    string Mode,
    string ConnectionSummary,
    string? SiteAgentName,
    IReadOnlyList<CodeSheetLine> Lines,
    IReadOnlyList<CodeSheetSampleLine> SampleCodes,
    DateTimeOffset GeneratedAtUtc,
    string? GeneratedBy);

public sealed record CodeSheetLine(string AnalyserCode, string? HostCode, string? TestCode, string? TestNameEn, string? TestNameAr, string? UnitCode, int ResultSelection, decimal Slope, decimal Intercept, bool AutoSend);

public sealed record CodeSheetSampleLine(string Code, string? SampleTypeCode, string? SampleTypeNameEn, string? SampleTypeNameAr);

public sealed record SaveSampleCodeRequest(Guid SampleTypeId, string Code);

public sealed record SampleCodeItem(Guid Id, Guid AnalyserId, Guid SampleTypeId, string? SampleTypeCode, string? SampleTypeNameEn, string? SampleTypeNameAr, string Code, bool IsActive);

public sealed record CreateSiteAgentRequest(string Name, Guid BranchId, string? ConfigJson);

public sealed record UpdateSiteAgentRequest(string Name, string? ConfigJson);

/// <summary>The only response carrying the agent's secret (creation, rotation, reactivation): shown once, never stored in clear.</summary>
public sealed record SiteAgentSecretResponse
{
    public Guid AgentId { get; init; }

    public Guid CredentialId { get; init; }

    public string ClientId { get; init; } = string.Empty;

    [CredentialField]
    public string ClientSecret { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record SiteAgentListItem(
    Guid Id,
    string Name,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    string? ClientId,
    string? CredentialStatus,
    string? Version,
    string Status,
    DateTimeOffset? LastHeartbeatAtUtc,
    int AnalyserCount,
    int ConfigVersion,
    bool IsActive);

public sealed record SiteAgentAnalyserItem(Guid Id, string Code, string NameEn, string NameAr, string LinkStatus, DateTimeOffset? LastSeenAtUtc, string? Message, bool IsActive);

public sealed record SiteAgentDetail(
    Guid Id,
    string Name,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    string ConfigJson,
    int ConfigVersion,
    string? ClientId,
    string? CredentialStatus,
    DateTimeOffset? CredentialExpiresAtUtc,
    string? Version,
    string Status,
    string? StatusMessage,
    DateTimeOffset? LastHeartbeatAtUtc,
    IReadOnlyList<SiteAgentAnalyserItem> Analysers,
    bool IsActive);

public sealed record AgentLogItem(Guid Id, Guid AgentId, DateTimeOffset AtUtc, string Level, string Message, string? SampleNumber, string? AnalyserCode, DateTimeOffset ReceivedAtUtc);

public sealed record IntakeLogItem(
    Guid Id,
    Guid AgentId,
    string? AgentName,
    Guid? AnalyserId,
    string AnalyserCode,
    string SampleNumber,
    string AnalyserTestCode,
    Guid? TestId,
    string RawValue,
    string? ConvertedValue,
    IReadOnlyList<string> Flags,
    string Status,
    string? Reason,
    string Source,
    string? FileName,
    DateTimeOffset? MeasuredAtUtc,
    DateTimeOffset AtUtc);

public sealed record PcrGeneDto(string Name, bool IsInternalControl, decimal PositiveCtMax, decimal BorderlineCtMax);

public sealed record PcrRunFileLayoutDto(string Delimiter, string SampleColumn, string GeneColumn, string? WellColumn, string? ContentColumn, string? PatientContentValue, string? SerialLabel, IReadOnlyList<string>? NoAmplificationTokens);

public sealed record PcrVerdictRulesDto(int DetectedMinPositiveTargets, string DetectedText, string NotDetectedText, string PresumptiveText, string InvalidText);

public sealed record SavePcrKitRequest(
    string Name,
    Guid AnalyserId,
    string AnalyserTestCode,
    string? SerialLock,
    PcrRunFileLayoutDto Layout,
    string ValueColumn,
    IReadOnlyList<PcrGeneDto> Genes,
    PcrVerdictRulesDto Rules,
    string ResultFolder,
    string BackupFolder,
    string FailedFolder);

public sealed record PcrKitListItem(Guid Id, string Name, Guid AnalyserId, string? AnalyserCode, string? AnalyserNameEn, string? AnalyserNameAr, string AnalyserTestCode, string? SerialLock, int GeneCount, bool IsActive);

public sealed record PcrKitDetail(
    Guid Id,
    string Name,
    Guid AnalyserId,
    string AnalyserTestCode,
    string? SerialLock,
    PcrRunFileLayoutDto Layout,
    string ValueColumn,
    IReadOnlyList<PcrGeneDto> Genes,
    PcrVerdictRulesDto Rules,
    string ResultFolder,
    string BackupFolder,
    string FailedFolder,
    bool IsActive);

/// <summary>Simulate a run (CP-62): paste the run file as delimited text.</summary>
public sealed record SimulatePcrRunRequest(string? InstrumentSerial, string Content);

public sealed record PcrGeneReadingView(string Gene, decimal? Ct, string Call, bool IsInternalControl);

public sealed record PcrSampleView(string SampleNumber, string? Well, IReadOnlyList<PcrGeneReadingView> Genes, string? Verdict, string? VerdictText, string? Problem);

/// <summary><paramref name="Disposition"/>: Backup (the file was read) or Failed (refused with <paramref name="RefusalCode"/>).</summary>
public sealed record PcrRunView(bool Readable, string Disposition, string? RefusalCode, string? RefusalReason, string? FileSerial, IReadOnlyList<PcrSampleView> Samples);
