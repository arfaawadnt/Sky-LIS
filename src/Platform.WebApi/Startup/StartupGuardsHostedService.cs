using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Platform.WebApi.Startup;

/// <summary>
/// Runs the start-up guards (<see cref="StartupGuards.RunAsync"/>) as the first hosted service of the process: after
/// the host lifetime has started and before the server listens (the web host's own hosted service is registered
/// last). Under <c>UseWindowsService</c> the lifetime connects to the service controller first; everything that ran
/// before <c>app.Run()</c> counted against the controller's 30 s start timeout, which a cold start after a reboot
/// exceeded on 2026-10-26 (events 7009 / 7000, service left stopped). A refusing guard still fails the host start:
/// this is a plain <see cref="IHostedService"/>, never a <see cref="BackgroundService"/>, whose refusal would not
/// stop the start.
/// </summary>
public sealed class StartupGuardsHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment,
    IServer server,
    ILogger<StartupGuardsHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var urls = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        return StartupGuards.RunAsync(services, configuration, environment, logger, urls, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
