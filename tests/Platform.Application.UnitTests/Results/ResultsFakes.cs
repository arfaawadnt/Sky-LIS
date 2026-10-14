using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Registration;
using Platform.Application.Results;
using Platform.Application.Samples;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Contracts.Patients;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Results;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.UnitTests.Results;

// In-memory stand-ins for the ports the Results handlers and policies use, so the use-case logic is tested without a
// database (the engine side lives in tests/Platform.IntegrationTests/ResultsModuleTests.cs).

internal sealed class InMemoryResultRepository : IResultRepository
{
    public List<Result> Items { get; } = [];

    public Task<Result?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(result => result.Id == id));

    public void Add(Result aggregate) => Items.Add(aggregate);

    public Task<IReadOnlySet<(Guid LineId, Guid TestId)>> ExistingLineTestsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<(Guid LineId, Guid TestId)>>(Items.Where(result => lineIds.Contains(result.LineId)).Select(result => (result.LineId, result.TestId)).ToHashSet());

    public Task<IReadOnlyList<Result>> ListForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Many(result => ids.Contains(result.Id));

    public Task<IReadOnlyList<Result>> ListByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken) => Many(result => result.VisitId == visitId);

    public Task<IReadOnlyList<Result>> ListByLineForUpdateAsync(Guid lineId, CancellationToken cancellationToken) => Many(result => result.LineId == lineId);

    public Task<IReadOnlyList<Result>> ListByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken) => Many(result => result.TubeId == tubeId);

    public Task<IReadOnlyList<Result>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) => Many(result => result.PatientId == patientId);

    public Task<IReadOnlyList<Result>> ListByUnitsForUpdateAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken) => Many(result => reportUnitKeys.Contains(result.ReportUnitKey));

    public Task<IReadOnlyList<Result>> ReadByVisitAsync(Guid visitId, CancellationToken cancellationToken) => Many(result => result.VisitId == visitId);

    public Task<IReadOnlyList<Result>> ReadByUnitsAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken) => Many(result => reportUnitKeys.Contains(result.ReportUnitKey));

    public Task<Result?> ReadAsync(Guid id, CancellationToken cancellationToken) => GetForUpdateAsync(id, cancellationToken);

    public Task<Guid?> FindVisitByAccessionAsync(string accession, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(result => result.Accession == accession).Select(result => (Guid?)result.VisitId).FirstOrDefault());

    public Task<Guid?> FindVisitByTubeAsync(Guid tubeId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(result => result.TubeId == tubeId).Select(result => (Guid?)result.VisitId).FirstOrDefault());

    public Task<PreviousResultValue?> PreviousVerifiedAsync(Guid patientId, Guid testId, Guid excludingVisitId, DateTimeOffset? sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Items
            .Where(result => result.PatientId == patientId && result.TestId == testId && result.VisitId != excludingVisitId && ResultStatuses.IsSigned(result.Status) && result.NumericValue is not null)
            .Where(result => sinceUtc is null || result.VerifiedAtUtc >= sinceUtc)
            .OrderByDescending(result => result.VerifiedAtUtc)
            .Select(result => new PreviousResultValue(result.Id, result.NumericValue!.Value, result.VerifiedAtUtc!.Value))
            .FirstOrDefault());

    public Task<IReadOnlyList<Result>> ReadPreviousAsync(Guid patientId, Guid testId, Guid excludingResultId, int take, CancellationToken cancellationToken) =>
        Many(result => result.PatientId == patientId && result.TestId == testId && result.Id != excludingResultId);

    public Task<IReadOnlyList<ResultLogEntry>> ReadLogAsync(Guid resultId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ResultLogEntry>>([.. Items.Single(result => result.Id == resultId).Log]);

    /// <summary>The rows the worklist answers (the engine filters are tested in the integration suite); the last filter asked.</summary>
    public List<WorklistRow> WorklistRows { get; } = [];

    public WorklistFilter? LastWorklistFilter { get; private set; }

    public Task<WorklistSlice> WorklistAsync(WorklistFilter filter, ResultVisibility visibility, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        LastWorklistFilter = filter;
        return Task.FromResult(new WorklistSlice([.. WorklistRows], WorklistRows.Count, WorklistRows.Count, WorklistRows.Count, 0));
    }

    /// <summary>The rows the CP-19 search answers (the engine filters are tested in the integration suite); the last filter asked.</summary>
    public List<ResultVisitRow> VisitRows { get; } = [];

    public ResultVisitFilter? LastVisitFilter { get; private set; }

    public Task<Paged<ResultVisitRow>> SearchVisitsAsync(ResultVisitFilter filter, ResultVisibility visibility, PageRequest page, CancellationToken cancellationToken)
    {
        LastVisitFilter = filter;
        return Task.FromResult(new Paged<ResultVisitRow>([.. VisitRows], page.Page, page.PageSize, VisitRows.Count));
    }

    public Task<bool> AnyVerifiedAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(result => lineIds.Contains(result.LineId) && ResultStatuses.IsSigned(result.Status)));

    private Task<IReadOnlyList<Result>> Many(Func<Result, bool> predicate) => Task.FromResult<IReadOnlyList<Result>>([.. Items.Where(predicate)]);
}

