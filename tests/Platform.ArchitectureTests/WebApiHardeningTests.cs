using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Platform.Application.Identity;
using Platform.Application.Settings.Commands;
using Platform.Application.Settings.Queries;
using Platform.ArchitectureTests.Support;
using Platform.Contracts.Common;
using Platform.Contracts.Settings;
using Platform.Infrastructure.Security;
using Platform.WebApi.Security;
using Platform.WebApi.Startup;

namespace Platform.ArchitectureTests;

/// <summary>
/// WebApiHardeningTests and friends (01 §8): controllers inject only ISender, no role attributes, no CORS, the
/// middleware order is the specified one, no business data in custom headers, loopback-only binds, no optional
/// envelope mode, and no committed configuration above the 15-minute token ceiling.
/// </summary>
public sealed class WebApiHardeningTests
{
    private static readonly string WebApiFolder = Path.Combine(SolutionAssemblies.RepositoryRoot, "src", "Platform.WebApi");
    private static readonly string[] ExpectedPipeline =
    [
        "ForwardedHeaders", "Correlation", "SecurityHeaders", "ExceptionHandler", "Authentication", "CredentialAccountKey",
        "RateLimiter", "AuthOriginCheck", "TenantResolution", "MfaEnrollmentGate", "PasswordChangeGate", "Authorization", "Endpoints",
    ];
    private static readonly string[] AllowedCustomHeaders = ["X-Requested-With", "X-Correlation-Id", "X-Forwarded", "Idempotency-Key", "X-Powered-By", "X-Frame-Options", "X-Content-Type-Options"];

