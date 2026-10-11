using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.Account;
using Platform.Application.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-REP-001 (Release 1): the SA-03 activity function counts registrations, served
/// patients and tests per client for the platform tenant only (S-A3, S-A4), and the CP-05 result figures count pending
/// results, the unverified backlog and the delayed tests of one branch (S-D3). Rows of Registration and Results are
/// written with raw SQL on their MODULES.md §5 columns; a source that is not migrated yet yields "not available".
/// Periods lie in 2001 so the cross-tenant counts never meet rows of other suites.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReportingModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Day = new(2001, 3, 1, 0, 0, 0, TimeSpan.Zero);
    // S-D3 writes its rows in its own month: the platform-wide counts of S-A3 would otherwise include them (xunit runs the
    // tests of a class in no fixed order and the fixture truncates once per run).
    private static readonly DateTimeOffset BranchDay = new(2001, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task S_A3_the_activity_of_each_client_is_counted_for_the_platform_tenant()
    {
        var (labA, labB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sources = await SourcesAsync();
        if (sources.Visits)
        {
            var (patient1, patient2) = (Guid.CreateVersion7(), Guid.CreateVersion7());
            var first = await InsertVisitAsync(labA, Guid.CreateVersion7(), patient1, Day.AddHours(8));
            await InsertVisitAsync(labA, Guid.CreateVersion7(), patient1, Day.AddHours(9));
            await InsertVisitAsync(labA, Guid.CreateVersion7(), patient2, Day.AddHours(10));
            await InsertVisitAsync(labA, Guid.CreateVersion7(), patient2, Day.AddDays(2));   // outside the period
            await InsertVisitAsync(labB, Guid.CreateVersion7(), Guid.CreateVersion7(), Day.AddHours(11));
            if (sources.Results)
            {
                await InsertResultAsync(labA, first, null, "Verified", Day.AddHours(20));
                await InsertResultAsync(labA, first, null, "Ordered", Day.AddHours(20));
                await InsertResultAsync(labA, first, null, "Cancelled", Day.AddHours(20));
            }
        }

        using var scope = fixture.AppScope(TenantIds.Platform);
        var snapshot = await scope.ServiceProvider.GetRequiredService<IClientActivityReader>().ReadAsync(Day, Day.AddDays(1), CancellationToken.None);

        if (!sources.Visits || !sources.Function)
        {
            Assert.Equal((0, 0, 0), (snapshot.Registrations, snapshot.ServedPatients, snapshot.Tests));
            return;
        }

        Assert.Equal((4, 3, sources.Results ? 2 : 0), (snapshot.Registrations, snapshot.ServedPatients, snapshot.Tests));
        Assert.Equal([(labA, 3), (labB, 1)], snapshot.ByClient.Select(item => (item.TenantId, item.Registrations)).ToArray());
    }

    [DatabaseFact]
    public async Task S_A4_a_laboratory_session_cannot_read_the_activity_of_the_clients()
    {
        var sources = await SourcesAsync();
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var reader = scope.ServiceProvider.GetRequiredService<IClientActivityReader>();

        if (!sources.Function)
        {
            Assert.Equal(0, (await reader.ReadAsync(Day, Day.AddDays(1), CancellationToken.None)).Registrations);
            return;
        }

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => reader.ReadAsync(Day, Day.AddDays(1), CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);

        // The function belongs to the owner and runs as its owner; the runtime role may only execute it.
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var definer = await context.Database.SqlQueryRaw<bool>("SELECT prosecdef AS \"Value\" FROM pg_proc WHERE oid = 'saas.client_activity_counts(timestamptz, timestamptz)'::regprocedure").SingleAsync();
        Assert.True(definer);
    }

    [DatabaseFact]
    public async Task S_D3_the_result_figures_count_pending_backlog_and_delayed_tests_of_the_branch()
    {
        var lab = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var window = new DashboardWindow(BranchDay, BranchDay.AddDays(1), BranchDay.AddDays(-6), 0);
        var sources = await SourcesAsync();

        if (sources.Visits && sources.Results)
        {
            var patient = Guid.CreateVersion7();
            var test = Guid.CreateVersion7();
            await RawRows.InsertAsync("app.patients", new Dictionary<string, object?> { ["tenant_id"] = lab, ["id"] = patient, ["full_name"] = "Delayed Patient", ["status"] = "Active", ["date_of_birth"] = new DateOnly(1980, 1, 1) });
            var clinicalGroup = Guid.CreateVersion7();
            await RawRows.InsertAsync("app.clinical_groups", new Dictionary<string, object?> { ["tenant_id"] = lab, ["id"] = clinicalGroup, ["code"] = "DLY", ["unit_code"] = "D", ["name_en"] = "Delay group", ["name_ar"] = "مجموعة" });
            await RawRows.InsertAsync("app.tests", new Dictionary<string, object?> { ["tenant_id"] = lab, ["id"] = test, ["clinical_group_id"] = clinicalGroup, ["code"] = "DLY", ["name_en"] = "Delay test", ["name_ar"] = "تحليل متأخر" });
            var today = await InsertVisitAsync(lab, branch, patient, BranchDay.AddHours(1));
            var yesterday = await InsertVisitAsync(lab, branch, Guid.CreateVersion7(), BranchDay.AddHours(-20));
            var otherBranch = await InsertVisitAsync(lab, Guid.CreateVersion7(), Guid.CreateVersion7(), BranchDay.AddHours(2));

            await InsertResultAsync(lab, today, branch, "Ordered", BranchDay.AddHours(3), test); // pending, delayed (due before the day ended)
            await InsertResultAsync(lab, today, branch, "Complete", BranchDay.AddDays(3));      // pending, backlog, on time
            await InsertResultAsync(lab, today, branch, "Reviewed", BranchDay.AddHours(23));    // pending, backlog, delayed
            await InsertResultAsync(lab, today, branch, "Verified", BranchDay.AddHours(2));     // done
            await InsertResultAsync(lab, today, branch, "Cancelled", BranchDay.AddHours(2));    // ignored
            await InsertResultAsync(lab, yesterday, branch, "Complete", BranchDay.AddDays(5));  // backlog only
            await InsertResultAsync(lab, otherBranch, null, "Ordered", BranchDay.AddHours(3));  // another branch
        }

        using var scope = fixture.AppScope(lab);
        var figures = await scope.ServiceProvider.GetRequiredService<IBranchDashboardReader>().ReadAsync(branch, window, CancellationToken.None);

        if (!sources.Visits || !sources.Results)
        {
            Assert.Null(figures.PendingResults);
            Assert.Null(figures.UnverifiedBacklog);
            Assert.Null(figures.DelayedTests);
            Assert.Null(figures.DelayedList);
            return;
        }

        Assert.Equal((3, 3, 2), (figures.PendingResults, figures.UnverifiedBacklog, figures.DelayedTests));

        // UAT R1-02: the table lists the tests the tile counts, most delayed first, judged at the end of the past day.
        Assert.NotNull(figures.DelayedList);
        Assert.Equal(figures.DelayedTests, figures.DelayedList.Count);
        Assert.Equal(["Ordered", "Reviewed"], figures.DelayedList.Select(item => item.Status).ToArray());
        Assert.Equal([21 * 60, 60], figures.DelayedList.Select(item => item.DelayMinutes).ToArray());
        var first = figures.DelayedList[0];
        Assert.Equal(("Delayed Patient", "Delay test", "تحليل متأخر"), (first.PatientName, first.TestName, first.TestNameAr));
        Assert.StartsWith("ACC-IT-", first.Accession, StringComparison.Ordinal);
        Assert.Equal(BranchDay.AddHours(3), first.ExpectedAtUtc);
    }

    // ---- sources and rows ---------------------------------------------------------------------------------------------

    private sealed record Sources(bool Visits, bool Results, bool Function);

    private static async Task<Sources> SourcesAsync()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT to_regclass('app.visits') IS NOT NULL,
                   to_regclass('app.results') IS NOT NULL,
                   to_regprocedure('saas.client_activity_counts(timestamptz,timestamptz)') IS NOT NULL
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new Sources(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task<Guid> InsertVisitAsync(Guid tenant, Guid branch, Guid patient, DateTimeOffset registeredAtUtc)
    {
        var id = Guid.CreateVersion7();
        await RawRows.InsertAsync("app.visits", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = id,
            ["accession"] = $"ACC-IT-{id.ToString("N")[^10..].ToUpperInvariant()}",
            ["patient_id"] = patient,
            ["branch_id"] = branch,
            ["status"] = "Open",
            ["registered_at_utc"] = registeredAtUtc,
            ["registered_by_user_id"] = Guid.CreateVersion7(),
            ["urgent"] = false,
            ["total_list_price"] = 100m,
            ["discount_total"] = 0m,
            ["patient_share_total"] = 100m,
            ["payer_share_total"] = 0m,
        });
        return id;
    }

    /// <summary>A result row of the visit; <paramref name="branch"/> null takes a fresh branch id.</summary>
    private static Task InsertResultAsync(Guid tenant, Guid visitId, Guid? branch, string status, DateTimeOffset expectedAtUtc, Guid? testId = null) =>
        RawRows.InsertAsync("app.results", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = Guid.CreateVersion7(),
            ["visit_id"] = visitId,
            ["branch_id"] = branch ?? Guid.CreateVersion7(),
            ["status"] = status,
            ["tube_id"] = null,
            ["source"] = "Manual",
            ["expected_at_utc"] = expectedAtUtc,
            ["verified_at_utc"] = status == "Verified" ? expectedAtUtc : null,
            ["test_id"] = testId ?? Guid.CreateVersion7(),
        });
}

