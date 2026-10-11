using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security;

namespace Platform.IntegrationTests.Support;

/// <summary>
/// Connection strings come from the environment, never from source: <c>LIS_TEST_OWNER_CONNECTION</c> (the
/// migration role, BYPASSRLS) and <c>LIS_TEST_APP_CONNECTION</c> (the runtime role, NOBYPASSRLS). Without both,
/// or when the server is unreachable, every database test is skipped and says so.
/// </summary>
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
    /// one process at a time, and prepares the schema once; nothing else coordinates them.
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
            return $"Set {OwnerVariable} and {AppVariable} to run the PostgreSQL integration tests.";
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

/// <summary>A fact that runs only when the test database is reachable.</summary>
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

/// <summary>Migrates once as the owner role, truncates the tables, and hands out scoped service providers for the runtime role.</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private ServiceProvider? _appProvider;

    public ServiceProvider AppProvider => _appProvider ?? throw new InvalidOperationException("The fixture is not initialised.");

    public async Task InitializeAsync()
    {
        if (TestDatabase.SkipReason is not null)
        {
            return;
        }

        await TestDatabase.PrepareOnceAsync(async () =>
        {
            await using var owner = CreateOwnerProvider();
            using var scope = owner.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();
            await TruncateAsync(context);
        });

        _appProvider = new ServiceCollection().AddInfrastructure(TestDatabase.AppConnection!).BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_appProvider is not null)
        {
            await _appProvider.DisposeAsync();
        }
    }

/// <summary>
    /// Owner provider for migrations. Model changes of modules still in progress may precede their migration; the
    /// migrator then refuses by default (EF 9 PendingModelChangesWarning). Tests migrate what exists, and
    /// MigrationRoundTripTests stays the gate that a migration matches the model.
    /// </summary>
    public static ServiceProvider CreateOwnerProvider() =>
        new ServiceCollection()
            .AddInfrastructure(TestDatabase.OwnerConnection!)
            .ConfigureDbContext<AppDbContext>((_, options) => options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)))
            .BuildServiceProvider();

    /// <summary>A runtime-role scope bound to a tenant (or unresolved when <paramref name="tenantId"/> is null) with an optional actor, branch and correlation id.</summary>
    public IServiceScope AppScope(Guid? tenantId, Guid? userId = null, string displayName = "Integration tester", Guid? branchId = null, string? correlationId = null)
    {
        var scope = AppProvider.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContextAccessor>();
        if (tenantId is { } id)
        {
            tenant.SetTenant(id);
        }

        if (userId is { } user)
        {
            scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().Set(user, null, displayName, branchId);
        }

        if (correlationId is not null)
        {
            scope.ServiceProvider.GetRequiredService<RequestContextAccessor>().Set(correlationId, "127.0.0.1", "integration-tests");
        }

        return scope;
    }

    /// <summary>Runs on an owner-role context: lis_owner has BYPASSRLS and owns the tables, so no elevation is needed.</summary>
    public static Task TruncateAsync(AppDbContext context) =>
        context.Database.ExecuteSqlRawAsync(TestDatabase.TruncateAllSql);
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
