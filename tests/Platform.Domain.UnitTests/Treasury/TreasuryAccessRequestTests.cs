using Platform.Domain.Treasury;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Treasury;

/// <summary>Acceptance scenarios of intake LIS-INT-TRS-001 §16: one test per transition row, per refused precondition, per SoD rule and per invariant (03 §10).</summary>
public sealed class TreasuryAccessRequestTests
{
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid Approver = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static Domain.Treasury.Treasury ActiveTreasury() => Domain.Treasury.Treasury.Create("main", new LocalizedText("Main drawer", "الخزينة الرئيسية"), Guid.CreateVersion7(), Now);

    private static TreasuryAccessRequest Pending() => TreasuryAccessRequest.Request(ActiveTreasury(), "TRQ-2026-00001", Requester, " device-a ", "Reception PC 1", Now);

    [Fact]
    public void T1_Request_happy_path()
    {
        var treasury = ActiveTreasury();

        var request = TreasuryAccessRequest.Request(treasury, "TRQ-2026-00001", Requester, " device-a ", "Reception PC 1", Now);

        Assert.Equal(TreasuryAccessStatus.Pending, request.Status);
        Assert.Equal(treasury.Id, request.TreasuryId);
        Assert.Equal(treasury.BranchId, request.BranchId);
        Assert.Equal("device-a", request.DeviceId);
        Assert.Equal(Requester, request.RequestedByUserId);
        Assert.True(request.IsLive);
        Assert.Null(request.Approval);
        var raised = Assert.IsType<TreasuryAccessRequested>(Assert.Single(request.DomainEvents));
        Assert.Equal((request.Id, "TRQ-2026-00001", "device-a"), (raised.RequestId, raised.Number, raised.DeviceId));
    }