    private static IEnumerable<string> Sources => Directory.EnumerateFiles(WebApiFolder, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void Controllers_inject_only_ISender()
    {
        var controllers = SolutionAssemblies.WebApiAssembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract).ToList();
        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            foreach (var constructor in controller.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                var offenders = constructor.GetParameters().Where(parameter => parameter.ParameterType != typeof(ISender)).Select(parameter => parameter.ParameterType.Name).ToList();
                Assert.True(offenders.Count == 0, $"{controller.Name} injects {string.Join(", ", offenders)}; controllers inject only ISender");
            }
        }
    }

    [Fact]
    public void No_role_attribute_no_cors_and_no_business_data_in_custom_headers()
    {
        foreach (var path in Sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("Roles =", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddCors", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UseCors", source, StringComparison.Ordinal);

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(source, "\"(X-[A-Za-z0-9-]+)\""))
            {
                var header = match.Groups[1].Value;
                Assert.True(AllowedCustomHeaders.Any(allowed => header.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)), $"{Path.GetFileName(path)} reads or writes custom header {header}; business data travels in typed command properties (pitfall P-16)");
            }
        }
    }

    [Fact]
    public void The_pipeline_order_is_the_specified_sequence_and_Program_walks_it()
    {
        Assert.Equal(ExpectedPipeline, PipelineOrder.Sequence);

        var program = File.ReadAllText(Path.Combine(WebApiFolder, "Program.cs"));
        Assert.Contains("foreach (var step in PipelineOrder.Sequence)", program, StringComparison.Ordinal);
        Assert.Contains("Filters.Add<CredentialTransportFilter>()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("app.UseCors", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bind_guard_refuses_every_non_loopback_bind()
    {
        Assert.Empty(StartupGuards.InspectBindings(["http://127.0.0.1:5100", "http://localhost:5100", "http://[::1]:5100"]));
        Assert.NotEmpty(StartupGuards.InspectBindings(["http://0.0.0.0:5100"]));
        Assert.NotEmpty(StartupGuards.InspectBindings(["http://+:5100"]));
        Assert.NotEmpty(StartupGuards.InspectBindings(["http://*:5100"]));
        Assert.NotEmpty(StartupGuards.InspectBindings(["https://lis.example.com"]));
    }

    [Fact]
    public void An_optional_envelope_mode_key_refuses_to_start()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:CredentialEnvelope:Mode"] = "Optional" }).Build();
        Assert.Throws<SecurityConfigurationException>(() => StartupGuards.RefuseForbiddenKeys(configuration));

        var clean = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "x" }).Build();
        StartupGuards.RefuseForbiddenKeys(clean);
    }

    [Fact]
    public void No_committed_configuration_exceeds_the_access_token_ceiling()
    {
        foreach (var path in Directory.EnumerateFiles(WebApiFolder, "appsettings*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Lis", out var lis) && lis.TryGetProperty("Security", out var security) && security.TryGetProperty("Session", out var session) && session.TryGetProperty("AccessTokenLifetime", out var lifetime))
            {
                Assert.True(TimeSpan.Parse(lifetime.GetString()!, System.Globalization.CultureInfo.InvariantCulture) <= SessionPolicy.MaxAccessTokenLifetime, $"{Path.GetFileName(path)} sets an access token lifetime above 15 minutes");
            }

            Assert.DoesNotContain("SigningKey\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("MasterKey\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            // Isolation (CLAUDE.md §1): no generic key that another application on this host also sets.
            Assert.False(document.RootElement.TryGetProperty("ConnectionStrings", out _), $"{Path.GetFileName(path)} uses the generic ConnectionStrings section");
            Assert.False(document.RootElement.TryGetProperty("Jwt", out _), $"{Path.GetFileName(path)} uses the generic Jwt section");
        }
    }

    [Fact]
    public void The_host_reads_no_generic_configuration_key()
    {
        // Every LIS key lives under the Lis root; the generic names belong to other applications on this machine.
        foreach (var path in Sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("GetConnectionString(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"ConnectionStrings:", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Jwt:", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"DataProtection:", source, StringComparison.Ordinal);
        }

        Assert.Equal("Lis", SecurityConfiguration.Root);
        Assert.Contains("\"Kestrel\"", File.ReadAllText(Path.Combine(WebApiFolder, "appsettings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_password_named_contract_property_carries_the_credential_field_attribute()
    {
        // CredentialFieldTests (01 §8, D-03): scope comes from the attribute, so the attribute must be everywhere a credential is.
        var offenders = SolutionAssemblies.ContractsAssembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => (type, property)))
            .Where(entry => entry.property.PropertyType == typeof(string)
                            && (entry.property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || entry.property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || entry.property.Name.Contains("Pin", StringComparison.Ordinal))
                            && entry.property.GetCustomAttribute<CredentialFieldAttribute>(inherit: true) is null)
            .Select(entry => $"{entry.type.Name}.{entry.property.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Credential-bearing properties without [CredentialField]: " + string.Join(", ", offenders));
        Assert.Contains(SolutionAssemblies.ContractsAssembly.GetTypes(), type => type.GetProperties().Any(property => property.GetCustomAttribute<CredentialFieldAttribute>() is not null));
    }

    [Fact]
    public void Elevation_happens_only_at_allow_listed_sites_and_always_in_a_using()
    {
        // ElevationScopingTests (01 §8, pitfall P-30).
        string[] allowed = ["SessionPrincipalQuery.cs", "TenantProvisioning.cs"];   // principal query + membership directory (sign-in tenant selection); tenant provisioning (one transaction + the BR-25 catalogue copy)
        var sources = Directory.EnumerateFiles(Path.Combine(SolutionAssemblies.RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var path in sources)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(".Elevate()", StringComparison.Ordinal) || lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) || lines[i].Contains("IDisposable Elevate", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(allowed.Contains(Path.GetFileName(path)), $"{Path.GetFileName(path)}:{i + 1} elevates outside the allow-list");
                Assert.True(lines[i].Contains("using (", StringComparison.Ordinal) || lines[i].Contains("using var", StringComparison.Ordinal), $"{Path.GetFileName(path)}:{i + 1} elevates outside a using");
            }
        }
    }

    /// <summary>
    /// Validation F-17: an unreadable body is reported only after the action's requests are authorized. That rests on the
    /// map of each action to the commands and queries it sends, so every action must name at least one.
    /// </summary>
    [Fact]
    public void Every_controller_action_names_the_requests_it_sends()
    {
        var actions = SolutionAssemblies.WebApiAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(controller => controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.True(actions.Count > 100, $"only {actions.Count} actions found");

        var unmapped = actions.Where(action => ActionRequestPolicies.RequestTypesOf(action).Count == 0).Select(action => $"{action.DeclaringType!.Name}.{action.Name}").ToList();
        Assert.True(unmapped.Count == 0, "Actions that send no recognisable command or query: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void The_action_map_follows_async_bodies_and_controller_helpers_and_ignores_actions_without_requests()
    {
        // gate self-test: the scanner finds requests built in the action, in its state machine and in a private helper
        Assert.Equal([typeof(UpdateTenantSettingsCommand)], ActionRequestPolicies.RequestTypesOf(typeof(MapFixtureController).GetMethod(nameof(MapFixtureController.Put))!));
        Assert.Equal([typeof(GetTenantSettingsQuery)], ActionRequestPolicies.RequestTypesOf(typeof(MapFixtureController).GetMethod(nameof(MapFixtureController.ViaHelper))!));
        Assert.Empty(ActionRequestPolicies.RequestTypesOf(typeof(MapFixtureController).GetMethod(nameof(MapFixtureController.Nothing))!));
    }

    /// <summary>01 §3.4 and validation F-17: Authorization is registered before Validation, so a refusal never discloses a request's rules.</summary>
    [Fact]
    public void The_request_pipeline_authorizes_before_it_validates()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Platform.Application.ApplicationServiceRegistration.AddApplication(services);
        var behaviors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        var authorization = behaviors.IndexOf(typeof(Platform.Application.Behaviors.AuthorizationBehavior<,>));
        var validation = behaviors.IndexOf(typeof(Platform.Application.Behaviors.ValidationBehavior<,>));
        Assert.True(authorization >= 0 && validation >= 0, "both behaviors are registered");
        Assert.True(authorization < validation, "AuthorizationBehavior must run before ValidationBehavior");

        var problem = File.ReadAllText(Path.Combine(WebApiFolder, "Middleware", "ProblemResponse.cs"));
        Assert.Contains("ActionRequestPolicies.Demand(", problem, StringComparison.Ordinal);   // the MVC body check defers to the same decision
    }

    private sealed class MapFixtureController(ISender sender) : ControllerBase
    {
        public async Task<IActionResult> Put(UpdateTenantSettingsRequest request, CancellationToken cancellationToken) =>
            Ok(await sender.Send(new UpdateTenantSettingsCommand(request.Settings), cancellationToken));

        public Task<IActionResult> ViaHelper(CancellationToken cancellationToken) => ReadAsync(cancellationToken);

        public IActionResult Nothing() => Ok();

        private async Task<IActionResult> ReadAsync(CancellationToken cancellationToken) => Ok(await sender.Send(new GetTenantSettingsQuery(), cancellationToken));
    }
}
