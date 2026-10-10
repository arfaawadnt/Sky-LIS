using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Integration;
using Platform.Contracts.Integration;

namespace Platform.Infrastructure.Persistence.Integration;

// Read ports over other modules' tables (MODULES.md §1 "Cross-module reads", MODULE-PATTERN §3 "Same-wave lookups"): raw
// SQL on the agreed tables and columns, never their entity types. Every statement names the tenant explicitly (RLS is the
// independent second layer). A table that does not exist yet (42P01) or a column a module has not shipped (42703) reads as
// "nothing found": validation then fails closed and the download list is empty.

/// <summary>A catalogue / organization row as the module reads it (snake_case columns of the unmapped result type).</summary>
internal sealed class ReferenceRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

internal sealed class AnalyserModelRow
{
    public Guid Id { get; set; }

    public string Vendor { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? Protocol { get; set; }

    public string? PhysicalLink { get; set; }
}

internal sealed class SampleRow
{
    public Guid Id { get; set; }

    public string SampleNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

internal sealed class OrderSqlRow
{
    public string SampleNumber { get; set; } = string.Empty;

    public string? Accession { get; set; }

    public Guid SampleTypeId { get; set; }

    public bool Urgent { get; set; }

    public DateTimeOffset? CollectedAtUtc { get; set; }

    public DateTimeOffset? ReceivedAtUtc { get; set; }

    public Guid TestId { get; set; }
}

internal static class MissingSchema
{
    /// <summary>The engine says the table, column or (for the platform library) the grant is not there yet.</summary>
    public static bool Is(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege };
}

/// <summary>
/// <see cref="IIntegrationReferenceLookup"/>: catalogue <c>app.clinical_groups</c>, <c>app.sample_types</c>, <c>app.tests</c>,
/// <c>app.units</c> and organization <c>app.branches</c> (<c>id, tenant_id, code, name_en, name_ar, is_active</c>); the
/// platform library <c>saas.analyser_models</c> (<c>id, vendor, model, protocol, physical_link, is_active</c>; no tenant).
/// </summary>
internal sealed class IntegrationReferenceLookup(AppDbContext context) : IIntegrationReferenceLookup
{
    public const int MaxIds = 1000;

    // {0} = tenant, {1} = uuid[]; the table names are constants chosen by the enum, never input.
    private const string ClinicalGroupsSql = "SELECT t.id, t.code, t.name_en, t.name_ar, t.is_active FROM app.clinical_groups t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    private const string SampleTypesSql = "SELECT t.id, t.code, t.name_en, t.name_ar, t.is_active FROM app.sample_types t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    private const string TestsSql = "SELECT t.id, t.code, t.name_en, t.name_ar, t.is_active FROM app.tests t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    private const string UnitsSql = "SELECT t.id, t.code, t.name_en, t.name_ar, t.is_active FROM app.units t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    private const string BranchesSql = "SELECT t.id, t.code, t.name_en, t.name_ar, t.is_active FROM app.branches t WHERE t.tenant_id = {0} AND t.id = ANY({1})";

    private const string ModelExistsSql = "SELECT m.id AS \"Value\" FROM saas.analyser_models m WHERE m.id = {0}";
    private const string ModelsSql = "SELECT m.id, m.vendor, m.model, m.protocol, m.physical_link FROM saas.analyser_models m WHERE m.is_active ORDER BY m.vendor, m.model LIMIT 500";
    private const string ModelsSearchSql = "SELECT m.id, m.vendor, m.model, m.protocol, m.physical_link FROM saas.analyser_models m WHERE m.is_active AND (m.vendor ILIKE {0} OR m.model ILIKE {0}) ORDER BY m.vendor, m.model LIMIT 500";

    public async Task<IReadOnlyDictionary<Guid, ReferenceName>> FindAsync(ReferenceKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wanted.Length, MaxIds, nameof(ids));
        if (wanted.Length == 0)
        {
            return new Dictionary<Guid, ReferenceName>();
        }

        var sql = kind switch
        {
            ReferenceKind.ClinicalGroup => ClinicalGroupsSql,
            ReferenceKind.SampleType => SampleTypesSql,
            ReferenceKind.Test => TestsSql,
            ReferenceKind.Unit => UnitsSql,
            ReferenceKind.Branch => BranchesSql,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown reference kind."),
        };

        try
        {
            var rows = await context.Database.SqlQueryRaw<ReferenceRow>(sql, context.CurrentTenantIdOrNil, wanted).ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.ToDictionary(row => row.Id, row => new ReferenceName(row.Id, row.Code, row.NameEn, row.NameAr, row.IsActive));
        }
        catch (Exception exception) when (MissingSchema.Is(exception))
        {
            return new Dictionary<Guid, ReferenceName>();
        }
    }

