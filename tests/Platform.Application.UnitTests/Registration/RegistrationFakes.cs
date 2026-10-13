using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Application.Registration;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Contracts.Patients;
using Platform.Contracts.Pricing;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Registration;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.UnitTests.Registration;

// In-memory stand-ins for the ports the Registration handlers use: a small catalogue (chemistry on serum, haematology on
// whole blood, a male-only PSA, a package, an additional service), a cash contract, rules, preparations and reasons.
// The engine side lives in tests/Platform.IntegrationTests/RegistrationModuleTests.cs.

internal sealed class RegistrationWorld
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);

    public RegistrationWorld()
    {
        Catalogue = new FakeCatalogue();
        Prices = new FakePrices(Catalogue);
        Patients.Add(Female);
        Patients.Add(Male);
        Payers.AddContract(PayerId, TierId);
        Payers.AddContract(InsurerId, InsurerTierId);
        Prices.Splits[InsurerTierId] = (20m, 80m);
        User.UserId = Guid.CreateVersion7();
        User.BranchId = BranchId;
    }

    public Guid BranchId { get; } = Guid.CreateVersion7();

    public Guid PayerId { get; } = Guid.CreateVersion7();

    public Guid TierId { get; } = Guid.CreateVersion7();

    public Guid InsurerId { get; } = Guid.CreateVersion7();

    public Guid InsurerTierId { get; } = Guid.CreateVersion7();

    public PatientSummary Female { get; } = Patient("Female");

    public PatientSummary Male { get; } = Patient("Male");

    public FakeCatalogue Catalogue { get; }

    public FakePrices Prices { get; }

    public FakeRules Rules { get; } = new();

    public FakePatients Patients { get; } = new();

    public FakePayers Payers { get; } = new();

    public FakeDoctors Doctors { get; } = new();

    public FakeRegistrationSettings Settings { get; } = new();

    public FakeRegistrationPrivileges Privileges { get; } = new();

    public SequenceNumbers Numbers { get; } = new();

    public InMemoryVisits Visits { get; } = new();

    public InMemoryBookings Bookings { get; } = new();

    public CommitCounter UnitOfWork { get; } = new();

    public FakeRegistrationUser User { get; } = new();

    public FakeResults Results { get; } = new();

    public FakeUserDirectory Users { get; } = new();

    public IClock Clock { get; set; } = new FixedRegistrationClock(Now);

    public static PatientSummary Patient(string gender, string status = "Active", string? mobile = "01001234567") =>
        new(Guid.CreateVersion7(), "PAT-2026-00001", gender == "Male" ? "Mr" : "Mrs", "Test Patient", null, gender, null, true, new PatientAgeView(40, 0, 0), null, false, mobile, null, status, null);
}

internal sealed class FakeCatalogue : ICatalogueLookup
{
    public static readonly Guid Chemistry = Guid.CreateVersion7();
    public static readonly Guid Haematology = Guid.CreateVersion7();
    public static readonly Guid Serum = Guid.CreateVersion7();
    public static readonly Guid WholeBlood = Guid.CreateVersion7();

    public FakeCatalogue()
    {
        Glucose = Add("GLU", ServiceType.Test, Chemistry, Serum, test: Test("GLU", frequencyDays: 30), precaution: "Fasting 8 hours");
        Urea = Add("UREA", ServiceType.Test, Chemistry, Serum, test: Test("UREA"));
        Cbc = Add("CBC", ServiceType.Test, Haematology, WholeBlood, test: Test("CBC"));
        Hba1c = Add("HBA1C", ServiceType.Test, Haematology, WholeBlood, test: Test("HBA1C", frequencyDays: 90, action: FrequencyCheckAction.Prevent));
        Psa = Add("PSA", ServiceType.Test, Chemistry, Serum, test: Test("PSA"), gender: GenderRestriction.Male);
        HomeVisit = Add("HOME", ServiceType.Additional, null, null);
        Inactive = Add("OLD", ServiceType.Test, Chemistry, Serum, test: Test("OLD"), active: false);
        Unpriced = Add("NOPRICE", ServiceType.Test, Chemistry, Serum, test: Test("NOPRICE"));
        Package = Add("CHK", ServiceType.Package, null, null, members: [Urea.Id, Cbc.Id]);
        Creatinine = Add("CREA", ServiceType.Test, Chemistry, Serum, test: Test("CREA", frequencyDays: 30));
        Renal = Add("RENAL", ServiceType.Profile, Chemistry, Serum, tests: [Creatinine.Members[0].MemberId, Tests.Values.Single(test => test.Code == "UREA").Id]);
        Wellness = Add("WELL", ServiceType.Package, null, null, members: [Renal.Id, Glucose.Id]);
    }

