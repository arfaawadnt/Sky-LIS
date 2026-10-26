using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.Tracker;
using Platform.Domain.Patients;
using Platform.Domain.Tracker;

namespace Platform.Infrastructure.Persistence.Tracker;

/// <summary>
/// The Lab Tracker read (LIS-CR-006 §0): one statement over the MODULES.md §5 tables of Registration (<c>app.visits</c>,
/// <c>app.visit_lines</c>), Samples (<c>app.samples</c>), Results (<c>app.results</c>) and Billing (<c>app.payments</c>,
/// <c>app.refunds</c>) with the names of Patients, Pricing and Catalogue; the per-visit aggregates are grouped sub-selects over
/// the visits of the day. The connection is opened through EF, so the tenant stamp and row-level security apply (no
/// elevation); every statement is additionally bounded to the tenant and the branch. The existing indexes serve it:
/// <c>ix_visits_tenant_id_branch_id_registered_at_utc</c> and the <c>(tenant_id, visit_id)</c> indexes of the other tables.
/// Results are counted visible by the user's result rights (the worklist's rule, <c>ResultVisibilityFilter</c>) and in all.
/// </summary>
internal sealed class LabTrackerReader(AppDbContext context) : ILabTrackerReader
{
    /// <summary>The visits of the day (the first CTE of both statements); <c>{0}</c> is the search clause (empty without a search).</summary>
    internal const string DayVisitsSql = """
        WITH day_visits AS (
            SELECT v.tenant_id, v.id, v.accession, v.patient_id, v.branch_id, v.payer_id, v.contract_tier_id, v.batch_id,
                   v.urgent, v.registered_at_utc, v.created_by_display_name, v.patient_share_total
            FROM app.visits v
            WHERE v.tenant_id = @tenant AND v.branch_id = @branch
              AND v.registered_at_utc >= @from AND v.registered_at_utc < @to
              {0}
        ),

        """;

