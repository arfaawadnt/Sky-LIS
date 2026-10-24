using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Services;
using Platform.Application.Results;
using Platform.Application.Results.Commands;
using Platform.Application.Results.Policies;
using Platform.Application.Results.Queries;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Registration;
using Platform.Domain.Results;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Results;

/// <summary>
/// Use cases of intake LIS-INT-RES-001 over in-memory ports: the order policy (T1, BR-04 units, BR-13 clock), entry with
/// ranges by sex (BR-08), formulas (T2b), the Received gate (BR-03), rights, bulk verification, cancellation and the
/// report document (BR-20, preview rule).
/// </summary>
public sealed class ResultsUseCaseTests
{
    private static readonly DateTimeOffset Registered = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task R_T1_order_policy_creates_one_result_per_test_of_every_Ordered_line_once()
    {
        var world = new World();
        var potassium = world.AddTest("K");
        var sodium = world.AddTest("NA");
        var profile = world.AddService(ServiceType.Profile, potassium.Id, sodium.Id);
        var reservedService = world.AddService(ServiceType.Test, world.AddTest("CL").Id);
        var additional = world.AddService(ServiceType.Additional);
        var package = world.AddService(ServiceType.Package);

        var lines = new[]
        {
            new RegisteredLine(Guid.CreateVersion7(), profile.Id, world.Tube, "Ordered", []),
            new RegisteredLine(Guid.CreateVersion7(), reservedService.Id, null, "Reserved", []),
            new RegisteredLine(Guid.CreateVersion7(), additional.Id, null, "Ordered", []),
            new RegisteredLine(Guid.CreateVersion7(), package.Id, null, "Ordered", []),
        };
        await world.RegisterAsync(lines);
        await world.RegisterAsync(lines);

        Assert.Equal(2, world.Results.Items.Count);
        Assert.All(world.Results.Items, result =>
        {
            Assert.Equal(ResultStatus.Ordered, result.Status);
            Assert.Equal(ReportUnits.ForGroup(world.Visit, world.Group), result.ReportUnitKey);
            Assert.Equal((world.Tube, profile.Id, profile.Id), (result.TubeId!.Value, result.ServiceId, result.LineServiceId));
            Assert.Equal(Registered.AddHours(24), result.ExpectedAtUtc);
        });
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task R_T1_a_print_alone_service_has_its_own_unit_and_a_collected_tube_starts_the_clock()
    {
        var world = new World();
        var test = world.AddTest("HBA1C");
        var alone = world.AddService(ServiceType.Test, printAlone: true, members: test.Id);
        var line = new RegisteredLine(Guid.CreateVersion7(), alone.Id, world.Tube, "Ordered", [test.Id]);
        world.Samples.Collected(world.Tube, world.Visit, Registered.AddHours(1));

        await world.RegisterAsync(line);

        var result = Assert.Single(world.Results.Items);
        Assert.Equal(ReportUnits.ForService(world.Visit, line.LineId, alone.Id), result.ReportUnitKey);
        Assert.Equal((Registered.AddHours(1), Registered.AddHours(25)), (result.ClockStartUtc, result.ExpectedAtUtc));
    }

    [Fact]
    public async Task R_BR08_the_range_resolves_by_the_patient_s_sex()
    {
        var world = new World();
        var test = world.AddTest("HB", bands: [FakeCatalogue.Band(GenderRestriction.Female, 12m, 15m), FakeCatalogue.Band(GenderRestriction.Male, 13.5m, 17.5m)]);
        world.Patients.Items[world.Patient] = FakePatients.Patient(world.Patient, "Female", 40);
        var result = await world.OrderedAsync(test);
        world.Samples.Received(world.Tube, world.Visit);

        await world.SaveAsync(new ResultValueItem(result.Id, 13m, null, null, null));

        Assert.Equal((ResultFlag.Normal, 12m, 15m), (result.Flag, result.Range!.Low!.Value, result.Range.High!.Value));
    }

    [Fact]
    public async Task C08_the_case_shows_the_patient_s_range_before_entry_and_the_stored_range_after()
    {
        // LIS-CR-007 C-08: an ordered line shows the band BR-08 picks for the patient, marked provisional; once a value is
        // entered the stored range is served and the mark goes.
        var world = new World();
        var test = world.AddTest("HB", bands: [FakeCatalogue.Band(GenderRestriction.Female, 12m, 15m), FakeCatalogue.Band(GenderRestriction.Male, 13.5m, 17.5m)]);
        world.Patients.Items[world.Patient] = FakePatients.Patient(world.Patient, "Female", 40);
        var result = await world.OrderedAsync(test);
        world.Samples.Received(world.Tube, world.Visit);

        var before = (await world.CaseAsync()).Units.Single().Results.Single();
        Assert.Null(result.Range);
        Assert.Equal((12m, 15m, true), (before.Range!.Low!.Value, before.Range.High!.Value, before.RangeProvisional));

        await world.SaveAsync(new ResultValueItem(result.Id, 13m, null, null, null));
        world.Patients.Items[world.Patient] = FakePatients.Patient(world.Patient, "Male", 40);   // a later file change does not move a stored range

        var after = (await world.CaseAsync()).Units.Single().Results.Single();
        Assert.Equal((12m, 15m, false), (after.Range!.Low!.Value, after.Range.High!.Value, after.RangeProvisional));
    }

    [Fact]
    public async Task C08_a_test_without_a_matching_band_shows_no_range_before_entry()
    {
        var world = new World();
        var test = world.AddTest("PSA", bands: [FakeCatalogue.Band(GenderRestriction.Male, 0m, 4m)]);
        world.Patients.Items[world.Patient] = FakePatients.Patient(world.Patient, "Female", 40);
        await world.OrderedAsync(test);

        var row = (await world.CaseAsync()).Units.Single().Results.Single();

        Assert.Equal((null, false), (row.Range, row.RangeProvisional));
    }

    [Fact]
    public async Task R_T2b_formulas_compute_from_their_inputs_in_the_same_save()
    {
        var world = new World();
        var a = world.AddTest("A");
        var b = world.AddTest("B");
        var total = world.AddTest("TOT", type: LabTestType.Formula, formula: "[A] + [B]", references: ["A", "B"]);
        var service = world.AddService(ServiceType.Profile, a.Id, b.Id, total.Id);
        await world.RegisterAsync(new RegisteredLine(Guid.CreateVersion7(), service.Id, world.Tube, "Ordered", []));
        world.Samples.Received(world.Tube, world.Visit);
        var byTest = world.Results.Items.ToDictionary(result => result.TestId);

        var outcome = await world.SaveAsync(new ResultValueItem(byTest[a.Id].Id, 1.2m, null, null, null), new ResultValueItem(byTest[b.Id].Id, null, "2.3", null, null));

        Assert.Empty(outcome.Refused);
        Assert.Contains(byTest[total.Id].Id, outcome.Changed);
        Assert.Equal((ResultStatus.Complete, 3.5m), (byTest[total.Id].Status, byTest[total.Id].NumericValue!.Value));
    }

    [Fact]
    public async Task R_T2_gate_a_tube_that_is_not_received_refuses_its_row_and_the_others_save()
    {
        var world = new World();
        var test = world.AddTest("GLU");
        var other = world.AddTest("UREA");
        var first = await world.OrderedAsync(test);
        var secondTube = Guid.CreateVersion7();
        await world.RegisterAsync(secondTube, new RegisteredLine(Guid.CreateVersion7(), world.AddService(ServiceType.Test, members: other.Id).Id, secondTube, "Ordered", []));
        var second = world.Results.Items.Single(result => result.TestId == other.Id);
        world.Samples.Received(secondTube, world.Visit);

        var outcome = await world.SaveAsync(new ResultValueItem(first.Id, 5m, null, null, null), new ResultValueItem(second.Id, 4m, null, null, null));

        Assert.Equal(ResultsCodes.SampleNotReceived, Assert.Single(outcome.Refused).Code);
        Assert.Equal([second.Id], outcome.Changed);
        Assert.Equal(ResultStatus.Ordered, first.Status);

        var single = await Assert.ThrowsAsync<DomainException>(() => world.EnterAsync(first.Id, 5m));
        Assert.Equal(ResultsCodes.SampleNotReceived, single.Code);
    }

    [Fact]
    public async Task R_T2_right_without_the_edit_right_entry_is_refused()
    {
        var world = new World();
        var result = await world.OrderedAsync(world.AddTest("GLU"));
        world.Samples.Received(world.Tube, world.Visit);
        world.Privileges.Rights.Clear();
        world.Privileges.Grant(world.Group, edit: false);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => world.EnterAsync(result.Id, 5m));
        Assert.Equal(ResultsCodes.NoEditRight, refusal.Code);
    }

