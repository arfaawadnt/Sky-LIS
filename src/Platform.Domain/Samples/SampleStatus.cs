namespace Platform.Domain.Samples;

/// <summary>
/// State table of a sample (intake LIS-INT-SMP-001 §6). Ordered → Collected → Received is the physical track
/// (LDM §2.3); Rejected waits for a reinstatement or a replacement; Cancelled and Replaced are terminal. Only
/// Received opens the result gate (BR-03).
/// </summary>
public enum SampleStatus
{
    Ordered = 0,
    Collected = 1,
    Received = 2,
    Rejected = 3,
    Cancelled = 4,
    Replaced = 5,
}

/// <summary>What a history row of the timeline records (intake §5, CP-17).</summary>
public enum SampleAction
{
    Ordered = 0,
    Collected = 1,
    Received = 2,
    Rejected = 3,
    Unrejected = 4,
    Replaced = 5,
    ReplacementOrdered = 6,
    Cancelled = 7,
}

/// <summary>The mode of the scan box on CP-14: the phlebotomy scan collects, the unit scan receives.</summary>
public enum ScanMode
{
    Collect = 0,
    Receive = 1,
}