    /// <summary>The per-visit aggregates of the visits of the day (after <see cref="DayVisitsSql"/>).</summary>
    internal const string VisitsSql = DayVisitsSql + """
        line_counts AS (
            SELECT l.visit_id,
                   count(*) FILTER (WHERE l.status <> 'Cancelled')::int AS live_lines,
                   count(*) FILTER (WHERE l.status = 'Cancelled')::int AS cancelled_lines
            FROM app.visit_lines l
            JOIN day_visits d ON d.tenant_id = l.tenant_id AND d.id = l.visit_id
            WHERE l.kind <> 'PackageMember'
            GROUP BY l.visit_id
        ),
        sample_counts AS (
            SELECT s.visit_id,
                   count(*) FILTER (WHERE s.status NOT IN ('Cancelled', 'Replaced'))::int AS total,
                   count(*) FILTER (WHERE s.status = 'Ordered')::int AS ordered,
                   count(*) FILTER (WHERE s.status = 'Collected')::int AS collected,
                   count(*) FILTER (WHERE s.status = 'Received')::int AS received,
                   count(*) FILTER (WHERE s.status = 'Rejected')::int AS rejected,
                   count(*) FILTER (WHERE s.status NOT IN ('Cancelled', 'Replaced') AND s.collected_outside)::int AS outside,
                   (array_agg(s.rejection_reason_name_en ORDER BY s.rejected_at_utc, s.sample_number) FILTER (WHERE s.status = 'Rejected'))[1] AS rejection_en,
                   (array_agg(s.rejection_reason_name_ar ORDER BY s.rejected_at_utc, s.sample_number) FILTER (WHERE s.status = 'Rejected'))[1] AS rejection_ar
            FROM app.samples s
            JOIN day_visits d ON d.tenant_id = s.tenant_id AND d.id = s.visit_id
            GROUP BY s.visit_id
        ),
        result_rows AS (
            SELECT r.visit_id, r.id, r.status, r.is_panic, r.delta_flag, r.test_id, r.service_sequence, r.member_order,
                   r.clock_start_utc, r.expected_at_utc,
                   r.status IN ('Ordered', 'Complete', 'Reviewed') AS open,
                   (@unrestricted
                    OR r.clinical_group_id = ANY(@all_branch_groups)
                    OR EXISTS (SELECT 1 FROM unnest(@pair_branches, @pair_groups) AS g(branch_id, group_id)
                               WHERE g.branch_id = r.branch_id AND g.group_id = r.clinical_group_id)) AS visible
            FROM app.results r
            JOIN day_visits d ON d.tenant_id = r.tenant_id AND d.id = r.visit_id
            WHERE r.status <> 'Cancelled'
        ),
        result_counts AS (
            SELECT x.visit_id,
                   count(*) FILTER (WHERE x.status = 'Ordered')::int AS all_ordered,
                   count(*) FILTER (WHERE x.status = 'Complete')::int AS all_complete,
                   count(*) FILTER (WHERE x.status = 'Reviewed')::int AS all_reviewed,
                   count(*) FILTER (WHERE x.status = 'Verified')::int AS all_verified,
                   count(*) FILTER (WHERE x.status = 'Printed')::int AS all_printed,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Ordered')::int AS vis_ordered,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Complete')::int AS vis_complete,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Reviewed')::int AS vis_reviewed,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Verified')::int AS vis_verified,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Printed')::int AS vis_printed,
                   count(*) FILTER (WHERE x.visible AND x.is_panic)::int AS vis_panic,
                   count(*) FILTER (WHERE x.visible AND x.is_panic AND x.open)::int AS vis_open_panic,
                   count(*) FILTER (WHERE x.visible AND x.delta_flag)::int AS vis_delta,
                   (array_agg(x.test_id ORDER BY x.service_sequence, x.member_order, x.id) FILTER (WHERE x.visible AND x.is_panic))[1] AS panic_test_id,
                   array_agg(x.clock_start_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.visible AND x.open) AS vis_starts,
                   array_agg(x.expected_at_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.visible AND x.open) AS vis_expected,
                   array_agg(x.clock_start_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.open) AS all_starts,
                   array_agg(x.expected_at_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.open) AS all_expected
            FROM result_rows x
            GROUP BY x.visit_id
        ),
        paid AS (
            SELECT p.visit_id, sum(p.amount) AS amount
            FROM app.payments p
            JOIN day_visits d ON d.tenant_id = p.tenant_id AND d.id = p.visit_id
            WHERE p.status = 'Posted'
            GROUP BY p.visit_id
        ),
        refunded AS (
            SELECT f.visit_id, sum(f.amount) AS amount
            FROM app.refunds f
            JOIN day_visits d ON d.tenant_id = f.tenant_id AND d.id = f.visit_id
            GROUP BY f.visit_id
        )
        SELECT d.id, d.accession, d.patient_id, pt.number, pt.full_name, pt.name_ar, COALESCE(pt.is_vip, false), pt.gender,
               pt.date_of_birth, pt.age_years, pt.age_months, pt.age_days, pt.age_recorded_on,
               d.urgent, d.registered_at_utc, d.created_by_display_name,
               py.name_en, py.name_ar, ct.name_en, ct.name_ar, d.branch_id,
               COALESCE(lc.live_lines, 0), COALESCE(lc.cancelled_lines, 0),
               COALESCE(sc.total, 0), COALESCE(sc.ordered, 0), COALESCE(sc.collected, 0), COALESCE(sc.received, 0), COALESCE(sc.rejected, 0),
               sc.rejection_en, sc.rejection_ar,
               COALESCE(rc.vis_ordered, 0), COALESCE(rc.vis_complete, 0), COALESCE(rc.vis_reviewed, 0), COALESCE(rc.vis_verified, 0), COALESCE(rc.vis_printed, 0),
               COALESCE(rc.all_ordered, 0), COALESCE(rc.all_complete, 0), COALESCE(rc.all_reviewed, 0), COALESCE(rc.all_verified, 0), COALESCE(rc.all_printed, 0),
               COALESCE(rc.vis_panic, 0), COALESCE(rc.vis_open_panic, 0), COALESCE(rc.vis_delta, 0),
               t.name_en, t.name_ar,
               rc.vis_starts, rc.vis_expected, rc.all_starts, rc.all_expected,
               d.patient_share_total, COALESCE(pd.amount, 0), COALESCE(rf.amount, 0),
               COALESCE(b.kind = 'LabToLab', false),
               COALESCE(sc.outside, 0)
        FROM day_visits d
        LEFT JOIN app.patients pt ON pt.tenant_id = d.tenant_id AND pt.id = d.patient_id
        LEFT JOIN app.payers py ON py.tenant_id = d.tenant_id AND py.id = d.payer_id
        LEFT JOIN app.contract_tiers ct ON ct.tenant_id = d.tenant_id AND ct.id = d.contract_tier_id
        LEFT JOIN app.batches b ON b.tenant_id = d.tenant_id AND b.id = d.batch_id
        LEFT JOIN line_counts lc ON lc.visit_id = d.id
        LEFT JOIN sample_counts sc ON sc.visit_id = d.id
        LEFT JOIN result_counts rc ON rc.visit_id = d.id
        LEFT JOIN app.tests t ON t.tenant_id = d.tenant_id AND t.id = rc.panic_test_id
        LEFT JOIN paid pd ON pd.visit_id = d.id
        LEFT JOIN refunded rf ON rf.visit_id = d.id
        """;

