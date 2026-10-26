using Platform.Application.Abstractions.Audit;
using Platform.Application.Identity;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Notifications;
using Platform.Infrastructure.Security;

namespace Platform.WebApi.Startup;

/// <summary>
/// Start-up guards that run identically under every environment name (01 §5.3, C-07), against the final
/// configuration of the built host: no optional envelope mode key may exist, the secrets must be present and
/// strong, Kestrel may bind loopback only, a relaxation flag is announced as an error-level security event, and the
/// database target and runtime role must be LIS's own and not over-privileged.
/// </summary>
public static class StartupGuards
{
    public const string ForbiddenKeyFragment = "CredentialEnvelope:Mode";
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    /// <summary>D-03 has no optional mode: a configuration that tries to introduce one refuses to start.</summary>
    public static void RefuseForbiddenKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var offenders = configuration.AsEnumerable()
            .Select(pair => pair.Key)
            .Where(key => key.Contains(ForbiddenKeyFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (offenders.Count > 0)
        {
            throw new SecurityConfigurationException(offenders.Select(key => $"configuration key '{key}' is not allowed: the credential envelope has no optional mode").ToList());
        }
    }

    /// <summary>Kestrel binds loopback HTTP only; TLS terminates at the reverse proxy (02 §2.1).</summary>
    public static IReadOnlyList<string> InspectBindings(IEnumerable<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var violations = new List<string>();
        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            var candidate = url.Replace("*", "0.0.0.0", StringComparison.Ordinal).Replace("+", "0.0.0.0", StringComparison.Ordinal);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                violations.Add($"'{url}' is not a loopback bind");
            }
        }

        return violations;
    }

    /// <summary>The addresses Kestrel will use: the configured endpoints (which take precedence) or, failing those, the url settings.</summary>
    public static IEnumerable<string> ConfiguredBindings(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList();
        if (endpoints.Count > 0)
        {
            return endpoints;
        }

        return (configuration["urls"] ?? configuration["ASPNETCORE_URLS"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Runs every guard against the built host. Called from <see cref="StartupGuardsHostedService"/>, the first hosted
    /// service, so that under the Windows service lifetime the process has already reported itself to the service
    /// controller before any database or configuration work begins (SCM start timeout, 2026-10-26).
    /// </summary>
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment, ILogger logger, IEnumerable<string> serverUrls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serverUrls);

        RefuseForbiddenKeys(configuration);
        ConfigGuard.Enforce(services.GetRequiredService<SecurityOptions>(), services.GetRequiredService<SessionPolicy>());
        var notifications = services.GetRequiredService<NotificationOptions>();
        ConfigGuard.EnforceNotifications(notifications, environment.IsProduction());
        ConfigGuard.EnforcePortalBaseUrl(configuration[ConfigGuard.PortalBaseUrlKey], environment.IsProduction());   // LIS-CR-005
        if (notifications.RevealCodes)
        {
            logger.LogWarning("{Key} is on: one-time codes of the subscription wizard are shown on the page (interim until an SMS gateway is configured)", NotificationOptions.RevealCodesKey);
        }

        var bindViolations = InspectBindings(ConfiguredBindings(configuration).Concat(serverUrls));
        if (bindViolations.Count > 0)
        {
            throw new SecurityConfigurationException(bindViolations);
        }

        var postgres = SecurityConfiguration.ReadConnectionString(configuration)
            ?? throw new InvalidOperationException($"{SecurityConfiguration.ConnectionStringKey} is required. Set it with 'dotnet user-secrets set' or the Lis__Database__ConnectionString environment variable.");

        using var scope = services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<ISecurityEventLog>();
        if (services.GetRequiredService<HostSecuritySettings>().AllowInsecureLocalDevelopment)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SecurityRelaxationActive, SecurityEvent.Error, Detail: "Lis:Security:AllowInsecureLocalDevelopment"), cancellationToken);
        }

        try
        {
            await DatabaseRoleGuard.EnforceAsync(postgres, cancellationToken);
            logger.LogInformation("database target {Target}", DatabaseRoleGuard.Describe(postgres));
        }
        catch (InvalidOperationException exception)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.RoleGuardViolation, SecurityEvent.Error, Detail: exception.Message), cancellationToken);
            throw;
        }
    }
}
