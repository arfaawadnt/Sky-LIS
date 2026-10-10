using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;

namespace Platform.Infrastructure.Persistence.Samples;

/// <summary>
/// Names of other modules' records for the sample registers and documents, read by raw SQL on the agreed tables
/// (MODULE-PATTERN §3 "same-wave lookups"; intake LIS-INT-SMP-001 §16): patients, clinical groups, sample types and
/// conditions, services, branches and reasons are owned by wave-1 modules whose entity types this module never
/// references. Each read first checks that the table and its columns exist (cached once found), so a missing table
/// yields no names instead of an error; the tenant is filtered explicitly on top of row-level security. User names come
/// from the foundation's <c>identity.platform_users</c>.
/// </summary>
internal sealed class SampleReferenceReader(AppDbContext context, ITenantContextAccessor tenant)
{
    private static readonly ConcurrentDictionary<string, bool> Installed = new(StringComparer.Ordinal);

    private const string ColumnCountSql = """
        SELECT count(*)::int AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = {0} AND column_name::text = ANY({1})
        """;

    private const string PatientsSql = """
        SELECT id, number AS code, full_name AS name_en, name_ar FROM app.patients WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string ClinicalGroupsSql = """
        SELECT id, unit_code AS code, name_en, name_ar FROM app.clinical_groups WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string SampleTypesSql = """
        SELECT id, code, name_en, name_ar FROM app.sample_types WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string SampleConditionsSql = """
        SELECT id, code, name_en, name_ar FROM app.sample_conditions WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string ServicesSql = """
        SELECT id, code, name_en, name_ar, 0 AS label_count FROM app.services WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string ServicesWithLabelsSql = """
        SELECT id, code, name_en, name_ar, coalesce(label_count, 0)::int AS label_count FROM app.services WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string BranchesSql = """
        SELECT id, code, name_en, name_ar FROM app.branches WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private const string ReasonsSql = """
        SELECT id, kind, name_en, name_ar, is_active FROM app.reasons WHERE tenant_id = {0} AND id = ANY({1})
        """;

    private static readonly string[] PatientColumns = ["id", "tenant_id", "number", "full_name", "name_ar"];
    private static readonly string[] ClinicalGroupColumns = ["id", "tenant_id", "unit_code", "name_en", "name_ar"];
    private static readonly string[] NamedColumns = ["id", "tenant_id", "code", "name_en", "name_ar"];
    private static readonly string[] LabelCountColumn = ["label_count"];
    private static readonly string[] ReasonColumns = ["id", "tenant_id", "kind", "name_en", "name_ar", "is_active"];

    public Task<IReadOnlyDictionary<Guid, NameRow>> PatientsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        NamesAsync("patients", PatientColumns, PatientsSql, ids, cancellationToken);

    /// <summary><see cref="NameRow.Code"/> is the unit letter printed on labels (LDM §2.2).</summary>
    public Task<IReadOnlyDictionary<Guid, NameRow>> ClinicalGroupsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        NamesAsync("clinical_groups", ClinicalGroupColumns, ClinicalGroupsSql, ids, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, NameRow>> SampleTypesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        NamesAsync("sample_types", NamedColumns, SampleTypesSql, ids, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, NameRow>> SampleConditionsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        NamesAsync("sample_conditions", NamedColumns, SampleConditionsSql, ids, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, NameRow>> BranchesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        NamesAsync("branches", NamedColumns, BranchesSql, ids, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ServiceRow>> ServicesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var (tenantId, keys) = Prepare(ids);
        if (tenantId is not { } scope || keys.Length == 0 || !await IsInstalledAsync("services", NamedColumns, cancellationToken).ConfigureAwait(false))
        {
            return new Dictionary<Guid, ServiceRow>();
        }

        var sql = await IsInstalledAsync("services", LabelCountColumn, cancellationToken).ConfigureAwait(false) ? ServicesWithLabelsSql : ServicesSql;
        var rows = await context.Database.SqlQueryRaw<ServiceRow>(sql, scope, keys).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>Null when the reasons table is not installed; otherwise the rows found (possibly none).</summary>
    public async Task<IReadOnlyDictionary<Guid, ReasonRow>?> ReasonsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        if (!await IsInstalledAsync("reasons", ReasonColumns, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var (tenantId, keys) = Prepare(ids);
        if (tenantId is not { } scope || keys.Length == 0)
        {
            return new Dictionary<Guid, ReasonRow>();
        }

        var rows = await context.Database.SqlQueryRaw<ReasonRow>(ReasonsSql, scope, keys).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>Display names of platform users (foundation table, no tenant), as recorded on the account.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> UsersAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Where(id => id is { } value && value != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await context.PlatformUsers.AsNoTracking()
            .Where(user => keys.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<Guid, NameRow>> NamesAsync(string table, string[] columns, string sql, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var (tenantId, keys) = Prepare(ids);
        if (tenantId is not { } scope || keys.Length == 0 || !await IsInstalledAsync(table, columns, cancellationToken).ConfigureAwait(false))
        {
            return new Dictionary<Guid, NameRow>();
        }

        var rows = await context.Database.SqlQueryRaw<NameRow>(sql, scope, keys).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First());
    }

    private (Guid? TenantId, Guid[] Keys) Prepare(IEnumerable<Guid> ids) =>
        (tenant.CurrentTenantId, [.. ids.Where(id => id != Guid.Empty).Distinct()]);

    private async Task<bool> IsInstalledAsync(string table, string[] columns, CancellationToken cancellationToken)
    {
        var key = $"{table}:{string.Join(',', columns)}";
        if (Installed.ContainsKey(key))
        {
            return true;
        }

        var counts = await context.Database.SqlQueryRaw<int>(ColumnCountSql, table, columns).ToListAsync(cancellationToken).ConfigureAwait(false);
        var found = counts.Count == 0 ? 0 : counts[0];
        if (found < columns.Length)
        {
            return false;   // not cached: the table arrives with a later migration
        }

        Installed[key] = true;
        return true;
    }

    /// <summary>A named record of another module; <see cref="Code"/> is the code, the unit letter or the patient number.</summary>
    internal sealed class NameRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string? NameEn { get; set; }

        public string? NameAr { get; set; }
    }

    internal sealed class ServiceRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string? NameEn { get; set; }

        public string? NameAr { get; set; }

        public int LabelCount { get; set; }
    }

    internal sealed class ReasonRow
    {
        public Guid Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