    /// <summary>
    /// The live top-level lines (services and packages) of the visits of the day with their per-line aggregates (LIS-CR-014
    /// T3, after <see cref="DayVisitsSql"/>): a package member and its tubes and results fold into its package
    /// (<c>line_family</c>: every live line of the day with its top-level line); the tubes of a line are the tubes its lines
    /// name and the tubes its results were ordered on; its samples are the live samples of those tubes, its results the live
    /// results of its lines, visible by the same result rights as the visits statement.
    /// </summary>
    internal const string ServicesSql = DayVisitsSql + """
        line_family AS (
            SELECT l.id AS line_id, COALESCE(l.parent_line_id, l.id) AS top_id, l.tube_id
            FROM app.visit_lines l
            JOIN day_visits d ON d.tenant_id = l.tenant_id AND d.id = l.visit_id
            WHERE l.status <> 'Cancelled'
        ),
        result_rows AS (
            SELECT f.top_id, r.id, r.tube_id, r.status, r.is_panic, r.delta_flag, r.test_id, r.service_sequence, r.member_order,
                   r.clock_start_utc, r.expected_at_utc,
                   r.status IN ('Ordered', 'Complete', 'Reviewed') AS open,
                   (@unrestricted
                    OR r.clinical_group_id = ANY(@all_branch_groups)
                    OR EXISTS (SELECT 1 FROM unnest(@pair_branches, @pair_groups) AS g(branch_id, group_id)
                               WHERE g.branch_id = r.branch_id AND g.group_id = r.clinical_group_id)) AS visible
            FROM app.results r
            JOIN day_visits d ON d.tenant_id = r.tenant_id AND d.id = r.visit_id
            JOIN line_family f ON f.line_id = r.line_id
            WHERE r.status <> 'Cancelled'
        ),
        line_tubes AS (
            SELECT f.top_id, f.tube_id FROM line_family f WHERE f.tube_id IS NOT NULL
            UNION
            SELECT x.top_id, x.tube_id FROM result_rows x WHERE x.tube_id IS NOT NULL
        ),
        day_samples AS (
            SELECT s.tube_id, s.status, s.collected_outside, s.rejection_reason_name_en, s.rejection_reason_name_ar, s.rejected_at_utc, s.sample_number
            FROM app.samples s
            JOIN day_visits d ON d.tenant_id = s.tenant_id AND d.id = s.visit_id
        ),
        sample_counts AS (
            SELECT t.top_id,
                   count(*) FILTER (WHERE s.status NOT IN ('Cancelled', 'Replaced'))::int AS total,
                   count(*) FILTER (WHERE s.status = 'Ordered')::int AS ordered,
                   count(*) FILTER (WHERE s.status = 'Collected')::int AS collected,
                   count(*) FILTER (WHERE s.status = 'Received')::int AS received,
                   count(*) FILTER (WHERE s.status = 'Rejected')::int AS rejected,
                   count(*) FILTER (WHERE s.status NOT IN ('Cancelled', 'Replaced') AND s.collected_outside)::int AS outside,
                   (array_agg(s.rejection_reason_name_en ORDER BY s.rejected_at_utc, s.sample_number) FILTER (WHERE s.status = 'Rejected'))[1] AS rejection_en,
                   (array_agg(s.rejection_reason_name_ar ORDER BY s.rejected_at_utc, s.sample_number) FILTER (WHERE s.status = 'Rejected'))[1] AS rejection_ar
            FROM line_tubes t
            JOIN day_samples s ON s.tube_id = t.tube_id
            GROUP BY t.top_id
        ),
        result_counts AS (
            SELECT x.top_id,
                   count(*) FILTER (WHERE x.status = 'Ordered')::int AS all_ordered,
                   count(*) FILTER (WHERE x.status = 'Complete')::int AS all_complete,
                   count(*) FILTER (WHERE x.status = 'Reviewed')::int AS all_reviewed,
                   count(*) FILTER (WHERE x.status = 'Verified')::int AS all_verified,
                   count(*) FILTER (WHERE x.status = 'Printed')::int AS all_printed,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Ordered')::int AS vis_ordered,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Complete')::int AS vis_complete,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Reviewed')::int AS vis_reviewed,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Verified')::int AS vis_verified,
                   count(*) FILTER (WHERE x.visible AND x.status = 'Printed')::int AS vis_printed,
                   count(*) FILTER (WHERE x.visible AND x.is_panic)::int AS vis_panic,
                   count(*) FILTER (WHERE x.visible AND x.is_panic AND x.open)::int AS vis_open_panic,
                   count(*) FILTER (WHERE x.visible AND x.delta_flag)::int AS vis_delta,
                   (array_agg(x.test_id ORDER BY x.service_sequence, x.member_order, x.id) FILTER (WHERE x.visible AND x.is_panic))[1] AS panic_test_id,
                   array_agg(x.clock_start_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.visible AND x.open) AS vis_starts,
                   array_agg(x.expected_at_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.visible AND x.open) AS vis_expected,
                   array_agg(x.clock_start_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.open) AS all_starts,
                   array_agg(x.expected_at_utc ORDER BY x.expected_at_utc, x.id) FILTER (WHERE x.open) AS all_expected
            FROM result_rows x
            GROUP BY x.top_id
        )
        SELECT l.visit_id, l.id, l.service_id, l.service_code, l.service_name_en, l.service_name_ar, l.kind, l.sequence,
               COALESCE(sc.total, 0), COALESCE(sc.ordered, 0), COALESCE(sc.collected, 0), COALESCE(sc.received, 0), COALESCE(sc.rejected, 0),
               COALESCE(sc.outside, 0), sc.rejection_en, sc.rejection_ar,
               COALESCE(rc.vis_ordered, 0), COALESCE(rc.vis_complete, 0), COALESCE(rc.vis_reviewed, 0), COALESCE(rc.vis_verified, 0), COALESCE(rc.vis_printed, 0),
               COALESCE(rc.all_ordered, 0), COALESCE(rc.all_complete, 0), COALESCE(rc.all_reviewed, 0), COALESCE(rc.all_verified, 0), COALESCE(rc.all_printed, 0),
               COALESCE(rc.vis_panic, 0), COALESCE(rc.vis_open_panic, 0), COALESCE(rc.vis_delta, 0),
               t.name_en, t.name_ar,
               rc.vis_starts, rc.vis_expected, rc.all_starts, rc.all_expected
        FROM app.visit_lines l
        JOIN day_visits d ON d.tenant_id = l.tenant_id AND d.id = l.visit_id
        LEFT JOIN sample_counts sc ON sc.top_id = l.id
        LEFT JOIN result_counts rc ON rc.top_id = l.id
        LEFT JOIN app.tests t ON t.tenant_id = l.tenant_id AND t.id = rc.panic_test_id
        WHERE l.kind <> 'PackageMember' AND l.status <> 'Cancelled'
        """;