    [Fact]
    public async Task R_T4_bulk_verify_reports_refusals_and_honours_review_before_verify()
    {
        var world = new World();
        var test = world.AddTest("K");
        var other = world.AddTest("NA");
        var service = world.AddService(ServiceType.Profile, test.Id, other.Id);
        await world.RegisterAsync(new RegisteredLine(Guid.CreateVersion7(), service.Id, world.Tube, "Ordered", []));
        world.Samples.Received(world.Tube, world.Visit);
        var withValue = world.Results.Items.Single(result => result.TestId == test.Id);
        var empty = world.Results.Items.Single(result => result.TestId == other.Id);
        await world.SaveAsync(new ResultValueItem(withValue.Id, 4m, null, null, null));

        world.Settings.Values[SettingKeys.ReviewBeforeVerify.Key] = "true";
        var held = await world.VerifyAsync(withValue.Id, empty.Id);
        Assert.Equal([(withValue.Id, ResultsCodes.ReviewRequired), (empty.Id, ResultsCodes.NoValue)], held.Refused.Select(refusal => (refusal.ResultId, refusal.Code)));

        world.Settings.Values[SettingKeys.ReviewBeforeVerify.Key] = "false";
        var verified = await world.VerifyAsync(withValue.Id);
        Assert.Equal([withValue.Id], verified.Changed);
        Assert.Equal(("Dr. Verifier", ResultStatus.Verified), (withValue.VerifiedByName, withValue.Status));
    }

