using Platform.Application.Catalogue;
using Platform.Application.Registration;
using Platform.Application.Registration.Commands;
using Platform.Application.Registration.Policies;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Registration;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Registration;

/// <summary>The use cases of intake LIS-INT-REG-001 over in-memory ports: the order planner rules and the handlers' preconditions.</summary>
public sealed class RegistrationUseCaseTests
{
    private readonly RegistrationWorld _world = new();

    private FakeCatalogue Catalogue => _world.Catalogue;

    [Theory]
    [InlineData("010 1919 9912", 10, "1019199912")]
    [InlineData("1019199912", 10, "1019199912")]
    [InlineData("01019199912", null, "01019199912")]
    public async Task F08_the_visit_register_searches_a_mobile_without_its_trunk_0_for_the_laboratory_country(string typed, int? length, string searched)
    {
        // Validation v0.5.0 F-08: a file stored as 1019199912 was not found by 01019199912 (CP-10 mobile search).
        var visits = new InMemoryVisits();
        var country = new FixedLaboratoryCountry(length is { } national ? new Platform.Application.Patients.LaboratoryCountry("EG", national) : null);
        var handler = new Platform.Application.Registration.Queries.SearchVisitsHandler(visits, null!, new FakePatients(), null!, new FakePayers(), null!, new FakeRegistrationPrivileges(), country);

        await handler.Handle(new Platform.Application.Registration.Queries.SearchVisitsQuery(null, null, null, typed, null, null, null, null, null, null, null, 1, 20), CancellationToken.None);

        Assert.Equal(searched, visits.LastFilter!.Mobile);
    }

    private sealed class FixedLaboratoryCountry(Platform.Application.Patients.LaboratoryCountry? country) : Platform.Application.Patients.ILaboratoryCountry
    {
        public Task<Platform.Application.Patients.LaboratoryCountry?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(country);
    }

    [Fact]
    public async Task S_V1_ok_registers_with_tubes_numbers_warnings_and_one_commit()
    {
        _world.Prices.AlertFor = "CBC";

        var response = await Register(_world.Female.Id, Catalogue.Glucose.Id, Catalogue.Urea.Id, Catalogue.Cbc.Id);

        Assert.Equal(("ACC-2026-00001", 2), (response.Accession, response.TubeCount));
        Assert.Equal(1, _world.UnitOfWork.Commits);
        var visit = Assert.Single(_world.Visits.Items);
        Assert.Equal((_world.BranchId, VisitLocationType.Walkin, 300m), (visit.BranchId, visit.LocationType, visit.PatientShareTotal));
        Assert.Equal(["SMP-2026-00001", "SMP-2026-00002"], visit.Tubes.Select(tube => tube.SampleNumber).Order().ToArray());
        Assert.Equal("C", visit.Tubes.Single(tube => tube.ClinicalGroupId == FakeCatalogue.Chemistry).UnitCode);
        Assert.Contains(response.Warnings, warning => warning.Code == RegistrationCodes.WarningPrecaution && warning.ServiceCode == "GLU");
        Assert.Contains(response.Warnings, warning => warning.Code == RegistrationCodes.WarningContractAlert && warning.Detail == "Needs referral");
        Assert.All(visit.Lines, line => Assert.Equal(RegistrationWorld.Now.AddHours(24), line.ExpectedResultAtUtc));
    }

    [Fact]
    public async Task S_V1_refusals_unknown_inactive_unpriced_gender_duplicate()
    {
        Assert.Equal(RegistrationCodes.ServiceUnknown, (await Refused(() => Register(_world.Female.Id, Guid.CreateVersion7()))).Code);
        Assert.Equal(RegistrationCodes.ServiceNotOrderable, (await Refused(() => Register(_world.Female.Id, Catalogue.Inactive.Id))).Code);
        Assert.Equal(RegistrationCodes.ServiceUnpriced, (await Refused(() => Register(_world.Female.Id, Catalogue.Unpriced.Id))).Code);

        var gender = await Refused(() => Register(_world.Female.Id, Catalogue.Psa.Id));
        Assert.Equal((RegistrationCodes.ServiceGenderRestricted, DomainFault.Validation), (gender.Code, gender.Fault));

        var duplicate = await Refused(() => Register(_world.Female.Id, Catalogue.Urea.Id, Catalogue.Package.Id));
        Assert.Equal((RegistrationCodes.DuplicateService, DomainFault.Conflict), (duplicate.Code, duplicate.Fault));
        Assert.Empty(_world.Visits.Items);
        Assert.Equal(0, _world.UnitOfWork.Commits);

        await Register(_world.Male.Id, Catalogue.Psa.Id);
        Assert.Single(_world.Visits.Items);
    }

