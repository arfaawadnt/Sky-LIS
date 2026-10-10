using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Authorization;
using Platform.Infrastructure.Security;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Outbox;

/// <summary>Settings of the outbox processor, read by the host from <c>Lis:Outbox</c>; the defaults are 01 §7's.</summary>
public sealed class OutboxOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; init; } = 50;

    /// <summary>How long a claimed row stays invisible to other workers; a crashed worker's rows come back after it.</summary>
    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; init; } = 10;
}

/// <summary>Resolves the CLR type of a stored event name: every concrete <see cref="IDomainEvent"/> in Platform.Domain, keyed by full name.</summary>
public sealed class DomainEventRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _types;

    public DomainEventRegistry()
        : this(typeof(PermissionCatalog).Assembly)
    {
    }

    public DomainEventRegistry(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        _types = assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToDictionary(type => type.FullName ?? type.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<Type> Types => _types.Values.ToList();

    public Type? Resolve(string typeName) => typeName is not null && _types.TryGetValue(typeName, out var type) ? type : null;
}

/// <summary>
/// The outbox worker of 01 §7: every poll claims a batch of due rows with <c>FOR UPDATE SKIP LOCKED</c> under a
/// lease (so several replicas share the queue without a leader), deserialises each event, opens a scope bound to the
/// event's tenant (no elevation: the handlers see exactly what the tenant sees), dispatches to every registered
/// <see cref="IDomainEventHandler{TEvent}"/>, and marks the row processed. A failing handler counts an attempt and
/// reschedules with exponential back-off; the tenth failure dead-letters the row. Handlers are idempotent by
/// contract, because a crash between dispatch and the mark replays the event after the lease.
/// </summary>
public sealed partial class OutboxProcessor(IServiceScopeFactory scopes, OutboxOptions options, DomainEventRegistry registry, IClock clock, ILogger<OutboxProcessor> logger)
{
    private const string ClaimSql = """
        UPDATE ops.outbox_events AS o
        SET claimed_until_utc = {1}
        FROM (
            SELECT id FROM ops.outbox_events
            WHERE processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL AND next_attempt_at_utc <= {0}
              AND (claimed_until_utc IS NULL OR claimed_until_utc < {0})
            ORDER BY next_attempt_at_utc, enqueued_at_utc
            LIMIT {2}
            FOR UPDATE SKIP LOCKED
        ) AS due
        WHERE o.id = due.id
        RETURNING o.id, o.type, o.payload::text AS payload, o.tenant_id, o.attempts, o.trace_parent
        """;   // column names in the snake_case the naming convention expects for the unmapped result type

    private static readonly MethodInfo DispatchMethod = typeof(OutboxProcessor).GetMethod(nameof(DispatchTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The polling loop the host runs for the lifetime of the process (the WebApi wraps it in a hosted service; Infrastructure carries no hosting package).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, options.PollInterval, options.BatchSize);
        using var timer = new PeriodicTimer(options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunOnceAsync(stoppingToken).ConfigureAwait(false) == options.BatchSize)
                {
                    // a full batch means more is waiting: drain before sleeping
                }

                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPollFailed(logger, exception);
                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>One poll: claim, dispatch, mark. Returns the number of rows claimed. Public so a test can drive the worker without timers.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var claimScope = scopes.CreateScope();
        var context = claimScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.UtcNow;

        var claimed = await context.Database.SqlQueryRaw<ClaimedRow>(ClaimSql, now, now + options.Lease, options.BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DispatchAsync(row, cancellationToken).ConfigureAwait(false);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE ops.outbox_events SET processed_at_utc = {clock.UtcNow}, claimed_until_utc = NULL, last_error = NULL WHERE id = {row.Id}",
                    cancellationToken).ConfigureAwait(false);
                LogProcessed(logger, row.Id, row.Type, row.TenantId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var attempts = row.Attempts + 1;
                var error = Describe(exception);
                if (attempts >= options.MaxAttempts)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE ops.outbox_events SET attempts = {attempts}, last_error = {error}, dead_lettered_at_utc = {clock.UtcNow}, claimed_until_utc = NULL WHERE id = {row.Id}",
                        cancellationToken).ConfigureAwait(false);
                    LogDeadLettered(logger, exception, row.Id, row.Type, attempts);
                }
                else
                {
                    var next = clock.UtcNow + Backoff(attempts);
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE ops.outbox_events SET attempts = {attempts}, last_error = {error}, next_attempt_at_utc = {next}, claimed_until_utc = NULL WHERE id = {row.Id}",
                        cancellationToken).ConfigureAwait(false);
                    LogFailed(logger, exception, row.Id, row.Type, attempts, next);
                }
            }
        }

        return claimed.Count;
    }

    /// <summary>2 s · 2^attempt, capped at one hour: 4 s, 8 s, 16 s … so a transient fault clears quickly and a stubborn one stops hammering.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(3600, 2 * Math.Pow(2, Math.Max(1, attempts))));

    private async Task DispatchAsync(ClaimedRow row, CancellationToken cancellationToken)
    {
        var type = registry.Resolve(row.Type) ?? throw new InvalidOperationException($"No domain event type named '{row.Type}' exists in this build.");
        var domainEvent = JsonSerializer.Deserialize(row.Payload, type, OutboxEvent.SerializerOptions) as IDomainEvent
            ?? throw new InvalidOperationException($"The payload of outbox row {row.Id} did not deserialise as {row.Type}.");

        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<RequestContextAccessor>().Set(row.TraceParent, null, null);
        using var tenantScope = row.TenantId is { } tenantId ? services.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenantId) : null;

        var task = (Task)DispatchMethod.MakeGenericMethod(type).Invoke(null, [services, domainEvent, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private static async Task DispatchTypedAsync<TEvent>(IServiceProvider services, TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Describe(Exception exception)
    {
        var text = exception.GetType().Name + ": " + exception.Message;
        return text.Length <= 2000 ? text : text[..2000];
    }

    /// <summary>The claimed row as the claim statement returns it.</summary>
    public sealed class ClaimedRow
    {
        public Guid Id { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public Guid? TenantId { get; set; }

        public int Attempts { get; set; }

        public string? TraceParent { get; set; }
    }

    [LoggerMessage(EventId = 7300, Level = LogLevel.Information, Message = "outbox processor started poll={PollInterval} batch={BatchSize}")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval, int batchSize);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information, Message = "outbox processor disabled (Lis:Outbox:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Debug, Message = "outbox event {EventId} {EventType} tenant={TenantId} processed")]
    private static partial void LogProcessed(ILogger logger, Guid eventId, string eventType, Guid? tenantId);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning, Message = "outbox event {EventId} {EventType} failed (attempt {Attempts}); next attempt at {NextAttemptAtUtc}")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid eventId, string eventType, int attempts, DateTimeOffset nextAttemptAtUtc);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Error, Message = "outbox event {EventId} {EventType} dead-lettered after {Attempts} attempts")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid eventId, string eventType, int attempts);

    [LoggerMessage(EventId = 7305, Level = LogLevel.Error, Message = "outbox poll failed; retrying after the poll interval")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