    [Fact]
    public async Task R_BR11_A02_a_previous_result_verified_after_entry_flags_the_new_one_before_verification()
    {
        // Validation A-02 (2026-10-11): CREA 1.2 (ACC-24) and 2.0 (ACC-25) entered, then ACC-24 verified, then ACC-25.
        var world = new World();
        var creatinine = world.AddTest("CREA", deltaPercent: 20m);
        var older = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00024");
        var newer = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00025");
        await world.SaveAsync(new ResultValueItem(older.Id, 1.2m, null, null, null), new ResultValueItem(newer.Id, 2.0m, null, null, null));
        Assert.False(newer.DeltaFlag);

        await world.VerifyOneAsync(older.Id);
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(5);
        var commits = world.UnitOfWork.Commits;

        var refusal = await Assert.ThrowsAsync<DomainException>(() => world.VerifyOneAsync(newer.Id));
        Assert.Equal(ResultsCodes.DeltaNotAcknowledged, refusal.Code);
        Assert.Equal(commits + 1, world.UnitOfWork.Commits);   // the refreshed flag is saved with the refusal
        Assert.True(newer.DeltaFlag);
        Assert.Equal((66.7m, older.Id), (newer.DeltaPercent!.Value, newer.PreviousResultId!.Value));

        await world.AcknowledgeAsync(newer.Id);
        await world.VerifyOneAsync(newer.Id);
        Assert.Equal(ResultStatus.Verified, newer.Status);
    }

    [Fact]
    public async Task R_BR11_A02_bulk_verification_of_both_visits_flags_the_later_one_and_review_refreshes_the_flag()
    {
        var world = new World();
        var creatinine = world.AddTest("CREA", deltaPercent: 20m);
        var older = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00024");
        var newer = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00025");
        await world.SaveAsync(new ResultValueItem(older.Id, 1.2m, null, null, null), new ResultValueItem(newer.Id, 2.0m, null, null, null));

        var outcome = await world.VerifyAsync(older.Id, newer.Id);

        Assert.Equal([older.Id], outcome.Changed);
        var refused = Assert.Single(outcome.Refused);
        Assert.Equal((newer.Id, ResultsCodes.DeltaNotAcknowledged), (refused.ResultId, refused.Code));
        Assert.True(newer.DeltaFlag);

        // A third visit entered before the second was verified: review alone brings the flag forward.
        var third = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00026");
        await world.SaveAsync(new ResultValueItem(third.Id, 1.3m, null, null, null));
        Assert.Equal(1.2m, third.PreviousValue);
        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(1);
        await world.AcknowledgeAsync(newer.Id);
        await world.VerifyOneAsync(newer.Id);
        await world.ReviewOneAsync(third.Id);
        Assert.Equal((ResultStatus.Reviewed, newer.Id, true), (third.Status, third.PreviousResultId!.Value, third.DeltaFlag));
    }

