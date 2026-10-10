using Platform.Domain.Catalogue;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Results;

/// <summary>What the order policy knows when it creates a result (T1): the visit line, the test, the tube and the report unit.</summary>
public sealed record ResultOrder(
    Guid VisitId,
    string Accession,
    Guid LineId,
    Guid LineServiceId,
    Guid ServiceId,
    Guid TestId,
    LabTestType TestType,
    bool IsHiddenHelper,
    Guid? TubeId,
    Guid ClinicalGroupId,
    string ReportUnitKey,
    Guid PatientId,
    Guid BranchId,
    Guid? PayerId,
    bool Urgent,
    int ServiceSequence,
    int MemberOrder,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset ExpectedAtUtc,
    Guid? OrderedByUserId);

/// <summary>
/// The medical track of one test of one visit (SRS CP-18, CP-19, WF-06; LDM §2.3, §5). The transition table of
/// intake LIS-INT-RES-001 §7 lives here, one method per row: Order (T1), Enter (T2), ApplyComputed (T2b),
///, Review (T3), Verify (T4, the signature), Undo (T5), Amend (T6), AcknowledgeDelta (T7),
/// MarkPrinted (T8), Cancel (T9), comment and note (T10, T11), MoveToPatient (T12), Retime (T13), FollowPayer (T14).
/// Every value change and every status change appends one <see cref="ResultLogEntry"/> (INV-RES-010). Rights are
/// evaluated by the handler for the result's group and branch and decided here (<see cref="ResultActor"/>).
/// </summary>
public sealed class Result : AggregateRoot, ITenantScoped
{
    public const int MaxAccessionLength = 40;
    public const int MaxReportUnitKeyLength = 100;
    public const int MaxTextLength = 500;
    public const int MaxUnitLength = 50;
    public const int MaxCommentLength = 2000;
    public const int MaxNoteLength = 2000;
    public const int MaxReasonLength = 1000;
    public const int MaxAnalyserCodeLength = 32;
    public const int MaxAnalyserFlagsLength = 200;
    public const int MaxNameLength = 200;
    public const int MaxPrintListNumberLength = 20;

    private const string RecomputedReason = "Recomputed after an input changed.";

    private readonly List<ResultLogEntry> _log = [];

    private Result()
    {
        Accession = string.Empty;
        ReportUnitKey = string.Empty;
    }