internal sealed class FakeCatalogue : ICatalogueLookup
{
    public Dictionary<Guid, CatalogueTestInfo> Tests { get; } = [];

    public Dictionary<Guid, CatalogueServiceInfo> Services { get; } = [];

    public Dictionary<Guid, CatalogueClinicalGroupInfo> Groups { get; } = [];

    public static CatalogueTestInfo Test(
        string code,
        Guid groupId,
        LabTestType type = LabTestType.Numeric,
        int decimals = 1,
        IReadOnlyList<CatalogueRangeBandInfo>? bands = null,
        string? formula = null,
        IReadOnlyList<string>? references = null,
        bool hidden = false,
        decimal? deltaPercent = null,
        int durationHours = 24) =>
        new(Guid.CreateVersion7(), code, code + " name", code + " اسم", null, type, groupId, null, null, null, "mmol/L", GenderRestriction.Any, null, decimals, hidden, true, 1, null,
            new LabTestTiming(durationHours, 0, null, FrequencyCheckAction.Alert, deltaPercent, null, LabTestTiming.EveryDay), formula, references ?? [], bands ?? [], [], [], true);

    public static CatalogueRangeBandInfo Band(GenderRestriction sex, decimal low, decimal high, decimal? panicLow = null, decimal? panicHigh = null) =>
        new(sex, 0, 150, RangeAgeUnit.Years, null, low, high, panicLow, panicHigh, null, null, null, 0);

    public static CatalogueServiceInfo Service(Guid groupId, ServiceType type, IEnumerable<(ServiceMemberKind Kind, Guid Id)> members, bool printAlone = false, int sequence = 10) =>
        new(Guid.CreateVersion7(), "S" + sequence, "S", null, "Service", "خدمة", null, type, groupId, null, null, GenderRestriction.Any, 24, 1, null, printAlone, true, false, null, sequence, true,
            [.. members.Select((member, index) => new CatalogueServiceMemberInfo(member.Kind, member.Id, index + 1, true, true))]);

    public FakeCatalogue With(CatalogueTestInfo test)
    {
        Tests[test.Id] = test;
        return this;
    }

    public FakeCatalogue With(CatalogueServiceInfo service)
    {
        Services[service.Id] = service;
        return this;
    }

