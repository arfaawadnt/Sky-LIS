using Microsoft.EntityFrameworkCore;
using Platform.Contracts.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>A row of <c>app.services</c> (Catalogue A) as Catalogue B reads it; snake_case columns for the unmapped result type.</summary>
internal sealed class CatalogueBServiceRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

/// <summary>A row of <c>app.clinical_groups</c> (Catalogue A).</summary>
internal sealed class CatalogueBGroupRow
{
    public Guid Id { get; set; }

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;
}

/// <summary>
/// The same-wave lookups of Catalogue B (MODULE-PATTERN §3 "Same-wave lookups"): raw SQL on the agreed tables of
/// Catalogue A (<c>app.services</c>, <c>app.clinical_groups</c>: <c>id, tenant_id, code, name_en, name_ar, is_active</c>),
/// Organization (<c>app.branches</c>: <c>id, tenant_id</c>) and Pricing (<c>app.payers</c>: <c>id, tenant_id</c>;
/// <c>app.contract_tiers</c>: <c>id, tenant_id, payer_id</c>). Never their entity types. Every statement names the tenant
/// explicitly, so an elevated scope still reads one tenant.
/// </summary>
internal static class CatalogueBSql
{
    public static async Task<IReadOnlyDictionary<Guid, LinkedServiceView>> ServicesAsync(AppDbContext context, IEnumerable<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var ids = serviceIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, LinkedServiceView>();
        }

        var rows = await context.Database.SqlQueryRaw<CatalogueBServiceRow>(
                "SELECT s.id, s.code, s.name_en, s.name_ar, s.is_active FROM app.services s WHERE s.tenant_id = {0} AND s.id = ANY({1})",
                context.CurrentTenantIdOrNil,
                ids)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new LinkedServiceView(row.Id, row.Code, row.NameEn, row.NameAr, row.IsActive));
    }

    /// <summary>The services in the stored order; an id Catalogue A no longer knows shows as an inactive placeholder.</summary>
    public static IReadOnlyList<LinkedServiceView> Link(IEnumerable<Guid> ids, IReadOnlyDictionary<Guid, LinkedServiceView> known) =>
        ids.Select(id => known.TryGetValue(id, out var service) ? service : new LinkedServiceView(id, id.ToString("N")[..8].ToUpperInvariant(), string.Empty, string.Empty, false)).ToList();

    public static async Task<IReadOnlyList<Guid>> MissingAsync(AppDbContext context, string existingIdsSql, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        var found = await context.Database.SqlQueryRaw<Guid>(
                existingIdsSql,
                context.CurrentTenantIdOrNil,
                wanted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return wanted.Except(found).ToList();
    }

    public static async Task<IReadOnlyDictionary<Guid, CatalogueBGroupRow>> ClinicalGroupsAsync(AppDbContext context, IEnumerable<Guid?> groupIds, CancellationToken cancellationToken)
    {
        var ids = groupIds.Where(id => id is { } value && value != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, CatalogueBGroupRow>();
        }

        var rows = await context.Database.SqlQueryRaw<CatalogueBGroupRow>(
                "SELECT g.id, g.name_en, g.name_ar FROM app.clinical_groups g WHERE g.tenant_id = {0} AND g.id = ANY({1})",
                context.CurrentTenantIdOrNil,
                ids)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id);
    }

    public static async Task<bool> ContractTierOfPayerExistsAsync(AppDbContext context, Guid contractTierId, Guid payerId, CancellationToken cancellationToken)
    {
        var found = await context.Database.SqlQueryRaw<Guid>(
                "SELECT t.id AS \"Value\" FROM app.contract_tiers t WHERE t.tenant_id = {0} AND t.id = {1} AND t.payer_id = {2}",
                context.CurrentTenantIdOrNil,
                contractTierId,
                payerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return found.Count > 0;
    }

    // {0} = tenant, {1} = uuid[] of wanted ids; the statements are constants, never built from input.
    public const string ExistingServicesSql = "SELECT t.id AS \"Value\" FROM app.services t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    public const string ExistingClinicalGroupsSql = "SELECT t.id AS \"Value\" FROM app.clinical_groups t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    public const string ExistingBranchesSql = "SELECT t.id AS \"Value\" FROM app.branches t WHERE t.tenant_id = {0} AND t.id = ANY({1})";
    public const string ExistingPayersSql = "SELECT t.id AS \"Value\" FROM app.payers t WHERE t.tenant_id = {0} AND t.id = ANY({1})";

    /// <summary>A case-insensitive <c>ILIKE</c> pattern for the search text (wildcards in the text are matched literally), or <see langword="null"/>.</summary>
    public static string? SearchPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }
}
