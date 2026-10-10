using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Treasury;
using Platform.Domain.Treasury;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;

namespace Platform.IntegrationTests;

/// <summary>
/// 01 §7 on a real PostgreSQL 17: a committed event is claimed, delivered to every registered policy inside a
/// scope bound to the event's tenant, and marked processed; a throwing policy counts an attempt and reschedules
/// with back-off; the tenth failure dead-letters the row.
/// </summary>
/// <remarks>
/// The queue is shared with every other test of the process (every module's commits enqueue events), and the
/// processor claims due rows oldest first, 50 at a time. So these tests never assume their row is in the first
/// batch: they poll until their own row reaches the state under test and assert on that row only. The throwing
/// policy fails for the test's own aggregate only, so other tests' rows are processed normally.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxProcessorTests(DatabaseFixture fixture)
{
    private const int MaxPolls = 500;
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_committed_event_is_delivered_to_the_policy_in_the_tenant_scope_and_marked_processed()
    {
        var tenant = Guid.CreateVersion7();
        var treasuryId = await CreateTreasuryAsync(tenant, "OBX1");
        var eventId = await EventIdOfAsync(tenant, typeof(TreasuryCreated));

        var recorder = new Recorder();
        await using var provider = BuildProvider(recorder);
        var processor = provider.GetRequiredService<OutboxProcessor>();

        var row = await PollUntilAsync(processor, eventId, candidate => candidate.ProcessedAtUtc is not null);

        var delivery = Assert.Single(recorder.Created, e => e.TreasuryId == treasuryId);
        Assert.Equal(eventId, delivery.EventId);
        Assert.Equal(tenant, recorder.TenantSeen[delivery.EventId]);
        Assert.False(recorder.ElevatedSeen[delivery.EventId]);
        Assert.Null(row.ClaimedUntilUtc);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.DeadLetteredAtUtc);

        await processor.RunOnceAsync(CancellationToken.None);   // a processed row is never claimed again
        Assert.Single(recorder.Created, e => e.TreasuryId == treasuryId);
    }

    [DatabaseFact]
    public async Task A_throwing_policy_counts_an_attempt_reschedules_with_back_off_and_dead_letters_on_the_tenth()
    {
        var tenant = Guid.CreateVersion7();
        var treasuryId = await CreateTreasuryAsync(tenant, "OBX2");
        using (var scope = fixture.AppScope(tenant))
        {
            var treasury = (await scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().GetForUpdateAsync(treasuryId, CancellationToken.None))!;
            treasury.Deactivate(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var eventId = await EventIdOfAsync(tenant, typeof(TreasuryDeactivated));
        var recorder = new Recorder { FailingTreasuryId = treasuryId };
        await using var provider = BuildProvider(recorder);
        var processor = provider.GetRequiredService<OutboxProcessor>();

        var row = await PollUntilAsync(processor, eventId, candidate => candidate.Attempts >= 1);
        Assert.Null(row.ProcessedAtUtc);
        Assert.Null(row.ClaimedUntilUtc);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("deliberate", row.LastError, StringComparison.Ordinal);
        Assert.True(row.NextAttemptAtUtc > row.EnqueuedAtUtc.AddSeconds(3), "the retry is pushed into the future by the back-off");

        // Not due yet: another poll leaves it alone.
        await processor.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, (await LoadAsync(eventId)).Attempts);

        // The tenth failure dead-letters the row (ops has no RLS; the runtime role may update it).
        using (var scope = fixture.AppScope(null))
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE ops.outbox_events SET attempts = 9, next_attempt_at_utc = now() - interval '1 second' WHERE id = {eventId}");
        }

        var dead = await PollUntilAsync(processor, eventId, candidate => candidate.DeadLetteredAtUtc is not null);
        Assert.Equal(10, dead.Attempts);
        Assert.Null(dead.ProcessedAtUtc);
        Assert.Null(dead.ClaimedUntilUtc);

        Assert.Equal(TimeSpan.FromSeconds(4), OutboxProcessor.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(1024), OutboxProcessor.Backoff(9));
        Assert.Equal(TimeSpan.FromHours(1), OutboxProcessor.Backoff(20));
    }

    /// <summary>Polls like the hosted loop does until the test's own row satisfies <paramref name="reached"/>; fails if the queue drains first.</summary>
    private async Task<OutboxEvent> PollUntilAsync(OutboxProcessor processor, Guid eventId, Func<OutboxEvent, bool> reached)
    {
        for (var poll = 0; poll < MaxPolls; poll++)
        {
            var row = await LoadAsync(eventId);
            if (reached(row))
            {
                return row;
            }

            if (await processor.RunOnceAsync(CancellationToken.None) == 0)
            {
                var last = await LoadAsync(eventId);
                Assert.True(reached(last), $"the queue drained but outbox row {eventId} did not reach the expected state (attempts {last.Attempts}, processed {last.ProcessedAtUtc}, next {last.NextAttemptAtUtc:O})");
                return last;
            }
        }

        throw new Xunit.Sdk.XunitException($"outbox row {eventId} did not reach the expected state within {MaxPolls} polls");
    }

    private async Task<OutboxEvent> LoadAsync(Guid eventId)
    {
        using var scope = fixture.AppScope(null);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
    }

    private async Task<Guid> EventIdOfAsync(Guid tenantId, Type eventType)
    {
        using var scope = fixture.AppScope(null);
        var typeName = eventType.FullName;
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Type == typeName)
            .Select(e => e.Id)
            .SingleAsync();
    }

    private static ServiceProvider BuildProvider(Recorder recorder) =>
        new ServiceCollection()
            .AddSingleton(recorder)
            .AddInfrastructure(TestDatabase.AppConnection!)
            .AddScoped<IDomainEventHandler<TreasuryCreated>, CreatedPolicy>()
            .AddScoped<IDomainEventHandler<TreasuryDeactivated>, ThrowingPolicy>()
            .BuildServiceProvider();

    private async Task<Guid> CreateTreasuryAsync(Guid tenantId, string code)
    {
        using var scope = fixture.AppScope(tenantId, Guid.CreateVersion7(), "Outbox tester");
        var treasury = Domain.Treasury.Treasury.Create(code, new LocalizedText("Drawer " + code, "خزينة " + code), Guid.CreateVersion7(), Now);
        scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().Add(treasury);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return treasury.Id;
    }

    private sealed class Recorder
    {
        public Guid? FailingTreasuryId { get; init; }

        public ConcurrentBag<TreasuryCreated> Created { get; } = [];

        public ConcurrentDictionary<Guid, Guid?> TenantSeen { get; } = new();

        public ConcurrentDictionary<Guid, bool> ElevatedSeen { get; } = new();
    }

    private sealed class CreatedPolicy(Recorder recorder, ICurrentTenant tenant) : IDomainEventHandler<TreasuryCreated>
    {
        public Task HandleAsync(TreasuryCreated domainEvent, CancellationToken cancellationToken)
        {
            recorder.Created.Add(domainEvent);
            recorder.TenantSeen[domainEvent.EventId] = tenant.TenantId;
            recorder.ElevatedSeen[domainEvent.EventId] = tenant.IsElevated;
            return Task.CompletedTask;
        }
    }

    /// <summary>Fails for the test's own treasury only; every other deactivation in the shared queue is handled normally.</summary>
    private sealed class ThrowingPolicy(Recorder recorder) : IDomainEventHandler<TreasuryDeactivated>
    {
        public Task HandleAsync(TreasuryDeactivated domainEvent, CancellationToken cancellationToken) =>
            domainEvent.TreasuryId == recorder.FailingTreasuryId
                ? throw new InvalidOperationException("deliberate failure")
                : Task.CompletedTask;
    }
}