    public Task<CatalogueServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken) => Task.FromResult(Services.GetValueOrDefault(serviceId));

    public Task<IReadOnlyList<CatalogueServiceInfo>> GetServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueServiceInfo>>([.. serviceIds.Where(Services.ContainsKey).Select(id => Services[id])]);

    public Task<CatalogueTestInfo?> GetTestAsync(Guid testId, CancellationToken cancellationToken) => Task.FromResult(Tests.GetValueOrDefault(testId));

    public Task<IReadOnlyList<CatalogueTestInfo>> GetTestsAsync(IReadOnlyCollection<Guid> testIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueTestInfo>>([.. testIds.Where(Tests.ContainsKey).Select(id => Tests[id])]);

    public Task<IReadOnlyList<CatalogueTestInfo>> GetTestsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueTestInfo>>([.. Tests.Values.Where(test => codes.Contains(test.Code))]);

    public Task<CatalogueClinicalGroupInfo?> GetClinicalGroupAsync(Guid clinicalGroupId, CancellationToken cancellationToken) => Task.FromResult(Groups.GetValueOrDefault(clinicalGroupId));

    public Task<IReadOnlyList<CatalogueClinicalGroupInfo>> GetClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueClinicalGroupInfo>>([.. clinicalGroupIds.Where(Groups.ContainsKey).Select(id => Groups[id])]);

    public Task<IReadOnlyList<CatalogueSampleTypeInfo>> GetSampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CatalogueSampleTypeInfo>>([]);

    public Task<CatalogueRangeBandInfo?> ResolveRangeAsync(Guid testId, GenderRestriction sex, PatientAge age, Guid? patientConditionId, CancellationToken cancellationToken) =>
        Task.FromResult(Tests.TryGetValue(testId, out var test) ? ReferenceRangeResolver.Instance.Resolve(test.RangeBands, sex, age, patientConditionId) : null);
}

internal sealed class FakePatients : IPatientLookup
{
    public Dictionary<Guid, PatientSummary> Items { get; } = [];

    public static PatientSummary Patient(Guid id, string gender, int years) =>
        new(id, "PAT-2026-00001", "Mr", "Patient Name", "اسم المريض", gender, null, true, new PatientAgeView(years, 0, 0), null, false, null, null, "Active", null);

    public Task<PatientSummary?> GetAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(Items.GetValueOrDefault(patientId));

    public Task<IReadOnlyDictionary<Guid, PatientSummary>> GetManyAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PatientSummary>>(patientIds.Where(Items.ContainsKey).ToDictionary(id => id, id => Items[id]));
}

internal sealed class FakeSampleGates : ISampleStatusLookup
{
    public Dictionary<Guid, SampleStatusView> ByTube { get; } = [];

    public FakeSampleGates Received(Guid tubeId, Guid visitId, DateTimeOffset? collectedAt = null)
    {
        ByTube[tubeId] = new SampleStatusView(Guid.CreateVersion7(), tubeId, "SMP-" + tubeId.ToString("N")[..6], visitId, "Received", collectedAt, collectedAt);
        return this;
    }

    public FakeSampleGates Collected(Guid tubeId, Guid visitId, DateTimeOffset collectedAt)
    {
        ByTube[tubeId] = new SampleStatusView(Guid.CreateVersion7(), tubeId, "SMP-" + tubeId.ToString("N")[..6], visitId, "Collected", collectedAt, null);
        return this;
    }

    public Task<SampleStatusView?> ByTubeAsync(Guid tubeId, CancellationToken cancellationToken) => Task.FromResult(ByTube.GetValueOrDefault(tubeId));

    public Task<IReadOnlyDictionary<Guid, SampleStatusView>> ByTubesAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, SampleStatusView>>(tubeIds.Where(ByTube.ContainsKey).ToDictionary(id => id, id => ByTube[id]));

    public Task<SampleStatusView?> BySampleNumberAsync(string sampleNumber, CancellationToken cancellationToken) => Task.FromResult(ByTube.Values.FirstOrDefault(sample => sample.SampleNumber == sampleNumber));

    public Task<SampleStatusView?> BySampleIdAsync(Guid sampleId, CancellationToken cancellationToken) => Task.FromResult(ByTube.Values.FirstOrDefault(sample => sample.SampleId == sampleId));

