using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Treasury;

/// <summary>
/// A cashier's request to take cash on a treasury from one device (SRS CP-29, BR-27). The transition table of
/// intake LIS-INT-TRS-001 §7 lives here, one method per row: Request (T1), Approve (T2, approval gate with
/// SOD-TRS-001), Reject (T3, SoD, reason), Revoke (T4, reason), Cancel (T5, requester only). Approved, Rejected,
/// Revoked and Cancelled are frozen: nothing but the next transition touches the row.
/// </summary>
public sealed class TreasuryAccessRequest : AggregateRoot, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxDeviceIdLength = 128;
    public const int MaxDeviceLabelLength = 100;
    public const int MaxReasonLength = 1000;
    public const string NumberPrefix = "TRQ";
    public const string NumberKind = "treasury-access-request";

    private TreasuryAccessRequest()
    {
        Number = string.Empty;
        DeviceId = string.Empty;
    }

    private TreasuryAccessRequest(Guid id, string number, Treasury treasury, Guid requestedByUserId, string deviceId, string? deviceLabel, DateTimeOffset nowUtc)
        : base(id)
    {
        Number = number;
        TreasuryId = treasury.Id;
        BranchId = treasury.BranchId;
        RequestedByUserId = requestedByUserId;
        DeviceId = deviceId;
        DeviceLabel = deviceLabel;
        RequestedAtUtc = nowUtc;
        Status = TreasuryAccessStatus.Pending;
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>TRQ-&lt;year&gt;-&lt;sequence&gt;</c>, from the per-tenant counter (pitfall P-12), unique per tenant.</summary>
    public string Number { get; private set; }

    public Guid TreasuryId { get; private set; }

    public Guid BranchId { get; private set; }

    /// <summary>The SoD anchor for SOD-TRS-001 and the owner for T5.</summary>
    public Guid RequestedByUserId { get; private set; }

    public string DeviceId { get; private set; }

    public string? DeviceLabel { get; private set; }

    public TreasuryAccessStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    /// <summary>Recorded by T2 only (intake §10).</summary>
    public Approval? Approval { get; private set; }

    /// <summary>Who rejected, revoked or cancelled, and when.</summary>
    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Required on reject and revoke; optional on cancel.</summary>
    public string? DecisionReason { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsLive => Status is TreasuryAccessStatus.Pending or TreasuryAccessStatus.Approved;

    /// <summary>T1 Request (<c>treasury.create</c>): the treasury must be active (TRS-010); the handler checks INV-TRS-002 (one live request per user, treasury and device) and the database enforces it.</summary>
    public static TreasuryAccessRequest Request(Treasury treasury, string number, Guid requestedByUserId, string deviceId, string? deviceLabel, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(treasury);
        Require(treasury.IsActive, TreasuryCodes.TreasuryInactive, "Access cannot be requested on an inactive treasury.", DomainFault.Conflict);
        Require(requestedByUserId != Guid.Empty, TreasuryCodes.NotRequester, "A request needs a requester.", DomainFault.Validation);
        Require(!string.IsNullOrWhiteSpace(number) && number.Length <= MaxNumberLength, TreasuryCodes.StatusInvalid, "A request number is required.", DomainFault.Validation);

        var request = new TreasuryAccessRequest(Guid.CreateVersion7(), number.Trim(), treasury, requestedByUserId, CleanDeviceId(deviceId), CleanLabel(deviceLabel), nowUtc);
        request.Raise(new TreasuryAccessRequested(request.Id, request.Number, request.TreasuryId, requestedByUserId, request.DeviceId, nowUtc));
        return request;
    }

    /// <summary>
    /// T1 guard of intake Q3 (validation v0.5.0 O-4; an assumption awaiting the business owner): a user asks for access only
    /// to a treasury of a branch they may work in. <paramref name="userBranchIds"/> is the membership's assignment as stored:
    /// empty means every branch of the laboratory. Refused with TRS-015 (422; the client shows it under the treasury field).
    /// </summary>
    public static void EnsureRequesterWorksIn(Treasury treasury, IReadOnlyCollection<Guid> userBranchIds)
    {
        ArgumentNullException.ThrowIfNull(treasury);
        ArgumentNullException.ThrowIfNull(userBranchIds);
        Require(
            userBranchIds.Count == 0 || userBranchIds.Contains(treasury.BranchId),
            TreasuryCodes.TreasuryOutsideUserBranches,
            "This treasury belongs to a branch you are not assigned to.",
            DomainFault.Validation);
    }

    /// <summary>T2 Approve (<c>treasury.approve</c>, approval gate): Pending only (TRS-013); SOD-TRS-001; records the approval; the request is frozen afterwards.</summary>
    public void Approve(Guid actorUserId, DateTimeOffset nowUtc, string? comment = null)
    {
        RequirePending("approve");
        EnsureActorIsNot(RequestedByUserId, actorUserId, TreasuryCodes.SodApproverIsRequester, "The requester cannot approve their own request.");

        Approval = new Approval(actorUserId, nowUtc, comment);
        DecidedByUserId = actorUserId;
        DecidedAtUtc = nowUtc;
        Status = TreasuryAccessStatus.Approved;
        Raise(new TreasuryAccessApproved(Id, Number, TreasuryId, RequestedByUserId, actorUserId, DeviceId, nowUtc));
    }

    /// <summary>T3 Reject (<c>treasury.approve</c>): Pending only; SOD-TRS-001; reason required (TRS-020). Terminal.</summary>
    public void Reject(Guid actorUserId, DateTimeOffset nowUtc, string reason)
    {
        RequirePending("reject");
        EnsureActorIsNot(RequestedByUserId, actorUserId, TreasuryCodes.SodApproverIsRequester, "The requester cannot reject their own request.");
        var cleaned = RequireReason(reason, TreasuryCodes.RejectReasonRequired, "A rejection needs a reason.");

        DecidedByUserId = actorUserId;
        DecidedAtUtc = nowUtc;
        DecisionReason = cleaned;
        Status = TreasuryAccessStatus.Rejected;
        Raise(new TreasuryAccessRejected(Id, Number, TreasuryId, RequestedByUserId, actorUserId, cleaned, nowUtc));
    }

    /// <summary>T4 Revoke (<c>treasury.approve</c>): Approved only (TRS-014); reason required (TRS-021). Terminal; the approval stays on record.</summary>
    public void Revoke(Guid actorUserId, DateTimeOffset nowUtc, string reason)
    {
        Require(Status == TreasuryAccessStatus.Approved, TreasuryCodes.RequestNotApproved, $"Only an approved request can be revoked (status: {Status}).", DomainFault.Conflict);
        var cleaned = RequireReason(reason, TreasuryCodes.RevokeReasonRequired, "A revocation needs a reason.");

        DecidedByUserId = actorUserId;
        DecidedAtUtc = nowUtc;
        DecisionReason = cleaned;
        Status = TreasuryAccessStatus.Revoked;
        Raise(new TreasuryAccessRevoked(Id, Number, TreasuryId, RequestedByUserId, actorUserId, DeviceId, cleaned, nowUtc));
    }

    /// <summary>T5 Cancel (<c>treasury.cancel</c>): Pending only; the requester withdraws their own request (TRS-030); reason optional. Terminal.</summary>
    public void Cancel(Guid actorUserId, DateTimeOffset nowUtc, string? reason = null)
    {
        RequirePending("cancel");
        Require(actorUserId == RequestedByUserId, TreasuryCodes.NotRequester, "Only the requester can cancel a request.", DomainFault.Refused);

        DecidedByUserId = actorUserId;
        DecidedAtUtc = nowUtc;
        DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = TreasuryAccessStatus.Cancelled;
        Raise(new TreasuryAccessCancelled(Id, Number, TreasuryId, RequestedByUserId, nowUtc));
    }

    private void RequirePending(string action)
    {
        if (Status != TreasuryAccessStatus.Pending)
        {
            throw new InvalidStateTransitionException(TreasuryCodes.RequestNotPending, "treasury access request", Status.ToString(), action);
        }
    }

    private static string RequireReason(string reason, string code, string message)
    {
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length is >= 1 and <= MaxReasonLength, code, message, DomainFault.Validation);
        return cleaned;
    }

    private static string CleanDeviceId(string deviceId)
    {
        var value = (deviceId ?? string.Empty).Trim();
        Require(value.Length is >= 1 and <= MaxDeviceIdLength, TreasuryCodes.DeviceRequired, $"A device identifier of 1 to {MaxDeviceIdLength} characters is required.", DomainFault.Validation);
        return value;
    }

    private static string? CleanLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var value = label.Trim();
        return value.Length <= MaxDeviceLabelLength ? value : value[..MaxDeviceLabelLength];
    }
}
