using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using Platform.Contracts.Reporting;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The work amount report over the real API (change request 2026-10-13 item 3; intake LIS-INT-REP-001 §16 S-W12…S-W14):
/// a reporter reads today's income of the session branch with totals, a member whose role may report on today only is
/// refused a period starting yesterday (REP-001, 403), the CSV needs <c>reporting.export</c>, and a grouping the report
/// does not know is a validation problem.
/// </summary>
public sealed class WorkAmountReportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ReportPath = "/api/v1/reports/work-amount";

    [DatabaseFact]
    public async Task S_W12_today_s_income_of_the_session_branch_is_reported_with_a_grand_total()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var payer = Guid.CreateVersion7();
        var (reporter, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["reporting.view"], branchIds: [branch]);
        var visit = await InsertVisitAsync(tenant, branch, payer, factory.Clock.UtcNow);
        await InsertLineAsync(tenant, visit, "CBC", "Ordered", 100m, 70m, 30m, 0m, 1);
        await InsertLineAsync(tenant, visit, "GLU", "Cancelled", 40m, 40m, 0m, 0m, 2);
        var client = await SignedInClientAsync(reporter);

        var report = await client.GetFromJsonAsync<WorkAmountReport>($"{ReportPath}?groupBy=Payer");

        Assert.NotNull(report);
        Assert.Equal(("Payer", branch), (report.GroupBy, report.BranchId));
        Assert.Equal(report.FromDay, report.ToDay);
        var row = Assert.Single(report.Rows);
        Assert.Equal((payer.ToString(), 1, 1, 100m, 0m, 70m, 30m, 100m), (row.Key, row.Visits, row.Services, row.Gross, row.Discount, row.CashIncome, row.CreditIncome, row.Net));
        Assert.Equal((1, 1, 100m), (report.Totals.Visits, report.Totals.Services, report.Totals.Net));
        Assert.Equal("Functional Laboratory", report.Letterhead.LabNameEn);   // LIS-CR-005: the test host gives every seeded laboratory a client record

        // by day: one row keyed by the report's own day
        var byDay = await client.GetFromJsonAsync<WorkAmountReport>($"{ReportPath}?groupBy=Day");
        Assert.Equal(byDay!.FromDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Assert.Single(byDay.Rows).Key);

        // an unknown grouping is a shape problem
        var unknown = await client.GetAsync($"{ReportPath}?groupBy=Nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [DatabaseFact]
    public async Task S_W13_a_member_limited_to_today_is_refused_a_period_starting_yesterday_with_REP_001()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (limited, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["reporting.view"], branchIds: [branch]);
        await LimitRoleToTodayAsync(tenant, limited);
        var client = await SignedInClientAsync(limited);
        var today = DateOnly.FromDateTime(factory.Clock.UtcNow.UtcDateTime);
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var refused = await client.GetAsync($"{ReportPath}?from={yesterday}&groupBy=Day");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("REP-001", problem.RootElement.GetProperty("code").GetString());

        // today itself stays available, and the export applies the same limit
        var today200 = await client.GetAsync($"{ReportPath}?groupBy=Day");
        Assert.Equal(HttpStatusCode.OK, today200.StatusCode);
    }

    [DatabaseFact]
    public async Task S_W14_the_csv_export_needs_reporting_export_and_is_a_utf8_file_with_the_total()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["reporting.view"], branchIds: [branch]);
        var (exporter, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["reporting.view", "reporting.export"], branchIds: [branch]);
        var visit = await InsertVisitAsync(tenant, branch, Guid.CreateVersion7(), factory.Clock.UtcNow);
        await InsertLineAsync(tenant, visit, "TSH", "Ordered", 150m, 150m, 0m, 0m, 1);

        var refused = await (await SignedInClientAsync(viewer)).GetAsync($"{ReportPath}/export?groupBy=Service");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var export = await (await SignedInClientAsync(exporter)).GetAsync($"{ReportPath}/export?groupBy=Service");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("work-amount-", export.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        var lines = Encoding.UTF8.GetString(bytes.AsSpan(3)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Group,Group (Arabic),Code,Visits,Services,Gross,Discount,Cash income,Credit income,Net", lines[0]);
        Assert.Equal("Service TSH,خدمة TSH,TSH,1,1,150.00,0.00,150.00,0.00,150.00", lines[1]);
        Assert.Equal("Total,الإجمالي,,1,1,150.00,0.00,150.00,0.00,150.00", lines[2]);
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    /// <summary>The seeded user's role (named after the user by <c>ApiFactory.SeedUserAsync</c>) may report on today only (CP-37 special right).</summary>
    private static async Task LimitRoleToTodayAsync(Guid tenant, string username)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE app.roles SET special_report_days_limit = 0 WHERE tenant_id = @tenant AND name_en = @name", connection);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("name", $"role-{username}");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    // ---- Registration rows on their MODULES.md §5 columns, as the owner role; every other NOT NULL column without a
    // default takes a type default (a domain-checked text column its first allowed value), as ReportingFlowTests does.

    private static async Task<Guid> InsertVisitAsync(Guid tenant, Guid branch, Guid payer, DateTimeOffset registeredAtUtc)
    {
        var id = Guid.CreateVersion7();
        await InsertAsync("app", "visits", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = id,
            ["accession"] = $"ACC-WF-{id.ToString("N")[^10..].ToUpperInvariant()}",
            ["patient_id"] = Guid.CreateVersion7(),
            ["branch_id"] = branch,
            ["payer_id"] = payer,
            ["contract_tier_id"] = Guid.CreateVersion7(),
            ["status"] = "Open",
            ["registered_at_utc"] = registeredAtUtc,
            ["registered_by_user_id"] = Guid.CreateVersion7(),
            ["urgent"] = false,
            ["total_list_price"] = 0m,
            ["discount_total"] = 0m,
            ["patient_share_total"] = 0m,
            ["payer_share_total"] = 0m,
        });
        return id;
    }

    private static async Task InsertLineAsync(Guid tenant, Guid visit, string code, string status, decimal listPrice, decimal patientShare, decimal payerShare, decimal discount, int sequence)
    {
        var cancelled = status == "Cancelled";
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertAsync("app", "visit_lines", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = Guid.CreateVersion7(),
            ["visit_id"] = visit,
            ["service_id"] = Guid.CreateVersion7(),
            ["service_code"] = code,
            ["service_name_en"] = $"Service {code}",
            ["service_name_ar"] = $"خدمة {code}",
            ["service_type"] = "Test",
            ["kind"] = "Service",
            ["status"] = status,
            ["list_price"] = listPrice,
            ["patient_share"] = patientShare,
            ["payer_share"] = payerShare,
            ["discount"] = discount,
            ["origin"] = "Manual",
            ["sequence"] = sequence,
            ["ordered_at_utc"] = now,
            ["ordered_by_user_id"] = Guid.CreateVersion7(),
            ["cancelled_at_utc"] = cancelled ? now : null,
            ["cancellation_reason"] = cancelled ? "functional test" : null,
            ["cancelled_by_user_id"] = cancelled ? Guid.CreateVersion7() : null,
        });
    }

    private static async Task InsertAsync(string schema, string table, IReadOnlyDictionary<string, object?> values)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();

        var checks = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = @table::regclass AND contype = 'c'", connection))
        {
            command.Parameters.AddWithValue("table", $"{schema}.{table}");
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                checks.Add(reader.GetString(0));
            }
        }

        var row = values.ToDictionary(pair => pair.Key, pair => Parameter(pair.Key, pair.Value), StringComparer.Ordinal);
        const string Columns = """
            SELECT column_name, data_type, udt_name FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table AND is_nullable = 'NO' AND column_default IS NULL AND is_generated = 'NEVER' AND is_identity = 'NO'
            """;
        await using (var command = new NpgsqlCommand(Columns, connection))
        {
            command.Parameters.AddWithValue("schema", schema);
            command.Parameters.AddWithValue("table", table);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = reader.GetString(0);
                if (!row.ContainsKey(column))
                {
                    row[column] = Default(column, reader.GetString(1), reader.GetString(2), checks);
                }
            }
        }

        var names = row.Keys.ToList();
        await using var insert = new NpgsqlCommand($"INSERT INTO {schema}.{table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(name => "@" + name))})", connection);
        insert.Parameters.AddRange(names.Select(name => row[name]).ToArray());
        await insert.ExecuteNonQueryAsync();
    }

    private static NpgsqlParameter Parameter(string column, object? value) => value switch
    {
        null => new NpgsqlParameter(column, DBNull.Value),
        DateTimeOffset instant => new NpgsqlParameter(column, NpgsqlDbType.TimestampTz) { Value = instant.ToUniversalTime() },
        decimal number => new NpgsqlParameter(column, NpgsqlDbType.Numeric) { Value = number },
        _ => new NpgsqlParameter(column, value),
    };

    private static NpgsqlParameter Default(string column, string dataType, string udtName, IReadOnlyList<string> checks) => dataType switch
    {
        "uuid" => new NpgsqlParameter(column, Guid.CreateVersion7()),
        "boolean" => new NpgsqlParameter(column, false),
        "integer" or "smallint" or "bigint" => new NpgsqlParameter(column, NpgsqlDbType.Integer) { Value = 0 },
        "numeric" or "double precision" or "real" => new NpgsqlParameter(column, NpgsqlDbType.Numeric) { Value = 0m },
        "timestamp with time zone" => new NpgsqlParameter(column, NpgsqlDbType.TimestampTz) { Value = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        "date" => new NpgsqlParameter(column, NpgsqlDbType.Date) { Value = new DateOnly(2026, 1, 1) },
        "jsonb" => new NpgsqlParameter(column, NpgsqlDbType.Jsonb) { Value = "{}" },
        "ARRAY" when udtName == "_uuid" => new NpgsqlParameter(column, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = Array.Empty<Guid>() },
        "ARRAY" => new NpgsqlParameter(column, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Array.Empty<string>() },
        _ => new NpgsqlParameter(column, FirstAllowed(column, checks) ?? "x"),
    };

    private static string? FirstAllowed(string column, IReadOnlyList<string> checks)
    {
        var pattern = new Regex($@"\b{Regex.Escape(column)}\b", RegexOptions.CultureInvariant);
        var check = checks.FirstOrDefault(definition => pattern.IsMatch(definition) && definition.Contains('\'', StringComparison.Ordinal));
        return check is null ? null : Regex.Match(check, "'([^']*)'", RegexOptions.CultureInvariant).Groups[1].Value;
    }
}
