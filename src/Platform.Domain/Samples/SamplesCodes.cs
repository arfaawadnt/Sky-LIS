namespace Platform.Domain.Samples;

/// <summary>Refusal codes of the Samples module (intake LIS-INT-SMP-001, sections 7–8).</summary>
public static class SamplesCodes
{
    public const string TubeRequired = "SMP-001";
    public const string SampleNumberInvalid = "SMP-002";
    public const string ReferenceRequired = "SMP-003";

    /// <summary>Collect needs an Ordered sample (a second collect scan is refused).</summary>
    public const string NotOrdered = "SMP-010";

    /// <summary>Receive needs an Ordered or Collected sample.</summary>
    public const string NotReceivable = "SMP-011";

    /// <summary>Reject needs a Collected or Received sample.</summary>
    public const string NotRejectable = "SMP-012";

    /// <summary>Unreject and replace need a Rejected sample.</summary>
    public const string NotRejected = "SMP-013";

    /// <summary>A Replaced sample is never cancelled; the live replacement is.</summary>
    public const string NotCancellable = "SMP-014";

    /// <summary>BR-18: the tenant requires a new barcode, so the rejected sample is replaced, not reinstated.</summary>
    public const string ReinstatementNotAllowed = "SMP-016";

    public const string RejectionReasonRequired = "SMP-020";
    public const string RejectionReasonUnknown = "SMP-021";
    public const string NoteTooLong = "SMP-022";

    public const string SampleNotFound = "SMP-404";
    public const string VisitSamplesNotFound = "SMP-405";
}
