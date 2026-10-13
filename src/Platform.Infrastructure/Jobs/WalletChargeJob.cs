using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Jobs;

/// <summary>Settings of the monthly wallet charge run, read by the host from <c>Lis:Jobs:WalletCharge</c>.</summary>
public sealed class WalletChargeOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>How often the job looks for due wallets; a wallet is charged once per period whatever the interval.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>
/// W2 monthly subscription charge (intake LIS-INT-PLT-001 §7), hosted like the outbox worker (01 §7): every poll lists
/// the clients whose wallet is due and charges each through <see cref="WalletCharger"/> in its own scope bound to the
/// platform tenant (no elevation: the saas tables carry no tenant and no RLS), so one client's commit or failure never
/// touches another's. A lost race on a wallet's row version (another process charged it) is retried in a fresh scope
/// and reads as <c>not-due</c>; any other error is logged, written to the security ledger and counted as failed.
/// Passes are serialised in-process (the SA-05 "Run charges now" request waits for a running poll).
/// </summary>
public sealed partial class WalletChargeJob(IServiceScopeFactory scopes, WalletChargeOptions options, IClock clock, ILogger<WalletChargeJob> logger) : IWalletChargeRun, IDisposable
{
    private const int MaxAttempts = 3;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The polling loop the host runs for the lifetime of the process (WebApi <c>WalletChargeHostedService</c>).</summary>
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
                if (summary.Items.Count > 0)
                {
                    LogPass(logger, summary.Charged, summary.Skipped, summary.Failed, summary.AtOrBelowZero);
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

    public Task<WalletChargeRunSummary> RunOnceAsync(CancellationToken cancellationToken) => RunOnceAsync(clock.UtcNow, cancellationToken);

    /// <summary>One pass at <paramref name="nowUtc"/>: every due wallet once, one period each. Public so a test can drive the job at a chosen time.</summary>
    public async Task<WalletChargeRunSummary> RunOnceAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<Guid> due;
            using (var scope = scopes.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform))
            {
                due = await scope.ServiceProvider.GetRequiredService<IWalletRepository>().ListDueForChargeAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            }

            var items = new List<WalletChargeRunItem>(due.Count);
            foreach (var clientId in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await ChargeAsync(clientId, nowUtc, cancellationToken).ConfigureAwait(false));
            }

            return WalletCharger.Summarise(nowUtc, items);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<WalletChargeRunItem> ChargeAsync(Guid clientId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform);
                var charger = ActivatorUtilities.CreateInstance<WalletCharger>(scope.ServiceProvider);
                var item = await charger.ChargeAsync(clientId, nowUtc, cancellationToken).ConfigureAwait(false);
                if (item.Outcome == WalletCharger.Charged)
                {
                    LogCharged(logger, clientId, item.Period, item.Amount, item.BalanceAfter);
                }

                return item;
            }
            catch (DomainException exception) when (exception.Code == ProblemCodes.ConcurrencyConflict && attempt < MaxAttempts)
            {
                LogRaceLost(logger, clientId, attempt);   // another process moved the wallet: re-read it
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogChargeFailed(logger, exception, clientId);
                await RecordFailureAsync(clientId, cancellationToken).ConfigureAwait(false);
                return new WalletChargeRunItem(clientId, WalletCharger.Failed, "error", null, null, null);
            }
        }
    }

    private async Task RecordFailureAsync(Guid clientId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform);
        await scope.ServiceProvider.GetRequiredService<ISecurityEventLog>()
            .WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.WalletChargeFailed, SecurityEvent.Warning, Detail: $"tenant:{clientId} reason:error"), cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 7310, Level = LogLevel.Information, Message = "wallet charge job started poll={PollInterval}")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(EventId = 7311, Level = LogLevel.Information, Message = "wallet charge job disabled (Lis:Jobs:WalletCharge:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Information, Message = "wallet charge pass: charged={Charged} skipped={Skipped} failed={Failed} atOrBelowZero={AtOrBelowZero}")]
    private static partial void LogPass(ILogger logger, int charged, int skipped, int failed, int atOrBelowZero);

    [LoggerMessage(EventId = 7313, Level = LogLevel.Information, Message = "wallet of tenant {TenantId} charged for {Period}: {Amount}, balance after {BalanceAfter}")]
    private static partial void LogCharged(ILogger logger, Guid tenantId, string? period, decimal? amount, decimal? balanceAfter);

    [LoggerMessage(EventId = 7314, Level = LogLevel.Debug, Message = "wallet of tenant {TenantId} changed concurrently (attempt {Attempt}); re-reading")]
    private static partial void LogRaceLost(ILogger logger, Guid tenantId, int attempt);

    [LoggerMessage(EventId = 7315, Level = LogLevel.Error, Message = "wallet charge of tenant {TenantId} failed")]
    private static partial void LogChargeFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 7316, Level = LogLevel.Error, Message = "wallet charge poll failed; retrying after the poll interval")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
