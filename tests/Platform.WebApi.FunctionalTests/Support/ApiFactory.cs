using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Organization;
using Platform.Application.Tenancy;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Infrastructure;
using Platform.Infrastructure.Notifications;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;

namespace Platform.WebApi.FunctionalTests.Support;

/// <summary>Connection strings from the environment only; without them every functional test is skipped with a reason.</summary>
internal static class TestDatabase
{
    public const string OwnerVariable = "LIS_TEST_OWNER_CONNECTION";
    public const string AppVariable = "LIS_TEST_APP_CONNECTION";

    public static string? OwnerConnection { get; } = Environment.GetEnvironmentVariable(OwnerVariable);

    public static string? AppConnection { get; } = Environment.GetEnvironmentVariable(AppVariable);

    public static string? SkipReason { get; } = Probe();

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static NpgsqlConnection? _lockConnection;
    private static bool _prepared;

    /// <summary>
    /// Test processes share one database. Each process holds a session advisory lock for its lifetime, so suites run
    /// one process at a time, and prepares the schema once; factories inside one process never truncate under each other.
    /// </summary>
    /// <summary>
    /// Empties every table of the application schemas (CASCADE), whatever modules exist, so fixtures never list tables. Tables
    /// whose rows are never deleted refuse TRUNCATE by trigger (migration AppendOnlyTruncateGuard); the reset disables those
    /// triggers explicitly, inside the same transaction, and enables them again.
    /// </summary>
    public const string TruncateAllSql = """
        DO $$
        DECLARE tables text; guard record;
        BEGIN
            SELECT string_agg(format('%I.%I', table_schema, table_name), ', ') INTO tables
            FROM information_schema.tables
            WHERE table_schema IN ('identity', 'app', 'ops', 'audit', 'read', 'saas') AND table_type = 'BASE TABLE' AND table_name NOT LIKE '\_\_%';
            IF tables IS NOT NULL THEN
                FOR guard IN SELECT format('ALTER TABLE %I.%I %%s TRIGGER %I', n.nspname, c.relname, t.tgname) AS statement
                             FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                             WHERE NOT t.tgisinternal AND (t.tgtype & 32) <> 0 AND n.nspname IN ('identity', 'app', 'ops', 'audit', 'read', 'saas') LOOP
                    EXECUTE format(guard.statement, 'DISABLE');
                END LOOP;
                EXECUTE 'TRUNCATE ' || tables || ' CASCADE';
                FOR guard IN SELECT format('ALTER TABLE %I.%I %%s TRIGGER %I', n.nspname, c.relname, t.tgname) AS statement
                             FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                             WHERE NOT t.tgisinternal AND (t.tgtype & 32) <> 0 AND n.nspname IN ('identity', 'app', 'ops', 'audit', 'read', 'saas') LOOP
                    EXECUTE format(guard.statement, 'ENABLE');
                END LOOP;
            END IF;
        END $$;
        """;

    public static async Task PrepareOnceAsync(Func<Task> prepare)
    {
        await Gate.WaitAsync();
        try
        {
            if (_prepared)
            {
                return;
            }

            _lockConnection = new NpgsqlConnection(OwnerConnection);
            await _lockConnection.OpenAsync();
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('lis:test-database'))", _lockConnection))
            {
                await command.ExecuteNonQueryAsync();
            }

            await prepare();
            _prepared = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string? Probe()
    {
        if (string.IsNullOrWhiteSpace(OwnerConnection) || string.IsNullOrWhiteSpace(AppConnection))
        {
            return $"Set {OwnerVariable} and {AppVariable} to run the functional tests.";
        }

        try
        {
            using var connection = new NpgsqlConnection(OwnerConnection);
            connection.Open();
            return null;
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            return $"PostgreSQL is not reachable with {OwnerVariable}: {exception.Message}";
        }
    }
}

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (TestDatabase.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>A clock the tests can move. It starts at the machine time because JWT lifetime validation uses the machine clock.</summary>
public sealed class TestClock : IClock
{
#pragma warning disable RS0030 // the test clock must start aligned with the token validator's clock
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
#pragma warning restore RS0030

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// The real pipeline over the real database. Secrets are generated per factory and injected through configuration;
/// the environment name is neutral so <c>appsettings.Development.json</c> and its relaxation flag do not apply.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Correct-Horse-Battery-Staple-9";
    // One key set per test process: every factory is a replica of the same deployment and shares the stored envelope keys.
    private static readonly string SigningKeyPem = CreateSigningKey();
    private static readonly string SubjectKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public TestClock Clock { get; } = new();

    /// <summary>Every notification the host would have sent (the one-time codes of CP-03 included); replaces the logging sender.</summary>
    public RecordingNotificationSender Notifications { get; } = new();

    /// <summary>Configuration added at Build(): a test may turn the outbox loop on with <c>Lis:Outbox:Enabled = true</c> (off by default here).</summary>
    public Dictionary<string, string?> Overrides { get; } = new(StringComparer.Ordinal);

    public async Task InitializeAsync()
    {
        if (TestDatabase.SkipReason is not null)
        {
            return;
        }

        await TestDatabase.PrepareOnceAsync(async () =>
        {
            // Model changes of modules still in progress may precede their migration (EF 9 PendingModelChangesWarning); MigrationRoundTripTests is the gate.
            await using var owner = new ServiceCollection()
                .AddInfrastructure(TestDatabase.OwnerConnection!)
                .ConfigureDbContext<AppDbContext>((_, options) => options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)))
                .BuildServiceProvider();
            using var scope = owner.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync(TestDatabase.TruncateAllSql);
        });
    }

