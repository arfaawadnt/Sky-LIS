namespace Platform.Domain.Integration;

/// <summary>Refusal codes of the Integration module (intake LIS-INT-INT-001 §7–§8).</summary>
public static class IntegrationCodes
{
    // Validation (001–029)
    public const string CodeInvalid = "INT-001";
    public const string NameRequired = "INT-002";
    public const string ClinicalGroupRequired = "INT-003";
    public const string ModeConnectionMismatch = "INT-004";
    public const string SerialSettingsInvalid = "INT-005";
    public const string TcpEndpointInvalid = "INT-006";
    public const string FolderPathInvalid = "INT-007";
    public const string PrintCopiesInvalid = "INT-008";
    public const string FlagsInvalid = "INT-009";
    public const string AnalyserCodeInvalid = "INT-010";
    public const string SlopeInvalid = "INT-011";
    public const string ConversionsInvalid = "INT-012";
    public const string RangeOverrideInvalid = "INT-013";
    public const string ResultSelectionInvalid = "INT-014";
    public const string SampleCodeInvalid = "INT-015";
    public const string AgentNameInvalid = "INT-016";
    public const string BranchRequired = "INT-017";
    public const string ConfigurationInvalid = "INT-018";
    public const string HeartbeatInvalid = "INT-019";
    public const string LogBatchInvalid = "INT-020";
    public const string KitNameInvalid = "INT-021";
    public const string GenesInvalid = "INT-022";
    public const string RunFileLayoutInvalid = "INT-023";
    public const string FoldersInvalid = "INT-024";
    public const string VerdictRulesInvalid = "INT-025";
    public const string SerialLockInvalid = "INT-026";
    public const string TextTooLong = "INT-027";
    public const string ResultBatchInvalid = "INT-028";
    public const string TestRequired = "INT-029";

    // State and caller (030–039)
    public const string AnalyserInactive = "INT-030";
    public const string AgentInactive = "INT-031";
    public const string KitInactive = "INT-032";
    public const string MappingOfAnotherAnalyser = "INT-033";
    public const string CallerIsNotAgent = "INT-035";
    public const string AnalyserNotOnAgent = "INT-036";
    public const string RunFileFromOtherInstrument = "INT-037";
    public const string RunFileUnreadable = "INT-038";

    // References to other modules (040–049)
    public const string ClinicalGroupUnknown = "INT-040";
    public const string SampleTypeUnknown = "INT-041";
    public const string TestUnknown = "INT-042";
    public const string UnitUnknown = "INT-043";
    public const string BranchUnknown = "INT-044";
    public const string AnalyserModelUnknown = "INT-045";

    // Conflicts (100–199)
    public const string AnalyserCodeExists = "INT-100";
    public const string MappingCodeExists = "INT-101";
    public const string SampleCodeExists = "INT-103";
    public const string AgentNameExists = "INT-104";
    public const string KitNameExists = "INT-105";

    // Not found (404–408)
    public const string AnalyserNotFound = "INT-404";
    public const string MappingNotFound = "INT-405";
    public const string SampleCodeNotFound = "INT-406";
    public const string AgentNotFound = "INT-407";
    public const string KitNotFound = "INT-408";
}
