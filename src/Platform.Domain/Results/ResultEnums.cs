namespace Platform.Domain.Results;

/// <summary>
/// The test track of a result (SRS status model "Test / result"; LDM §2.3): Ordered → Complete → Reviewed → Verified →
/// Printed, plus Cancelled when the visit line is cancelled. The order of the members is the order of the track
/// (<see cref="ResultStatuses.Rank"/>), which the work queue's "Not X" boxes rely on (BR-10).
/// </summary>
public enum ResultStatus
{
    Ordered = 0,
    Complete = 1,
    Reviewed = 2,
    Verified = 3,
    Printed = 4,
    Cancelled = 5,
}

/// <summary>The flag of a value against the resolved range (LDM §5.3 colour key: High, Low, High Panic, Low Panic) or of a list value.</summary>
public enum ResultFlag
{
    Normal = 0,
    Low = 1,
    High = 2,
    CriticalLow = 3,
    CriticalHigh = 4,
    Abnormal = 5,
}

/// <summary>Where the value came from (MODULES.md §5 contract: 'Manual' | 'Analyser'). Formula values take the source of the input that triggered them.</summary>
public enum ResultSource
{
    Manual = 0,
    Analyser = 1,
}

/// <summary>What a row of the result log records (LDM §5.3 "RL" value log and "SL" status log, in one append-only table).</summary>
public enum ResultLogKind
{
    Ordered = 0,
    Value = 1,
    Status = 2,
    DeltaAcknowledged = 3,
    Comment = 4,
    Note = 5,
    PatientMoved = 6,
}

/// <summary>Helpers over the track order.</summary>
public static class ResultStatuses
{
    /// <summary>The position of the status on the track; Cancelled sorts after Printed but is never "below" anything.</summary>
    public static int Rank(ResultStatus status) => (int)status;

    /// <summary>True for the statuses that carry a value (Complete and beyond, Cancelled excluded).</summary>
    public static bool HasPassedEntry(ResultStatus status) => status is ResultStatus.Complete or ResultStatus.Reviewed or ResultStatus.Verified or ResultStatus.Printed;

    /// <summary>Verified and Printed: the value is signed (BR-09) and changes only through an amendment.</summary>
    public static bool IsSigned(ResultStatus status) => status is ResultStatus.Verified or ResultStatus.Printed;

    /// <summary>BR-04: the lowest status of the live results (Cancelled ignored); null when every result is cancelled.</summary>
    public static ResultStatus? Lowest(IEnumerable<ResultStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ResultStatus? lowest = null;
        foreach (var status in statuses)
        {
            if (status == ResultStatus.Cancelled)
            {
                continue;
            }

            if (lowest is null || Rank(status) < Rank(lowest.Value))
            {
                lowest = status;
            }
        }

        return lowest;
    }

    /// <summary>BR-10: "Not X" returns everything below X on the track (Not Verified = Ordered + Complete + Reviewed).</summary>
    public static IReadOnlyList<ResultStatus> Below(ResultStatus status) =>
        [.. Enum.GetValues<ResultStatus>().Where(candidate => candidate != ResultStatus.Cancelled && Rank(candidate) < Rank(status))];
}