    [Fact]
    public void T1_Request_refused_on_an_inactive_treasury_TRS_010()
    {
        var treasury = ActiveTreasury();
        treasury.Deactivate(Now);

        var refusal = Assert.Throws<DomainException>(() => TreasuryAccessRequest.Request(treasury, "TRQ-2026-00001", Requester, "device-a", null, Now));

        Assert.Equal(TreasuryCodes.TreasuryInactive, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void T1_Request_refused_without_a_device_TRS_011(string deviceId)
    {
        var refusal = Assert.Throws<DomainException>(() => TreasuryAccessRequest.Request(ActiveTreasury(), "TRQ-2026-00001", Requester, deviceId, null, Now));

        Assert.Equal(TreasuryCodes.DeviceRequired, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void T2_Approve_happy_path_records_the_approval_and_freezes_the_request()
    {
        var request = Pending();
        request.ClearDomainEvents();

        request.Approve(Approver, Now.AddMinutes(5), " looks fine ");

        Assert.Equal(TreasuryAccessStatus.Approved, request.Status);
        Assert.Equal(new Approval(Approver, Now.AddMinutes(5), "looks fine"), request.Approval);
        Assert.Equal(Approver, request.DecidedByUserId);
        Assert.Equal(Now.AddMinutes(5), request.DecidedAtUtc);
        Assert.True(request.IsLive);
        var raised = Assert.IsType<TreasuryAccessApproved>(Assert.Single(request.DomainEvents));
        Assert.Equal(Approver, raised.ApprovedByUserId);

        Assert.Equal(TreasuryCodes.RequestNotPending, Assert.Throws<InvalidStateTransitionException>(() => request.Approve(Approver, Now)).Code);
    }

    [Fact]
    public void T2_Approve_refused_by_SOD_TRS_001_when_the_requester_approves_and_nothing_is_recorded()
    {
        var request = Pending();
        request.ClearDomainEvents();

        var refusal = Assert.Throws<DomainException>(() => request.Approve(Requester, Now));

        Assert.Equal(TreasuryCodes.SodApproverIsRequester, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
        Assert.Equal(TreasuryAccessStatus.Pending, request.Status);
        Assert.Null(request.Approval);
        Assert.Empty(request.DomainEvents);
    }

    [Fact]
    public void T3_Reject_happy_path_needs_a_reason_and_is_terminal()
    {
        var request = Pending();

        request.Reject(Approver, Now, "  unknown device ");

        Assert.Equal(TreasuryAccessStatus.Rejected, request.Status);
        Assert.Equal("unknown device", request.DecisionReason);
        Assert.False(request.IsLive);
        Assert.Contains(request.DomainEvents, e => e is TreasuryAccessRejected rejected && rejected.Reason == "unknown device");
        Assert.Equal(TreasuryCodes.RequestNotPending, Assert.Throws<InvalidStateTransitionException>(() => request.Cancel(Requester, Now)).Code);
    }

    [Fact]
    public void T3_Reject_refused_without_a_reason_TRS_020_and_by_SoD()
    {
        var request = Pending();

        Assert.Equal(TreasuryCodes.RejectReasonRequired, Assert.Throws<DomainException>(() => request.Reject(Approver, Now, " ")).Code);
        Assert.Equal(TreasuryCodes.SodApproverIsRequester, Assert.Throws<DomainException>(() => request.Reject(Requester, Now, "reason")).Code);
        Assert.Equal(TreasuryAccessStatus.Pending, request.Status);
    }

    [Fact]
    public void T4_Revoke_happy_path_keeps_the_approval_on_record()
    {
        var request = Pending();
        request.Approve(Approver, Now);
        var revoker = Guid.CreateVersion7();

        request.Revoke(revoker, Now.AddDays(1), "device lost");

        Assert.Equal(TreasuryAccessStatus.Revoked, request.Status);
        Assert.NotNull(request.Approval);
        Assert.Equal(revoker, request.DecidedByUserId);
        Assert.Equal("device lost", request.DecisionReason);
        Assert.False(request.IsLive);
        Assert.Contains(request.DomainEvents, e => e is TreasuryAccessRevoked);
    }

    [Fact]
    public void T4_Revoke_refused_when_not_approved_TRS_014_or_without_a_reason_TRS_021()
    {
        var pending = Pending();
        Assert.Equal(TreasuryCodes.RequestNotApproved, Assert.Throws<DomainException>(() => pending.Revoke(Approver, Now, "reason")).Code);

        var approved = Pending();
        approved.Approve(Approver, Now);
        Assert.Equal(TreasuryCodes.RevokeReasonRequired, Assert.Throws<DomainException>(() => approved.Revoke(Approver, Now, "")).Code);
        Assert.Equal(TreasuryAccessStatus.Approved, approved.Status);
    }

    [Fact]
    public void T5_Cancel_by_the_requester_is_terminal_and_the_reason_is_optional()
    {
        var request = Pending();

        request.Cancel(Requester, Now);

        Assert.Equal(TreasuryAccessStatus.Cancelled, request.Status);
        Assert.Null(request.DecisionReason);
        Assert.False(request.IsLive);
        Assert.Contains(request.DomainEvents, e => e is TreasuryAccessCancelled);
    }

    [Fact]
    public void T5_Cancel_refused_for_anyone_but_the_requester_TRS_030()
    {
        var request = Pending();

        var refusal = Assert.Throws<DomainException>(() => request.Cancel(Approver, Now, "not mine"));

        Assert.Equal(TreasuryCodes.NotRequester, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
        Assert.Equal(TreasuryAccessStatus.Pending, request.Status);
    }

    [Fact]
    public void T1_Request_is_limited_to_the_branches_of_the_requester_TRS_015()
    {
        var treasury = ActiveTreasury();

        TreasuryAccessRequest.EnsureRequesterWorksIn(treasury, [treasury.BranchId, Guid.CreateVersion7()]);
        TreasuryAccessRequest.EnsureRequesterWorksIn(treasury, []);   // no assignment = every branch of the laboratory
        var refusal = Assert.Throws<DomainException>(() => TreasuryAccessRequest.EnsureRequesterWorksIn(treasury, [Guid.CreateVersion7()]));

        Assert.Equal((TreasuryCodes.TreasuryOutsideUserBranches, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }
}

public sealed class TreasuryTests
{
    [Fact]
    public void T6_Create_normalises_the_code_and_requires_a_branch()
    {
        var treasury = Domain.Treasury.Treasury.Create(" main-1 ", new LocalizedText("Main", "رئيسية"), Guid.CreateVersion7(), FixedClock.Instant);

        Assert.Equal("MAIN-1", treasury.Code);
        Assert.True(treasury.IsActive);
        Assert.IsType<TreasuryCreated>(Assert.Single(treasury.DomainEvents));
        Assert.Equal(TreasuryCodes.BranchRequired, Assert.Throws<DomainException>(() => Domain.Treasury.Treasury.Create("x", new LocalizedText("a", "b"), Guid.Empty, FixedClock.Instant)).Code);
        Assert.Equal(TreasuryCodes.CodeInvalid, Assert.Throws<DomainException>(() => Domain.Treasury.Treasury.Create("bad code!", new LocalizedText("a", "b"), Guid.CreateVersion7(), FixedClock.Instant)).Code);
    }

    [Fact]
    public void T8_T9_Deactivate_and_reactivate_are_idempotent_with_one_event_each()
    {
        var treasury = Domain.Treasury.Treasury.Create("main", new LocalizedText("Main", "رئيسية"), Guid.CreateVersion7(), FixedClock.Instant);
        treasury.ClearDomainEvents();

        treasury.Deactivate(FixedClock.Instant);
        treasury.Deactivate(FixedClock.Instant);
        treasury.Reactivate(FixedClock.Instant);

        Assert.True(treasury.IsActive);
        Assert.Equal(2, treasury.DomainEvents.Count);
    }

    [Fact]
    public void T7_Rename_raises_TreasuryUpdated_only_when_the_name_changes()
    {
        var treasury = Domain.Treasury.Treasury.Create("main", new LocalizedText("Main drawer", "الخزينة الرئيسية"), Guid.CreateVersion7(), FixedClock.Instant);
        treasury.ClearDomainEvents();

        treasury.Rename(new LocalizedText("Main drawer", "الخزينة الرئيسية"), FixedClock.Instant);
        Assert.Empty(treasury.DomainEvents);

        treasury.Rename(new LocalizedText("Main cash desk", "الخزينة الرئيسية"), FixedClock.Instant);

        Assert.Equal("Main cash desk", treasury.Name.En);
        var raised = Assert.IsType<TreasuryUpdated>(Assert.Single(treasury.DomainEvents));   // validation v0.5.0 O-5
        Assert.Equal((treasury.Id, "MAIN", FixedClock.Instant), (raised.TreasuryId, raised.Code, raised.OccurredAtUtc));
    }
}
