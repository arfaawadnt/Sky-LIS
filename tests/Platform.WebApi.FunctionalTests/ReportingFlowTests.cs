using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using Platform.Contracts.Account;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Release-1 scope of intake LIS-INT-REP-001 over the real API: the Super Admin dashboard (SA-03) shows the clients'
/// registrations and served patients of the period through the platform-only activity function (S-A5), a laboratory
/// cannot reach it (S-A6), and the branch dashboard (CP-05) carries the result figures once Results is migrated (S-D4).
/// </summary>
public sealed class ReportingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string PlatformDashboardPath = "/api/v1/platform/dashboard";
    private const string BranchDashboardPath = "/api/v1/dashboard/branch";

    [DatabaseFact]
    public async Task S_A5_the_platform_dashboard_counts_the_registrations_and_patients_of_the_clients()
    {
        var lab = Guid.CreateVersion7();
        var patient = Guid.CreateVersion7();
        var installed = await ActivityInstalledAsync();
        await factory.EnsureClientRecordAsync(lab);   // every tenant_id references saas.tenants (DbReviewTenantKeys)
        if (installed)
        {
            await InsertVisitAsync(lab, patient, factory.Clock.UtcNow);
            await InsertVisitAsync(lab, patient, factory.Clock.UtcNow);
        }

        var (admin, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["platform.view"], displayName: "Operations Admin");
        var dashboard = await (await SignedInClientAsync(admin)).GetFromJsonAsync<PlatformDashboard>($"{PlatformDashboardPath}?period=year");

        if (!installed)
        {
            Assert.Equal(new ActivityTotals(0, 0, 0), dashboard!.Activity);
            return;
        }

        Assert.True(dashboard!.Activity.Registrations >= 2, $"registrations {dashboard.Activity.Registrations}");
        Assert.True(dashboard.Activity.ServedPatients >= 1, $"patients {dashboard.Activity.ServedPatients}");
        if (dashboard.RegistrationsByClient.Count < 10)
        {
            Assert.Contains(dashboard.RegistrationsByClient, item => item.TenantId == lab && item.Registrations == 2);
        }
    }

    [DatabaseFact]
    public async Task S_A6_a_laboratory_user_cannot_read_the_platform_dashboard()
    {
        var (labUser, _, _) = await factory.SeedUserAsync(tenantId: Guid.CreateVersion7(), permissions: ["platform.view", "dashboard.view"]);

        var refused = await (await SignedInClientAsync(labUser)).GetAsync($"{PlatformDashboardPath}?period=today");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [DatabaseFact]
    public async Task S_D4_the_branch_dashboard_carries_the_result_figures_when_results_exist()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var resultsExist = await ExistsAsync("SELECT to_regclass('app.results') IS NOT NULL AND to_regclass('app.visits') IS NOT NULL");
        var (user, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["dashboard.view"], branchIds: [branch]);

        var dashboard = await (await SignedInClientAsync(user)).GetFromJsonAsync<BranchDashboard>(BranchDashboardPath);

        var expected = resultsExist ? 0 : (int?)null;
        Assert.Equal((expected, expected, expected), (dashboard!.Kpis.PendingResults, dashboard.Kpis.UnverifiedBacklog, dashboard.Kpis.DelayedTests));
    }

    private static Task<bool> ActivityInstalledAsync() =>
        ExistsAsync("SELECT to_regclass('app.visits') IS NOT NULL AND to_regprocedure('saas.client_activity_counts(timestamptz,timestamptz)') IS NOT NULL");

    private static async Task<bool> ExistsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() is true;
    }

    /// <summary>
    /// A visit of Registration on its MODULES.md §5 columns, as the owner role; every other NOT NULL column without a default
    /// takes a type default (a domain-checked text column its first allowed value).
    /// </summary>
    private static async Task InsertVisitAsync(Guid tenant, Guid patient, DateTimeOffset registeredAtUtc)
    {
        var id = Guid.CreateVersion7();
        var values = new Dictionary<string, NpgsqlParameter>(StringComparer.Ordinal)
        {
            ["tenant_id"] = new("tenant_id", tenant),
            ["id"] = new("id", id),
            ["accession"] = new("accession", $"ACC-FT-{id.ToString("N")[^10..].ToUpperInvariant()}"),
            ["patient_id"] = new("patient_id", patient),
            ["branch_id"] = new("branch_id", Guid.CreateVersion7()),
            ["status"] = new("status", "Open"),
            ["registered_at_utc"] = new("registered_at_utc", NpgsqlDbType.TimestampTz) { Value = registeredAtUtc.ToUniversalTime() },
            ["registered_by_user_id"] = new("registered_by_user_id", Guid.CreateVersion7()),
            ["urgent"] = new("urgent", false),
            ["total_list_price"] = new("total_list_price", 100m),
            ["discount_total"] = new("discount_total", NpgsqlDbType.Numeric) { Value = 0m },
            ["patient_share_total"] = new("patient_share_total", 100m),
            ["payer_share_total"] = new("payer_share_total", NpgsqlDbType.Numeric) { Value = 0m },
        };

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        var checks = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'app.visits'::regclass AND contype = 'c'", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                checks.Add(reader.GetString(0));
            }
        }

        const string Columns = """
            SELECT column_name, data_type FROM information_schema.columns
            WHERE table_schema = 'app' AND table_name = 'visits' AND is_nullable = 'NO' AND column_default IS NULL AND is_generated = 'NEVER' AND is_identity = 'NO'
            """;
        await using (var command = new NpgsqlCommand(Columns, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var (column, type) = (reader.GetString(0), reader.GetString(1));
                if (!values.ContainsKey(column))
                {
                    values[column] = type switch
                    {
                        "uuid" => new NpgsqlParameter(column, Guid.CreateVersion7()),
                        "boolean" => new NpgsqlParameter(column, false),
                        "integer" or "smallint" or "bigint" => new NpgsqlParameter(column, NpgsqlDbType.Integer) { Value = 0 },
                        "numeric" => new NpgsqlParameter(column, NpgsqlDbType.Numeric) { Value = 0m },
                        "timestamp with time zone" => new NpgsqlParameter(column, NpgsqlDbType.TimestampTz) { Value = registeredAtUtc.ToUniversalTime() },
                        _ => new NpgsqlParameter(column, FirstAllowed(column, checks) ?? "x"),
                    };
                }
            }
        }

        var names = values.Keys.ToList();
        await using var insert = new NpgsqlCommand($"INSERT INTO app.visits ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(name => "@" + name))})", connection);
        insert.Parameters.AddRange(names.Select(name => values[name]).ToArray());
        await insert.ExecuteNonQueryAsync();
    }

    private static string? FirstAllowed(string column, IReadOnlyList<string> checks)
    {
        var check = checks.FirstOrDefault(definition => Regex.IsMatch(definition, $@"\b{Regex.Escape(column)}\b") && definition.Contains('\'', StringComparison.Ordinal));
        return check is null ? null : Regex.Match(check, "'([^']*)'").Groups[1].Value;
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
