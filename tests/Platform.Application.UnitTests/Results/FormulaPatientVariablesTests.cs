using Platform.Application.Catalogue.Services;
using Platform.Application.Results.Commands;
using Platform.Application.Results.Policies;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.Domain.Results;

namespace Platform.Application.UnitTests.Results;

/// <summary>
/// T2b with the patient variables (validation v0.5.0, MDRD eGFR): a formula reads @AGE (completed years) and @FEMALE from
/// the patient file; when the age or the sex is unknown it waits, like a formula whose input test has no value yet.
/// </summary>
public sealed class FormulaPatientVariablesTests
{
    private const string Mdrd = "ROUND(175 * [CREA] ^ -1.154 * @AGE ^ -0.203 * IF(@FEMALE, 0.742, 1), 0)";

    private static readonly DateTimeOffset Registered = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Registered.UtcDateTime);

    [Theory]
    [InlineData("Female", 30, 53)]   // 175 × 1.2^-1.154 × 30^-0.203 × 0.742 = 52.75
    [InlineData("Male", 45, 65)]     // 175 × 1.2^-1.154 × 45^-0.203 = 65.47
    public async Task eGFR_computes_from_creatinine_with_the_age_and_sex_of_the_patient(string gender, int years, int expected)
    {
        var lab = new Lab(FakePatients.Patient(Guid.Empty, gender, years) with { DateOfBirth = Today.AddYears(-years), DateOfBirthIsEstimated = false });
        var (creatinine, egfr) = await lab.OrderRenalAsync();

        var outcome = await lab.SaveAsync(new ResultValueItem(creatinine.Id, 1.2m, null, null, null));

        Assert.Contains(egfr.Id, outcome.Changed);
        Assert.Equal((ResultStatus.Complete, (decimal?)expected), (egfr.Status, egfr.NumericValue));
    }

    [Fact]
    public async Task A_patient_without_age_leaves_the_formula_waiting()
    {
        var lab = new Lab(FakePatients.Patient(Guid.Empty, "Female", 0));   // no date of birth, no stated age
        var (creatinine, egfr) = await lab.OrderRenalAsync();

        var outcome = await lab.SaveAsync(new ResultValueItem(creatinine.Id, 1.2m, null, null, null));

        Assert.Equal([creatinine.Id], outcome.Changed);
        Assert.Equal((ResultStatus.Ordered, (decimal?)null), (egfr.Status, egfr.NumericValue));
    }

    [Fact]
    public async Task A_patient_of_unknown_sex_leaves_the_formula_waiting()
    {
        var lab = new Lab(FakePatients.Patient(Guid.Empty, "Unknown", 40) with { DateOfBirth = Today.AddYears(-40) });
        var (creatinine, egfr) = await lab.OrderRenalAsync();

        await lab.SaveAsync(new ResultValueItem(creatinine.Id, 1.2m, null, null, null));

        Assert.Equal((ResultStatus.Ordered, (decimal?)null), (egfr.Status, egfr.NumericValue));
    }

    /// <summary>One visit of one patient with a RENAL profile (CREA + eGFR) on one received tube.</summary>
    private sealed class Lab
    {
        private readonly Guid _group = Guid.CreateVersion7();
        private readonly Guid _branch = Guid.CreateVersion7();
        private readonly Guid _patient = Guid.CreateVersion7();
        private readonly Guid _visit = Guid.CreateVersion7();
        private readonly Guid _tube = Guid.CreateVersion7();
        private readonly Guid _actor = Guid.CreateVersion7();
        private readonly FakeCatalogue _catalogue = new();
        private readonly InMemoryResultRepository _results = new();
        private readonly FakePatients _patients = new();
        private readonly FakeSampleGates _samples = new();
        private readonly FakePrivileges _privileges = new();
        private readonly ResultsUnitOfWork _unitOfWork = new();
        private readonly ResultsClock _clock = new(Registered.AddHours(2));

        public Lab(Contracts.Patients.PatientSummary patient)
        {
            _privileges.Grant(_group);
            _patients.Items[_patient] = patient with { Id = _patient };
        }

        public async Task<(Result Creatinine, Result Egfr)> OrderRenalAsync()
        {
            var creatinine = FakeCatalogue.Test("CREA", _group, decimals: 2);
            var egfr = FakeCatalogue.Test("EGFR", _group, LabTestType.Formula, decimals: 0, formula: Mdrd, references: ["CREA"]);
            _catalogue.With(creatinine).With(egfr);
            var renal = FakeCatalogue.Service(_group, ServiceType.Profile, [(ServiceMemberKind.Test, creatinine.Id), (ServiceMemberKind.Test, egfr.Id)]);
            _catalogue.With(renal);
            var line = new RegisteredLine(Guid.CreateVersion7(), renal.Id, _tube, "Ordered", []);
            var registered = new VisitRegistered(_visit, "ACC-2026-00009", _branch, _patient, null, null, false, _actor, [new RegisteredTube(_tube, "SMP-1", _group, Guid.CreateVersion7(), null, [renal.Id])], [line], Registered);
            await new CreateResultsOnVisitRegisteredPolicy(_results, _catalogue, _samples, new FakeVisits(), _unitOfWork).HandleAsync(registered, CancellationToken.None);
            _samples.Received(_tube, _visit);
            return (_results.Items.Single(result => result.TestId == creatinine.Id), _results.Items.Single(result => result.TestId == egfr.Id));
        }

        public Task<BulkResultOutcome> SaveAsync(params ResultValueItem[] values) =>
            new SaveResultValuesHandler(_results, _catalogue, _patients, _samples, new FakeReferences(), new FormulaEvaluator(), _privileges, new FakeDirectory(), _unitOfWork, new ResultsUser(_actor, _branch), _clock)
                .Handle(new SaveResultValuesCommand(values), CancellationToken.None);
    }
}
