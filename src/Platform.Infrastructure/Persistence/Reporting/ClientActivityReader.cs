using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.Reporting;

/// <summary>
/// SA-03 activity of the clients for a period (intake LIS-INT-REP-001 §14): served patients, tests and registrations per
/// tenant, read through <c>saas.client_activity_counts(timestamptz, timestamptz)</c> — a <c>SECURITY DEFINER</c> function
/// owned by <c>lis_owner</c> (<c>Reporting.migration.sql</c>) that returns aggregate counts only and refuses (42501) any
/// session whose tenant is not the platform tenant. No elevation in C# (the pattern of <c>ClientUsageCounts</c>); the
/// connection is opened through EF so the tenant stamp of the request applies. Replaces the platform module's
/// <c>[FallbackAdapter]</c> <c>ZeroClientActivityReader</c> by convention. Before the function is installed (42883) or
/// while its schema is missing (3F000 / 42P01) the dashboard shows zeros, as the fallback did.
/// </summary>
internal sealed class ClientActivityReader(AppDbContext context) : IClientActivityReader
{
    public const string Sql = "SELECT tenant_id, served_patients, tests, registrations FROM saas.client_activity_counts(@from, @to)";

    public async Task<ClientActivitySnapshot> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        if (fromUtc >= toUtc)
        {
            return ClientActivitySnapshot.Empty;
        }

        var rows = new List<ActivityRow>();
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = database.GetDbConnection().CreateCommand();
            command.CommandText = Sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromUtc.ToUniversalTime() });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toUtc.ToUniversalTime() });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new ActivityRow(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
            }
        }
        catch (PostgresException exception) when (IsNotInstalled(exception))
        {
            return ClientActivitySnapshot.Empty;
        }
        finally
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return Snapshot(rows);
    }

    /// <summary>The totals across the clients and the registrations per client (the platform tenant itself never registers visits).</summary>
    internal static ClientActivitySnapshot Snapshot(IReadOnlyCollection<ActivityRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return ClientActivitySnapshot.Empty;
        }

        var byClient = rows
            .Where(row => row.Registrations > 0)
            .OrderByDescending(row => row.Registrations)
            .ThenBy(row => row.TenantId)
            .Select(row => new ClientRegistrations(row.TenantId, Clamp(row.Registrations)))
            .ToList();

        return new ClientActivitySnapshot(
            Clamp(rows.Sum(row => row.ServedPatients)),
            Clamp(rows.Sum(row => row.Tests)),
            Clamp(rows.Sum(row => row.Registrations)),
            byClient);
    }

    internal static bool IsNotInstalled(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UndefinedFunction or PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName;

    private static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    internal readonly record struct ActivityRow(Guid TenantId, long ServedPatients, long Tests, long Registrations);
}