    [Fact]
    public async Task R_BR11_A02_the_verified_policy_flags_the_patient_s_open_results_of_the_test()
    {
        var world = new World();
        var creatinine = world.AddTest("CREA", deltaPercent: 20m);
        var older = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00024");
        var newer = await world.OrderedInNewVisitAsync(creatinine, "ACC-2026-00025");
        await world.SaveAsync(new ResultValueItem(older.Id, 1.2m, null, null, null), new ResultValueItem(newer.Id, 2.0m, null, null, null));
        await world.VerifyOneAsync(older.Id);
        var verified = Assert.Single(older.DomainEvents.OfType<ResultVerified>());

        var policy = new RecheckDeltasOnResultVerifiedPolicy(world.Results, world.Catalogue, world.UnitOfWork, world.Clock);
        await policy.HandleAsync(verified, CancellationToken.None);

        Assert.True(newer.DeltaFlag);
        Assert.Equal(older.Id, newer.PreviousResultId);
        var commits = world.UnitOfWork.Commits;
        await policy.HandleAsync(verified, CancellationToken.None);
        Assert.Equal(commits, world.UnitOfWork.Commits);   // idempotent
    }

    [Fact]
    public async Task R_T9_a_cancelled_line_cancels_its_results_once()
    {
        var world = new World();
        var result = await world.OrderedAsync(world.AddTest("GLU"));
        var policy = new CancelResultsOnVisitLineCancelledPolicy(world.Results, world.UnitOfWork);
        var cancelled = new VisitLineCancelled(world.Visit, "ACC-1", result.LineId, result.LineServiceId, world.Tube, world.Actor, "Patient refused.", Registered);

        await policy.HandleAsync(cancelled, CancellationToken.None);
        await policy.HandleAsync(cancelled, CancellationToken.None);

        Assert.Equal((ResultStatus.Cancelled, "Patient refused."), (result.Status, result.CancellationReason));
        Assert.Equal(2, world.UnitOfWork.Commits);   // the registration, then the cancellation once
    }

    [Fact]
    public async Task R_PREVIEW_refused_while_a_value_is_missing_and_hidden_helpers_never_print()
    {
        var world = new World();
        var shown = world.AddTest("ALB");
        var helper = world.AddTest(".HLP", hidden: true);
        var service = world.AddService(ServiceType.Profile, shown.Id, helper.Id);
        await world.RegisterAsync(new RegisteredLine(Guid.CreateVersion7(), service.Id, world.Tube, "Ordered", []));
        world.Samples.Received(world.Tube, world.Visit);
        var key = ReportUnits.ForGroup(world.Visit, world.Group);
        var albumin = world.Results.Items.Single(result => result.TestId == shown.Id);
        await world.SaveAsync(new ResultValueItem(albumin.Id, 4m, null, null, null));

        world.Privileges.Special = new SpecialRights(null, true, true, true, true, true, false, true, true);
        var missing = await Assert.ThrowsAsync<DomainException>(() => world.ReportAsync(key, preview: true));
        Assert.Equal(ResultsCodes.PreviewIncomplete, missing.Code);

        world.Privileges.Special = SpecialRights.All;
        var preview = await world.ReportAsync(key, preview: true);
        var line = Assert.Single(Assert.Single(preview.Sections).Lines);
        Assert.Equal((shown.ReportName ?? shown.NameEn, "4.0"), (line.NameEn, line.Value));
        Assert.True(preview.IsPreliminary);
        Assert.Equal("ACC-2026-00009", preview.QrValue);

        var print = await Assert.ThrowsAsync<DomainException>(() => world.ReportAsync(key, preview: false));
        Assert.Equal(ResultsCodes.NotPrintable, print.Code);
    }

    [Fact]
    public async Task The_worklist_masks_a_VIP_name_and_asks_for_no_VIP_name_match_without_the_See_VIP_right()
    {
        // Validation 2026-10-11 (A-03 extended to CP-18): the worklist showed VIP names and found them by name.
        var group = Guid.CreateVersion7();
        var results = new InMemoryResultRepository();
        results.WorklistRows.Add(Row(group, "PAT-2026-00010", "V14 Walk-in Two", "ووك إن", isVip: true));
        results.WorklistRows.Add(Row(group, "PAT-2026-00009", "Mona Hassan", "منى حسن", isVip: false));
        var privileges = new FakePrivileges { Special = new SpecialRights(10, true, false, false, false, false, false, false, false) }.Grant(group);
        var handler = new GetWorklistHandler(results, new NoAnalysers(), new ResultsSettings(), privileges, new ResultsUser(Guid.CreateVersion7(), null), new ResultsClock(DateTimeOffset.UnixEpoch.AddYears(56)));
        var query = new GetWorklistQuery(null, null, null, null, null, null, null, null, false, "V14", false, null, null);

        var masked = await handler.Handle(query, CancellationToken.None);

        Assert.False(results.LastWorklistFilter!.IncludeVip);
        Assert.Equal(("PAT-2026-00010", "VIP patient", null, true), (masked.Items[0].PatientNumber, masked.Items[0].PatientName, masked.Items[0].PatientNameAr, masked.Items[0].PatientMasked));
        Assert.Equal(("Mona Hassan", false), (masked.Items[1].PatientName, masked.Items[1].PatientMasked));

        privileges.Special = SpecialRights.All;
        var shown = await handler.Handle(query, CancellationToken.None);
        Assert.True(results.LastWorklistFilter!.IncludeVip);
        Assert.Equal(("V14 Walk-in Two", "ووك إن", false), (shown.Items[0].PatientName, shown.Items[0].PatientNameAr, shown.Items[0].PatientMasked));
    }

