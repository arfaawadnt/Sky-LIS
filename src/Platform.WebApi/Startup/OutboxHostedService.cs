using Platform.Infrastructure.Persistence.Outbox;

namespace Platform.WebApi.Startup;

/// <summary>
/// Hosts the outbox worker (01 §7) for the lifetime of the process. The worker itself lives in Infrastructure
/// (which carries no hosting package) and reads <c>Lis:Outbox:Enabled</c> lazily, so a test host can turn it off
/// or on through configuration added at <c>Build()</c>.
/// </summary>
public sealed class OutboxHostedService(OutboxProcessor processor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => processor.RunAsync(stoppingToken);
}
