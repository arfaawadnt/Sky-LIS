using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Account;
using Platform.Contracts.Account;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Persistence.Account;

/// <summary>
/// CP-05 figures by raw SQL over the wave-2/3 tables of MODULES.md §5 (<c>app.visits</c>, <c>app.samples</c>,
/// <c>app.payments</c>, <c>app.refunds</c>, <c>app.results</c>), which belong to modules built in parallel: no entity type
/// of theirs is referenced (MODULE-PATTERN §3 "same-wave lookups"). The connection is opened through EF, so the tenant
/// stamp and row-level security apply; every statement is additionally bounded to the session branch. A statement whose
/// table or column does not exist yet (42P01 / 42703) yields <see langword="null"/>: "not available", never zero.
/// The three result figures (pending, unverified backlog, delayed) are the Reporting module's (intake LIS-INT-REP-001 §14).
/// </summary>
internal sealed class BranchDashboardReader(AppDbContext context, ITenantSettings settings, IClock clock) : IBranchDashboardReader
{
    internal const string RegistrationsSql = """
        SELECT count(*)::int FROM app.visits v
        WHERE v.branch_id = @branch AND v.registered_at_utc >= @from AND v.registered_at_utc < @to
        """;

    internal const string RegistrationsByDaySql = """
        SELECT to_char((v.registered_at_utc AT TIME ZONE 'UTC') + make_interval(mins => @offset), 'YYYY-MM-DD') AS day, count(*)::int AS registrations
        FROM app.visits v
        WHERE v.branch_id = @branch AND v.registered_at_utc >= @chart_from AND v.registered_at_utc < @to
        GROUP BY 1
        """;

    // Cash of the day by the movement's own branch (the cashier's session branch, as the cash closing scopes it;
    // LIS-CR-004 2026-10-20: no treasury join).
    internal const string PaymentsSql = """
        SELECT COALESCE(sum(p.amount), 0)::numeric FROM app.payments p
        WHERE p.status = 'Posted' AND p.paid_at_utc >= @from AND p.paid_at_utc < @to AND p.branch_id = @branch
        """;

    internal const string RefundsSql = """
        SELECT COALESCE(sum(r.amount), 0)::numeric FROM app.refunds r
        WHERE r.refunded_at_utc >= @from AND r.refunded_at_utc < @to AND r.branch_id = @branch
        """;

    internal const string SamplesReceivedSql = """
        SELECT count(*)::int FROM app.samples s
        JOIN app.visits v ON v.tenant_id = s.tenant_id AND v.id = s.visit_id
        WHERE v.branch_id = @branch AND s.received_at_utc >= @from AND s.received_at_utc < @to
        """;

    // The wave-2 contract has no rejection time: rejected samples of the visits registered that day (intake §16 Q5).
    internal const string RejectedSamplesSql = """
        SELECT count(*)::int FROM app.samples s
        JOIN app.visits v ON v.tenant_id = s.tenant_id AND v.id = s.visit_id
        WHERE v.branch_id = @branch AND s.status = 'Rejected' AND v.registered_at_utc >= @from AND v.registered_at_utc < @to
        """;

    // ---- result figures (Reporting, intake LIS-INT-REP-001 §14; app.results is the Results module's MODULES.md §5 contract) ----

    /// <summary>Results of the visits registered in the day whose result is not verified yet (Ordered, Complete, Reviewed).</summary>
    internal const string PendingResultsSql = """
        SELECT count(*)::int FROM app.results r
        JOIN app.visits v ON v.tenant_id = r.tenant_id AND v.id = r.visit_id
        WHERE r.branch_id = @branch AND r.status IN ('Ordered', 'Complete', 'Reviewed')
          AND v.registered_at_utc >= @from AND v.registered_at_utc < @to
        """;

    /// <summary>Results holding a value that waits for verification (Complete, Reviewed), of visits registered up to the end of the day.</summary>
    internal const string UnverifiedBacklogSql = """
        SELECT count(*)::int FROM app.results r
        JOIN app.visits v ON v.tenant_id = r.tenant_id AND v.id = r.visit_id
        WHERE r.branch_id = @branch AND r.status IN ('Complete', 'Reviewed') AND v.registered_at_utc < @to
        """;