    [Fact]
    public async Task Results_by_accession_lists_a_VIP_visit_masked_and_asks_for_no_VIP_name_match_without_the_See_VIP_right()
    {
        // A-03 extended to CP-19 (coordinator decision 2026-10-12): the matching list showed the VIP name.
        var group = Guid.CreateVersion7();
        var results = new InMemoryResultRepository();
        var patients = new FakePatients();
        var vip = FakePatients.Patient(Guid.CreateVersion7(), "Female", 30) with { Number = "PAT-2026-00010", FullName = "V14 Walk-in Two", NameAr = "ووك إن", IsVip = true };
        var plain = FakePatients.Patient(Guid.CreateVersion7(), "Female", 30) with { Number = "PAT-2026-00009", FullName = "Mona Hassan", NameAr = "منى حسن" };
        patients.Items[vip.Id] = vip;
        patients.Items[plain.Id] = plain;
        results.VisitRows.Add(new ResultVisitRow(Guid.CreateVersion7(), "ACC-2026-00027", DateTimeOffset.UnixEpoch, vip.Id, Guid.CreateVersion7(), ResultStatus.Ordered, 2, 1, false, false, false));
        results.VisitRows.Add(new ResultVisitRow(Guid.CreateVersion7(), "ACC-2026-00026", DateTimeOffset.UnixEpoch, plain.Id, Guid.CreateVersion7(), ResultStatus.Ordered, 1, 1, false, false, false));
        var privileges = new FakePrivileges { Special = new SpecialRights(10, true, false, false, false, false, false, false, false) }.Grant(group);
        var handler = new SearchResultVisitsHandler(results, patients, privileges, new ResultsUser(Guid.CreateVersion7(), null));
        var query = new SearchResultVisitsQuery(null, null, "walk", null, null, null, false, false, false, null, null, false, null, null);

        var masked = await handler.Handle(query, CancellationToken.None);

        Assert.False(results.LastVisitFilter!.IncludeVip);
        Assert.Equal(("PAT-2026-00010", "VIP patient", null, true), (masked.Items[0].PatientNumber, masked.Items[0].PatientName, masked.Items[0].PatientNameAr, masked.Items[0].PatientMasked));
        Assert.Equal(("Mona Hassan", "منى حسن", false), (masked.Items[1].PatientName, masked.Items[1].PatientNameAr, masked.Items[1].PatientMasked));

        privileges.Special = SpecialRights.All;
        var shown = await handler.Handle(query, CancellationToken.None);
        Assert.True(results.LastVisitFilter!.IncludeVip);
        Assert.Equal(("V14 Walk-in Two", false), (shown.Items[0].PatientName, shown.Items[0].PatientMasked));
    }

    [Fact]
    public async Task F06_an_opened_VIP_case_reads_VIP_patient_without_the_See_VIP_right()
    {
        // Validation v0.5.0 F-06: opening a VIP visit on Results by accession showed the real name to uat.tech.
        var world = new World();
        world.MakeVipPatient();
        await world.OrderedAsync(world.AddTest("GLU"));
        world.Privileges.Special = NoSeeVip;

        var masked = (await world.CaseAsync()).Patient;

        Assert.Equal(("PAT-2026-00010", "VIP patient", null, true, true), (masked.Number, masked.FullName, masked.NameAr, masked.IsVip, masked.IsMasked));
        Assert.Null(masked.DateOfBirth);
        Assert.Equal(("Female", 30), (masked.Gender, masked.AgeYears!.Value));   // the bench keeps sex and age (ranges, formulas)

        world.Privileges.Special = SpecialRights.All;
        var shown = (await world.CaseAsync()).Patient;
        Assert.Equal(("V14 Walk-in Two", "ووك إن", false), (shown.FullName, shown.NameAr, shown.IsMasked));
    }

