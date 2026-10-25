using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>Acceptance scenarios of intake LIS-INT-REG-001 §16 on the Visit aggregate: one test per transition, refused precondition and invariant.</summary>
public sealed class VisitTests
{
    [Fact]
    public void S_V1_ok_registers_lines_tubes_by_BR02_totals_and_raises_VisitRegistered()
    {
        var glucose = Test("GLU", ChemistrySerum(), patient: 80m, payer: 20m);
        var urea = Test("UREA", ChemistrySerum(), patient: 60m);
        var cbc = Test("CBC", HaematologyBlood(), patient: 120m);

        var visit = Visit.Register(Header(), [glucose, urea, cbc], [], null, Numbers(5), Actor, Now);

        Assert.Equal(VisitStatus.Open, visit.Status);
        Assert.Equal(2, visit.Tubes.Count);
        Assert.Equal(["SMP-2026-00001", "SMP-2026-00002"], visit.Tubes.Select(tube => tube.SampleNumber).ToArray());
        var chemistryTube = visit.Tubes.Single(tube => tube.ClinicalGroupId == Chemistry);
        Assert.All(visit.Lines.Where(line => line.ServiceCode is "GLU" or "UREA"), line => Assert.Equal(chemistryTube.Id, line.TubeId));
        Assert.Equal(("C", "Serum"), (chemistryTube.UnitCode, chemistryTube.SampleTypeNameEn));
        Assert.Equal((280m, 260m, 20m, 0m), (visit.TotalListPrice, visit.PatientShareTotal, visit.PayerShareTotal, visit.DiscountTotal));
        Assert.Equal(1, visit.Revision);

        var raised = Assert.IsType<VisitRegistered>(Assert.Single(visit.DomainEvents));
        Assert.Equal((visit.Id, "ACC-2026-00001", 2, 3), (raised.VisitId, raised.Accession, raised.Tubes.Count, raised.Lines.Count));
        Assert.Equal(2, raised.Tubes.Single(tube => tube.TubeId == chemistryTube.Id).ServiceIds.Count);
        Assert.All(raised.Lines, line => Assert.Equal("Ordered", line.Status));
        Assert.All(raised.Lines, line => Assert.Single(line.TestIds));
    }

    [Fact]
    public void S_BR02_a_different_condition_splits_the_tube()
    {
        var visit = Register(Test("GLU", ChemistrySerum()), Test("FBS", ChemistrySerum(Fasting)));

        Assert.Equal(2, visit.Tubes.Count);
        Assert.NotEqual(visit.Lines[0].TubeId, visit.Lines[1].TubeId);
    }

    [Fact]
    public void S_V1_reserved_keeps_the_bill_without_a_tube_BR05()
    {
        var visit = Register(Test("GLU", ChemistrySerum(), patient: 80m), Test("LIPID", ChemistrySerum(), patient: 150m, reserved: true));

        var reserved = visit.Lines.Single(line => line.ServiceCode == "LIPID");
        Assert.Equal(VisitLineStatus.Reserved, reserved.Status);
        Assert.Null(reserved.TubeId);
        Assert.Equal(230m, visit.PatientShareTotal);
        var raised = Assert.IsType<VisitRegistered>(Assert.Single(visit.DomainEvents));
        Assert.Contains(raised.Lines, line => line.Status == "Reserved" && line.TubeId is null);
        Assert.DoesNotContain(reserved.ServiceId, raised.Tubes.Single().ServiceIds);
    }

    [Fact]
    public void S_V1_additional_service_has_no_tube_and_no_tests()
    {
        var visit = Register(Additional("HOME"));

        Assert.Empty(visit.Tubes);
        Assert.Null(visit.Lines.Single().TubeId);
        Assert.Equal(50m, visit.PatientShareTotal);
    }