    /// <summary>
    /// Open results (Ordered, Complete, Reviewed) whose consumed share of the turnaround reached <c>delayLatePercent</c> at
    /// <c>@as_of</c> (BR-13): the clock starts at the tube's collection, else at registration, and ends at the expected
    /// time Results stored (<c>expected_at_utc</c>); consumed ≥ late ⇔ as_of ≥ start + (expected − start) × late / 100.
    /// </summary>
    internal const string DelayedTestsSql = """
        SELECT count(*)::int FROM app.results r
        JOIN app.visits v ON v.tenant_id = r.tenant_id AND v.id = r.visit_id
        LEFT JOIN LATERAL (
            SELECT max(s.collected_at_utc) AS collected_at_utc FROM app.samples s
            WHERE s.tenant_id = r.tenant_id AND s.tube_id = r.tube_id AND s.status NOT IN ('Replaced', 'Cancelled')
        ) c ON true
        WHERE r.branch_id = @branch AND r.status IN ('Ordered', 'Complete', 'Reviewed') AND r.expected_at_utc IS NOT NULL
          AND v.registered_at_utc < @to
          AND @as_of >= COALESCE(c.collected_at_utc, v.registered_at_utc)
                        + (r.expected_at_utc - COALESCE(c.collected_at_utc, v.registered_at_utc)) * @late_factor
        """;

    /// <summary>
    /// The rows behind <see cref="DelayedTestsSql"/> (same tables, same predicate), most delayed first, at most
    /// <c>@limit</c>: the "Delayed tests" table of the dashboard (UAT R1-02). Names come from the patient and test records.
    /// </summary>
    internal const string DelayedListSql = """
        SELECT r.visit_id, v.accession, p.full_name, t.name_en, t.name_ar, r.expected_at_utc, r.status
        FROM app.results r
        JOIN app.visits v ON v.tenant_id = r.tenant_id AND v.id = r.visit_id
        LEFT JOIN app.patients p ON p.tenant_id = v.tenant_id AND p.id = v.patient_id
        LEFT JOIN app.tests t ON t.tenant_id = r.tenant_id AND t.id = r.test_id
        LEFT JOIN LATERAL (
            SELECT max(s.collected_at_utc) AS collected_at_utc FROM app.samples s
            WHERE s.tenant_id = r.tenant_id AND s.tube_id = r.tube_id AND s.status NOT IN ('Replaced', 'Cancelled')
        ) c ON true
        WHERE r.branch_id = @branch AND r.status IN ('Ordered', 'Complete', 'Reviewed') AND r.expected_at_utc IS NOT NULL
          AND v.registered_at_utc < @to
          AND @as_of >= COALESCE(c.collected_at_utc, v.registered_at_utc)
                        + (r.expected_at_utc - COALESCE(c.collected_at_utc, v.registered_at_utc)) * @late_factor
        ORDER BY r.expected_at_utc, v.accession, r.id
        LIMIT @limit
        """;