    /// <summary>
    /// The search clause: accession, a sample number of the visit and the patient number contain the text; the Latin or
    /// Arabic name contains it unless the file is VIP and the user lacks See-VIP (A-03); the mobile contains the digits when
    /// the text is a number. Patterns are escaped with a backslash and every ILIKE names its escape.
    /// </summary>
    internal const string SearchSql = """
        AND (v.accession ILIKE @contains ESCAPE '\'
             OR EXISTS (SELECT 1 FROM app.samples s WHERE s.tenant_id = v.tenant_id AND s.visit_id = v.id AND s.sample_number ILIKE @contains ESCAPE '\')
             OR EXISTS (SELECT 1 FROM app.patients p
                        WHERE p.tenant_id = v.tenant_id AND p.id = v.patient_id
                          AND (p.number ILIKE @contains ESCAPE '\'
                               OR ((@include_vip OR NOT p.is_vip)
                                   AND (p.full_name ILIKE @contains ESCAPE '\'
                                        OR p.name_ar ILIKE @contains ESCAPE '\'
                                        OR (@mobile IS NOT NULL AND p.mobile LIKE @mobile))))))
        """;

    public Task<IReadOnlyList<LabTrackerRow>> ReadAsync(LabTrackerFilter filter, CancellationToken cancellationToken) =>
        ReadAllAsync(VisitsSql, filter, Row, cancellationToken);

    public Task<IReadOnlyList<LabTrackerServiceRow>> ReadServicesAsync(LabTrackerFilter filter, CancellationToken cancellationToken) =>
        ReadAllAsync(ServicesSql, filter, ServiceRow, cancellationToken);

