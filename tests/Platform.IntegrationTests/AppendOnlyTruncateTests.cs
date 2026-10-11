using Npgsql;
using Platform.IntegrationTests.Support;

namespace Platform.IntegrationTests;

/// <summary>
/// Validation 2026-10-11, D-01 on a real PostgreSQL 17: a row trigger never fires on TRUNCATE, so every table whose rows are
/// never deleted carries a statement-level BEFORE TRUNCATE refusal (migration AppendOnlyTruncateGuard), and even the owner
/// role, which owns the tables and runs the migrations, cannot empty them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AppendOnlyTruncateTests(DatabaseFixture fixture)
{
    private static readonly string[] AppendOnlyTables =
    [
        "audit.audit_trail",
        "audit.field_changes",
        "audit.security_events",
        "app.payments",
        "app.refunds",
    ];

    [DatabaseFact]
    public async Task Every_table_that_refuses_row_deletion_refuses_truncate_with_an_enabled_statement_trigger()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();

        // tgtype bits: 1 ROW, 2 BEFORE, 8 DELETE, 32 TRUNCATE; tgenabled 'O' = fires in the normal (origin) mode.
        const string sql = """
            WITH guarded AS (
                SELECT DISTINCT n.nspname || '.' || c.relname AS table_name, c.oid
                FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE NOT t.tgisinternal AND (t.tgtype & 1) <> 0 AND (t.tgtype & 2) <> 0 AND (t.tgtype & 8) <> 0
                  AND n.nspname IN ('identity', 'app', 'ops', 'audit', 'read', 'saas'))
            SELECT g.table_name,
                   EXISTS (SELECT 1 FROM pg_trigger t
                           WHERE t.tgrelid = g.oid AND NOT t.tgisinternal AND (t.tgtype & 32) <> 0 AND (t.tgtype & 1) = 0
                             AND (t.tgtype & 2) <> 0 AND t.tgenabled = 'O') AS refuses_truncate
            FROM guarded g ORDER BY 1
            """;
        var rows = new List<(string Table, bool RefusesTruncate)>();
        await using (var command = new NpgsqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetBoolean(1)));
            }
        }

        Assert.NotEmpty(rows);
        Assert.All(AppendOnlyTables, table => Assert.Contains(rows, row => row.Table == table));
        var unguarded = rows.Where(row => !row.RefusesTruncate).Select(row => row.Table).ToList();
        Assert.True(unguarded.Count == 0, "Tables that refuse DELETE but accept TRUNCATE: " + string.Join(", ", unguarded));
    }

    [DatabaseFact]
    public async Task Even_the_owner_role_cannot_truncate_an_append_only_table()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();

        foreach (var table in AppendOnlyTables)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"TRUNCATE {table} CASCADE", connection, transaction);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);
            Assert.Contains("TRUNCATE is not allowed", refusal.MessageText, StringComparison.Ordinal);
            await transaction.RollbackAsync();
        }
    }
}