    private Result(Guid id, ResultOrder order)
        : base(id)
    {
        VisitId = order.VisitId;
        Accession = order.Accession;
        LineId = order.LineId;
        LineServiceId = order.LineServiceId;
        ServiceId = order.ServiceId;
        TestId = order.TestId;
        TestType = order.TestType;
        IsHiddenHelper = order.IsHiddenHelper;
        TubeId = order.TubeId is { } tube && tube != Guid.Empty ? tube : null;
        ClinicalGroupId = order.ClinicalGroupId;
        ReportUnitKey = order.ReportUnitKey;
        PatientId = order.PatientId;
        BranchId = order.BranchId;
        PayerId = order.PayerId is { } payer && payer != Guid.Empty ? payer : null;
        Urgent = order.Urgent;
        ServiceSequence = order.ServiceSequence;
        MemberOrder = order.MemberOrder;
        RegisteredAtUtc = order.RegisteredAtUtc;
        ClockStartUtc = order.RegisteredAtUtc;
        ExpectedAtUtc = order.ExpectedAtUtc < order.RegisteredAtUtc ? order.RegisteredAtUtc : order.ExpectedAtUtc;
        Status = ResultStatus.Ordered;
        StatusChangedAtUtc = order.RegisteredAtUtc;
        Source = ResultSource.Manual;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    public string Accession { get; private set; }

    public Guid LineId { get; private set; }

    /// <summary>The service ordered on the line (a package, a profile or a single test).</summary>
    public Guid LineServiceId { get; private set; }

    /// <summary>The member service that owns the test (the profile banner of the report).</summary>
    public Guid ServiceId { get; private set; }

    public Guid TestId { get; private set; }

    /// <summary>The test type when the result was ordered (the catalogue may change later; entry follows the current definition).</summary>
    public LabTestType TestType { get; private set; }

    /// <summary>BR-20: never printed.</summary>
    public bool IsHiddenHelper { get; private set; }

    public Guid? TubeId { get; private set; }

    public Guid ClinicalGroupId { get; private set; }

    /// <summary>The printable record (LDM §2.6): visit × clinical group, or the line's print-alone service.</summary>
    public string ReportUnitKey { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>The branch of the visit; result rights are evaluated for it.</summary>
    public Guid BranchId { get; private set; }

    public Guid? PayerId { get; private set; }

    public bool Urgent { get; private set; }

    public int ServiceSequence { get; private set; }

    public int MemberOrder { get; private set; }

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    /// <summary>BR-13: the turnaround clock starts at the collection (registration until then).</summary>
    public DateTimeOffset ClockStartUtc { get; private set; }

    /// <summary>BR-13: start + the test's duration counting only test days.</summary>
    public DateTimeOffset ExpectedAtUtc { get; private set; }

    public ResultStatus Status { get; private set; }

    public DateTimeOffset StatusChangedAtUtc { get; private set; }

    public decimal? NumericValue { get; private set; }

    /// <summary>The value as displayed and printed (numbers with decimals and reportable markers, list values, text, culture summary).</summary>
    public string? TextValue { get; private set; }

    public FormattedReport? Formatted { get; private set; }

    public CultureReport? Culture { get; private set; }

    public string? Unit { get; private set; }

    public int Decimals { get; private set; }

    public ResolvedRange? Range { get; private set; }

    public ResultFlag? Flag { get; private set; }

    public bool IsPanic { get; private set; }

    public bool DeltaFlag { get; private set; }

    public decimal? DeltaPercent { get; private set; }

    public Guid? PreviousResultId { get; private set; }

    public decimal? PreviousValue { get; private set; }

    public DateTimeOffset? PreviousVerifiedAtUtc { get; private set; }

    public Guid? DeltaAcknowledgedByUserId { get; private set; }

    public DateTimeOffset? DeltaAcknowledgedAtUtc { get; private set; }

    public string? DeltaComment { get; private set; }

    public ResultSource Source { get; private set; }

    public Guid? AnalyserId { get; private set; }

    public string? AnalyserCode { get; private set; }

    public string? AnalyserFlags { get; private set; }

    public DateTimeOffset? MeasuredAtUtc { get; private set; }

    public Guid? EnteredByUserId { get; private set; }

    public DateTimeOffset? EnteredAtUtc { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public Guid? VerifiedByUserId { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    /// <summary>The verifier's name as of the action: the electronic signature printed on the report (BR-09).</summary>
    public string? VerifiedByName { get; private set; }

    public DateTimeOffset? FirstPrintedAtUtc { get; private set; }

    public DateTimeOffset? LastPrintedAtUtc { get; private set; }

    public Guid? LastPrintedByUserId { get; private set; }

    public int PrintCount { get; private set; }

    public string? LastPrintListNumber { get; private set; }

    public int AmendmentCount { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Prints under the test (LDM §5.4 "Comment").</summary>
    public string? ReportComment { get; private set; }

    /// <summary>For colleagues; never printed (LDM §5.4 "Comments").</summary>
    public string? InternalNote { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The history, oldest first as appended.</summary>
    public IReadOnlyCollection<ResultLogEntry> Log => _log.AsReadOnly();

    public bool HasValue => NumericValue is not null || TextValue is not null || Formatted is not null || Culture is not null;

    public bool IsPrinted => PrintCount > 0;

    public bool IsLive => Status != ResultStatus.Cancelled;

    /// <summary>T1 Order (policy on <c>VisitRegistered</c> / <c>VisitLinesAdded</c>): an Ordered result of one test of an Ordered line.</summary>
    public static Result Order(ResultOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        Require(
            order.VisitId != Guid.Empty && order.LineId != Guid.Empty && order.LineServiceId != Guid.Empty && order.ServiceId != Guid.Empty
            && order.TestId != Guid.Empty && order.ClinicalGroupId != Guid.Empty && order.PatientId != Guid.Empty && order.BranchId != Guid.Empty,
            ResultsCodes.ReferenceRequired,
            "A result needs its visit, line, service, test, clinical group, patient and branch.",
            DomainFault.Validation);
        var accession = (order.Accession ?? string.Empty).Trim();
        var unit = (order.ReportUnitKey ?? string.Empty).Trim();
        Require(accession.Length is >= 1 and <= MaxAccessionLength && unit.Length is >= 1 and <= MaxReportUnitKeyLength, ResultsCodes.ReferenceRequired, "A result needs its accession and report unit.", DomainFault.Validation);

        var result = new Result(Guid.CreateVersion7(), order with { Accession = accession, ReportUnitKey = unit });
        result.Append(ResultLogKind.Ordered, order.RegisteredAtUtc, order.OrderedByUserId, null, toStatus: ResultStatus.Ordered);
        return result;
    }

    /// <summary>
    /// T2 Enter (<c>results.create</c>): Ordered, Complete or Reviewed (RES-011 for a signed result: amend it); the edit
    /// right (RES-050); the tube Received (RES-010, BR-03); the value by type (RES-002…009). A Reviewed result returns to
    /// Complete (the review was of the old value). Returns false when the value is unchanged.
    /// </summary>
    public bool Enter(ResultValueInput input, ResultTestRules rules, ResolvedRange? range, PreviousResultValue? previous, ResultActor actor, bool sampleReceived, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("enter");
        if (ResultStatuses.IsSigned(Status))
        {
            throw new InvalidStateTransitionException(ResultsCodes.NotEditable, "result", Status.ToString(), "enter (amend the verified result instead)");
        }

        Require(actor.Rights.CanEdit, ResultsCodes.NoEditRight, "You have no right to enter results of this clinical group in this branch.");
        Require(sampleReceived, ResultsCodes.SampleNotReceived, "The sample is not received yet: results cannot be entered (BR-03).", DomainFault.Conflict);
        var value = ResultValueRules.Normalize(input, rules, range);
        return ApplyValue(value, rules, range, previous, ResultSource.Manual, actor.UserId, actor.DisplayName, nowUtc, null, isAmendment: false);
    }

    /// <summary>
    /// T2b Compute a formula from its inputs (the entry handlers, after an input changed): no
    /// value yet keeps the result as it is; a changed value on a signed formula reopens it to Complete (the signature is
    /// removed and logged, intake §16 Q10). Returns false when nothing changed.
    /// </summary>
    public bool ApplyComputed(decimal? computed, ResultTestRules rules, ResolvedRange? range, PreviousResultValue? previous, ResultSource source, Guid? actorUserId, string? actorName, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (Status == ResultStatus.Cancelled || computed is null || TestType != LabTestType.Formula)
        {
            return false;
        }

        var value = ResultValueRules.Normalize(ResultValueInput.Number(computed.Value), rules, range, computed: true);
        if (HasValue && value.Numeric == NumericValue && string.Equals(value.Text, TextValue, StringComparison.Ordinal))
        {
            return false;
        }

        var reason = ResultStatuses.IsSigned(Status) ? RecomputedReason : null;
        return ApplyValue(value, rules, range, previous, source, actorUserId, actorName, nowUtc, reason, isAmendment: false, force: true);
    }

    /// <summary>T3 Review (<c>results.approve</c>): the review right (RES-051); Complete only (RES-012).</summary>
    public void Review(ResultActor actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("review");
        Require(actor.Rights.CanReview, ResultsCodes.NoReviewRight, "You have no right to review results of this clinical group in this branch.");
        if (Status != ResultStatus.Complete)
        {
            throw new InvalidStateTransitionException(ResultsCodes.NotReviewable, "result", Status.ToString(), "review");
        }

        ReviewedByUserId = actor.UserId;
        ReviewedAtUtc = nowUtc;
        MoveTo(ResultStatus.Reviewed, nowUtc, actor.UserId, actor.DisplayName);
        Raise(new ResultReviewed(Id, VisitId, Accession, TestId, ClinicalGroupId, ReportUnitKey, actor.UserId, nowUtc));
    }

    /// <summary>
    /// T4 Verify (<c>results.approve</c>, the signature gate of intake §10): the verify right (RES-052); a value (RES-014);
    /// Complete or Reviewed (RES-013); an acknowledged delta flag (RES-015, BR-11); Reviewed first when the tenant
    /// requires it (RES-016). Records the verifier, the time and the verifier's name as of the action (BR-09).
    /// </summary>
    public void Verify(ResultActor actor, bool reviewBeforeVerify, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("verify");
        Require(actor.Rights.CanVerify, ResultsCodes.NoVerifyRight, "You have no right to verify results of this clinical group in this branch.");
        Require(Status != ResultStatus.Ordered && HasValue, ResultsCodes.NoValue, "A result without a value cannot be verified.", DomainFault.Conflict);
        if (Status is not (ResultStatus.Complete or ResultStatus.Reviewed))
        {
            throw new InvalidStateTransitionException(ResultsCodes.NotVerifiable, "result", Status.ToString(), "verify");
        }

        Require(!DeltaFlag || DeltaAcknowledgedAtUtc is not null, ResultsCodes.DeltaNotAcknowledged, "Acknowledge the delta check before verifying (BR-11).", DomainFault.Conflict);
        Require(!reviewBeforeVerify || Status == ResultStatus.Reviewed, ResultsCodes.ReviewRequired, "The laboratory requires the result to be reviewed before it is verified.", DomainFault.Conflict);

        VerifiedByUserId = actor.UserId;
        VerifiedAtUtc = nowUtc;
        VerifiedByName = Clip(string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.UserId.ToString("D") : actor.DisplayName, MaxNameLength);
        MoveTo(ResultStatus.Verified, nowUtc, actor.UserId, actor.DisplayName);
        Raise(new ResultVerified(Id, VisitId, Accession, TestId, ClinicalGroupId, ReportUnitKey, actor.UserId, nowUtc));
    }

    /// <summary>
    /// T5 Undo (<c>results.cancel</c>): Verified → Reviewed (when it was reviewed) or Complete, with the verify right;
    /// Reviewed → Complete, with the review right (RES-017 otherwise; RES-018 for a printed result: amend it); reason
    /// required (RES-019). The removed review or signature stays in the log.
    /// </summary>
    public void Undo(ResultActor actor, string reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("undo");
        if (Status == ResultStatus.Printed)
        {
            throw new DomainException(ResultsCodes.PrintedNotUndoable, "A printed result is amended, not undone.", DomainFault.Conflict);
        }

        if (Status is not (ResultStatus.Reviewed or ResultStatus.Verified))
        {
            throw new InvalidStateTransitionException(ResultsCodes.NothingToUndo, "result", Status.ToString(), "undo");
        }

        var from = Status;
        ResultStatus to;
        if (from == ResultStatus.Verified)
        {
            Require(actor.Rights.CanVerify, ResultsCodes.NoVerifyRight, "You have no right to undo verifications of this clinical group in this branch.");
            to = ReviewedAtUtc is null ? ResultStatus.Complete : ResultStatus.Reviewed;
        }
        else
        {
            Require(actor.Rights.CanReview, ResultsCodes.NoReviewRight, "You have no right to undo reviews of this clinical group in this branch.");
            to = ResultStatus.Complete;
        }

        var cleaned = RequireReason(reason, "An undo needs a reason.");
        if (from == ResultStatus.Verified)
        {
            ClearSignature();
        }
        else
        {
            ClearReview();
        }

        MoveTo(to, nowUtc, actor.UserId, actor.DisplayName, cleaned);
        Raise(new ResultUndone(Id, VisitId, Accession, from.ToString(), to.ToString(), actor.UserId, cleaned, nowUtc));
    }

    /// <summary>
    /// T6 Amend (<c>results.edit</c>): a Verified or Printed result (RES-023) takes a new value with the edit right
    /// (RES-050) and the special right "modify verified result" (RES-055), a reason (RES-019) and a Received tube
    /// (RES-010); it returns to Complete and needs a new verification; the print count stays.
    /// </summary>
    public void Amend(ResultValueInput input, ResultTestRules rules, ResolvedRange? range, PreviousResultValue? previous, ResultActor actor, bool sampleReceived, string reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("amend");
        if (!ResultStatuses.IsSigned(Status))
        {
            throw new InvalidStateTransitionException(ResultsCodes.NotAmendable, "result", Status.ToString(), "amend (only verified results are amended)");
        }

        Require(actor.Rights.CanEdit, ResultsCodes.NoEditRight, "You have no right to enter results of this clinical group in this branch.");
        Require(actor.Rights.CanModifyVerified, ResultsCodes.NoModifyVerifiedRight, "Modifying a verified result needs the special right \"modify verified result\".");
        var cleaned = RequireReason(reason, "An amendment needs a reason.");
        Require(sampleReceived, ResultsCodes.SampleNotReceived, "The sample is not received: results cannot be entered (BR-03).", DomainFault.Conflict);
        var value = ResultValueRules.Normalize(input, rules, range);

        AmendmentCount++;
        ApplyValue(value, rules, range, previous, ResultSource.Manual, actor.UserId, actor.DisplayName, nowUtc, cleaned, isAmendment: true, force: true);
        Raise(new ResultAmended(Id, VisitId, Accession, TestId, actor.UserId, cleaned, nowUtc));
    }

    /// <summary>T7 Acknowledge the delta flag (<c>results.approve</c>): the review or verify right (RES-051); a flag not yet acknowledged (RES-020).</summary>
    public void AcknowledgeDelta(ResultActor actor, string? comment, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("acknowledge the delta check of");
        Require(actor.Rights.CanReview || actor.Rights.CanVerify, ResultsCodes.NoReviewRight, "You have no right to review results of this clinical group in this branch.");
        Require(DeltaFlag && DeltaAcknowledgedAtUtc is null, ResultsCodes.NoDeltaToAcknowledge, "The result has no delta flag to acknowledge.", DomainFault.Conflict);
        var cleaned = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Require(cleaned is null || cleaned.Length <= MaxReasonLength, ResultsCodes.CommentTooLong, $"The comment holds at most {MaxReasonLength} characters.", DomainFault.Validation);

        DeltaAcknowledgedByUserId = actor.UserId;
        DeltaAcknowledgedAtUtc = nowUtc;
        DeltaComment = cleaned;
        Append(ResultLogKind.DeltaAcknowledged, nowUtc, actor.UserId, actor.DisplayName, detail: cleaned);
        Raise(new ResultDeltaAcknowledged(Id, VisitId, Accession, TestId, actor.UserId, cleaned, nowUtc));
    }

    /// <summary>
    /// T8 Print (<c>results.export</c>): the print right (RES-053); a value (RES-014); Verified or Printed (RES-021, BR-09)
    /// unless the tenant prints unverified results — then only the printed flag is set and the status stays.
    /// </summary>
    public void MarkPrinted(ResultActor actor, bool printUnverified, string? printListNumber, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("print");
        EnsurePrintable(actor, printUnverified);

        var number = Clip(printListNumber, MaxPrintListNumberLength);
        PrintCount++;
        FirstPrintedAtUtc ??= nowUtc;
        LastPrintedAtUtc = nowUtc;
        LastPrintedByUserId = actor.UserId;
        LastPrintListNumber = number ?? LastPrintListNumber;
        var detail = number is null ? $"Printed (copy {PrintCount})." : $"Printed on list {number} (copy {PrintCount}).";
        if (Status == ResultStatus.Verified)
        {
            MoveTo(ResultStatus.Printed, nowUtc, actor.UserId, actor.DisplayName, detail);
        }
        else
        {
            Append(ResultLogKind.Status, nowUtc, actor.UserId, actor.DisplayName, fromStatus: Status, toStatus: Status, detail: detail);
        }

        Raise(new ResultPrinted(Id, VisitId, Accession, ReportUnitKey, PrintCount, number, actor.UserId, nowUtc));
    }

    /// <summary>The preconditions of T8, callable before a unit is printed so that nothing is marked when one result refuses.</summary>
    public void EnsurePrintable(ResultActor actor, bool printUnverified)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("print");
        Require(actor.Rights.CanPrint, ResultsCodes.NoPrintRight, "You have no right to print results of this clinical group in this branch.");
        Require(HasValue, ResultsCodes.NoValue, "A result without a value cannot be printed.", DomainFault.Conflict);
        Require(ResultStatuses.IsSigned(Status) || printUnverified, ResultsCodes.NotPrintable, "Only verified results print (BR-09).", DomainFault.Conflict);
    }

    /// <summary>T9 Cancel (policy on <c>VisitLineCancelled</c>): idempotent; returns false when already cancelled.</summary>
    public bool Cancel(Guid actorUserId, string? reason, DateTimeOffset nowUtc)
    {
        if (Status == ResultStatus.Cancelled)
        {
            return false;
        }

        var cleaned = Clip(string.IsNullOrWhiteSpace(reason) ? "The service was cancelled." : reason, MaxReasonLength)!;
        CancelledAtUtc = nowUtc;
        CancellationReason = cleaned;
        MoveTo(ResultStatus.Cancelled, nowUtc, actorUserId, null, cleaned);
        Raise(new ResultCancelled(Id, VisitId, Accession, LineId, TestId, actorUserId, cleaned, nowUtc));
        return true;
    }

    /// <summary>T10 Report comment (<c>results.create</c>): the edit right (RES-050); not on a signed result (RES-024); ≤ 2 000 (RES-025).</summary>
    public bool SetReportComment(string? comment, ResultActor actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("comment");
        Require(actor.Rights.CanEdit, ResultsCodes.NoEditRight, "You have no right to enter results of this clinical group in this branch.");
        Require(!ResultStatuses.IsSigned(Status), ResultsCodes.CommentLocked, "The result is verified: undo the verification or amend it to change the report comment.", DomainFault.Conflict);
        var cleaned = CleanText(comment, MaxCommentLength);
        if (string.Equals(cleaned, ReportComment, StringComparison.Ordinal))
        {
            return false;
        }

        Append(ResultLogKind.Comment, nowUtc, actor.UserId, actor.DisplayName, valueBefore: ReportComment, valueAfter: cleaned);
        ReportComment = cleaned;
        return true;
    }

    /// <summary>T11 Internal note (<c>results.create</c>): any live result; ≤ 2 000 (RES-025); never printed.</summary>
    public bool SetInternalNote(string? note, ResultActor actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireLive("annotate");
        Require(actor.Rights.CanSee, ResultsCodes.NoSeeRight, "You have no right to see results of this clinical group in this branch.");
        var cleaned = CleanText(note, MaxNoteLength);
        if (string.Equals(cleaned, InternalNote, StringComparison.Ordinal))
        {
            return false;
        }

        Append(ResultLogKind.Note, nowUtc, actor.UserId, actor.DisplayName, valueBefore: InternalNote, valueAfter: cleaned);
        InternalNote = cleaned;
        return true;
    }

    /// <summary>T12 Move to the surviving patient file (policy on <c>PatientsMerged</c>): idempotent.</summary>
    public bool MoveToPatient(Guid survivorPatientId, Guid actorUserId, string? reason, DateTimeOffset nowUtc)
    {
        if (survivorPatientId == Guid.Empty || survivorPatientId == PatientId)
        {
            return false;
        }

        Append(ResultLogKind.PatientMoved, nowUtc, actorUserId, null, valueBefore: PatientId.ToString("D"), valueAfter: survivorPatientId.ToString("D"), detail: reason);
        PatientId = survivorPatientId;
        return true;
    }

    /// <summary>T13 Restart the turnaround clock (policy on <c>SampleCollected</c>, BR-13); a signed or cancelled result keeps its clock.</summary>
    public bool Retime(DateTimeOffset clockStartUtc, DateTimeOffset expectedAtUtc)
    {
        if (Status is ResultStatus.Cancelled || ResultStatuses.IsSigned(Status) || (ClockStartUtc == clockStartUtc && ExpectedAtUtc == expectedAtUtc))
        {
            return false;
        }

        ClockStartUtc = clockStartUtc;
        ExpectedAtUtc = expectedAtUtc < clockStartUtc ? clockStartUtc : expectedAtUtc;
        return true;
    }

    /// <summary>T14 Follow the visit's payer after a transfer (policy on <c>VisitTransferred</c>).</summary>
    public bool FollowPayer(Guid? payerId)
    {
        var value = payerId is { } payer && payer != Guid.Empty ? payer : (Guid?)null;
        if (PayerId == value)
        {
            return false;
        }

        PayerId = value;
        return true;
    }

    /// <summary>The value as a log row and the documents show it.</summary>
    public string? DescribeValue() => Formatted?.Describe() ?? Culture?.Describe() ?? TextValue;

    private bool ApplyValue(
        NormalizedResultValue value,
        ResultTestRules rules,
        ResolvedRange? range,
        PreviousResultValue? previous,
        ResultSource source,
        Guid? actorUserId,
        string? actorName,
        DateTimeOffset nowUtc,
        string? reason,
        bool isAmendment,
        bool force = false)
    {
        var before = DescribeValue();
        var after = value.Describe();
        if (!force && Status == ResultStatus.Complete && HasValue && string.Equals(before, after, StringComparison.Ordinal) && Source == source)
        {
            return false;
        }

        var wasPanic = IsPanic;
        var from = Status;
        NumericValue = value.Numeric;
        TextValue = value.Text is null ? null : Clip(value.Text, MaxTextLength);
        Formatted = value.Formatted;
        Culture = value.Culture;
        Unit = Clip(rules.Unit, MaxUnitLength);
        Decimals = rules.Decimals;
        Range = range;
        Flag = value.Flag;
        IsPanic = value.IsPanic;

        var (deltaFlag, deltaPercent) = ResultValueRules.Delta(value.Numeric, previous, rules.DeltaPercent);
        DeltaFlag = deltaFlag;
        DeltaPercent = deltaPercent;
        PreviousResultId = previous?.ResultId;
        PreviousValue = previous?.Value;
        PreviousVerifiedAtUtc = previous?.VerifiedAtUtc;
        DeltaAcknowledgedByUserId = null;
        DeltaAcknowledgedAtUtc = null;
        DeltaComment = null;

        Source = source;
        if (source == ResultSource.Manual)
        {
            AnalyserId = null;
            AnalyserCode = null;
            AnalyserFlags = null;
            MeasuredAtUtc = null;
        }

        EnteredByUserId = actorUserId is { } user && user != Guid.Empty ? user : null;
        EnteredAtUtc = nowUtc;

        Append(
            ResultLogKind.Value,
            nowUtc,
            actorUserId,
            actorName,
            valueBefore: before,
            valueAfter: after,
            source: source,
            detail: isAmendment ? reason : null);

        if (from != ResultStatus.Complete)
        {
            if (ResultStatuses.IsSigned(from))
            {
                ClearSignature();
                ClearReview();
            }
            else if (from == ResultStatus.Reviewed)
            {
                ClearReview();
            }

            MoveTo(ResultStatus.Complete, nowUtc, actorUserId, actorName, reason);
        }

        Raise(new ResultEntered(Id, VisitId, Accession, TestId, ClinicalGroupId, ReportUnitKey, source.ToString(), Flag?.ToString(), IsPanic, DeltaFlag, EnteredByUserId, nowUtc));
        if (IsPanic && !wasPanic)
        {
            Raise(new ResultPanicDetected(Id, VisitId, Accession, PatientId, TestId, BranchId, TextValue ?? after, Flag?.ToString(), nowUtc));
        }

        return true;
    }

    private void ClearReview()
    {
        ReviewedByUserId = null;
        ReviewedAtUtc = null;
    }

    private void ClearSignature()
    {
        VerifiedByUserId = null;
        VerifiedAtUtc = null;
        VerifiedByName = null;
    }

    private void MoveTo(ResultStatus status, DateTimeOffset nowUtc, Guid? actorUserId, string? actorName, string? detail = null)
    {
        var from = Status;
        Status = status;
        StatusChangedAtUtc = nowUtc;
        Append(ResultLogKind.Status, nowUtc, actorUserId, actorName, fromStatus: from, toStatus: status, detail: detail);
    }

    private void RequireLive(string action)
    {
        if (Status == ResultStatus.Cancelled)
        {
            throw new InvalidStateTransitionException(ResultsCodes.Cancelled, "result", Status.ToString(), action);
        }
    }

    private static string RequireReason(string? reason, string message)
    {
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 and <= MaxReasonLength, ResultsCodes.ReasonRequired, message, DomainFault.Validation);
        return cleaned;
    }

    private static string? CleanText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        Require(trimmed.Length <= maxLength, ResultsCodes.CommentTooLong, $"The text holds at most {maxLength} characters.", DomainFault.Validation);
        return trimmed;
    }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private void Append(
        ResultLogKind kind,
        DateTimeOffset atUtc,
        Guid? byUserId,
        string? byName,
        ResultStatus? fromStatus = null,
        ResultStatus? toStatus = null,
        string? valueBefore = null,
        string? valueAfter = null,
        ResultSource? source = null,
        Guid? analyserId = null,
        string? detail = null) =>
        _log.Add(ResultLogEntry.Record(Id, _log.Count == 0 ? 1 : _log.Max(entry => entry.Sequence) + 1, kind, atUtc, byUserId, byName, fromStatus, toStatus, valueBefore, valueAfter, source, analyserId, detail));
}