    private async Task<IReadOnlyList<TRow>> ReadAllAsync<TRow>(string sql, LabTrackerFilter filter, Func<DbDataReader, TRow> map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = Command(connection, sql, filter, context.CurrentTenantIdOrNil);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var rows = new List<TRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A user's text as an ILIKE "contains" pattern: <c>\</c>, <c>%</c> and <c>_</c> escaped with a backslash.</summary>
    internal static string ContainsPattern(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private static DbCommand Command(DbConnection connection, string sql, LabTrackerFilter filter, Guid tenantId)
    {
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search;
        var command = connection.CreateCommand();
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, search is null ? string.Empty : SearchSql);
        var visibility = filter.Visibility;
        var pairs = visibility.GroupIdsByBranch.SelectMany(entry => entry.Value.Select(group => (Branch: entry.Key, Group: group))).ToArray();
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("branch", NpgsqlDbType.Uuid) { Value = filter.BranchId });
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = filter.FromUtc.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = filter.ToUtc.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("unrestricted", NpgsqlDbType.Boolean) { Value = visibility.Unrestricted });
        command.Parameters.Add(new NpgsqlParameter("all_branch_groups", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = visibility.AllBranchGroupIds.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("pair_branches", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pairs.Select(pair => pair.Branch).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("pair_groups", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pairs.Select(pair => pair.Group).ToArray() });
        if (search is not null)
        {
            command.Parameters.Add(new NpgsqlParameter("contains", NpgsqlDbType.Text) { Value = ContainsPattern(search) });
            command.Parameters.Add(new NpgsqlParameter("include_vip", NpgsqlDbType.Boolean) { Value = filter.IncludeVip });
            command.Parameters.Add(new NpgsqlParameter("mobile", NpgsqlDbType.Text) { Value = filter.MobileDigits is { Length: > 0 } digits ? $"%{digits}%" : DBNull.Value });
        }

        return command;
    }

    private static LabTrackerRow Row(DbDataReader reader)
    {
        string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        int? Number(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        DateOnly? Date(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

        var birth = Date(8) ?? (Number(9) is { } years && Date(12) is { } recordedOn
            ? new PatientAge(years, Number(10) ?? 0, Number(11) ?? 0).BirthDateOn(recordedOn)
            : null);

        return new LabTrackerRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            Text(3),
            Text(4),
            Text(5),
            reader.GetBoolean(6),
            Text(7),
            birth,
            reader.GetBoolean(13),
            Instant(reader.GetFieldValue<DateTime>(14)),
            Text(15),
            Text(16),
            Text(17),
            Text(18),
            Text(19),
            reader.GetGuid(20),
            reader.GetInt32(21),
            reader.GetInt32(22),
            new VisitSampleCounts(reader.GetInt32(23), reader.GetInt32(24), reader.GetInt32(25), reader.GetInt32(26), reader.GetInt32(27), reader.GetInt32(53)),
            Text(28),
            Text(29),
            new VisitResultCounts(reader.GetInt32(30), reader.GetInt32(31), reader.GetInt32(32), reader.GetInt32(33), reader.GetInt32(34)),
            new VisitResultCounts(reader.GetInt32(35), reader.GetInt32(36), reader.GetInt32(37), reader.GetInt32(38), reader.GetInt32(39)),
            reader.GetInt32(40),
            reader.GetInt32(41),
            reader.GetInt32(42),
            Text(43),
            Text(44),
            Clocks(reader, 45, 46),
            Clocks(reader, 47, 48),
            reader.GetDecimal(49),
            reader.GetDecimal(50),
            reader.GetDecimal(51),
            reader.GetBoolean(52));
    }

    private static LabTrackerServiceRow ServiceRow(DbDataReader reader)
    {
        string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

        return new LabTrackerServiceRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            new VisitSampleCounts(reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13)),
            Text(14),
            Text(15),
            new VisitResultCounts(reader.GetInt32(16), reader.GetInt32(17), reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20)),
            new VisitResultCounts(reader.GetInt32(21), reader.GetInt32(22), reader.GetInt32(23), reader.GetInt32(24), reader.GetInt32(25)),
            reader.GetInt32(26),
            reader.GetInt32(27),
            reader.GetInt32(28),
            Text(29),
            Text(30),
            Clocks(reader, 31, 32),
            Clocks(reader, 33, 34));
    }

    private static IReadOnlyList<ResultClock> Clocks(DbDataReader reader, int starts, int expected)
    {
        if (reader.IsDBNull(starts) || reader.IsDBNull(expected))
        {
            return [];
        }

        var from = reader.GetFieldValue<DateTime[]>(starts);
        var to = reader.GetFieldValue<DateTime[]>(expected);
        return [.. from.Zip(to, (start, end) => new ResultClock(Instant(start), Instant(end)))];
    }

    private static DateTimeOffset Instant(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