    public async Task<bool> AnalyserModelExistsAsync(Guid analyserModelId, CancellationToken cancellationToken)
    {
        try
        {
            var found = await context.Database.SqlQueryRaw<Guid>(ModelExistsSql, analyserModelId).ToListAsync(cancellationToken).ConfigureAwait(false);
            return found.Count > 0;
        }
        catch (Exception exception) when (MissingSchema.Is(exception))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<AnalyserModelItem>> ListAnalyserModelsAsync(string? search, CancellationToken cancellationToken)
    {
        try
        {
            var rows = search is null
                ? await context.Database.SqlQueryRaw<AnalyserModelRow>(ModelsSql).ToListAsync(cancellationToken).ConfigureAwait(false)
                : await context.Database.SqlQueryRaw<AnalyserModelRow>(ModelsSearchSql, SearchPattern.Contains(search)).ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.Select(row => new AnalyserModelItem(row.Id, row.Vendor, row.Model, row.Protocol, row.PhysicalLink)).ToList();
        }
        catch (Exception exception) when (MissingSchema.Is(exception))
        {
            return [];
        }
    }
}

/// <summary>
/// <see cref="IIntegrationSampleLookup"/>: samples (<c>app.samples</c>: id, tenant_id, tube_id, sample_number, visit_id,
/// sample_type_id, status, collected_at_utc, received_at_utc), registration (<c>app.visits</c>: id, accession, branch_id,
/// urgent; <c>app.visit_lines</c>: visit_id, service_id, tube_id, status) and the catalogue's service members
/// (<c>app.service_members</c>: service_id, kind 'Test' | 'Service', member_id), nested packages followed three levels deep.
/// </summary>
internal sealed class IntegrationSampleLookup(AppDbContext context) : IIntegrationSampleLookup
{
    public const int MaxNumbers = 1000;

    private const string SamplesSql = "SELECT s.id, s.sample_number, s.status FROM app.samples s WHERE s.tenant_id = {0} AND s.sample_number = ANY({1})";

    // {0} tenant, {1} branch, {2} since, {3} uuid[] mapped tests, {4} sample limit.
    private const string OrdersSql = """
        WITH RECURSIVE chosen AS (
            SELECT s.tenant_id, s.visit_id, s.tube_id, s.sample_number, s.sample_type_id, s.collected_at_utc, s.received_at_utc, v.accession, v.urgent
            FROM app.samples s
            JOIN app.visits v ON v.tenant_id = s.tenant_id AND v.id = s.visit_id
            WHERE s.tenant_id = {0} AND v.branch_id = {1} AND s.status IN ('Collected', 'Received')
              AND COALESCE(s.received_at_utc, s.collected_at_utc) >= {2}
            ORDER BY COALESCE(s.received_at_utc, s.collected_at_utc), s.sample_number
            LIMIT {4}
        ), line_services AS (
            SELECT c.sample_number, c.sample_type_id, c.collected_at_utc, c.received_at_utc, c.accession, c.urgent, l.service_id, 0 AS depth
            FROM chosen c
            JOIN app.visit_lines l ON l.tenant_id = c.tenant_id AND l.visit_id = c.visit_id AND l.tube_id = c.tube_id AND l.status = 'Ordered'
            UNION ALL
            SELECT ls.sample_number, ls.sample_type_id, ls.collected_at_utc, ls.received_at_utc, ls.accession, ls.urgent, m.member_id, ls.depth + 1
            FROM line_services ls
            JOIN app.service_members m ON m.tenant_id = {0} AND m.service_id = ls.service_id AND m.kind = 'Service'
            WHERE ls.depth < 3
        )
        SELECT DISTINCT ls.sample_number, ls.accession, ls.sample_type_id, ls.urgent, ls.collected_at_utc, ls.received_at_utc, m.member_id AS test_id
        FROM line_services ls
        JOIN app.service_members m ON m.tenant_id = {0} AND m.service_id = ls.service_id AND m.kind = 'Test'
        WHERE m.member_id = ANY({3})
        """;

    public async Task<IReadOnlyDictionary<string, SampleRef>> FindByNumbersAsync(IReadOnlyCollection<string> sampleNumbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sampleNumbers);
        var wanted = sampleNumbers.Where(number => !string.IsNullOrWhiteSpace(number)).Distinct(StringComparer.Ordinal).ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wanted.Length, MaxNumbers, nameof(sampleNumbers));
        if (wanted.Length == 0)
        {
            return new Dictionary<string, SampleRef>(StringComparer.Ordinal);
        }

        try
        {
            var rows = await context.Database.SqlQueryRaw<SampleRow>(SamplesSql, context.CurrentTenantIdOrNil, wanted).ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.GroupBy(row => row.SampleNumber, StringComparer.Ordinal).ToDictionary(group => group.Key, group => new SampleRef(group.First().Id, group.Key, group.First().Status), StringComparer.Ordinal);
        }
        catch (Exception exception) when (MissingSchema.Is(exception))
        {
            return new Dictionary<string, SampleRef>(StringComparer.Ordinal);
        }
    }

    public async Task<IReadOnlyList<OrderRow>> ListOrdersAsync(Guid branchId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testIds);
        var tests = testIds.Distinct().ToArray();
        if (tests.Length == 0 || limit <= 0)
        {
            return [];
        }

        try
        {
            var rows = await context.Database.SqlQueryRaw<OrderSqlRow>(OrdersSql, context.CurrentTenantIdOrNil, branchId, sinceUtc, tests, limit).ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.OrderBy(row => row.SampleNumber, StringComparer.Ordinal)
                .Select(row => new OrderRow(row.SampleNumber, row.Accession, row.SampleTypeId, row.Urgent, row.CollectedAtUtc, row.ReceivedAtUtc, row.TestId))
                .ToList();
        }
        catch (Exception exception) when (MissingSchema.Is(exception))
        {
            return [];
        }
    }
}