    public new Task DisposeAsync()
    {
        base.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers a user with a membership (and, when <paramref name="permissions"/> is given, a role holding them) in
    /// <paramref name="tenantId"/> or a fresh tenant, through the production services; returns the username and tenant.
    /// </summary>
    public async Task<(string Username, Guid TenantId, Guid UserId)> SeedUserAsync(string? password = null, Guid? tenantId = null, IReadOnlyList<string>? permissions = null, string displayName = "Functional Tester", string? mobile = null, IReadOnlyList<Guid>? branchIds = null)
    {
        var tenant = tenantId ?? Guid.CreateVersion7();
        var username = $"u.{Guid.CreateVersion7():N}"[..24];
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContextAccessor>().SetTenant(tenant);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = PlatformUser.Register(username, $"{username}@example.test", displayName, hasher.Hash(password ?? Password), Clock.UtcNow, mobile);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        var membership = TenantMembership.Create(tenant, user.Id, $"F-{username[^6..]}", new LocalizedText(displayName, "مختبر وظيفي"), "en", Clock.UtcNow);
        if (branchIds is { Count: > 0 })
        {
            membership.AssignScope(branchIds, []);
        }

        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(membership);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        if (permissions is { Count: > 0 })
        {
            var role = Role.Create(new LocalizedText($"role-{username}", $"دور-{username}"), permissions, Clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<IRoleRepository>().Add(role);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);   // the tenant stamp lands here, before the membership references the role
            membership.AssignRole(role, Clock.UtcNow);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        return (username, tenant, user.Id);
    }

    public async Task<PlatformUser> LoadUserAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().GetForUpdateAsync(userId, CancellationToken.None))!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("FunctionalTests");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Lis:Database:ConnectionString"] = TestDatabase.AppConnection,
                ["Lis:Jwt:Issuer"] = "https://tests.lis.local",
                ["Lis:Jwt:Audience"] = "platform-api",
                ["Lis:Jwt:SigningKey"] = SigningKeyPem,
                ["Lis:Jwt:SubjectEncryptionKey"] = SubjectKey,
                ["Lis:DataProtection:MasterKey"] = MasterKey,
                ["Lis:Security:AllowedOrigins:0"] = "http://localhost",
                ["Lis:Outbox:Enabled"] = "false",   // the loop stays idle unless a test turns it on through Overrides
                ["Lis:Jobs:WalletCharge:Enabled"] = "false",   // no background wallet charges under the tests' balances; SA-05 "Run charges now" still runs the pass
            };
            foreach (var (key, value) in Overrides)
            {
                settings[key] = value;
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton(Clock);
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton(Notifications);
            if (!Overrides.ContainsKey(NotificationOptions.PickupDirectoryKey))
            {
                // A test that configures a pickup directory exercises the production sender (NotificationDeliveryFlowTests).
                services.RemoveAll<INotificationSender>();
                services.AddSingleton<INotificationSender>(Notifications);
            }
        });
    }

    private static string CreateSigningKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }
}