    public Task<IReadOnlyList<SampleStatusView>> ByVisitAsync(Guid visitId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SampleStatusView>>([.. ByTube.Values.Where(sample => sample.VisitId == visitId)]);

    public Task<bool> IsResultEntryAllowedAsync(Guid tubeId, CancellationToken cancellationToken) => Task.FromResult(ByTube.GetValueOrDefault(tubeId)?.IsResultEntryAllowed ?? false);
}

internal sealed class FakeReferences : IResultReferenceLookup
{
    public Task<IReadOnlyList<CommentTemplateOption>> CommentTemplatesAsync(string? search, Guid? clinicalGroupId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CommentTemplateOption>>([]);

    public Task<MicrobiologyLookups> MicrobiologyAsync(CancellationToken cancellationToken) => Task.FromResult(new MicrobiologyLookups([], [], []));

    public Task<IReadOnlyDictionary<Guid, CultureOrganismRef>> OrganismsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CultureOrganismRef>>(ids.ToDictionary(id => id, id => new CultureOrganismRef(id, "ECOLI", "E. coli", "إشريكية", true)));

    public Task<IReadOnlyDictionary<Guid, CultureAntibioticRef>> AntibioticsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CultureAntibioticRef>>(ids.ToDictionary(id => id, id => new CultureAntibioticRef(id, "AMC", "AMC", "Amoxiclav", "أموكسيكلاف", true)));

    public Task<IReadOnlySet<string>> SensitivityCodesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(["S", "I", "R"]));

    public Task<IReadOnlyList<LookupOption>> ClinicalGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LookupOption>>([]);

    public Task<IReadOnlyList<LookupOption>> TestsAsync(Guid? clinicalGroupId, string? search, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LookupOption>>([]);

    public Task<IReadOnlyList<LookupOption>> ActivePayersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LookupOption>>([]);

    public Task<ReportLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult(new ReportLetterhead("Lab", "مختبر", "Main", "الرئيسي"));

    public Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(new Dictionary<Guid, NamedReference>());

    public Task<IReadOnlyDictionary<Guid, NamedReference>> BranchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(new Dictionary<Guid, NamedReference>());

    public Task<IReadOnlyDictionary<Guid, NamedReference>> PatientConditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(new Dictionary<Guid, NamedReference>());
}

/// <summary>Privileges with result rights for listed groups in every branch, and the two special rights.</summary>
internal sealed class FakePrivileges : IUserPrivileges
{
    public List<ResultRight> Rights { get; } = [];

    public SpecialRights Special { get; set; } = SpecialRights.All;

    public FakePrivileges Grant(Guid groupId, bool see = true, bool review = true, bool verify = true, bool edit = true, bool print = true)
    {
        Rights.Add(new ResultRight(groupId, null, see, review, verify, edit, print));
        return this;
    }

    public bool Has(string permissionKey) => true;

    public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>();

    public SpecialRights SpecialRights => Special;

    public IReadOnlyList<ResultRight> ResultRights => Rights;

    public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => ResultRight.Allows(Rights, action, clinicalGroupId, branchId);
}

internal sealed class FakeDirectory : IUserDirectory
{
    public Task<IReadOnlyDictionary<Guid, UserNameItem>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserNameItem>>(userIds.ToDictionary(id => id, id => new UserNameItem(id, "user", "Dr. Verifier", "د. المعتمد")));
}

internal sealed class FakeVisits : IVisitLookup
{
    public Dictionary<Guid, VisitView> Items { get; } = [];

    public Task<VisitView?> GetAsync(Guid visitId, CancellationToken cancellationToken) => Task.FromResult(Items.GetValueOrDefault(visitId));

    public Task<VisitView?> GetByAccessionAsync(string accession, CancellationToken cancellationToken) => Task.FromResult(Items.Values.FirstOrDefault(visit => visit.Accession == accession));

    public Task<IReadOnlyDictionary<Guid, VisitView>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VisitView>>(visitIds.Where(Items.ContainsKey).ToDictionary(id => id, id => Items[id]));
}

internal sealed class ResultsSettings : ITenantSettings
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken) => ValueTask.FromResult(descriptor.Parse(Values.GetValueOrDefault(descriptor.Key)));

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(Values);
}

internal sealed class ResultsUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.FromResult(1);
    }
}

internal sealed class ResultsUser(Guid userId, Guid? branchId) : ICurrentUser
{
        public bool IsPrimaryAdministrator { get; set; }
    public bool IsAuthenticated => true;

    public Guid? UserId { get; } = userId;

    public Guid? SessionId => null;

    public string? DisplayName => "Bench user";

    public Guid? BranchId { get; } = branchId;
}

internal sealed class ResultsClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