    /// <summary>A package holding the RENAL profile and GLU (validation O-3: a test inside a profile inside a package).</summary>
    public CatalogueServiceInfo Wellness { get; }

    /// <summary>A single-test service whose test is also a member of <see cref="Renal"/> (validation A-11).</summary>
    public CatalogueServiceInfo Creatinine { get; }

    /// <summary>A profile of the CREA and UREA tests.</summary>
    public CatalogueServiceInfo Renal { get; }

    public CatalogueServiceInfo Glucose { get; }

    public CatalogueServiceInfo Urea { get; }

    public CatalogueServiceInfo Cbc { get; }

    public CatalogueServiceInfo Hba1c { get; }

    public CatalogueServiceInfo Psa { get; }

    public CatalogueServiceInfo HomeVisit { get; }

    public CatalogueServiceInfo Inactive { get; }

    public CatalogueServiceInfo Unpriced { get; }

    public CatalogueServiceInfo Package { get; }

    public Dictionary<Guid, CatalogueServiceInfo> Services { get; } = [];

    public Dictionary<Guid, CatalogueTestInfo> Tests { get; } = [];

    public Task<CatalogueServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken) => Task.FromResult(Services.GetValueOrDefault(serviceId));

    public Task<IReadOnlyList<CatalogueServiceInfo>> GetServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueServiceInfo>>([.. serviceIds.Where(Services.ContainsKey).Select(id => Services[id])]);

    public Task<CatalogueTestInfo?> GetTestAsync(Guid testId, CancellationToken cancellationToken) => Task.FromResult(Tests.GetValueOrDefault(testId));

    public Task<IReadOnlyList<CatalogueTestInfo>> GetTestsAsync(IReadOnlyCollection<Guid> testIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueTestInfo>>([.. testIds.Where(Tests.ContainsKey).Select(id => Tests[id])]);

    public Task<IReadOnlyList<CatalogueTestInfo>> GetTestsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CatalogueClinicalGroupInfo?> GetClinicalGroupAsync(Guid clinicalGroupId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<CatalogueClinicalGroupInfo>> GetClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueClinicalGroupInfo>>(
        [
            new(Chemistry, "CHEM", "C", "Chemistry", "الكيمياء", null, null, 1, true),
            new(Haematology, "HEMA", "H", "Haematology", "أمراض الدم", null, null, 2, true),
        ]);

    public Task<IReadOnlyList<CatalogueSampleTypeInfo>> GetSampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueSampleTypeInfo>>(
        [
            new(Serum, "SER", "Serum", "Serum", "مصل", true, []),
            new(WholeBlood, "WB", "WB", "Whole blood", "دم كامل", true, []),
        ]);

    public Task<CatalogueRangeBandInfo?> ResolveRangeAsync(Guid testId, GenderRestriction sex, PatientAge age, Guid? patientConditionId, CancellationToken cancellationToken) => throw new NotSupportedException();

    private CatalogueTestInfo Test(string code, int? frequencyDays = null, FrequencyCheckAction action = FrequencyCheckAction.Alert)
    {
        var test = new CatalogueTestInfo(
            Guid.CreateVersion7(), code, code, code, null, LabTestType.Numeric, Chemistry, null, null, null, null, GenderRestriction.Any, null, 2, false, true, 1, null,
            new LabTestTiming(24, 0, frequencyDays, action, null, null, LabTestTiming.EveryDay), null, [], [], [], [], true);
        Tests[test.Id] = test;
        return test;
    }

    private CatalogueServiceInfo Add(
        string code,
        ServiceType type,
        Guid? group,
        Guid? sampleType,
        CatalogueTestInfo? test = null,
        GenderRestriction gender = GenderRestriction.Any,
        string? precaution = null,
        bool active = true,
        IReadOnlyList<Guid>? members = null,
        IReadOnlyList<Guid>? tests = null)
    {
        var memberInfos = test is not null
            ? [new CatalogueServiceMemberInfo(ServiceMemberKind.Test, test.Id, 1, true, true)]
            : tests is not null
                ? tests.Select((id, index) => new CatalogueServiceMemberInfo(ServiceMemberKind.Test, id, index + 1, true, true)).ToList()
                : (members ?? []).Select((id, index) => new CatalogueServiceMemberInfo(ServiceMemberKind.Service, id, index + 1, true, true)).ToList();
        var service = new CatalogueServiceInfo(
            Guid.CreateVersion7(), code, code, null, code + " en", code + " ar", null, type, group, sampleType, null, gender, 0, 1, precaution, false, true, true, null, Services.Count + 1, active, memberInfos);
        Services[service.Id] = service;
        return service;
    }
}

internal sealed class FakePrices(FakeCatalogue catalogue) : IPriceLookup
{
    public string? AlertFor { get; set; }

    /// <summary>Patient / payer split of a tier; every other contract prices 100 / 0.</summary>
    public Dictionary<Guid, (decimal Patient, decimal Payer)> Splits { get; } = [];

    public Task<IReadOnlyList<PriceQuoteLine>> QuoteAsync(Guid? payerId, Guid? contractTierId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var (patient, payer) = contractTierId is { } tier && Splits.TryGetValue(tier, out var split) ? split : (100m, 0m);
        return Task.FromResult<IReadOnlyList<PriceQuoteLine>>([.. serviceIds.Distinct().Select(id =>
            id == catalogue.Unpriced.Id
                ? new PriceQuoteLine(id, 0m, 0m, 0m, null, false)
                : new PriceQuoteLine(id, 100m, patient, payer, AlertFor == catalogue.Services[id].Code ? "Needs referral" : null, true))]);
    }
}

internal sealed class FakeRules : IOrderRulesLookup
{
    public List<(IReadOnlyList<Guid> Triggers, Guid Added)> Rules { get; } = [];

    public Dictionary<Guid, DiagnosisServices> Diagnoses { get; } = [];

    public List<PreparationQuestion> Preparations { get; } = [];

    public Dictionary<Guid, ReasonOption> Reasons { get; } = [];

    public Task<IReadOnlyList<RuleAddedService>> ServicesAddedByRulesAsync(IReadOnlyCollection<Guid> orderedServiceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RuleAddedService>>([.. Rules
            .Where(rule => rule.Triggers.All(orderedServiceIds.Contains) && !orderedServiceIds.Contains(rule.Added))
            .Select(rule => new RuleAddedService(Guid.CreateVersion7(), "Companion rule", "قاعدة", rule.Added))]);

    public Task<DiagnosisServices?> DiagnosisAsync(Guid diagnosisId, CancellationToken cancellationToken) => Task.FromResult(Diagnoses.GetValueOrDefault(diagnosisId));

    public Task<IReadOnlyList<PreparationQuestion>> PreparationsForAsync(IReadOnlyCollection<Guid> serviceIds, PreparationGender? patientGender, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PreparationQuestion>>([.. Preparations.Where(question => question.ServiceIds.Count == 0 || question.ServiceIds.Any(serviceIds.Contains))]);

    public Task<ShortcutSetServices?> ShortcutSetByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult<ShortcutSetServices?>(null);

    public Task<IReadOnlyList<ReasonOption>> ReasonsAsync(ReasonKind kind, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReasonOption>>([.. Reasons.Values.Where(reason => reason.Kind == kind)]);

    public Task<ReasonOption?> ReasonAsync(Guid reasonId, ReasonKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(Reasons.TryGetValue(reasonId, out var reason) && reason.Kind == kind ? reason : null);

    public Guid AddReason(ReasonKind kind, string name)
    {
        var id = Guid.CreateVersion7();
        Reasons[id] = new ReasonOption(id, kind, name, name);
        return id;
    }
}

internal sealed class FakePatients : IPatientLookup
{
    private readonly Dictionary<Guid, PatientSummary> _patients = [];

    public void Add(PatientSummary patient) => _patients[patient.Id] = patient;

    public Task<PatientSummary?> GetAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(_patients.GetValueOrDefault(patientId));

    public Task<IReadOnlyDictionary<Guid, PatientSummary>> GetManyAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PatientSummary>>(patientIds.Where(_patients.ContainsKey).ToDictionary(id => id, id => _patients[id]));
}

internal sealed class FakePayers : IPayerLookup
{
    private readonly Dictionary<Guid, PayerSummary> _payers = [];
    private readonly Dictionary<Guid, ContractTierSummary> _tiers = [];

    public void AddContract(Guid payerId, Guid tierId, bool active = true)
    {
        _payers[payerId] = new PayerSummary(payerId, "Payer", "جهة", "P" + payerId.ToString("N")[..4], "Cash", active, false);
        _tiers[tierId] = new ContractTierSummary(tierId, payerId, "Cash", "Default", "افتراضي", true, active, true);
    }

    public Task<PayerSummary?> FindPayerAsync(Guid payerId, CancellationToken cancellationToken) => Task.FromResult(_payers.GetValueOrDefault(payerId));

    public Task<ContractTierSummary?> FindTierAsync(Guid contractTierId, CancellationToken cancellationToken) => Task.FromResult(_tiers.GetValueOrDefault(contractTierId));

    public Task<IReadOnlyList<ContractTierSummary>> ListTiersAsync(Guid payerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContractTierSummary>>([.. _tiers.Values.Where(tier => tier.PayerId == payerId)]);

    public Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<DefaultPayerIds?>(null);
}

internal sealed class FakeDoctors : IDoctorLookup
{
    public HashSet<Guid> Selectable { get; } = [];

    public Task<DoctorView?> GetAsync(Guid doctorId, CancellationToken cancellationToken) => Task.FromResult<DoctorView?>(null);

    public Task<IReadOnlyList<DoctorView>> GetManyAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DoctorView>>([]);

    public Task<bool> IsSelectableAsync(Guid doctorId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(Selectable.Contains(doctorId));
}

internal sealed class FakeRegistrationSettings : ITenantSettings
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public FakeRegistrationSettings With<T>(SettingDescriptor<T> descriptor, string value)
    {
        Values[descriptor.Key] = value;
        return this;
    }

    public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(descriptor.Parse(Values.GetValueOrDefault(descriptor.Key)));

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(Values);
}

internal sealed class FakeRegistrationPrivileges : IUserPrivileges
{
    public SpecialRights SpecialRights { get; set; } = SpecialRights.All;

    public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ResultRight> ResultRights { get; } = [];

    public bool Has(string permissionKey) => true;

    public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
}

internal sealed class SequenceNumbers : IReferenceNumbers
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        return Task.FromResult($"{prefix}-{nowUtc.Year}-{_counters[kind]:00000}");
    }
}

internal sealed class CommitCounter : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeRegistrationUser : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; set; }

    public Guid? SessionId => null;

    public string? DisplayName => "Reception Clerk";

    public Guid? BranchId { get; set; }
}

internal sealed class FixedRegistrationClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class FakeResults : IResultStatusLookup
{
    public HashSet<Guid> Verified { get; } = [];

    public Task<bool> HasVerifiedResultsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken) => Task.FromResult(lineIds.Any(Verified.Contains));
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    public Dictionary<Guid, UserNameItem> Names { get; } = [];

    public Task<IReadOnlyDictionary<Guid, UserNameItem>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserNameItem>>(userIds.Where(Names.ContainsKey).ToDictionary(id => id, id => Names[id]));
}

internal sealed class InMemoryVisits : IVisitRepository
{
    public List<Visit> Items { get; } = [];

    public Task<Visit?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(visit => visit.Id == id));

    public void Add(Visit aggregate) => Items.Add(aggregate);

    public Task<Visit?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) => GetForUpdateAsync(id, cancellationToken);

    public Task<Guid?> FindIdByAccessionAsync(string accession, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(visit => visit.Accession == accession)?.Id);

    public Task<IReadOnlyList<Visit>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Visit>>([.. Items.Where(visit => visit.PatientId == patientId)]);

    public Task<IReadOnlyList<PriorTestOrder>> PriorTestOrdersAsync(Guid patientId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, Guid? excludeVisitId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PriorTestOrder>>([.. Items
            .Where(visit => visit.PatientId == patientId && visit.Id != excludeVisitId)
            .SelectMany(visit => visit.Lines.Where(line => line.IsLive && line.OrderedAtUtc >= sinceUtc).SelectMany(line => line.TestIds.Where(testIds.Contains).Select(test => new PriorTestOrder(test, visit.Id, visit.Accession, line.OrderedAtUtc))))]);

    /// <summary>The last register filter asked (the engine filters are tested in the integration suite); answers no rows.</summary>
    public VisitSearchFilter? LastFilter { get; private set; }

    public Task<Paged<VisitRow>> SearchAsync(VisitSearchFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult(new Paged<VisitRow>([], page.Page, page.PageSize, 0));
    }

    public Task<IReadOnlyList<VisitRow>> SearchAllAsync(VisitSearchFilter filter, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> VisitOrdinalAsync(Guid patientId, DateTimeOffset registeredAtUtc, CancellationToken cancellationToken) => Task.FromResult(Items.Count(visit => visit.PatientId == patientId && visit.RegisteredAtUtc <= registeredAtUtc));

    public Task<IReadOnlyList<VisitRow>> ListByBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<VisitRow>>([]);

    public Task<Paged<CancelledLineRow>> CancelledLinesAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<CancelledLineRow>> CancelledLinesAllAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Paged<TransferRow>> TransfersAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, VisitTransferKind? kind, Guid? visitId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class InMemoryBookings : IBookingRepository
{
    public List<Booking> Items { get; } = [];

    public Task<Booking?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(booking => booking.Id == id));

    public void Add(Booking aggregate) => Items.Add(aggregate);

    public Task<Booking?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) => GetForUpdateAsync(id, cancellationToken);

    public Task<Paged<Booking>> ListAsync(BookingFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Booking>> ListAllAsync(BookingFilter filter, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Booking>> ListBookedByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Booking>>([.. Items.Where(booking => booking.PatientId == patientId && booking.Status == BookingStatus.Booked)]);
}