/// <summary>
/// Inserts a row into a table another module owns, with the given columns and type defaults for every other NOT NULL
/// column without a default (a domain-checked text column takes the first value its <c>CHECK</c> allows). Runs as the
/// owner role, so RLS does not apply; the agreed MODULES.md §5 columns carry the values the test asserts on.
/// </summary>
internal static class RawRows
{
    private const string ColumnsSql = """
        SELECT column_name, data_type, udt_name
        FROM information_schema.columns
        WHERE table_schema = @schema AND table_name = @table AND is_nullable = 'NO' AND column_default IS NULL
              AND is_generated = 'NEVER' AND is_identity = 'NO'
        """;

    private const string ChecksSql = "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = @table::regclass AND contype = 'c'";

    public static async Task InsertAsync(string qualifiedTable, IReadOnlyDictionary<string, object?> values)
    {
        var (schema, table) = (qualifiedTable.Split('.')[0], qualifiedTable.Split('.')[1]);
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();

        var checks = new List<string>();
        await using (var command = new NpgsqlCommand(ChecksSql, connection))
        {
            command.Parameters.AddWithValue("table", qualifiedTable);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                checks.Add(reader.GetString(0));
            }
        }

        var row = new Dictionary<string, NpgsqlParameter>(StringComparer.Ordinal);
        foreach (var (column, value) in values)
        {
            row[column] = Parameter(column, value);
        }

