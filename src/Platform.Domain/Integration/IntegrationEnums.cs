namespace Platform.Domain.Integration;

/// <summary>How the instrument talks to the laboratory (CP-58; LDM §9, manual §13.1).</summary>
public enum AnalyserMode
{
    /// <summary>Instrument → LIS only; the sample is scanned at the instrument and the tests are fixed (most CBC analysers).</summary>
    Unidirectional = 0,

    /// <summary>The instrument queries the orders by sample ID (LDM "Query").</summary>
    Bidirectional = 1,

    /// <summary>The LIS pushes the orders to the instrument (LDM "Load").</summary>
    Download = 2,

    /// <summary>The instrument exports files to a watched folder (most PCR).</summary>
    SharedFolder = 3,
}

/// <summary>The physical link the site agent uses (LDM manual §13.1, §13.3).</summary>
public enum ConnectionKind
{
    Serial = 0,
    Tcp = 1,
    File = 2,
}

/// <summary>Which side opens the TCP connection: the agent is the client or the server (the opposite of the instrument).</summary>
public enum TcpRole
{
    Client = 0,
    Server = 1,
}

/// <summary>What a flag sent by the instrument does (CP-58): Stop holds the result in auto-verification; Record only records it.</summary>
public enum FlagAction
{
    Record = 0,
    Stop = 1,
}

/// <summary>An analyser's link state as the agent last reported it.</summary>
public enum AnalyserLinkStatus
{
    Unknown = 0,
    Connected = 1,
    Disconnected = 2,
    Error = 3,
}

/// <summary>A site agent's state, derived at read time from the last heartbeat (offline after five minutes).</summary>
public enum SiteAgentStatus
{
    Offline = 0,
    Online = 1,
    Error = 2,
}

public enum AgentLogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>The outcome of one incoming value (WF-07: unmapped or Auto-Send-off codes are refused and logged).</summary>
public enum ResultIntakeStatus
{
    Accepted = 0,
    Refused = 1,
    Unmapped = 2,
    AutoSendOff = 3,
    UnknownSample = 4,
}

/// <summary>One gene's call from its Ct value (LDM manual §14.5): a low Ct is positive.</summary>
public enum PcrGeneCall
{
    Negative = 0,
    Borderline = 1,
    Positive = 2,
}

/// <summary>The overall verdict of a sample (LDM manual §14.6).</summary>
public enum PcrVerdict
{
    NotDetected = 0,
    Detected = 1,
    Presumptive = 2,
    Invalid = 3,
}

/// <summary>Where the agent moves a run file after the cloud read it (LDM manual §14.9).</summary>
public enum RunFileDisposition
{
    Backup = 0,
    Failed = 1,
}
