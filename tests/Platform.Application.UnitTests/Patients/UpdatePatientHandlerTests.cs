using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Patients;
using Platform.Application.Patients.Commands;
using Platform.Application.Patients.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Patients;

/// <summary>
/// The demographics lock of T2 (change request 2026-10-13 item 1, intake LIS-INT-PAT-001 §17): an identity change beyond the
/// member's <c>DemographicsEditUpTo</c> right is refused with PAT-032, a contact-only edit and an identity edit within the
/// right go through, the results are read only when identity changes, and the lock query reports the same decision.
/// </summary>
public sealed class UpdatePatientHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid VisitId = Guid.CreateVersion7();

    [Fact]
    public async Task An_identity_change_beyond_the_right_is_refused_with_PAT_032_and_nothing_is_saved()
    {
        var world = World.With(limit: DemographicsEditLimit.Complete, highest: DemographicsEditLimit.Verified);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => world.Handler.Handle(Command(world.Patient, fullName: "Mona Hassan Ali"), CancellationToken.None));

        Assert.Equal((PatientCodes.DemographicsLockedByResults, DomainFault.Refused), (refusal.Code, refusal.Fault));
        Assert.Contains("Verified", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Complete", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.UnitOfWork.Commits);
        Assert.Equal("Mona Hassan", world.Patient.FullName);
        Assert.Equal((world.Patient.Id, VisitId), world.Results.LastAsked);
    }

    [Fact]
    public async Task A_contact_only_edit_goes_through_beyond_the_right_without_reading_the_results()
    {
        var world = World.With(limit: DemographicsEditLimit.Ordered, highest: DemographicsEditLimit.Printed);

        await world.Handler.Handle(Command(world.Patient, mobile: "01112223333", city: "Giza"), CancellationToken.None);

        Assert.Equal(1, world.UnitOfWork.Commits);
        Assert.Equal(("01112223333", "Giza"), (world.Patient.Mobile, world.Patient.City));
        Assert.Null(world.Results.LastAsked);
    }

    [Fact]
    public async Task An_identity_edit_within_the_right_goes_through()
    {
        var world = World.With(limit: DemographicsEditLimit.Verified, highest: DemographicsEditLimit.Verified);

        await world.Handler.Handle(Command(world.Patient, fullName: "Mona Hassan Ali"), CancellationToken.None);

        Assert.Equal(1, world.UnitOfWork.Commits);
        Assert.Equal("Mona Hassan Ali", world.Patient.FullName);
    }

    [Fact]
    public async Task Without_a_visit_the_lock_is_judged_on_the_patient()
    {
        var world = World.With(limit: DemographicsEditLimit.Complete, highest: DemographicsEditLimit.Complete);

        await world.Handler.Handle(Command(world.Patient, fullName: "Mona Hassan Ali") with { VisitId = null }, CancellationToken.None);

        Assert.Equal((world.Patient.Id, (Guid?)null), world.Results.LastAsked);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(DemographicsEditLimit.Complete, DemographicsEditLimit.Verified, false)]
    [InlineData(DemographicsEditLimit.Verified, DemographicsEditLimit.Verified, true)]
    [InlineData(DemographicsEditLimit.Ordered, DemographicsEditLimit.Ordered, true)]
    public async Task The_lock_query_reports_the_status_the_right_and_the_decision(DemographicsEditLimit limit, DemographicsEditLimit highest, bool canEdit)
    {
        var world = World.With(limit, highest);
        var handler = new GetPatientDemographicsLockHandler(world.Patients, world.Results, world.Privileges);

        var lockState = await handler.Handle(new GetPatientDemographicsLockQuery(world.Patient.Id, VisitId), CancellationToken.None);

        Assert.Equal(new PatientDemographicsLock(highest.ToString(), canEdit, limit.ToString()), lockState);
        var missing = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetPatientDemographicsLockQuery(Guid.CreateVersion7(), null), CancellationToken.None));
        Assert.Equal(PatientCodes.PatientNotFound, missing.Code);
    }

    private static UpdatePatientCommand Command(Patient patient, string fullName = "Mona Hassan", string? mobile = "01001234567", string? city = "Cairo") =>
        new(patient.Id, "Mrs", fullName, "منى حسن", "Female", new DateOnly(1990, 5, 1), null, null, mobile, "29005010101234", "EG", null, null, city, null, null, null, null, VisitId);

    private sealed class World
    {
        public required Patient Patient { get; init; }

        public required FakePatients Patients { get; init; }

        public required FakeResults Results { get; init; }

        public required FakePrivileges Privileges { get; init; }

        public required CountingUnitOfWork UnitOfWork { get; init; }

        public required UpdatePatientHandler Handler { get; init; }

        public static World With(DemographicsEditLimit limit, DemographicsEditLimit highest)
        {
            var patient = Patient.Register(
                "PAT-2026-00001",
                new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", "منى حسن", PatientGender.Female, new DateOnly(1990, 5, 1), null, null, "01001234567", "29005010101234", "EG", null, null, "Cairo", null, null),
                PatientFieldRules.Optional,
                false,
                Now);
            var patients = new FakePatients(patient);
            var results = new FakeResults(highest);
            var privileges = new FakePrivileges(new SpecialRights(null, false, false, false, false, false, false, false, false, limit, false, null));
            var unitOfWork = new CountingUnitOfWork();
            return new World
            {
                Patient = patient,
                Patients = patients,
                Results = results,
                Privileges = privileges,
                UnitOfWork = unitOfWork,
                Handler = new UpdatePatientHandler(patients, new DefaultSettings(), privileges, results, new NoZone(), new NoLocations(), unitOfWork, new FixedClock(Now)),
            };
        }
    }

    /// <summary>These commands pick no city, district or area: the chain check reads nothing (change request 2026-10-19, A2).</summary>
    private sealed class NoLocations : IPatientLocations
    {
        public Task<IReadOnlyDictionary<Guid, LocationNode>> CitiesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, LocationNode>> DistrictsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, LocationNode>> AreasAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeResults(DemographicsEditLimit highest) : IPatientResultStatusLookup
    {
        public (Guid PatientId, Guid? VisitId)? LastAsked { get; private set; }

        public Task<DemographicsEditLimit> HighestStatusAsync(Guid patientId, Guid? visitId, CancellationToken cancellationToken)
        {
            LastAsked = (patientId, visitId);
            return Task.FromResult(highest);
        }
    }

    private sealed class FakePrivileges(SpecialRights rights) : IUserPrivileges
    {
        public SpecialRights SpecialRights { get; } = rights;

        public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<ResultRight> ResultRights { get; } = [];

        public bool Has(string permissionKey) => false;   // the VIP flag is never sent by these commands

        public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
    }

    private sealed class FakePatients(Patient patient) : IPatientRepository
    {
        public Task<Patient?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == patient.Id ? patient : null);

        public void Add(Patient aggregate) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == patient.Id);

        public Task<Paged<PatientRow>> SearchAsync(PatientSearchFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PatientRow>> SearchAllAsync(PatientSearchFilter filter, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PatientRow?> GetRowAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PatientRow>> GetRowsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MergedPatientReference>> ListMergedIntoAsync(Guid survivorId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> MergedFamilyAsync(Guid survivorId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PatientRow>> FindSimilarCandidatesAsync(IReadOnlyList<string> nameTokens, IReadOnlyList<string> phones, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DefaultSettings : ITenantSettings
    {
        public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return ValueTask.FromResult(descriptor.Default);
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(SettingKeys.All.ToDictionary(setting => setting.Key, setting => setting.DefaultValue));
    }

    private sealed class NoZone : ILaboratoryTimeZone
    {
        public Task<TimeZoneInfo?> FindAsync(CancellationToken cancellationToken) => Task.FromResult<TimeZoneInfo?>(null);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Commits++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