    public async Task<BranchDayFigures> ReadAsync(Guid branchId, DashboardWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Read before the connection is held open: the settings adapter queries through the same context.
        var latePercent = await settings.GetAsync(SettingKeys.DelayLatePercent, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var timing = new ResultTiming(now < window.ToUtc ? now : window.ToUtc, latePercent / 100d);

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registrations = await ScalarAsync<int>(connection, RegistrationsSql, branchId, window, cancellationToken).ConfigureAwait(false);
            var byDay = registrations is null ? null : await ByDayAsync(connection, branchId, window, cancellationToken).ConfigureAwait(false);

            var payments = await ScalarAsync<decimal>(connection, PaymentsSql, branchId, window, cancellationToken).ConfigureAwait(false);
            var refunds = payments is null ? null : await ScalarAsync<decimal>(connection, RefundsSql, branchId, window, cancellationToken).ConfigureAwait(false);
            decimal? collected = payments is { } paid ? paid - (refunds ?? 0m) : null;

            var received = await ScalarAsync<int>(connection, SamplesReceivedSql, branchId, window, cancellationToken).ConfigureAwait(false);
            var rejected = await ScalarAsync<int>(connection, RejectedSamplesSql, branchId, window, cancellationToken).ConfigureAwait(false);

            var pending = await ScalarAsync<int>(connection, PendingResultsSql, branchId, window, cancellationToken, timing).ConfigureAwait(false);
            var backlog = pending is null ? null : await ScalarAsync<int>(connection, UnverifiedBacklogSql, branchId, window, cancellationToken, timing).ConfigureAwait(false);
            var delayed = pending is null ? null : await ScalarAsync<int>(connection, DelayedTestsSql, branchId, window, cancellationToken, timing).ConfigureAwait(false);
            var delayedList = delayed is > 0 ? await DelayedListAsync(connection, branchId, window, timing, cancellationToken).ConfigureAwait(false) : null;

            return new BranchDayFigures(registrations, collected, received, rejected, byDay, pending, backlog, delayed, delayedList);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The laboratory's prepaid balance (<c>saas.wallets</c>, platform module, wave 1); <see langword="null"/> without a wallet or before the table exists.</summary>
    public async Task<(decimal Balance, string CurrencyCode)?> ReadWalletAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await context.Set<Wallet>().AsNoTracking()
                .Where(row => row.TenantId == tenantId)
                .Select(row => new { row.Balance, row.CurrencyCode })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return wallet is null ? null : (wallet.Balance, wallet.CurrencyCode);
        }
        catch (PostgresException exception) when (IsMissingSource(exception))
        {
            return null;
        }
    }

    internal static bool IsMissingSource(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn;

    private static async Task<T?> ScalarAsync<T>(DbConnection connection, string sql, Guid branchId, DashboardWindow window, CancellationToken cancellationToken, ResultTiming? timing = null)
        where T : struct
    {
        try
        {
            await using var command = Command(connection, sql, branchId, window, timing);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? default(T) : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (PostgresException exception) when (IsMissingSource(exception))
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<DelayedTestItem>?> DelayedListAsync(DbConnection connection, Guid branchId, DashboardWindow window, ResultTiming timing, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = Command(connection, DelayedListSql, branchId, window, timing);
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = BranchDashboardLimits.DelayedRows });
            var rows = new List<DelayedTestItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var expected = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
                var late = timing.AsOfUtc - expected;
                rows.Add(new DelayedTestItem(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    expected,
                    late > TimeSpan.Zero ? (int)Math.Min(late.TotalMinutes, int.MaxValue) : 0,
                    reader.GetString(6),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return rows;
        }
        catch (PostgresException exception) when (IsMissingSource(exception))
        {
            return null;
        }
    }

    private static async Task<IReadOnlyDictionary<string, int>?> ByDayAsync(DbConnection connection, Guid branchId, DashboardWindow window, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = Command(connection, RegistrationsByDaySql, branchId, window);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }

            return counts;
        }
        catch (PostgresException exception) when (IsMissingSource(exception))
        {
            return null;
        }
    }

    private static DbCommand Command(DbConnection connection, string sql, Guid branchId, DashboardWindow window, ResultTiming? timing = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("branch", NpgsqlDbType.Uuid) { Value = branchId });
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = window.FromUtc.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = window.ToUtc.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("chart_from", NpgsqlDbType.TimestampTz) { Value = window.ChartFromUtc.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = window.UtcOffsetMinutes });
        if (timing is { } result)
        {
            command.Parameters.Add(new NpgsqlParameter("as_of", NpgsqlDbType.TimestampTz) { Value = result.AsOfUtc.ToUniversalTime() });
            command.Parameters.Add(new NpgsqlParameter("late_factor", NpgsqlDbType.Double) { Value = result.LateFactor });
        }

        return command;
    }

    /// <summary>The moment the delay is judged at (now, or the end of a past day) and <c>delayLatePercent</c> / 100.</summary>
    private readonly record struct ResultTiming(DateTimeOffset AsOfUtc, double LateFactor);
}