    [Fact]
    public void S_V1_package_adds_a_priced_line_and_members_that_join_tubes_of_their_key()
    {
        var glucose = Test("GLU", ChemistrySerum());
        var panel = Package("CHK", 300m, Test("UREA", ChemistrySerum()), Test("CBC", HaematologyBlood()));

        var visit = Register(glucose, panel);

        var package = visit.Lines.Single(line => line.Kind == VisitLineKind.Package);
        var members = visit.Lines.Where(line => line.Kind == VisitLineKind.PackageMember).ToList();
        Assert.Equal(2, members.Count);
        Assert.All(members, member => Assert.Equal(package.Id, member.ParentLineId));
        Assert.All(members, member => Assert.Equal(0m, member.PatientShare));
        Assert.Null(package.TubeId);
        Assert.Equal(2, visit.Tubes.Count);
        Assert.Equal(visit.Lines.Single(line => line.ServiceCode == "GLU").TubeId, members.Single(member => member.ServiceCode == "UREA").TubeId);
        Assert.Equal(400m, visit.PatientShareTotal);
    }

    [Fact]
    public void S_V1_dup_the_same_service_twice_or_inside_a_package_is_refused_REG_100()
    {
        var glucose = Test("GLU", ChemistrySerum());
        var refusal = Assert.Throws<DomainException>(() => Register(glucose, glucose with { }));
        Assert.Equal((RegistrationCodes.DuplicateService, DomainFault.Conflict), (refusal.Code, refusal.Fault));

        var inPackage = Assert.Throws<DomainException>(() => Register(glucose, Package("CHK", 300m, glucose)));
        Assert.Equal(RegistrationCodes.DuplicateService, inPackage.Code);
    }

