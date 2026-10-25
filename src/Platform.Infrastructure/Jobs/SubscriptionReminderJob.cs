using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Jobs;

/// <summary>Settings of the subscription reminder run, read by the host from <c>Lis:Jobs:SubscriptionReminders</c> (LIS-CR-012).</summary>
public sealed class SubscriptionReminderOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>How often the job looks for due reminders; a reminder is sent once whatever the interval (its key is unique per client).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>
/// LIS-CR-012 (C-16): subscription reminders, hosted like the wallet charge (01 §7). Every poll lists the Trial and Active clients
/// with their figures once, reads the platform's grace days once, and hands each client to <see cref="SubscriptionReminderRun"/>
/// in its own scope bound to the platform tenant (no elevation: the saas tables carry no tenant and no RLS), so one client's
/// commit or failure never touches another's. A failure is logged, written to the security ledger
/// (<c>SUBSCRIPTION_REMINDER_FAILED</c>) and counted; the next poll retries it (nothing was recorded). Passes are serialised
/// in-process. The poll is hourly by default; each reminder goes out once, so a client hears at most once per threshold.
/// </summary>
public sealed partial class SubscriptionReminderJob(IServiceScopeFactory scopes, SubscriptionReminderOptions options, IClock clock, ILogger<SubscriptionReminderJob> logger) : ISubscriptionReminderRun, IDisposable
{
    /// <summary>Security event of a reminder that could not be recorded or sent (detail <c>tenant:&lt;id&gt; reason:error</c>).</summary>
    public const string ReminderFailedEvent = "SUBSCRIPTION_REMINDER_FAILED";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The polling loop the host runs for the lifetime of the process (WebApi <c>SubscriptionReminderHostedService</c>).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, options.PollInterval);
        using var timer = new PeriodicTimer(options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (summary.Sent > 0 || summary.Failed > 0)
                {
                    LogPass(logger, summary.Clients, summary.Sent, summary.Skipped, summary.Failed);
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

    public Task<SubscriptionReminderRunSummary> RunOnceAsync(CancellationToken cancellationToken) => RunOnceAsync(clock.UtcNow, cancellationToken);

    /// <summary>One pass at <paramref name="nowUtc"/>: every Trial or Active client once. Public so a test can drive the job at a chosen time.</summary>
    public async Task<SubscriptionReminderRunSummary> RunOnceAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<ClientSubscriptionRow> clients;
            int graceDays;
            using (var scope = scopes.CreateScope())
            {
                var services = scope.ServiceProvider;
                var tenantScope = services.GetRequiredService<ICurrentTenantSetter>();
                using (tenantScope.ForTenant(TenantIds.Platform))
                {
                    clients = await services.GetRequiredService<ISubscriptionStateReader>().ListClientsAsync(cancellationToken).ConfigureAwait(false);
                    graceDays = await SubscriptionGrace.DaysAsync(services.GetRequiredService<ITenantSettings>(), tenantScope, cancellationToken).ConfigureAwait(false);
                }
            }

            var items = new List<SubscriptionReminderRunItem>(clients.Count);
            foreach (var client in clients)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await RemindAsync(client, graceDays, nowUtc, cancellationToken).ConfigureAwait(false));
            }

            return SubscriptionReminderRun.Summarise(nowUtc, items);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<SubscriptionReminderRunItem> RemindAsync(ClientSubscriptionRow client, int graceDays, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform);
            var run = ActivatorUtilities.CreateInstance<SubscriptionReminderRun>(scope.ServiceProvider);
            var item = await run.RemindAsync(client, graceDays, nowUtc, cancellationToken).ConfigureAwait(false);
            if (item.Outcome == SubscriptionReminderRun.Sent)
            {
                LogSent(logger, client.TenantId, item.Key, item.TemplateCode);
            }

            return item;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogReminderFailed(logger, exception, client.TenantId);
            await RecordFailureAsync(client.TenantId, cancellationToken).ConfigureAwait(false);
            return new SubscriptionReminderRunItem(client.TenantId, SubscriptionReminderRun.Failed, "error", null, null, []);
        }
    }

    private async Task RecordFailureAsync(Guid clientId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform);
        await scope.ServiceProvider.GetRequiredService<ISecurityEventLog>()
            .WriteAsync(new SecurityEvent(ReminderFailedEvent, SecurityEvent.Warning, Detail: $"tenant:{clientId} reason:error"), cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 7320, Level = LogLevel.Information, Message = "subscription reminder job started poll={PollInterval}")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(EventId = 7321, Level = LogLevel.Information, Message = "subscription reminder job disabled (Lis:Jobs:SubscriptionReminders:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 7322, Level = LogLevel.Information, Message = "subscription reminder pass: clients={Clients} sent={Sent} skipped={Skipped} failed={Failed}")]
    private static partial void LogPass(ILogger logger, int clients, int sent, int skipped, int failed);

    [LoggerMessage(EventId = 7323, Level = LogLevel.Information, Message = "subscription reminder {Key} sent to tenant {TenantId} with template {TemplateCode}")]
    private static partial void LogSent(ILogger logger, Guid tenantId, string? key, string? templateCode);

    [LoggerMessage(EventId = 7324, Level = LogLevel.Error, Message = "subscription reminder of tenant {TenantId} failed")]
    private static partial void LogReminderFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 7325, Level = LogLevel.Error, Message = "subscription reminder poll failed; retrying after the poll interval")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
