using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Platform.Application;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security.Tokens;
using Platform.WebApi.Middleware;
using Platform.WebApi.Security;
using Platform.WebApi.Startup;

// Composition only (01 §3.2). Kestrel binds loopback HTTP; TLS terminates at the reverse proxy (02 §2.1).
// Every setting is read from the final configuration of the built host, never captured before Build(), and only
// from the Lis root: this host carries other applications' generic variables (CLAUDE.md §1).
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(provider => SecurityConfiguration.ReadHost(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(provider => SecurityConfiguration.ReadOptions(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(provider => SecurityConfiguration.ReadSessionPolicy(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(provider => SecurityConfiguration.ReadOutbox(provider.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService<OutboxHostedService>();   // 01 §7; Lis:Outbox:Enabled=false keeps the loop idle

builder.Services.AddApplication();
builder.Services.AddInfrastructure(provider =>
    SecurityConfiguration.ReadConnectionString(provider.GetRequiredService<IConfiguration>())   // user-secrets or environment; appsettings carries no password (02 §6.4)
    ?? throw new InvalidOperationException($"{SecurityConfiguration.ConnectionStringKey} is required. Set it with 'dotnet user-secrets set' or the Lis__Database__ConnectionString environment variable."));
builder.Services.AddSecurity();
builder.Services.AddPlatformAuthentication();
builder.Services.AddPlatformRateLimiting();

builder.Services
    .AddControllers(options => options.Filters.Add<CredentialTransportFilter>())   // D-03: global, no opt-out
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ProblemResponse.InvalidModelState);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;   // loopback proxy trusted by default; KnownNetworks only through configuration
});

var app = builder.Build();

await StartupGuards.RunAsync(app);   // forbidden keys, ConfigGuard, bind guard, relaxation event, database target and role guard

// The pipeline is the sequence of 02 §5.4, applied from data so it cannot drift (WebApiHardeningTests).
foreach (var step in PipelineOrder.Sequence)
{
    switch (step)
    {
        case PipelineOrder.ForwardedHeaders: app.UseForwardedHeaders(); break;
        case PipelineOrder.Correlation: app.UseMiddleware<CorrelationMiddleware>(); break;
        case PipelineOrder.SecurityHeaders: app.UseMiddleware<SecurityHeadersMiddleware>(); break;
        case PipelineOrder.ExceptionHandler: app.UseExceptionHandler(); break;
        case PipelineOrder.Authentication: app.UseAuthentication(); break;
        case PipelineOrder.CredentialAccountKey: app.UseMiddleware<CredentialAccountKeyMiddleware>(); break;
        case PipelineOrder.RateLimiter: app.UseRateLimiter(); break;
        case PipelineOrder.AuthOriginCheck: app.UseMiddleware<AuthOriginCheckMiddleware>(); break;
        case PipelineOrder.TenantResolution: app.UseMiddleware<TenantResolutionMiddleware>(); break;
        case PipelineOrder.MfaEnrollmentGate: app.UseMiddleware<MfaEnrollmentGateMiddleware>(); break;
        case PipelineOrder.Authorization: app.UseAuthorization(); break;
        case PipelineOrder.Endpoints:
            app.MapControllers();
            app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous().DisableRateLimiting();
            app.MapGet("/health/ready", async (AppDbContext db, ILoggerFactory loggers, CancellationToken ct) =>
            {
                try
                {
                    var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                    return pending.Count > 0
                        ? Results.Problem(statusCode: 503, title: $"{pending.Count} migration(s) pending.")
                        : Results.Ok(new { status = "ready" });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    loggers.CreateLogger("Readiness").LogWarning(exception, "readiness check failed");
                    return Results.Problem(statusCode: 503, title: "The database is not reachable.");
                }
            }).AllowAnonymous().DisableRateLimiting();
            app.MapGet("/.well-known/jwks.json", (SigningKeys keys) => Results.Json(new { keys = new[] { keys.PublicJwk } })).AllowAnonymous().DisableRateLimiting();
            break;
        default: throw new InvalidOperationException($"Unknown pipeline step '{step}'.");
    }
}

app.Run();

#pragma warning disable CA1050 // the generated entry point lives in the global namespace; exposed for WebApplicationFactory and the architecture tests
public partial class Program;
#pragma warning restore CA1050