    [Fact]
    public async Task S_V1_freq_alert_needs_confirmation_and_prevent_refuses()
    {
        await Register(_world.Female.Id, Catalogue.Glucose.Id, Catalogue.Hba1c.Id);
        _world.Clock = new FixedRegistrationClock(RegistrationWorld.Now.AddDays(10));

        var unconfirmed = await Refused(() => Register(_world.Female.Id, Catalogue.Glucose.Id));
        Assert.Equal((RegistrationCodes.FrequencyConfirmationRequired, DomainFault.Conflict), (unconfirmed.Code, unconfirmed.Fault));

        var confirmed = await Register(_world.Female.Id, confirm: true, Catalogue.Glucose.Id);
        Assert.Contains(confirmed.Warnings, warning => warning.Code == RegistrationCodes.WarningFrequency && warning.LastAccession == "ACC-2026-00001" && warning.WindowDays == 30);

        var prevented = await Refused(() => Register(_world.Female.Id, confirm: true, Catalogue.Hba1c.Id));
        Assert.Equal(RegistrationCodes.FrequencyPrevented, prevented.Code);

        _world.Clock = new FixedRegistrationClock(RegistrationWorld.Now.AddDays(95));
        await Register(_world.Female.Id, confirm: true, Catalogue.Hba1c.Id);
        Assert.Equal(3, _world.Visits.Items.Count);
    }

    [Fact]
    public async Task S_V1_freq_alert_names_the_repeated_member_test_and_the_profile_it_came_through()
    {
        // Validation A-11 (2026-10-11): CREA ordered alone, then RENAL (CREA + UREA): the alert named RENAL only.
        await Register(_world.Female.Id, Catalogue.Creatinine.Id);
        _world.Clock = new FixedRegistrationClock(RegistrationWorld.Now.AddMinutes(1));

        var unconfirmed = await Refused(() => Register(_world.Female.Id, Catalogue.Renal.Id));
        Assert.Equal(RegistrationCodes.FrequencyConfirmationRequired, unconfirmed.Code);
        Assert.StartsWith("'CREA (RENAL)' was ordered", unconfirmed.Message, StringComparison.Ordinal);

        var confirmed = await Register(_world.Female.Id, confirm: true, Catalogue.Renal.Id);
        var alert = Assert.Single(confirmed.Warnings, warning => warning.Code == RegistrationCodes.WarningFrequency);
        Assert.Equal(("CREA (RENAL)", "CREA", Catalogue.Renal.Id, "ACC-2026-00001"), (alert.ServiceCode, alert.Detail, alert.ServiceId!.Value, alert.LastAccession));

        // A single-test service keeps its own code.
        var again = await Register(_world.Female.Id, confirm: true, Catalogue.Creatinine.Id);
        Assert.Equal("CREA", Assert.Single(again.Warnings, warning => warning.Code == RegistrationCodes.WarningFrequency).ServiceCode);
    }

