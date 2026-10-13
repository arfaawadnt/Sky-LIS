using Platform.Infrastructure.Jobs;

namespace Platform.WebApi.Startup;

/// <summary>
/// Hosts the monthly wallet charge (W2, intake LIS-INT-PLT-001) for the lifetime of the process, like the outbox
/// worker. The job lives in Infrastructure and reads <c>Lis:Jobs:WalletCharge:Enabled</c> lazily, so a test host
/// turns it off through configuration added at <c>Build()</c>.
/// </summary>
public sealed class WalletChargeHostedService(WalletChargeJob job) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => job.RunAsync(stoppingToken);
}
