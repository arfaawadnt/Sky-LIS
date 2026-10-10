using Platform.Application.Samples;
using Platform.Application.Samples.Commands;
using Platform.Application.Samples.Policies;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Registration;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Samples;

/// <summary>The use cases of intake LIS-INT-SMP-001 over in-memory ports: policies P1/P2 (§11), scan and bulk actions, reject / unreject / replace (§7), timeline arithmetic (§14).</summary>
public sealed class SamplesUseCaseTests
{
    private static readonly DateTimeOffset At = StoppedClock.Instant;
    private static readonly Guid Registrar = Guid.CreateVersion7();
    private static readonly Guid Branch = Guid.CreateVersion7();
    private static readonly Guid Bench = Guid.CreateVersion7();

    private readonly InMemorySampleRepository _samples = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    private static FakeSettings ScanningLab() =>
        new FakeSettings().With(SettingKeys.AutoCollect, "false").With(SettingKeys.AutoReceive, "false");

    private static RegisteredTube Tube(string number) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7()]);

    private static VisitRegistered Registered(params RegisteredTube[] tubes) =>
        new(Guid.CreateVersion7(), "ACC-2026-00010", Branch, Guid.CreateVersion7(), null, null, false, Registrar, tubes,
            [.. tubes.Select(tube => new RegisteredLine(Guid.CreateVersion7(), tube.ServiceIds[0], tube.TubeId, "Ordered", [Guid.CreateVersion7()]))], At);

    private async Task<Sample> SeedAsync(FakeSettings settings, string number = "SMP-2026-00001")
    {
        await new CreateSamplesOnVisitRegisteredPolicy(_samples, settings, _unitOfWork).HandleAsync(Registered(Tube(number)), CancellationToken.None);
        return _samples.Items.Single(sample => sample.SampleNumber == number);
    }

    [Fact]
    public async Task S_T1_ok_P1_creates_one_Ordered_sample_per_tube_when_the_lab_scans()
    {
        var first = Tube("SMP-2026-00001");
        var second = Tube("SMP-2026-00002");
        var registered = Registered(first, second);

        await new CreateSamplesOnVisitRegisteredPolicy(_samples, ScanningLab(), _unitOfWork).HandleAsync(registered, CancellationToken.None);

        Assert.Equal(2, _samples.Items.Count);
        Assert.All(_samples.Items, sample => Assert.Equal(SampleStatus.Ordered, sample.Status));
        var sample = _samples.Items.Single(item => item.TubeId == first.TubeId);
        Assert.Equal(("SMP-2026-00001", registered.VisitId, registered.Accession, registered.PatientId, Branch), (sample.SampleNumber, sample.VisitId, sample.Accession, sample.PatientId, sample.BranchId));
        Assert.Equal((first.ClinicalGroupId, first.SampleTypeId), (sample.ClinicalGroupId, sample.SampleTypeId));
        Assert.Equal(first.ServiceIds, sample.ServiceIds);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task S_T1_auto_P1_with_the_Release_1_defaults_receives_by_the_registrar_at_the_event_time()
    {
        var sample = await SeedAsync(new FakeSettings());

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal((At, Registrar, Branch), (sample.CollectedAtUtc!.Value, sample.CollectedByUserId!.Value, sample.CollectedBranchId!.Value));
        Assert.Equal((At, Registrar), (sample.ReceivedAtUtc!.Value, sample.ReceivedByUserId!.Value));
    }

    [Fact]
    public async Task S_T1_idem_P1_handles_the_same_event_twice_without_a_second_sample()
    {
        var registered = Registered(Tube("SMP-2026-00001"));
        var policy = new CreateSamplesOnVisitRegisteredPolicy(_samples, ScanningLab(), _unitOfWork);

        await policy.HandleAsync(registered, CancellationToken.None);
        await policy.HandleAsync(registered, CancellationToken.None);

        Assert.Single(_samples.Items);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task S_T1_reserved_P1_skips_a_tube_of_Reserved_lines_and_VisitLinesAdded_creates_it_later()
    {
        var tube = Tube("SMP-2026-00003");
        var visitId = Guid.CreateVersion7();
        var line = Guid.CreateVersion7();
        var registered = new VisitRegistered(visitId, "ACC-2026-00011", Branch, Guid.CreateVersion7(), null, null, false, Registrar, [tube], [new RegisteredLine(line, tube.ServiceIds[0], tube.TubeId, "Reserved", [])], At);

        await new CreateSamplesOnVisitRegisteredPolicy(_samples, ScanningLab(), _unitOfWork).HandleAsync(registered, CancellationToken.None);
        Assert.Empty(_samples.Items);
        Assert.Equal(0, _unitOfWork.Commits);

        var released = new VisitLinesAdded(visitId, "ACC-2026-00011", Branch, registered.PatientId, Bench, [tube], [new RegisteredLine(line, tube.ServiceIds[0], tube.TubeId, "Ordered", [])], At.AddDays(1));
        await new CreateSamplesOnVisitLinesAddedPolicy(_samples, ScanningLab(), _unitOfWork).HandleAsync(released, CancellationToken.None);

        var sample = Assert.Single(_samples.Items);
        Assert.Equal((tube.TubeId, At.AddDays(1)), (sample.TubeId, sample.OrderedAtUtc));
        Assert.Equal(Bench, sample.Events.Single().ByUserId);
    }

    [Fact]
    public async Task S_T1_added_P1_adds_the_services_of_new_lines_to_a_tube_that_already_has_its_sample()
    {
        var sample = await SeedAsync(ScanningLab());
        var commitsBefore = _unitOfWork.Commits;
        var newService = Guid.CreateVersion7();
        var tube = new RegisteredTube(sample.TubeId, sample.SampleNumber, sample.ClinicalGroupId, sample.SampleTypeId, null, [sample.ServiceIds[0], newService]);
        var added = new VisitLinesAdded(sample.VisitId, sample.Accession, Branch, sample.PatientId, Bench, [tube], [new RegisteredLine(Guid.CreateVersion7(), newService, tube.TubeId, "Ordered", [])], At.AddHours(1));
        var policy = new CreateSamplesOnVisitLinesAddedPolicy(_samples, ScanningLab(), _unitOfWork);

        await policy.HandleAsync(added, CancellationToken.None);
        await policy.HandleAsync(added, CancellationToken.None);

        Assert.Single(_samples.Items);
        Assert.Contains(newService, sample.ServiceIds);
        Assert.Equal(commitsBefore + 1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task S_T7_ok_P2_cancels_the_tube_when_no_live_line_remains_and_only_once()
    {
        var sample = await SeedAsync(new FakeSettings());
        var commitsBefore = _unitOfWork.Commits;
        var lines = new FakeVisitLines(hasLiveLines: false);
        var policy = new CancelSampleOnVisitLineCancelledPolicy(_samples, lines, _unitOfWork);
        var lineId = Guid.CreateVersion7();
        var cancelled = new VisitLineCancelled(sample.VisitId, sample.Accession, lineId, sample.ServiceIds[0], sample.TubeId, Bench, "Patient declined", At.AddHours(1));

        await policy.HandleAsync(cancelled, CancellationToken.None);
        await policy.HandleAsync(cancelled, CancellationToken.None);

        Assert.Equal(SampleStatus.Cancelled, sample.Status);
        Assert.Equal("Patient declined", sample.CancellationReason);
        Assert.Equal(commitsBefore + 1, _unitOfWork.Commits);
        Assert.Equal((sample.TubeId, lineId), lines.Calls[0]);
    }

    [Fact]
    public async Task S_T7_live_P2_keeps_the_tube_while_another_line_is_live_and_ignores_lines_without_a_tube()
    {
        var sample = await SeedAsync(new FakeSettings());
        var commitsBefore = _unitOfWork.Commits;
        var policy = new CancelSampleOnVisitLineCancelledPolicy(_samples, new FakeVisitLines(hasLiveLines: true), _unitOfWork);

        await policy.HandleAsync(new VisitLineCancelled(sample.VisitId, sample.Accession, Guid.CreateVersion7(), Guid.CreateVersion7(), sample.TubeId, Bench, "one of two", At), CancellationToken.None);
        await policy.HandleAsync(new VisitLineCancelled(sample.VisitId, sample.Accession, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Bench, "reserved line", At), CancellationToken.None);

        Assert.Equal(SampleStatus.Received, sample.Status);
        Assert.Equal(commitsBefore, _unitOfWork.Commits);
    }

    [Fact]
    public async Task S_T2_ok_Scan_in_Collect_mode_collects_and_autoReceive_also_receives()
    {
        var scanning = ScanningLab();
        var sample = await SeedAsync(scanning);
        var handler = new ScanSampleHandler(_samples, scanning, _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At.AddMinutes(5)));

        var detail = await handler.Handle(new ScanSampleCommand(" smp-2026-00001 ", "collect"), CancellationToken.None);

        Assert.Equal("Collected", detail.Summary.Status);
        Assert.Equal((Bench, Branch), (sample.CollectedByUserId!.Value, sample.CollectedBranchId!.Value));

        var other = await SeedAsync(new FakeSettings().With(SettingKeys.AutoCollect, "false"), "SMP-2026-00002");   // autoReceive defaults to true
        var receivingHandler = new ScanSampleHandler(_samples, new FakeSettings().With(SettingKeys.AutoCollect, "false"), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At.AddMinutes(6)));
        Assert.Equal(SampleStatus.Received, other.Status);   // created Received by autoReceive at registration
        var refusal = await Assert.ThrowsAsync<InvalidStateTransitionException>(() => receivingHandler.Handle(new ScanSampleCommand("SMP-2026-00002", "Collect"), CancellationToken.None));
        Assert.Equal(SamplesCodes.NotOrdered, refusal.Code);
    }

    [Fact]
    public async Task S_T2_auto_Scan_in_Collect_mode_with_autoReceive_on_records_the_receipt_too()
    {
        var sample = await SeedAsync(ScanningLab());
        var handler = new ScanSampleHandler(_samples, new FakeSettings().With(SettingKeys.AutoCollect, "false"), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At.AddMinutes(5)));

        var detail = await handler.Handle(new ScanSampleCommand("SMP-2026-00001", "Collect"), CancellationToken.None);

        Assert.Equal("Received", detail.Summary.Status);
        Assert.True(detail.IsResultEntryAllowed);
        Assert.Equal(sample.CollectedAtUtc, sample.ReceivedAtUtc);
    }

    [Fact]
    public async Task S_BRANCH_Scan_needs_a_selected_branch_and_a_known_number()
    {
        await SeedAsync(ScanningLab());
        var noBranch = new ScanSampleHandler(_samples, ScanningLab(), _unitOfWork, new FakeUser(Bench, null), new StoppedClock(At));
        var withBranch = new ScanSampleHandler(_samples, ScanningLab(), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At));

        var branchRefusal = await Assert.ThrowsAsync<DomainException>(() => noBranch.Handle(new ScanSampleCommand("SMP-2026-00001", "Receive"), CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<DomainException>(() => withBranch.Handle(new ScanSampleCommand("SMP-2026-99999", "Receive"), CancellationToken.None));

        Assert.Equal(ProblemCodes.BranchRequired, branchRefusal.Code);
        Assert.Equal((SamplesCodes.SampleNotFound, DomainFault.NotFound), (unknown.Code, unknown.Fault));
    }

    [Fact]
    public async Task S_BULK_Receive_all_moves_what_it_can_and_reports_the_rest()
    {
        var first = Tube("SMP-2026-00011");
        var second = Tube("SMP-2026-00012");
        var third = Tube("SMP-2026-00013");
        var registered = Registered(first, second, third);
        await new CreateSamplesOnVisitRegisteredPolicy(_samples, ScanningLab(), _unitOfWork).HandleAsync(registered, CancellationToken.None);
        _samples.Items.Single(sample => sample.TubeId == first.TubeId).Receive(Bench, Branch, At);
        _samples.Items.Single(sample => sample.TubeId == second.TubeId).Collect(Bench, Branch, At);

        var result = await new ReceiveVisitSamplesHandler(_samples, _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At.AddMinutes(9)))
            .Handle(new ReceiveVisitSamplesCommand(registered.VisitId), CancellationToken.None);

        Assert.Equal((2, 1), (result.Changed, result.Unchanged));
        Assert.Equal(["SMP-2026-00012", "SMP-2026-00013"], result.ChangedSampleNumbers.Order(StringComparer.Ordinal));
        Assert.All(_samples.Items, sample => Assert.Equal(SampleStatus.Received, sample.Status));

        var collectAll = new CollectVisitSamplesHandler(_samples, ScanningLab(), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At));
        var none = await collectAll.Handle(new CollectVisitSamplesCommand(registered.VisitId), CancellationToken.None);
        Assert.Equal((0, 3), (none.Changed, none.Unchanged));

        var unknown = await Assert.ThrowsAsync<DomainException>(() => collectAll.Handle(new CollectVisitSamplesCommand(Guid.CreateVersion7()), CancellationToken.None));
        Assert.Equal(SamplesCodes.VisitSamplesNotFound, unknown.Code);
    }

    [Fact]
    public async Task S_T4_reason_Reject_checks_the_catalogue_reason_when_the_catalogue_is_installed()
    {
        var sample = await SeedAsync(new FakeSettings());
        var reasonId = Guid.CreateVersion7();
        var user = new FakeUser(Bench, Branch);
        var clock = new StoppedClock(At.AddHours(1));

        var wrongKind = new RejectSampleHandler(_samples, new FakeReasons(new RejectionReasonCheck(true, new RejectionReasonRow(reasonId, "Discount", "Staff", "موظف", true))), _unitOfWork, user, clock);
        var inactive = new RejectSampleHandler(_samples, new FakeReasons(new RejectionReasonCheck(true, new RejectionReasonRow(reasonId, "Rejection", "Old", "قديم", false))), _unitOfWork, user, clock);
        var missing = new RejectSampleHandler(_samples, new FakeReasons(new RejectionReasonCheck(true, null)), _unitOfWork, user, clock);
        foreach (var handler in new[] { wrongKind, inactive, missing })
        {
            var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new RejectSampleCommand(sample.Id, reasonId, null), CancellationToken.None));
            Assert.Equal(SamplesCodes.RejectionReasonUnknown, refusal.Code);
        }

        var valid = new RejectSampleHandler(_samples, new FakeReasons(new RejectionReasonCheck(true, new RejectionReasonRow(reasonId, "rejection", "Hemolysis", "تحلل الدم", true))), _unitOfWork, user, clock);
        await valid.Handle(new RejectSampleCommand(sample.Id, reasonId, "clotted"), CancellationToken.None);

        Assert.Equal(SampleStatus.Rejected, sample.Status);
        Assert.Equal(("Hemolysis", "تحلل الدم", "clotted"), (sample.Rejection!.ReasonNameEn, sample.Rejection.ReasonNameAr, sample.Rejection.Note));
    }

    [Fact]
    public async Task S_T4_ok_Reject_keeps_the_id_while_the_catalogue_is_not_installed()
    {
        var sample = await SeedAsync(new FakeSettings());
        var reasonId = Guid.CreateVersion7();

        await new RejectSampleHandler(_samples, new FakeReasons(new RejectionReasonCheck(false, null)), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At))
            .Handle(new RejectSampleCommand(sample.Id, reasonId, null), CancellationToken.None);

        Assert.Equal((SampleStatus.Rejected, reasonId), (sample.Status, sample.Rejection!.ReasonId));
        Assert.Null(sample.Rejection.ReasonNameEn);
    }

    [Fact]
    public async Task S_T5_policy_Unreject_follows_the_newBarcodeOnReject_parameter()
    {
        var sample = await SeedAsync(new FakeSettings());
        sample.Reject(new RejectionReason(Guid.CreateVersion7(), "Hemolysis", "تحلل"), null, Bench, Branch, At);
        var user = new FakeUser(Bench, Branch);

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            new UnrejectSampleHandler(_samples, new FakeSettings(), _unitOfWork, user, new StoppedClock(At)).Handle(new UnrejectSampleCommand(sample.Id, null), CancellationToken.None));
        Assert.Equal(SamplesCodes.ReinstatementNotAllowed, refused.Code);

        await new UnrejectSampleHandler(_samples, new FakeSettings().With(SettingKeys.NewBarcodeOnReject, "false"), _unitOfWork, user, new StoppedClock(At.AddMinutes(1)))
            .Handle(new UnrejectSampleCommand(sample.Id, "same barcode"), CancellationToken.None);
        Assert.Equal(SampleStatus.Received, sample.Status);
    }

    [Fact]
    public async Task S_T6_ok_Replace_draws_a_new_number_and_applies_the_arrival_parameters()
    {
        var sample = await SeedAsync(new FakeSettings());
        sample.Reject(new RejectionReason(Guid.CreateVersion7(), "Hemolysis", "تحلل"), null, Bench, Branch, At);
        var numbers = new SequenceNumbers();
        var handler = new ReplaceSampleHandler(_samples, numbers, ScanningLab(), _unitOfWork, new FakeUser(Bench, Branch), new StoppedClock(At.AddHours(1)));

        var created = await handler.Handle(new ReplaceSampleCommand(sample.Id, "recollect"), CancellationToken.None);

        var replacement = _samples.Items.Single(item => item.Id == created.Id);
        Assert.Equal(("sample:SMP-2026-00101", "SMP-2026-00101"), (Assert.Single(numbers.Issued), replacement.SampleNumber));
        Assert.Equal((SampleStatus.Ordered, sample.TubeId, 1), (replacement.Status, replacement.TubeId, replacement.Generation));
        Assert.Equal(SampleStatus.Replaced, sample.Status);

        var again = await Assert.ThrowsAsync<InvalidStateTransitionException>(() => handler.Handle(new ReplaceSampleCommand(sample.Id, null), CancellationToken.None));
        Assert.Equal(SamplesCodes.NotRejected, again.Code);
        Assert.Single(numbers.Issued);   // refused before a number is drawn
    }

    [Fact]
    public void Timeline_computes_the_time_since_the_previous_step_and_the_stage_durations()
    {
        var sample = Sample.Create(Guid.CreateVersion7(), "SMP-2026-00001", Guid.CreateVersion7(), "ACC-1", Guid.CreateVersion7(), Branch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [], Registrar, At);
        sample.Collect(Bench, Branch, At.AddMinutes(10));
        sample.Receive(Bench, Branch, At.AddMinutes(70));
        var detail = new SampleDetail(InMemorySampleRepository.Item(sample), [], null, null, null, null, null, null, null, true);
        var steps = sample.Events.Reverse().Select(step => new SampleTimelineStep(step.Id, step.Sequence, step.Action.ToString(), step.Status.ToString(), step.AtUtc, null, null, null, null, null, null, null, null, null, null)).ToList();

        var timeline = SampleTimelineCalculator.Build(detail, steps);

        Assert.Equal(["Ordered", "Collected", "Received"], timeline.Steps.Select(step => step.Action));
        Assert.Equal([null, 600L, 3600L], timeline.Steps.Select(step => step.SincePreviousSeconds));
        Assert.Equal(new SampleStageDurations(600, 3600, 4200), timeline.Durations);
    }

    [Fact]
    public void Timeline_keeps_the_transition_order_of_steps_that_share_an_instant()
    {
        var sample = Sample.Create(Guid.CreateVersion7(), "SMP-2026-00002", Guid.CreateVersion7(), "ACC-2", Guid.CreateVersion7(), Branch, Guid.CreateVersion7(), Guid.CreateVersion7(), null, [], Registrar, At);
        sample.AutoAdvance(true, true, Registrar, Branch, At);   // Ordered, Collected and Received at the registration instant
        var detail = new SampleDetail(InMemorySampleRepository.Item(sample), [], null, null, null, null, null, null, null, true);
        var steps = sample.Events.OrderByDescending(step => step.Id).Select(step => new SampleTimelineStep(step.Id, step.Sequence, step.Action.ToString(), step.Status.ToString(), step.AtUtc, null, null, null, null, null, null, null, null, null, null)).ToList();

        var timeline = SampleTimelineCalculator.Build(detail, steps);

        Assert.Equal(["Ordered", "Collected", "Received"], timeline.Steps.Select(step => step.Action));
        Assert.Equal([null, 0L, 0L], timeline.Steps.Select(step => step.SincePreviousSeconds));
    }

    [Theory]
    [InlineData(0, "2026-10-09T00:00:00+00:00")]
    [InlineData(180, "2026-10-08T21:00:00+00:00")]
    [InlineData(-300, "2026-10-09T05:00:00+00:00")]
    public void LocalDay_is_the_callers_calendar_day_as_a_UTC_window(int offsetMinutes, string expectedStart)
    {
        var (from, to) = LocalDay.Of(At, offsetMinutes);

        Assert.Equal(DateTimeOffset.Parse(expectedStart, System.Globalization.CultureInfo.InvariantCulture), from);
        Assert.Equal(from.AddDays(1), to);
    }
}
