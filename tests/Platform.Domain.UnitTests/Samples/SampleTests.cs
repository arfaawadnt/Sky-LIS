using Platform.Domain.Samples;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Samples;

/// <summary>Acceptance scenarios of intake LIS-INT-SMP-001 §16: one test per transition row, per refused precondition and per invariant (03 §10).</summary>
public sealed class SampleTests
{
    private static readonly Guid Registrar = Guid.CreateVersion7();
    private static readonly Guid Phlebotomist = Guid.CreateVersion7();
    private static readonly Guid Technician = Guid.CreateVersion7();
    private static readonly Guid MainBranch = Guid.CreateVersion7();
    private static readonly Guid LabBranch = Guid.CreateVersion7();
    private static readonly Guid Hemolysis = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static Sample Ordered(string number = "SMP-2026-00001") =>
        Sample.Create(Guid.CreateVersion7(), number, Guid.CreateVersion7(), "ACC-2026-00001", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7(), Guid.CreateVersion7()], Registrar, Now);

    private static Sample Received()
    {
        var sample = Ordered();
        sample.Receive(Technician, LabBranch, Now.AddMinutes(30));
        sample.ClearDomainEvents();
        return sample;
    }

    private static Sample Rejected()
    {
        var sample = Received();
        sample.Reject(new RejectionReason(Hemolysis, "Hemolysis", "تحلل الدم"), "clotted", Technician, LabBranch, Now.AddMinutes(40));
        sample.ClearDomainEvents();
        return sample;
    }

    [Fact]
    public void S_T1_ok_Create_records_an_Ordered_sample_with_one_history_row_and_cleans_the_number()
    {
        var tube = Guid.CreateVersion7();
        var condition = Guid.CreateVersion7();
        var service = Guid.CreateVersion7();

        var sample = Sample.Create(tube, " smp-2026-00007 ", Guid.CreateVersion7(), " ACC-2026-00002 ", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.CreateVersion7(), condition, [service, service, Guid.Empty], Registrar, Now);

        Assert.Equal(SampleStatus.Ordered, sample.Status);
        Assert.Equal(("SMP-2026-00007", "ACC-2026-00002", tube, 0), (sample.SampleNumber, sample.Accession, sample.TubeId, sample.Generation));
        Assert.Equal(condition, sample.SampleConditionId);
        Assert.Equal([service], sample.ServiceIds);
        Assert.Equal(Now, sample.OrderedAtUtc);
        Assert.True(sample.IsLive);
        Assert.False(sample.IsResultEntryAllowed);
        Assert.Empty(sample.DomainEvents);
        var step = Assert.Single(sample.Events);
        Assert.Equal((SampleAction.Ordered, SampleStatus.Ordered, Registrar, MainBranch), (step.Action, step.Status, step.ByUserId, step.BranchId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SMP 2026")]
    [InlineData("SMP-2026-000000000000000000000000001")]
    public void S_T1_number_Create_refuses_a_number_that_cannot_be_a_barcode_SMP_002(string number)
    {
        var refusal = Assert.Throws<DomainException>(() => Ordered(number));

        Assert.Equal(SamplesCodes.SampleNumberInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void S_T1_refs_Create_refuses_a_missing_tube_SMP_001_or_reference_SMP_003()
    {
        var noTube = Assert.Throws<DomainException>(() => Sample.Create(Guid.Empty, "SMP-1", Guid.CreateVersion7(), "ACC-1", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [], Registrar, Now));
        var noType = Assert.Throws<DomainException>(() => Sample.Create(Guid.CreateVersion7(), "SMP-1", Guid.CreateVersion7(), "ACC-1", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.Empty, null, [], Registrar, Now));
        var noAccession = Assert.Throws<DomainException>(() => Sample.Create(Guid.CreateVersion7(), "SMP-1", Guid.CreateVersion7(), " ", Guid.CreateVersion7(), MainBranch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [], Registrar, Now));

        Assert.Equal(SamplesCodes.TubeRequired, noTube.Code);
        Assert.Equal(SamplesCodes.ReferenceRequired, noType.Code);
        Assert.Equal(SamplesCodes.ReferenceRequired, noAccession.Code);
    }

    [Fact]
    public void S_T1_auto_AutoAdvance_with_both_settings_records_collection_and_receipt_by_the_registrar()
    {
        var sample = Ordered();

        sample.AutoAdvance(autoCollect: true, autoReceive: true, Registrar, MainBranch, Now);

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal((Now, Registrar, MainBranch), (sample.CollectedAtUtc!.Value, sample.CollectedByUserId!.Value, sample.CollectedBranchId!.Value));
        Assert.Equal((Now, Registrar, MainBranch), (sample.ReceivedAtUtc!.Value, sample.ReceivedByUserId!.Value, sample.ReceivedBranchId!.Value));
        Assert.Equal([SampleAction.Ordered, SampleAction.Collected, SampleAction.Received], sample.Events.Select(step => step.Action));
        Assert.Equal([1, 2, 3], sample.Events.Select(step => step.Sequence));   // one instant, three ordered steps
        Assert.Collection(sample.DomainEvents, first => Assert.IsType<SampleCollected>(first), second => Assert.IsType<SampleReceived>(second));
        Assert.True(sample.IsResultEntryAllowed);
    }

    [Theory]
    [InlineData(true, false, SampleStatus.Collected)]
    [InlineData(false, true, SampleStatus.Received)]
    [InlineData(false, false, SampleStatus.Ordered)]
    public void S_T1_auto_AutoAdvance_follows_each_setting(bool autoCollect, bool autoReceive, SampleStatus expected)
    {
        var sample = Ordered();

        sample.AutoAdvance(autoCollect, autoReceive, Registrar, MainBranch, Now);

        Assert.Equal(expected, sample.Status);
    }

    [Fact]
    public void S_T1_auto_AutoAdvance_leaves_a_sample_past_Ordered_alone()
    {
        var sample = Received();

        sample.AutoAdvance(true, true, Registrar, MainBranch, Now.AddHours(1));

        Assert.Empty(sample.DomainEvents);
        Assert.Equal(Now.AddMinutes(30), sample.ReceivedAtUtc);
    }

    [Fact]
    public void S_T1_added_AddServices_merges_new_services_once_and_never_on_a_closed_sample()
    {
        var sample = Ordered();
        var existing = sample.ServiceIds[0];
        var added = Guid.CreateVersion7();

        Assert.True(sample.AddServices([existing, added, Guid.Empty]));
        Assert.False(sample.AddServices([added]));
        Assert.Equal(3, sample.ServiceIds.Length);
        Assert.Contains(added, sample.ServiceIds);
        Assert.Empty(sample.DomainEvents);

        sample.Cancel(Guid.CreateVersion7(), "cancelled", Now);
        Assert.False(sample.AddServices([Guid.CreateVersion7()]));
        Assert.Equal(3, sample.ServiceIds.Length);
    }

    [Fact]
    public void S_T2_ok_Collect_records_time_user_and_branch_and_raises_SampleCollected()
    {
        var sample = Ordered();

        sample.Collect(Phlebotomist, MainBranch, Now.AddMinutes(5));

        Assert.Equal(SampleStatus.Collected, sample.Status);
        Assert.Equal(Now.AddMinutes(5), sample.StatusChangedAtUtc);
        Assert.Equal((Phlebotomist, MainBranch), (sample.CollectedByUserId!.Value, sample.CollectedBranchId!.Value));
        var raised = Assert.IsType<SampleCollected>(Assert.Single(sample.DomainEvents));
        Assert.Equal((sample.Id, sample.TubeId, sample.SampleNumber, Phlebotomist), (raised.SampleId, raised.TubeId, raised.SampleNumber, raised.ActorUserId));
        Assert.False(sample.IsResultEntryAllowed);
    }

    [Fact]
    public void S_T2_state_Collect_refuses_a_second_scan_SMP_010()
    {
        var sample = Ordered();
        sample.Collect(Phlebotomist, MainBranch, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => sample.Collect(Phlebotomist, MainBranch, Now.AddMinutes(1)));

        Assert.Equal(SamplesCodes.NotOrdered, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
        Assert.Equal("Collected", refusal.CurrentState);
    }

    [Fact]
    public void S_T3_ok_Receive_after_collection_opens_the_result_gate()
    {
        var sample = Ordered();
        sample.Collect(Phlebotomist, MainBranch, Now);
        sample.ClearDomainEvents();

        sample.Receive(Technician, LabBranch, Now.AddMinutes(45));

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal((Phlebotomist, Technician), (sample.CollectedByUserId!.Value, sample.ReceivedByUserId!.Value));
        Assert.Equal(LabBranch, sample.ReceivedBranchId);
        Assert.IsType<SampleReceived>(Assert.Single(sample.DomainEvents));
        Assert.True(sample.IsResultEntryAllowed);
    }

    [Fact]
    public void S_T3_skip_Receive_of_an_uncollected_tube_records_both_steps_INV_SMP_003()
    {
        var sample = Ordered();

        sample.Receive(Technician, LabBranch, Now.AddMinutes(10));

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal(sample.ReceivedAtUtc, sample.CollectedAtUtc);
        Assert.Equal(Technician, sample.CollectedByUserId);
        Assert.Equal([SampleAction.Ordered, SampleAction.Collected, SampleAction.Received], sample.Events.Select(step => step.Action));
        Assert.Equal(2, sample.DomainEvents.Count);
    }

    [Fact]
    public void S_T3_state_Receive_refuses_a_Received_sample_SMP_011()
    {
        var sample = Received();

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => sample.Receive(Technician, LabBranch, Now.AddHours(1)));

        Assert.Equal(SamplesCodes.NotReceivable, refusal.Code);
        Assert.Empty(sample.DomainEvents);
    }

    [Fact]
    public void S_T4_ok_Reject_records_the_reason_snapshot_and_notifies_reception()
    {
        var sample = Received();

        sample.Reject(new RejectionReason(Hemolysis, " Hemolysis ", "تحلل الدم"), "  clotted  ", Technician, LabBranch, Now.AddHours(1));

        Assert.Equal(SampleStatus.Rejected, sample.Status);
        Assert.False(sample.IsResultEntryAllowed);
        var rejection = sample.Rejection!;
        Assert.Equal((Hemolysis, "Hemolysis", "تحلل الدم", "clotted"), (rejection.ReasonId, rejection.ReasonNameEn, rejection.ReasonNameAr, rejection.Note));
        Assert.Equal((Technician, LabBranch, Now.AddHours(1)), (rejection.ByUserId, rejection.BranchId, rejection.AtUtc));
        var step = sample.Events.Last();
        Assert.Equal((SampleAction.Rejected, SampleStatus.Rejected, Hemolysis, "clotted"), (step.Action, step.Status, step.ReasonId!.Value, step.Note));
        var raised = Assert.IsType<SampleRejected>(Assert.Single(sample.DomainEvents));
        Assert.Equal((MainBranch, LabBranch, Hemolysis), (raised.RegisteredBranchId, raised.BranchId, raised.ReasonId));
        Assert.Equal("Hemolysis — clotted", raised.Reason);
    }

    [Fact]
    public void S_T4_ok_Reject_of_a_Collected_sample_without_a_note_uses_the_reason_as_audit_text()
    {
        var sample = Ordered();
        sample.Collect(Phlebotomist, MainBranch, Now);
        sample.ClearDomainEvents();

        sample.Reject(new RejectionReason(Hemolysis, null, null), null, Technician, LabBranch, Now);

        var raised = Assert.IsType<SampleRejected>(Assert.Single(sample.DomainEvents));
        Assert.Equal($"Reason {Hemolysis}", raised.Reason);
        Assert.Null(sample.Rejection!.Note);
    }

    [Fact]
    public void S_T4_state_Reject_refuses_an_Ordered_sample_SMP_012()
    {
        var refusal = Assert.Throws<InvalidStateTransitionException>(() => Ordered().Reject(new RejectionReason(Hemolysis, "Hemolysis", "تحلل"), null, Technician, LabBranch, Now));

        Assert.Equal(SamplesCodes.NotRejectable, refusal.Code);
    }

    [Fact]
    public void S_T4_reason_Reject_refuses_a_missing_reason_SMP_020_and_a_long_note_SMP_022()
    {
        var sample = Received();

        var noReason = Assert.Throws<DomainException>(() => sample.Reject(new RejectionReason(Guid.Empty, null, null), null, Technician, LabBranch, Now));
        var longNote = Assert.Throws<DomainException>(() => sample.Reject(new RejectionReason(Hemolysis, "H", "H"), new string('x', Sample.MaxNoteLength + 1), Technician, LabBranch, Now));

        Assert.Equal(SamplesCodes.RejectionReasonRequired, noReason.Code);
        Assert.Equal(SamplesCodes.NoteTooLong, longNote.Code);
        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Null(sample.Rejection);
    }

    [Fact]
    public void S_T5_policy_Unreject_is_refused_when_the_tenant_requires_a_new_barcode_SMP_016()
    {
        var sample = Rejected();

        var refusal = Assert.Throws<DomainException>(() => sample.Unreject(reinstatementAllowed: false, null, Technician, LabBranch, Now.AddHours(2)));

        Assert.Equal(SamplesCodes.ReinstatementNotAllowed, refusal.Code);
        Assert.Equal(DomainFault.Refused, refusal.Fault);
        Assert.Equal(SampleStatus.Rejected, sample.Status);
        Assert.Empty(sample.DomainEvents);
    }

    [Fact]
    public void S_T5_ok_Unreject_keeps_the_barcode_and_returns_to_Received()
    {
        var sample = Rejected();
        var number = sample.SampleNumber;

        sample.Unreject(reinstatementAllowed: true, " new tube drawn ", Technician, MainBranch, Now.AddHours(2));

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal(number, sample.SampleNumber);
        Assert.Equal((Now.AddHours(2), MainBranch), (sample.ReceivedAtUtc!.Value, sample.ReceivedBranchId!.Value));
        Assert.NotNull(sample.Rejection);   // BR-15: the rejection stays on record
        Assert.Equal((SampleAction.Unrejected, SampleStatus.Received, "new tube drawn"), (sample.Events.Last().Action, sample.Events.Last().Status, sample.Events.Last().Note));
        var raised = Assert.IsType<SampleUnrejected>(Assert.Single(sample.DomainEvents));
        Assert.Equal("new tube drawn", raised.Comment);
        Assert.True(sample.IsResultEntryAllowed);
    }

    [Fact]
    public void S_T5_state_Unreject_refuses_a_sample_that_is_not_Rejected_SMP_013()
    {
        var refusal = Assert.Throws<InvalidStateTransitionException>(() => Received().Unreject(true, null, Technician, LabBranch, Now));

        Assert.Equal(SamplesCodes.NotRejected, refusal.Code);
    }

    [Fact]
    public void S_T6_ok_Replace_supersedes_the_sample_with_a_new_number_on_the_same_tube()
    {
        var sample = Rejected();

        var replacement = sample.Replace("smp-2026-00099", "recollected", Technician, LabBranch, Now.AddHours(3));

        Assert.Equal(SampleStatus.Replaced, sample.Status);
        Assert.False(sample.IsLive);
        Assert.Equal(replacement.Id, sample.ReplacedBySampleId);
        Assert.Equal(sample.Id, replacement.ReplacesSampleId);
        Assert.Equal((sample.TubeId, 1, "SMP-2026-00099", SampleStatus.Ordered), (replacement.TubeId, replacement.Generation, replacement.SampleNumber, replacement.Status));
        Assert.Equal((sample.VisitId, sample.Accession, sample.PatientId, sample.BranchId), (replacement.VisitId, replacement.Accession, replacement.PatientId, replacement.BranchId));
        Assert.Equal(sample.ServiceIds, replacement.ServiceIds);
        Assert.Equal((SampleAction.ReplacementOrdered, sample.SampleNumber, 1), (Assert.Single(replacement.Events).Action, replacement.Events.Single().Note, replacement.Events.Single().Sequence));
        Assert.Equal(SampleAction.Replaced, sample.Events.Last().Action);
        var raised = Assert.IsType<SampleReplaced>(Assert.Single(sample.DomainEvents));
        Assert.Equal((sample.Id, replacement.Id, "SMP-2026-00099", sample.TubeId), (raised.OldSampleId, raised.NewSampleId, raised.NewSampleNumber, raised.TubeId));
        Assert.Empty(replacement.DomainEvents);

        replacement.AutoAdvance(true, true, Technician, LabBranch, Now.AddHours(3));
        Assert.Equal(SampleStatus.Received, replacement.Status);
    }

    [Fact]
    public void S_T6_state_Replace_refuses_a_sample_that_is_not_Rejected_or_the_same_number()
    {
        var notRejected = Assert.Throws<InvalidStateTransitionException>(() => Received().Replace("SMP-2026-00002", null, Technician, LabBranch, Now));
        var sample = Rejected();
        var sameNumber = Assert.Throws<DomainException>(() => sample.Replace(sample.SampleNumber, null, Technician, LabBranch, Now));

        Assert.Equal(SamplesCodes.NotRejected, notRejected.Code);
        Assert.Equal(SamplesCodes.SampleNumberInvalid, sameNumber.Code);
        Assert.Equal(SampleStatus.Rejected, sample.Status);
    }

    [Fact]
    public void S_T7_ok_Cancel_is_terminal_and_idempotent()
    {
        var sample = Received();
        var actor = Guid.CreateVersion7();

        Assert.True(sample.Cancel(actor, " Patient left ", Now.AddHours(1)));
        Assert.False(sample.Cancel(actor, "again", Now.AddHours(2)));

        Assert.Equal(SampleStatus.Cancelled, sample.Status);
        Assert.Equal(("Patient left", Now.AddHours(1), actor), (sample.CancellationReason, sample.CancelledAtUtc!.Value, sample.CancelledByUserId!.Value));
        var raised = Assert.IsType<SampleCancelled>(Assert.Single(sample.DomainEvents));
        Assert.Equal("Patient left", raised.Reason);
        Assert.Equal(SamplesCodes.NotOrdered, Assert.Throws<InvalidStateTransitionException>(() => sample.Collect(Phlebotomist, MainBranch, Now)).Code);
        Assert.Equal(SamplesCodes.NotRejectable, Assert.Throws<InvalidStateTransitionException>(() => sample.Reject(new RejectionReason(Hemolysis, null, null), null, Technician, LabBranch, Now)).Code);
    }

    [Fact]
    public void S_T7_ok_Cancel_reaches_a_Rejected_sample_and_falls_back_to_a_default_reason()
    {
        var sample = Rejected();

        Assert.True(sample.Cancel(Guid.CreateVersion7(), "  ", Now));

        Assert.Equal(SampleStatus.Cancelled, sample.Status);
        Assert.False(string.IsNullOrWhiteSpace(sample.CancellationReason));
    }

    [Fact]
    public void S_T7_replaced_Cancel_refuses_a_Replaced_sample_SMP_014()
    {
        var sample = Rejected();
        sample.Replace("SMP-2026-00050", null, Technician, LabBranch, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => sample.Cancel(Guid.CreateVersion7(), "line cancelled", Now));

        Assert.Equal(SamplesCodes.NotCancellable, refusal.Code);
    }

    [Fact]
    public void INV_SMP_007_every_step_appends_one_history_row_with_the_resulting_status()
    {
        var sample = Ordered();
        sample.Collect(Phlebotomist, MainBranch, Now.AddMinutes(1));
        sample.Receive(Technician, LabBranch, Now.AddMinutes(2));
        sample.Reject(new RejectionReason(Hemolysis, "Hemolysis", "تحلل"), null, Technician, LabBranch, Now.AddMinutes(3));
        sample.Unreject(true, null, Technician, LabBranch, Now.AddMinutes(4));

        Assert.Equal(
            [(SampleAction.Ordered, SampleStatus.Ordered), (SampleAction.Collected, SampleStatus.Collected), (SampleAction.Received, SampleStatus.Received), (SampleAction.Rejected, SampleStatus.Rejected), (SampleAction.Unrejected, SampleStatus.Received)],
            sample.Events.Select(step => (step.Action, step.Status)));
        Assert.All(sample.Events, step => Assert.Equal(sample.Id, step.SampleId));
        Assert.Equal([1, 2, 3, 4, 5], sample.Events.Select(step => step.Sequence));
        Assert.Equal(sample.Events.Count, sample.Events.Select(step => step.Id).Distinct().Count());
    }
}
