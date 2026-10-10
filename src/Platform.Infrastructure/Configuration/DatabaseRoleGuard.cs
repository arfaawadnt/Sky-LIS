using Npgsql;

namespace Platform.Infrastructure.Configuration;

/// <summary>
/// The deployment guard of 01 §1 and 00 §1.1, in two parts. Before any connection: the target must be LIS's own
/// database and role (CLAUDE.md §2 reserves the names <c>lis*</c> / <c>lis_*</c>); this host runs other
/// applications whose generic variables must never route LIS to their data. After connecting: the runtime role must
/// be neither SUPERUSER nor BYPASSRLS, must not create roles or databases, and must not own any application table.
/// Runs at start-up in every environment (C-07); a violation refuses to start.
/// </summary>
public static class DatabaseRoleGuard
{
    public const string DatabasePrefix = "lis";
    public const string RolePrefix = "lis_";
    private const string RoleSql = "SELECT rolsuper, rolbypassrls, rolcreaterole, rolcreatedb FROM pg_roles WHERE rolname = current_user";
    private const string OwnedTablesSql = "SELECT count(*) FROM pg_tables WHERE schemaname IN ('identity','app','audit','saas','read','ops') AND tableowner = current_user";

    /// <summary>Pure check of the connection target: database name starts with <c>lis</c>, user name with <c>lis_</c>, both present.</summary>
    public static IReadOnlyList<string> InspectConnectionTarget(string connectionString)
    {
        var violations = new List<string>();
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            return [$"the connection string cannot be parsed: {exception.Message}"];
        }

        if (string.IsNullOrWhiteSpace(builder.Database) || !builder.Database.StartsWith(DatabasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"database '{builder.Database}' is not an LIS database (expected a name starting with '{DatabasePrefix}')");
        }

        if (string.IsNullOrWhiteSpace(builder.Username) || !builder.Username.StartsWith(RolePrefix, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"role '{builder.Username}' is not an LIS role (expected a name starting with '{RolePrefix}')");
        }

        return violations;
    }

    /// <summary>Host, port, database and role of a connection string, for start-up logs; never the password.</summary>
    public static string Describe(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"{builder.Host}:{builder.Port}/{builder.Database} as {builder.Username}";
        }
        catch (ArgumentException)
        {
            return "<unparseable connection string>";
        }
    }

    public static async Task<IReadOnlyList<string>> InspectAsync(string connectionString, CancellationToken cancellationToken)
    {
        var violations = new List<string>(InspectConnectionTarget(connectionString));
        if (violations.Count > 0)
        {
            return violations;   // never connect to a database that is not ours
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(RoleSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetBoolean(0))
                {
                    violations.Add("runtime role is SUPERUSER");
                }

                if (reader.GetBoolean(1))
                {
                    violations.Add("runtime role has BYPASSRLS");
                }

                if (reader.GetBoolean(2))
                {
                    violations.Add("runtime role has CREATEROLE");
                }

                if (reader.GetBoolean(3))
                {
                    violations.Add("runtime role has CREATEDB");
                }
            }
        }

        await using (var command = new NpgsqlCommand(OwnedTablesSql, connection))
        {
            var owned = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            if (owned > 0)
            {
                violations.Add($"runtime role owns {owned} application table(s)");
            }
        }

        return violations;
    }

    public static async Task EnforceAsync(string connectionString, CancellationToken cancellationToken)
    {
        var violations = await InspectAsync(connectionString, cancellationToken).ConfigureAwait(false);
        if (violations.Count > 0)
        {
            throw new InvalidOperationException($"ROLE_GUARD_VIOLATION ({Describe(connectionString)}): " + string.Join("; ", violations));
        }
    }
}