    [Fact]
    public async Task S_V1_a_service_whose_every_test_is_already_ordered_is_refused_naming_the_test_and_its_service()
    {
        // Validation O-3 (2026-10-11): V14-CREA was accepted next to V14-PKG ⊃ V14-RFT ⊃ CREA and charged again.
        var inProfile = await Refused(() => Register(_world.Female.Id, Catalogue.Renal.Id, Catalogue.Creatinine.Id));
        Assert.Equal((RegistrationCodes.DuplicateTests, DomainFault.Conflict), (inProfile.Code, inProfile.Fault));
        Assert.Equal("Every test of 'CREA' is already on this visit: CREA (RENAL).", inProfile.Message);

        var inPackage = await Refused(() => Register(_world.Female.Id, Catalogue.Wellness.Id, Catalogue.Creatinine.Id));
        Assert.Equal("Every test of 'CREA' is already on this visit: CREA (WELL › RENAL).", inPackage.Message);
        Assert.Empty(_world.Visits.Items);

        var planner = new OrderPlanner(Catalogue, _world.Rules, _world.Prices, _world.Visits);
        var plan = await planner.PlanAsync(
            new OrderContext(null, _world.PayerId, _world.TierId, null, [new OrderLineRequest(Catalogue.Wellness.Id, false), new OrderLineRequest(Catalogue.Creatinine.Id, false)], new HashSet<Guid>(), null, RegistrationWorld.Now, 0),
            CancellationToken.None);
        var issue = Assert.Single(plan.Issues);
        Assert.Equal((RegistrationCodes.DuplicateTests, Catalogue.Creatinine.Id, "CREA", "CREA (WELL › RENAL)"), (issue.Code, issue.ServiceId!.Value, issue.ServiceCode, issue.Detail));
        Assert.Equal(["WELL"], plan.Lines.Select(line => line.Item.ServiceCode).ToArray());
    }

    [Fact]
    public async Task S_V1_a_service_that_adds_a_new_test_is_allowed_with_a_warning_naming_the_repeated_tests()
    {
        var response = await Register(_world.Female.Id, Catalogue.Creatinine.Id, Catalogue.Renal.Id, Catalogue.HomeVisit.Id);

        var warning = Assert.Single(response.Warnings, candidate => candidate.Code == RegistrationCodes.WarningRepeatedTests);
        Assert.Equal((Catalogue.Renal.Id, "RENAL", "CREA"), (warning.ServiceId!.Value, warning.ServiceCode, warning.Detail));
        Assert.Equal(3, _world.Visits.Items.Single().Lines.Count);

        // A package whose profile repeats UREA (already ordered alone) but adds CREA and GLU: allowed; the warning names the
        // repeated test where it is already ordered (the UREA service itself, so its code alone).
        var package = await Register(_world.Male.Id, Catalogue.Urea.Id, Catalogue.Wellness.Id);
        var repeat = Assert.Single(package.Warnings, candidate => candidate.Code == RegistrationCodes.WarningRepeatedTests);
        Assert.Equal(("WELL", "UREA"), (repeat.ServiceCode, repeat.Detail));
    }

    [Fact]
    public async Task S_V2_adding_a_service_whose_tests_are_on_the_saved_visit_is_refused_and_the_check_names_them()
    {
        await Register(_world.Female.Id, Catalogue.Wellness.Id);
        var visit = _world.Visits.Items.Single();
        Assert.Equal("WELL › RENAL", visit.LiveTestHolders()[Catalogue.Creatinine.Members[0].MemberId]);

        var refused = await Refused(() => AddLinesHandler().Handle(new AddVisitLinesCommand(visit.Id, [new OrderLineRequest(Catalogue.Creatinine.Id, false)], null, false), CancellationToken.None));
        Assert.Equal(RegistrationCodes.DuplicateTests, refused.Code);
        Assert.Contains("CREA (WELL › RENAL)", refused.Message, StringComparison.Ordinal);

        var added = await AddLinesHandler().Handle(new AddVisitLinesCommand(visit.Id, [new OrderLineRequest(Catalogue.Cbc.Id, false)], null, false), CancellationToken.None);
        Assert.DoesNotContain(added.Warnings, warning => warning.Code == RegistrationCodes.WarningRepeatedTests);
    }