        await using (var command = new NpgsqlCommand(ColumnsSql, connection))
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
        await using var insert = new NpgsqlCommand(
            $"INSERT INTO {schema}.{table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(name => "@" + name))})",
            connection);
        foreach (var name in names)
        {
            insert.Parameters.Add(row[name]);
        }

        await insert.ExecuteNonQueryAsync();
    }

    private static NpgsqlParameter Parameter(string column, object? value) => value switch
    {
        null => new NpgsqlParameter(column, DBNull.Value),
        DateTimeOffset instant => new NpgsqlParameter(column, NpgsqlDbType.TimestampTz) { Value = instant.ToUniversalTime() },
        _ => new NpgsqlParameter(column, value),
    };

    private static NpgsqlParameter Default(string column, string dataType, string udtName, IReadOnlyList<string> checks) => dataType switch
    {
        "uuid" => new NpgsqlParameter(column, Guid.CreateVersion7()),
        "boolean" => new NpgsqlParameter(column, false),
        "integer" or "smallint" or "bigint" => new NpgsqlParameter(column, NpgsqlDbType.Integer) { Value = 0 },
        "numeric" or "double precision" or "real" => new NpgsqlParameter(column, NpgsqlDbType.Numeric) { Value = 0m },
        "timestamp with time zone" => new NpgsqlParameter(column, NpgsqlDbType.TimestampTz) { Value = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        "date" => new NpgsqlParameter(column, NpgsqlDbType.Date) { Value = new DateOnly(2001, 1, 1) },
        "jsonb" => new NpgsqlParameter(column, NpgsqlDbType.Jsonb) { Value = "{}" },
        "ARRAY" when udtName == "_uuid" => new NpgsqlParameter(column, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = Array.Empty<Guid>() },
        "ARRAY" => new NpgsqlParameter(column, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Array.Empty<string>() },
        _ => new NpgsqlParameter(column, DomainValue(column, checks) ?? "x"),
    };

    /// <summary>The first literal of the CHECK that names the column (<c>"kind" IN ('Service', …)</c>), else null.</summary>
    private static string? DomainValue(string column, IReadOnlyList<string> checks)
    {
        var pattern = new Regex($@"\b{Regex.Escape(column)}\b", RegexOptions.CultureInvariant);
        var check = checks.FirstOrDefault(definition => pattern.IsMatch(definition) && definition.Contains('\'', StringComparison.Ordinal));
        return check is null ? null : Regex.Match(check, "'([^']*)'", RegexOptions.CultureInvariant).Groups[1].Value;
    }
}
