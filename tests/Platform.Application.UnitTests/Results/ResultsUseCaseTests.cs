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