    [Fact]
    public void S_V1_header_and_lines_are_checked()
    {
        Assert.Equal(RegistrationCodes.LinesRequired, Assert.Throws<DomainException>(() => Visit.Register(Header(), [], [], null, Numbers(1), Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.HeaderInvalid, Assert.Throws<DomainException>(() => Visit.Register(Header(accession: " "), [Additional("X")], [], null, Numbers(1), Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.PayerInvalid, Assert.Throws<DomainException>(() => Visit.Register(Header() with { PayerId = Guid.Empty }, [Additional("X")], [], null, Numbers(1), Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.HeaderInvalid, Assert.Throws<DomainException>(() => Visit.Register(Header() with { UtcOffsetMinutes = 900 }, [Additional("X")], [], null, Numbers(1), Actor, Now)).Code);
    }

    [Fact]
    public void S_V1_preparation_answers_are_recorded_with_the_question()
    {
        var question = Guid.CreateVersion7();
        var visit = Visit.Register(Header(), [Test("GLU", ChemistrySerum())], [new PreparationAnswerInput(question, "Fasting?", "صائم؟", " Yes ")], null, Numbers(1), Actor, Now);

        var answer = Assert.Single(visit.PreparationAnswers);
        Assert.Equal((question, "Fasting?", "Yes"), (answer.PreparationId, answer.QuestionEn, answer.Answer));
    }

    [Fact]
    public void S_V1_discount_percent_is_allocated_to_the_patient_share_only()
    {
        var visit = Visit.Register(Header(), [Test("GLU", ChemistrySerum(), patient: 100m, payer: 50m), Test("CBC", HaematologyBlood(), patient: 200m)], [], Discount(DiscountKind.Percent, 10m), Numbers(2), Actor, Now);

        Assert.Equal((30m, 270m, 50m), (visit.DiscountTotal, visit.PatientShareTotal, visit.PayerShareTotal));
        Assert.Equal([10m, 20m], visit.Lines.Select(line => line.Discount).ToArray());
        Assert.Equal((DiscountKind.Percent, 10m, "Staff discount"), (visit.DiscountKind, visit.DiscountValue, visit.DiscountReason));
        Assert.Contains(visit.DomainEvents, e => e is VisitDiscountChanged changed && changed.Amount == 30m && changed.Reason == "Staff discount");
    }

    [Fact]
    public void S_V1_discount_above_the_cap_is_refused_REG_060_and_needs_a_reason_REG_017()
    {
        var above = Assert.Throws<DomainException>(() => Visit.Register(Header(), [Test("GLU", ChemistrySerum())], [], Discount(DiscountKind.Percent, 30m, cap: 20), Numbers(1), Actor, Now));
        Assert.Equal((RegistrationCodes.DiscountAboveCap, DomainFault.Refused), (above.Code, above.Fault));

        var amountAbove = Assert.Throws<DomainException>(() => Visit.Register(Header(), [Test("GLU", ChemistrySerum(), patient: 100m)], [], Discount(DiscountKind.Amount, 25m, cap: 20), Numbers(1), Actor, Now));
        Assert.Equal(RegistrationCodes.DiscountAboveCap, amountAbove.Code);

        var noReason = Assert.Throws<DomainException>(() => Visit.Register(Header(), [Test("GLU", ChemistrySerum())], [], new DiscountRequest(DiscountKind.Amount, 5m, Guid.Empty, " ", 20), Numbers(1), Actor, Now));
        Assert.Equal(RegistrationCodes.DiscountReasonRequired, noReason.Code);

        var tooMuch = Assert.Throws<DomainException>(() => Visit.Register(Header(), [Test("GLU", ChemistrySerum(), patient: 10m)], [], Discount(DiscountKind.Amount, 11m, cap: 100), Numbers(1), Actor, Now));
        Assert.Equal(RegistrationCodes.DiscountInvalid, tooMuch.Code);

        var nothing = Assert.Throws<DomainException>(() => Visit.Register(Header(), [Test("GLU", ChemistrySerum(), patient: 0m, payer: 100m)], [], Discount(DiscountKind.Percent, 10m), Numbers(1), Actor, Now));
        Assert.Equal(RegistrationCodes.NothingToDiscount, nothing.Code);
    }

    [Fact]
    public void S_V2_join_an_added_service_joins_the_live_tube_and_raises_VisitLinesAdded()
    {
        var visit = Register(Test("GLU", ChemistrySerum()));
        visit.ClearDomainEvents();
        var tube = visit.Tubes.Single();
        var urea = Test("UREA", ChemistrySerum());

        Assert.Equal(0, visit.CountNewTubes(Visit.TubeNeedsOf([urea])));
        visit.AddLines([urea], [], SampleNumberSource.Empty, Actor, Now.AddHours(1));

        Assert.Single(visit.Tubes);
        Assert.Equal(tube.Id, visit.Lines.Single(line => line.ServiceCode == "UREA").TubeId);
        var raised = Assert.IsType<VisitLinesAdded>(Assert.Single(visit.DomainEvents));
        Assert.Equal(2, Assert.Single(raised.Tubes).ServiceIds.Count);
        Assert.Equal("UREA", visit.Lines.Single(line => line.Id == Assert.Single(raised.Lines).LineId).ServiceCode);
        Assert.Equal(2, visit.Revision);
    }

    [Fact]
    public void S_V2_newtube_a_key_whose_tube_has_only_cancelled_lines_gets_a_new_tube()
    {
        var visit = Register(Test("GLU", ChemistrySerum()));
        visit.CancelLines([visit.Lines[0].Id], null, "Wrong order", Actor, Now);
        var urea = Test("UREA", ChemistrySerum());

        Assert.Equal(1, visit.CountNewTubes(Visit.TubeNeedsOf([urea])));
        visit.AddLines([urea], [], Numbers(1, start: 50), Actor, Now);

        Assert.Equal(2, visit.Tubes.Count);
        Assert.Equal("SMP-2026-00050", visit.Tubes.Single(tube => tube.Id == visit.Lines.Single(line => line.ServiceCode == "UREA").TubeId).SampleNumber);
    }

    [Fact]
    public void S_V2_duplicate_of_a_live_line_is_refused_and_a_cancelled_one_may_be_ordered_again()
    {
        var glucose = Test("GLU", ChemistrySerum());
        var visit = Register(glucose);

        Assert.Equal(RegistrationCodes.DuplicateService, Assert.Throws<DomainException>(() => visit.AddLines([glucose with { }], [], Numbers(1), Actor, Now)).Code);

        visit.CancelLines([visit.Lines[0].Id], null, "Wrong order", Actor, Now);
        visit.AddLines([glucose with { }], [], Numbers(1, start: 9), Actor, Now);
        Assert.Equal(2, visit.Lines.Count(line => line.ServiceId == glucose.ServiceId));
    }

    [Fact]
    public void S_V3_release_issues_the_tube_and_raises_VisitLinesAdded_and_an_ordered_line_is_refused_REG_105()
    {
        var visit = Register(Test("GLU", ChemistrySerum()), Test("CBC", HaematologyBlood(), reserved: true));
        visit.ClearDomainEvents();
        var reserved = visit.Lines.Single(line => line.Status == VisitLineStatus.Reserved);

        visit.ReleaseReserved([new LineRelease(reserved.Id, HaematologyBlood(), Now.AddHours(30))], Numbers(1, start: 7), Actor, Now.AddHours(2));

        Assert.Equal(VisitLineStatus.Ordered, reserved.Status);
        Assert.NotNull(reserved.TubeId);
        Assert.Equal((Now.AddHours(30), Actor), (reserved.ExpectedResultAtUtc, reserved.ReleasedByUserId));
        Assert.Equal(2, visit.Tubes.Count);
        var raised = Assert.IsType<VisitLinesAdded>(Assert.Single(visit.DomainEvents));
        Assert.Equal("Ordered", Assert.Single(raised.Lines).Status);

        var again = Assert.Throws<DomainException>(() => visit.ReleaseReserved([new LineRelease(reserved.Id, HaematologyBlood(), null)], Numbers(1), Actor, Now));
        Assert.Equal((RegistrationCodes.LineNotReserved, DomainFault.Conflict), (again.Code, again.Fault));
    }

    [Fact]
    public void S_V4_cancel_keeps_the_line_with_reason_recomputes_and_raises_one_event_per_line()
    {
        var visit = Visit.Register(Header(), [Test("GLU", ChemistrySerum(), patient: 100m), Test("CBC", HaematologyBlood(), patient: 200m)], [], Discount(DiscountKind.Percent, 10m), Numbers(2), Actor, Now);
        visit.ClearDomainEvents();
        var cbc = visit.Lines.Single(line => line.ServiceCode == "CBC");

        visit.CancelLines([cbc.Id], Guid.CreateVersion7(), "  Patient refused ", Actor, Now.AddMinutes(5));

        Assert.Equal(VisitLineStatus.Cancelled, cbc.Status);
        Assert.Equal(("Patient refused", Actor, Now.AddMinutes(5)), (cbc.CancellationReason, cbc.CancelledByUserId!.Value, cbc.CancelledAtUtc!.Value));
        Assert.Equal(200m, cbc.ListPrice);
        Assert.Equal((100m, 10m, 90m), (visit.TotalListPrice, visit.DiscountTotal, visit.PatientShareTotal));
        var raised = Assert.IsType<VisitLineCancelled>(Assert.Single(visit.DomainEvents));
        Assert.Equal((cbc.Id, cbc.TubeId, "Patient refused"), (raised.LineId, raised.TubeId, raised.Reason));
    }

    [Fact]
    public void S_V4_cancel_needs_a_reason_a_live_line_of_the_visit_and_cancels_a_package_with_its_members()
    {
        var visit = Register(Package("CHK", 300m, Test("UREA", ChemistrySerum()), Test("CBC", HaematologyBlood())));
        var package = visit.Lines.Single(line => line.Kind == VisitLineKind.Package);
        var member = visit.Lines.First(line => line.Kind == VisitLineKind.PackageMember);

        Assert.Equal(RegistrationCodes.CancellationReasonRequired, Assert.Throws<DomainException>(() => visit.CancelLines([package.Id], null, " ", Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.LineNotOnVisit, Assert.Throws<DomainException>(() => visit.CancelLines([Guid.CreateVersion7()], null, "x", Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.PackageMemberAction, Assert.Throws<DomainException>(() => visit.CancelLines([member.Id], null, "x", Actor, Now)).Code);
        Assert.Equal(3, visit.LinesAffectedBy([package.Id]).Count);

        visit.ClearDomainEvents();
        var cancelled = visit.CancelLines([package.Id], null, "Not needed", Actor, Now);

        Assert.Equal(3, cancelled.Count);
        Assert.All(visit.Lines, line => Assert.Equal(VisitLineStatus.Cancelled, line.Status));
        Assert.Equal(3, visit.DomainEvents.OfType<VisitLineCancelled>().Count());
        Assert.Equal(0m, visit.PatientShareTotal);
        Assert.Equal(RegistrationCodes.LineNotLive, Assert.Throws<DomainException>(() => visit.CancelLines([package.Id], null, "again", Actor, Now)).Code);
    }

    [Fact]
    public void S_V5_details_change_only_while_open_REG_103()
    {
        var visit = Register(Test("GLU", ChemistrySerum()));
        var doctor = Guid.CreateVersion7();

        visit.UpdateDetails(new VisitDetails(" T-1 ", doctor, "INS-9", true, true));
        Assert.Equal(("T-1", doctor, "INS-9", true), (visit.TreatmentNo, visit.ReferringDoctorId!.Value, visit.InsuranceId, visit.Urgent));

        visit.Complete(Now);
        var refusal = Assert.Throws<InvalidStateTransitionException>(() => visit.UpdateDetails(new VisitDetails(null, null, null, false, false)));
        Assert.Equal(RegistrationCodes.VisitNotOpen, refusal.Code);
        Assert.Equal(RegistrationCodes.VisitNotOpen, Assert.Throws<InvalidStateTransitionException>(() => visit.AddLines([Additional("X")], [], Numbers(1), Actor, Now)).Code);
    }

    [Fact]
    public void S_V6_set_change_and_clear_the_discount()
    {
        var visit = Register(Test("GLU", ChemistrySerum(), patient: 100m), Test("CBC", HaematologyBlood(), patient: 100m));
        visit.ClearDomainEvents();

        visit.SetDiscount(Discount(DiscountKind.Amount, 15m), null, Actor, Now);
        Assert.Equal((15m, 185m), (visit.DiscountTotal, visit.PatientShareTotal));
        Assert.Equal(15m, visit.Lines.Sum(line => line.Discount));

        visit.SetDiscount(null, "Manager decision", Actor, Now);
        Assert.Equal((0m, 200m, (DiscountKind?)null), (visit.DiscountTotal, visit.PatientShareTotal, visit.DiscountKind));
        Assert.Equal(2, visit.DomainEvents.OfType<VisitDiscountChanged>().Count());
        Assert.Equal("Manager decision", visit.DomainEvents.OfType<VisitDiscountChanged>().Last().Reason);
    }

    [Fact]
    public void S_V7_transfer_reprices_logs_and_raises_VisitTransferred()
    {
        var glucose = Test("GLU", ChemistrySerum(), patient: 100m);
        var visit = Register(glucose);
        visit.ClearDomainEvents();
        var (payer, tier) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var preview = visit.PreviewTransfer(new Dictionary<Guid, LinePrice> { [glucose.ServiceId] = new(100m, 20m, 80m) });
        Assert.Equal((20m, 80m), (preview.PatientShareTotal, preview.PayerShareTotal));
        Assert.Equal(100m, visit.PatientShareTotal);

        var transfer = visit.TransferContract(payer, tier, new Dictionary<Guid, LinePrice> { [glucose.ServiceId] = new(100m, 20m, 80m) }, null, "Insurance approval arrived", Actor, Now);

        Assert.Equal((payer, tier, 20m, 80m), (visit.PayerId, visit.ContractTierId, visit.PatientShareTotal, visit.PayerShareTotal));
        Assert.Equal((VisitTransferKind.Contract, 100m, 20m), (transfer.Kind, transfer.PatientShareBefore, transfer.PatientShareAfter));
        Assert.Single(visit.Transfers);
        Assert.Contains(visit.DomainEvents, e => e is VisitTransferred moved && moved.ToContractTierId == tier);
        Assert.Contains(visit.DomainEvents, e => e is VisitTransferRecorded recorded && recorded.Reason == "Insurance approval arrived");
    }

    [Fact]
    public void S_V7_transfer_refuses_the_same_contract_an_unpriced_service_and_a_missing_reason()
    {
        var glucose = Test("GLU", ChemistrySerum());
        var visit = Register(glucose);
        var prices = new Dictionary<Guid, LinePrice> { [glucose.ServiceId] = new(100m, 100m, 0m) };

        Assert.Equal(RegistrationCodes.TransferSameContract, Assert.Throws<DomainException>(() => visit.TransferContract(visit.PayerId, visit.ContractTierId, prices, null, "x", Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.ServiceUnpriced, Assert.Throws<DomainException>(() => visit.TransferContract(Guid.CreateVersion7(), Guid.CreateVersion7(), new Dictionary<Guid, LinePrice>(), null, "x", Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.TransferReasonRequired, Assert.Throws<DomainException>(() => visit.TransferContract(Guid.CreateVersion7(), Guid.CreateVersion7(), prices, null, "", Actor, Now)).Code);
    }

    [Fact]
    public void S_V8_shares_keep_the_line_total_and_are_logged()
    {
        var visit = Register(Test("GLU", ChemistrySerum(), patient: 100m, payer: 50m));
        var line = visit.Lines.Single();
        visit.ClearDomainEvents();

        visit.AdjustLineShares(line.Id, 50m, 100m, null, "Company covers more", Actor, Now);

        Assert.Equal((50m, 100m), (line.PatientShare, line.PayerShare));
        Assert.Equal((50m, 100m), (visit.PatientShareTotal, visit.PayerShareTotal));
        Assert.Equal(VisitTransferKind.LineShares, Assert.Single(visit.Transfers).Kind);
        Assert.IsType<VisitLineSharesAdjusted>(Assert.Single(visit.DomainEvents));

        var changed = Assert.Throws<DomainException>(() => visit.AdjustLineShares(line.Id, 60m, 100m, null, "x", Actor, Now));
        Assert.Equal(RegistrationCodes.SharesInvalid, changed.Code);
    }

    [Fact]
    public void S_V9_complete_is_idempotent()
    {
        var visit = Register(Additional("X"));
        visit.ClearDomainEvents();

        Assert.True(visit.Complete(Now));
        Assert.False(visit.Complete(Now));
        Assert.Equal(VisitStatus.Completed, visit.Status);
        Assert.Single(visit.DomainEvents.OfType<VisitCompleted>());
    }

    [Fact]
    public void S_V10_merge_repoints_the_patient_once_and_keeps_the_original()
    {
        var merged = Guid.CreateVersion7();
        var survivor = Guid.CreateVersion7();
        var visit = Visit.Register(Header(patientId: merged), [Additional("X")], [], null, Numbers(1), Actor, Now);
        visit.ClearDomainEvents();

        Assert.True(visit.RepointPatient(merged, survivor, Actor, "Duplicate file", Now));
        Assert.False(visit.RepointPatient(merged, survivor, Actor, "Duplicate file", Now));

        Assert.Equal((survivor, merged), (visit.PatientId, visit.OriginalPatientId!.Value));
        var raised = Assert.IsType<VisitPatientRepointed>(Assert.Single(visit.DomainEvents));
        Assert.Equal("Duplicate file", raised.Reason);
    }
}