    [Fact]
    public async Task S_V1_rule_and_diagnosis_add_services_and_unorderable_companions_are_skipped()
    {
        _world.Rules.Rules.Add(([Catalogue.Glucose.Id, Catalogue.Urea.Id], Catalogue.Cbc.Id));
        _world.Rules.Rules.Add(([Catalogue.Glucose.Id], Catalogue.Unpriced.Id));
        var diagnosis = Guid.CreateVersion7();
        _world.Rules.Diagnoses[diagnosis] = new DiagnosisServices(diagnosis, "E11", "Diabetes", "سكري", [Catalogue.Urea.Id]);

        var response = await Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], diagnosisId: diagnosis), CancellationToken.None);

        var visit = Assert.Single(_world.Visits.Items);
        Assert.Equal(["GLU", "UREA", "CBC"], visit.Lines.Select(line => line.ServiceCode).ToArray());
        Assert.Equal(VisitLineOrigin.Diagnosis, visit.Lines[1].Origin);
        Assert.Equal(VisitLineOrigin.Rule, visit.Lines[2].Origin);
        Assert.Contains(response.Warnings, warning => warning.Code == RegistrationCodes.WarningAddedByRule && warning.ServiceCode == "CBC");
        Assert.Contains(response.Warnings, warning => warning.Code == RegistrationCodes.WarningAddedByDiagnosis && warning.ServiceCode == "UREA");
        Assert.Contains(response.Warnings, warning => warning.Code == RegistrationCodes.WarningSkippedCompanion && warning.ServiceCode == "NOPRICE");
    }

    [Fact]
    public async Task S_V1_discount_uses_the_role_cap_and_a_discount_reason()
    {
        var reason = _world.Rules.AddReason(ReasonKind.Discount, "Staff");
        var wrongKind = _world.Rules.AddReason(ReasonKind.Cancellation, "Error");
        _world.Privileges.SpecialRights = new SpecialRights(5, true, true, true, true, false, false, false, false);

        var above = await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], discount: new VisitDiscountRequest("Percent", 10m, reason)), CancellationToken.None));
        Assert.Equal((RegistrationCodes.DiscountAboveCap, DomainFault.Refused), (above.Code, above.Fault));
        Assert.Equal(RegistrationCodes.DiscountReasonInvalid, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], discount: new VisitDiscountRequest("Percent", 5m, wrongKind)), CancellationToken.None))).Code);

        await Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], discount: new VisitDiscountRequest("percent", 5m, reason)), CancellationToken.None);
        var visit = Assert.Single(_world.Visits.Items);
        Assert.Equal((5m, 95m, "Staff"), (visit.DiscountTotal, visit.PatientShareTotal, visit.DiscountReason));
    }

    [Fact]
    public async Task S_V1_mandatory_settings_branch_and_merged_file()
    {
        _world.Settings.With(SettingKeys.MandatoryDoctor, "true");
        Assert.Equal(RegistrationCodes.DoctorRequired, (await Refused(() => Register(_world.Female.Id, Catalogue.Glucose.Id))).Code);
        var doctor = Guid.CreateVersion7();
        Assert.Equal(RegistrationCodes.DoctorNotSelectable, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], doctor: doctor), CancellationToken.None))).Code);
        _world.Settings.With(SettingKeys.MandatoryDoctor, "false");

        var noMobile = RegistrationWorld.Patient("Female", mobile: null);
        _world.Patients.Add(noMobile);
        _world.Settings.With(SettingKeys.MandatoryMobile, "true");
        Assert.Equal(RegistrationCodes.MobileRequired, (await Refused(() => Register(noMobile.Id, Catalogue.Glucose.Id))).Code);

        var merged = RegistrationWorld.Patient("Female", status: "Merged");
        _world.Patients.Add(merged);
        Assert.Equal(RegistrationCodes.PatientMerged, (await Refused(() => Register(merged.Id, Catalogue.Glucose.Id))).Code);
        Assert.Equal(RegistrationCodes.PatientNotFound, (await Refused(() => Register(Guid.CreateVersion7(), Catalogue.Glucose.Id))).Code);

        _world.User.BranchId = null;
        Assert.Equal(ProblemCodes.BranchRequired, (await Refused(() => Register(_world.Female.Id, Catalogue.Glucose.Id))).Code);
    }

    [Fact]
    public async Task S_V1_contract_must_be_an_active_tier_of_the_payer()
    {
        var command = Command(_world.Female.Id, [Catalogue.Glucose.Id]) with { ContractTierId = _world.InsurerTierId };
        Assert.Equal(RegistrationCodes.TierInvalid, (await Refused(() => Handler().Handle(command, CancellationToken.None))).Code);
        Assert.Equal(RegistrationCodes.PayerInvalid, (await Refused(() => Handler().Handle(command with { PayerId = Guid.CreateVersion7() }, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task S_V1_preparation_answers_are_checked_against_the_applicable_questions()
    {
        var fasting = new PreparationQuestion(Guid.CreateVersion7(), "FAST", "Fasting?", "صائم؟", PreparationAnswerType.YesNo, [], PreparationGender.Any, [Catalogue.Glucose.Id]);
        var hours = new PreparationQuestion(Guid.CreateVersion7(), "HRS", "Hours?", "ساعات؟", PreparationAnswerType.OptionList, ["8 h", "12 h"], PreparationGender.Any, [Catalogue.Glucose.Id]);
        var other = new PreparationQuestion(Guid.CreateVersion7(), "OTH", "Other?", "أخرى؟", PreparationAnswerType.Text, [], PreparationGender.Any, [Catalogue.Cbc.Id]);
        _world.Rules.Preparations.AddRange([fasting, hours, other]);

        Assert.Equal(RegistrationCodes.PreparationInvalid, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], answers: [new(fasting.PreparationId, "maybe")]), CancellationToken.None))).Code);
        Assert.Equal(RegistrationCodes.PreparationInvalid, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], answers: [new(hours.PreparationId, "6 h")]), CancellationToken.None))).Code);
        Assert.Equal(RegistrationCodes.PreparationInvalid, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], answers: [new(other.PreparationId, "x")]), CancellationToken.None))).Code);

        await Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], answers: [new(fasting.PreparationId, "yes"), new(hours.PreparationId, "12 H")]), CancellationToken.None);
        Assert.Equal(["Yes", "12 h"], _world.Visits.Items.Single().PreparationAnswers.Select(answer => answer.Answer).ToArray());
    }

    [Fact]
    public async Task S_B4_converting_a_booking_registers_a_home_visit_with_the_phlebotomist_as_collector()
    {
        var phlebotomist = Guid.CreateVersion7();
        var booking = Booking.Create("BKG-2026-00001", _world.BranchId, new BookingContent(new BookingPatient(_world.Female.Id, null, null, null, null), "Nile St", RegistrationWorld.Now, phlebotomist, _world.PayerId, _world.TierId, [Catalogue.Glucose.Id], null), RegistrationWorld.Now);
        _world.Bookings.Add(booking);

        await Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id]) with { BookingId = booking.Id }, CancellationToken.None);

        var visit = Assert.Single(_world.Visits.Items);
        Assert.Equal((VisitLocationType.Home, booking.Id, phlebotomist), (visit.LocationType, visit.BookingId!.Value, visit.Lines[0].CollectorUserId!.Value));
        Assert.Equal((BookingStatus.Converted, visit.Id), (booking.Status, booking.VisitId!.Value));

        Assert.Equal(RegistrationCodes.BookingNotBooked, (await Refused(() => Handler().Handle(Command(_world.Female.Id, [Catalogue.Urea.Id]) with { BookingId = booking.Id }, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task S_V2_adding_services_skips_the_visit_itself_in_the_frequency_check_and_joins_tubes()
    {
        await Register(_world.Female.Id, Catalogue.Glucose.Id);
        var visit = _world.Visits.Items.Single();

        var added = await AddLinesHandler().Handle(new AddVisitLinesCommand(visit.Id, [new OrderLineRequest(Catalogue.Urea.Id, false)], null, false), CancellationToken.None);

        Assert.Equal((1, 0), (added.AddedLines, added.NewTubes));
        Assert.Equal(2, visit.Lines.Count);
        Assert.Single(visit.Tubes);
        Assert.Equal(RegistrationCodes.DuplicateService, (await Refused(() => AddLinesHandler().Handle(new AddVisitLinesCommand(visit.Id, [new OrderLineRequest(Catalogue.Glucose.Id, false)], null, false), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task S_V3_release_resolves_the_tube_from_the_catalogue()
    {
        await Handler().Handle(Command(_world.Female.Id, [Catalogue.Glucose.Id], reserved: [Catalogue.Cbc.Id]), CancellationToken.None);
        var visit = _world.Visits.Items.Single();
        var reserved = visit.Lines.Single(line => line.Status == VisitLineStatus.Reserved);

        await new ReleaseReservedLinesHandler(_world.Visits, Catalogue, _world.Numbers, _world.UnitOfWork, _world.User, _world.Clock).Handle(new ReleaseReservedLinesCommand(visit.Id, [reserved.Id]), CancellationToken.None);

        Assert.Equal(VisitLineStatus.Ordered, reserved.Status);
        Assert.Equal("H", visit.Tubes.Single(tube => tube.Id == reserved.TubeId).UnitCode);
    }

    [Fact]
    public async Task S_V4_cancel_needs_the_right_no_verified_result_and_a_reason()
    {
        await Register(_world.Female.Id, Catalogue.Glucose.Id, Catalogue.Cbc.Id);
        var visit = _world.Visits.Items.Single();
        var line = visit.Lines[0];
        var reason = _world.Rules.AddReason(ReasonKind.Cancellation, "Ordered by mistake");

        _world.Privileges.SpecialRights = SpecialRights.None;
        var right = await Refused(() => Cancel(visit.Id, line.Id, reason, null));
        Assert.Equal((RegistrationCodes.CancelRightRequired, DomainFault.Refused), (right.Code, right.Fault));
        _world.Privileges.SpecialRights = SpecialRights.All;

        _world.Results.Verified.Add(line.Id);
        var verified = await Refused(() => Cancel(visit.Id, line.Id, reason, null));
        Assert.Equal((RegistrationCodes.VerifiedResults, DomainFault.Conflict), (verified.Code, verified.Fault));
        _world.Results.Verified.Clear();

        Assert.Equal(RegistrationCodes.ReasonInvalid, (await Refused(() => Cancel(visit.Id, line.Id, _world.Rules.AddReason(ReasonKind.Discount, "x"), null))).Code);
        Assert.Equal(RegistrationCodes.CancellationReasonRequired, (await Refused(() => Cancel(visit.Id, line.Id, null, " "))).Code);

        await Cancel(visit.Id, line.Id, reason, "patient left");
        Assert.Equal((VisitLineStatus.Cancelled, "Ordered by mistake — patient left"), (line.Status, line.CancellationReason));
        Assert.Equal(100m, visit.PatientShareTotal);
    }

    [Fact]
    public async Task S_V7_transfer_needs_the_right_and_reprices_on_the_new_contract()
    {
        await Register(_world.Female.Id, Catalogue.Glucose.Id);
        var visit = _world.Visits.Items.Single();
        var handler = new TransferVisitContractHandler(_world.Visits, _world.Payers, _world.Prices, _world.Rules, _world.Privileges, _world.UnitOfWork, _world.User, _world.Clock);

        _world.Privileges.SpecialRights = SpecialRights.None;
        Assert.Equal(RegistrationCodes.TransferRightRequired, (await Refused(() => handler.Handle(new TransferVisitContractCommand(visit.Id, _world.InsurerId, _world.InsurerTierId, null, "Approval"), CancellationToken.None))).Code);
        _world.Privileges.SpecialRights = SpecialRights.All;

        await handler.Handle(new TransferVisitContractCommand(visit.Id, _world.InsurerId, _world.InsurerTierId, null, "Approval"), CancellationToken.None);
        Assert.Equal((_world.InsurerTierId, 20m, 80m), (visit.ContractTierId, visit.PatientShareTotal, visit.PayerShareTotal));
    }

    [Fact]
    public async Task S_V10_merge_policy_repoints_visits_and_bookings_once()
    {
        await Register(_world.Female.Id, Catalogue.Glucose.Id);
        var survivor = Guid.CreateVersion7();
        var policy = new RepointVisitsOnPatientsMergedPolicy(_world.Visits, _world.Bookings, _world.UnitOfWork, _world.Clock);
        var merged = new PatientsMerged(survivor, "PAT-2026-00002", _world.Female.Id, "PAT-2026-00001", Guid.CreateVersion7(), "Duplicate", RegistrationWorld.Now);
        var commits = _world.UnitOfWork.Commits;

        await policy.HandleAsync(merged, CancellationToken.None);
        await policy.HandleAsync(merged, CancellationToken.None);

        var visit = _world.Visits.Items.Single();
        Assert.Equal((survivor, _world.Female.Id), (visit.PatientId, visit.OriginalPatientId!.Value));
        Assert.Equal(commits + 1, _world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Planner_quote_keeps_unpriced_lines_and_ignores_frequency()
    {
        var planner = new OrderPlanner(Catalogue, _world.Rules, _world.Prices, _world.Visits);

        var plan = await planner.PlanAsync(
            new OrderContext(null, null, null, null, [new OrderLineRequest(Catalogue.Unpriced.Id, false), new OrderLineRequest(Catalogue.Package.Id, false)], new HashSet<Guid>(), null, RegistrationWorld.Now, 0, ForQuote: true),
            CancellationToken.None);

        Assert.Empty(plan.Issues);
        Assert.False(plan.Lines[0].Priced);
        var package = plan.Lines[1].Item;
        Assert.Equal((VisitLineKind.Package, 2), (package.Kind, package.Members.Count));
        Assert.Equal(RegistrationWorld.Now.AddHours(24), package.ExpectedResultAtUtc);
    }

    private Task<VisitRegisteredResponse> Register(Guid patientId, params Guid[] services) => Register(patientId, false, services);

    private Task<VisitRegisteredResponse> Register(Guid patientId, bool confirm, params Guid[] services) =>
        Handler().Handle(Command(patientId, services, confirm: confirm), CancellationToken.None);

    private Task Cancel(Guid visitId, Guid lineId, Guid? reasonId, string? reason) =>
        new CancelVisitLinesHandler(_world.Visits, _world.Results, _world.Rules, _world.Privileges, _world.UnitOfWork, _world.User, _world.Clock)
            .Handle(new CancelVisitLinesCommand(visitId, [lineId], reasonId, reason), CancellationToken.None);

    private RegisterVisitCommand Command(
        Guid patientId,
        IReadOnlyList<Guid> services,
        bool confirm = false,
        Guid? diagnosisId = null,
        VisitDiscountRequest? discount = null,
        Guid? doctor = null,
        IReadOnlyList<PreparationAnswerRequest>? answers = null,
        IReadOnlyList<Guid>? reserved = null) =>
        new(
            patientId,
            null,
            doctor,
            _world.PayerId,
            _world.TierId,
            null,
            diagnosisId,
            null,
            false,
            false,
            false,
            [.. services.Select(id => new OrderLineRequest(id, false)), .. (reserved ?? []).Select(id => new OrderLineRequest(id, true))],
            answers,
            discount,
            confirm,
            120);

    private RegisterVisitHandler Handler() =>
        new(_world.Visits, _world.Bookings, _world.Patients, _world.Payers, _world.Doctors, Catalogue, _world.Rules, _world.Prices, _world.Settings, _world.Privileges, _world.Numbers, _world.UnitOfWork, _world.User, _world.Clock);

    private AddVisitLinesHandler AddLinesHandler() =>
        new(_world.Visits, _world.Patients, Catalogue, _world.Rules, _world.Prices, _world.Numbers, _world.UnitOfWork, _world.User, _world.Clock);

    private static async Task<DomainException> Refused(Func<Task> action) => await Assert.ThrowsAnyAsync<DomainException>(action);
}