    [Fact]
    public async Task F06_a_VIP_report_previews_masked_and_does_not_print_without_the_See_VIP_right()
    {
        var world = new World();
        world.MakeVipPatient();
        var result = await world.OrderedAsync(world.AddTest("GLU"));
        world.Samples.Received(world.Tube, world.Visit);
        await world.SaveAsync(new ResultValueItem(result.Id, 5m, null, null, null));
        await world.VerifyOneAsync(result.Id);
        var key = ReportUnits.ForGroup(world.Visit, world.Group);
        world.Privileges.Special = NoSeeVip;

        var preview = await world.ReportAsync(key, preview: true);
        Assert.Equal(("PAT-2026-00010", "VIP patient", null, true), (preview.Patient.Number, preview.Patient.FullName, preview.Patient.NameAr, preview.Patient.IsMasked));

        var copy = await Assert.ThrowsAsync<DomainException>(() => world.ReportAsync(key, preview: false));
        Assert.Equal((ResultsCodes.NoSeeVipToPrint, DomainFault.Refused), (copy.Code, copy.Fault));
        var commits = world.UnitOfWork.Commits;
        var print = await Assert.ThrowsAsync<DomainException>(() => world.PrintAsync(key));
        Assert.Equal(ResultsCodes.NoSeeVipToPrint, print.Code);
        Assert.Equal((0, commits, ResultStatus.Verified), (result.PrintCount, world.UnitOfWork.Commits, result.Status));

        world.Privileges.Special = SpecialRights.All;
        await world.PrintAsync(key);
        var official = await world.ReportAsync(key, preview: false);
        Assert.Equal(("V14 Walk-in Two", false, 1), (official.Patient.FullName, official.Patient.IsMasked, result.PrintCount));
    }

    [Fact]
    public async Task F06_a_unit_the_user_cannot_see_answers_RES_405_before_the_VIP_rule()
    {
        var world = new World();
        world.MakeVipPatient();
        await world.OrderedAsync(world.AddTest("GLU"));
        world.Privileges.Rights.Clear();
        world.Privileges.Special = NoSeeVip;

        var refusal = await Assert.ThrowsAsync<DomainException>(() => world.PrintAsync(ReportUnits.ForGroup(world.Visit, world.Group)));
        Assert.Equal(ResultsCodes.ReportUnitNotFound, refusal.Code);
    }

    [Fact]
    public async Task F15_the_worklist_colour_and_the_delayed_filter_use_the_tenant_late_threshold()
    {
        // Validation v0.5.0 F-15: with a 2 % late threshold red rows showed "Delayed 0" and "Delayed only" was empty.
        var group = Guid.CreateVersion7();
        var results = new InMemoryResultRepository();
        results.WorklistRows.Add(Row(group, "PAT-2026-00009", "Mona Hassan", "منى حسن", isVip: false));
        var settings = new ResultsSettings();
        settings.Values[SettingKeys.DelayWarnPercent.Key] = "1";
        settings.Values[SettingKeys.DelayLatePercent.Key] = "2";
        var privileges = new FakePrivileges().Grant(group);
        var now = DateTimeOffset.UnixEpoch.AddYears(1);   // about 1.75 % of the row's 57-year turnaround: floor 1 %
        var handler = new GetWorklistHandler(results, new NoAnalysers(), settings, privileges, new ResultsUser(Guid.CreateVersion7(), null), new ResultsClock(now));
        var query = new GetWorklistQuery(null, null, null, null, null, null, null, null, true, null, false, null, null);

        var page = await handler.Handle(query, CancellationToken.None);

        Assert.Equal((2, true), (results.LastWorklistFilter!.LatePercent, results.LastWorklistFilter.DelayedOnly));
        var row = Assert.Single(page.Items);
        Assert.Equal((1m, "Warning"), (row.ConsumedPercent, row.DelayState));
        Assert.False(TurnaroundClock.IsLate(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(57), now, 2));
        Assert.Equal((1, 2), (page.WarnPercent, page.LatePercent));

        settings.Values[SettingKeys.DelayLatePercent.Key] = "1";
        var late = Assert.Single((await handler.Handle(query, CancellationToken.None)).Items);
        Assert.Equal("Late", late.DelayState);
        Assert.True(TurnaroundClock.IsLate(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(57), now, 1));
        Assert.Equal(1, results.LastWorklistFilter!.LatePercent);
    }

    private static readonly SpecialRights NoSeeVip = new(null, true, true, true, true, true, true, true, false);

