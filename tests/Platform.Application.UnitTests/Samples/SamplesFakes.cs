using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Samples;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.UnitTests.Samples;

// In-memory stand-ins for the ports the Samples handlers and policies use, so the use-case logic is tested without a
// database (the engine side lives in tests/Platform.IntegrationTests/SamplesModuleTests.cs).

internal sealed class InMemorySampleRepository : ISampleRepository
{
    public List<Sample> Items { get; } = [];

    public Task<Sample?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(sample => sample.Id == id));

    public void Add(Sample aggregate) => Items.Add(aggregate);

    public Task<IReadOnlySet<Guid>> ExistingTubeIdsAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(Items.Where(sample => tubeIds.Contains(sample.TubeId)).Select(sample => sample.TubeId).ToHashSet());

    public Task<Sample?> GetLiveByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(sample => sample.TubeId == tubeId && sample.IsLive).OrderByDescending(sample => sample.Generation).FirstOrDefault());

    public Task<Sample?> GetByNumberForUpdateAsync(string sampleNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(sample => sample.SampleNumber == sampleNumber));

    public Task<IReadOnlyList<Sample>> ListLiveByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Sample>>([.. Items.Where(sample => sample.VisitId == visitId && sample.IsLive)]);

    public Task<Guid?> FindIdByNumberAsync(string sampleNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(sample => sample.SampleNumber == sampleNumber)?.Id);

    public Task<SampleDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(sample => sample.Id == id) is { } sample ? Detail(sample) : null);

    public Task<Paged<SampleListItem>> ListAsync(SampleFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SampleListItem>> ListRejectedAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? branchId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SampleTimelineStep>> GetTimelineStepsAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SampleLabelDocument?> GetLabelAsync(Guid id, int labelCopies, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SampleLabelDocument>> GetVisitLabelsAsync(Guid visitId, int labelCopies, CancellationToken cancellationToken) => throw new NotSupportedException();

    public static SampleListItem Item(Sample sample) =>
        new(
            sample.Id, sample.SampleNumber, sample.TubeId, sample.Generation, sample.VisitId, sample.Accession, sample.PatientId, null, null, null, sample.BranchId,
            sample.ClinicalGroupId, null, null, null, sample.SampleTypeId, null, null, sample.SampleConditionId, null, null, sample.ServiceIds.Length, sample.Status.ToString(),
            sample.OrderedAtUtc, sample.StatusChangedAtUtc, sample.CollectedAtUtc, sample.ReceivedAtUtc, sample.Rejection?.ReasonId, null, null, sample.Rejection?.Note,
            sample.Rejection?.AtUtc, sample.ReplacedBySampleId, sample.ReplacesSampleId);

    private static SampleDetail Detail(Sample sample) =>
        new(Item(sample), [], null, null, null, null, null, sample.CancelledAtUtc, sample.CancellationReason, sample.IsResultEntryAllowed);
}

internal sealed class FakeSettings : ITenantSettings
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public FakeSettings With<T>(SettingDescriptor<T> descriptor, string value)
    {
        Values[descriptor.Key] = value;
        return this;
    }

    public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(descriptor.Parse(Values.GetValueOrDefault(descriptor.Key)));

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(Values);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeUser(Guid? userId, Guid? branchId) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; } = userId;

    public Guid? SessionId => null;

    public string? DisplayName => "Bench user";

    public Guid? BranchId { get; } = branchId;
}

internal sealed class SequenceNumbers : IReferenceNumbers
{
    private int _next = 100;

    public List<string> Issued { get; } = [];

    public Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var number = $"{prefix}-{nowUtc.Year:D4}-{++_next:D5}";
        Issued.Add($"{kind}:{number}");
        return Task.FromResult(number);
    }
}

internal sealed class StoppedClock(DateTimeOffset utcNow) : IClock
{
    public static readonly DateTimeOffset Instant = new(2026, 10, 9, 8, 30, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class FakeVisitLines(bool hasLiveLines) : IVisitLineLookup
{
    public List<(Guid TubeId, Guid ExcludingLineId)> Calls { get; } = [];

    public Task<bool> TubeHasLiveLinesAsync(Guid tubeId, Guid excludingLineId, CancellationToken cancellationToken)
    {
        Calls.Add((tubeId, excludingLineId));
        return Task.FromResult(hasLiveLines);
    }
}

internal sealed class FakeReasons(RejectionReasonCheck answer) : IRejectionReasonLookup
{
    public Task<RejectionReasonCheck> FindAsync(Guid reasonId, CancellationToken cancellationToken) => Task.FromResult(answer);
}