    private static WorklistRow Row(Guid group, string number, string name, string nameAr, bool isVip) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "ACC-2026-00001", Guid.CreateVersion7(), number, name, nameAr, null, null, Guid.CreateVersion7(), "GLU", "Glucose", "جلوكوز", group, "Chemistry", "الكيمياء",
            ResultStatus.Ordered, null, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(57), null, false, null, null, 0, ResultSource.Manual, isVip);

    private sealed class NoAnalysers : IResultAnalyserDirectory
    {
        public Task<IReadOnlyList<Guid>> MappedTestIdsAsync(Guid analyserId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<AnalyserOption>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AnalyserOption>>([]);
    }

    [Fact]
    public void Visibility_combines_every_branch_and_branch_specific_rights()
    {
        var privileges = new FakePrivileges();
        var everywhere = Guid.CreateVersion7();
        var local = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        privileges.Grant(everywhere);
        privileges.Rights.Add(new ResultRight(local, branch, true, false, false, false, false));

        var visibility = ResultRights.VisibilityOf(privileges);

        Assert.True(visibility.CanSee(everywhere, Guid.CreateVersion7()));
        Assert.True(visibility.CanSee(local, branch));
        Assert.False(visibility.CanSee(local, Guid.CreateVersion7()));
        Assert.False(ResultRights.VisibilityOf(new FakePrivileges()).CanSee(local, branch));
    }

    /// <summary>One laboratory, one patient, one visit and its tube, with every port in memory.</summary>
    private sealed class World
    {
        public Guid Group { get; } = Guid.CreateVersion7();

        public Guid Branch { get; } = Guid.CreateVersion7();

        public Guid Patient { get; } = Guid.CreateVersion7();

        public Guid Visit { get; } = Guid.CreateVersion7();

        public Guid Tube { get; } = Guid.CreateVersion7();

        public Guid Actor { get; } = Guid.CreateVersion7();

        public FakeCatalogue Catalogue { get; } = new();

        public InMemoryResultRepository Results { get; } = new();

        public FakePatients Patients { get; } = new();

        public FakeSampleGates Samples { get; } = new();

        public FakePrivileges Privileges { get; } = new();

        public ResultsSettings Settings { get; } = new();

        public ResultsUnitOfWork UnitOfWork { get; } = new();

        public FakeVisits Visits { get; } = new();

        public ResultsClock Clock { get; } = new(Registered.AddHours(2));

        public World()
        {
            Privileges.Grant(Group);
            Patients.Items[Patient] = FakePatients.Patient(Patient, "Male", 45);
        }

        private ResultsUser User => new(Actor, Branch);

        public CatalogueTestInfo AddTest(string code, LabTestType type = LabTestType.Numeric, IReadOnlyList<CatalogueRangeBandInfo>? bands = null, string? formula = null, IReadOnlyList<string>? references = null, bool hidden = false, decimal? deltaPercent = null)
        {
            var test = FakeCatalogue.Test(code, Group, type, 1, bands, formula, references, hidden, deltaPercent);
            Catalogue.With(test);
            return test;
        }

        public CatalogueServiceInfo AddService(ServiceType type, params Guid[] members) => AddService(type, false, members);

        public CatalogueServiceInfo AddService(ServiceType type, bool printAlone, params Guid[] members)
        {
            var service = FakeCatalogue.Service(Group, type, members.Select(id => (ServiceMemberKind.Test, id)), printAlone);
            Catalogue.With(service);
            return service;
        }

        public Task RegisterAsync(params RegisteredLine[] lines) => RegisterAsync(Tube, lines);

        public Task RegisterAsync(Guid tube, params RegisteredLine[] lines) => RegisterVisitAsync(Visit, "ACC-2026-00009", tube, lines);

        /// <summary>Another visit of the same patient (its own tube, received), with one result of <paramref name="test"/>.</summary>
        public async Task<Result> OrderedInNewVisitAsync(CatalogueTestInfo test, string accession)
        {
            var visit = Guid.CreateVersion7();
            var tube = Guid.CreateVersion7();
            await RegisterVisitAsync(visit, accession, tube, new RegisteredLine(Guid.CreateVersion7(), AddService(ServiceType.Test, test.Id).Id, tube, "Ordered", []));
            Samples.Received(tube, visit);
            return Results.Items.Single(result => result.VisitId == visit && result.TestId == test.Id);
        }

        public Task RegisterVisitAsync(Guid visit, string accession, Guid tube, params RegisteredLine[] lines)
        {
            var services = lines.Select(line => line.ServiceId).ToList();
            var registered = new VisitRegistered(visit, accession, Branch, Patient, null, null, false, Actor, [new RegisteredTube(tube, "SMP-1", Group, Guid.CreateVersion7(), null, services)], lines, Registered);
            return new CreateResultsOnVisitRegisteredPolicy(Results, Catalogue, Samples, Visits, UnitOfWork).HandleAsync(registered, CancellationToken.None);
        }

        public async Task<Result> OrderedAsync(CatalogueTestInfo test)
        {
            await RegisterAsync(new RegisteredLine(Guid.CreateVersion7(), AddService(ServiceType.Test, test.Id).Id, Tube, "Ordered", []));
            return Results.Items.Single(result => result.TestId == test.Id);
        }

        public Task<BulkResultOutcome> SaveAsync(params ResultValueItem[] values) =>
            new SaveResultValuesHandler(Results, Catalogue, Patients, Samples, new FakeReferences(), new FormulaEvaluator(), Privileges, new FakeDirectory(), UnitOfWork, User, Clock)
                .Handle(new SaveResultValuesCommand(values), CancellationToken.None);

        public Task EnterAsync(Guid resultId, decimal value) =>
            new EnterResultValueHandler(Results, Catalogue, Patients, Samples, new FakeReferences(), new FormulaEvaluator(), Privileges, new FakeDirectory(), UnitOfWork, User, Clock)
                .Handle(new EnterResultValueCommand(resultId, value, null, null, null), CancellationToken.None);

        public Task<BulkResultOutcome> VerifyAsync(params Guid[] ids) =>
            new VerifyResultsHandler(Results, Catalogue, Settings, Privileges, new FakeDirectory(), UnitOfWork, User, Clock).Handle(new VerifyResultsCommand(ids), CancellationToken.None);

        public Task VerifyOneAsync(Guid id) =>
            new VerifyResultHandler(Results, Catalogue, Settings, Privileges, new FakeDirectory(), UnitOfWork, User, Clock).Handle(new VerifyResultCommand(id), CancellationToken.None);

        public Task ReviewOneAsync(Guid id) =>
            new ReviewResultHandler(Results, Catalogue, Privileges, new FakeDirectory(), UnitOfWork, User, Clock).Handle(new ReviewResultCommand(id), CancellationToken.None);

        public Task AcknowledgeAsync(Guid id) =>
            new AcknowledgeDeltaHandler(Results, Catalogue, Privileges, new FakeDirectory(), UnitOfWork, User, Clock).Handle(new AcknowledgeDeltaCommand(id, "Checked."), CancellationToken.None);

        public void MakeVipPatient() =>
            Patients.Items[Patient] = FakePatients.Patient(Patient, "Female", 30) with { Number = "PAT-2026-00010", FullName = "V14 Walk-in Two", NameAr = "ووك إن", DateOfBirth = new DateOnly(1996, 1, 1), IsVip = true };

        public Task<ResultCaseView> CaseAsync() =>
            new GetResultCaseHandler(Results, Samples, Catalogue, Patients, new FakeDirectory(), Privileges, Visits, new FakeReferences(), new NoDoctors(), Settings, Clock)
                .Handle(new GetResultCaseQuery(null, Visit, null), CancellationToken.None);

        public Task PrintAsync(string key) =>
            new PrintReportUnitHandler(Results, Settings, Privileges, new FakeDirectory(), Patients, UnitOfWork, User, Clock).Handle(new PrintReportUnitCommand(key), CancellationToken.None);

        public Task<LaboratoryReportDocument> ReportAsync(string key, bool preview) =>
            new GetReportDocumentHandler(Results, Catalogue, Samples, new FakeDirectory(), Patients, Visits, new FakeReferences(), new NoDoctors(), new NoTemplates(), Settings, Privileges, User, Clock)
                .Handle(new GetReportDocumentQuery(key, preview), CancellationToken.None);
    }

    private sealed class NoDoctors : IDoctorLookup
    {
        public Task<DoctorView?> GetAsync(Guid doctorId, CancellationToken cancellationToken) => Task.FromResult<DoctorView?>(null);

        public Task<IReadOnlyList<DoctorView>> GetManyAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DoctorView>>([]);

        public Task<bool> IsSelectableAsync(Guid doctorId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoTemplates : IReportTemplateLookup
    {
        public Task<ReportTemplateView?> GetAsync(Guid templateId, CancellationToken cancellationToken) => Task.FromResult<ReportTemplateView?>(null);

        public Task<IReadOnlyList<ReportTemplateView>> ActiveOfTypeAsync(ReportTemplateType type, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReportTemplateView>>([]);
    }
}
