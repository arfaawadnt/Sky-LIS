using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Billing;
using Platform.Contracts.Billing;

namespace Platform.Infrastructure.Persistence.Billing;

// Names and reasons owned by modules of wave 1 (Catalogue A/B, Patients, Pricing, Organization, Platform administration),
// read with raw SQL on their tables so Billing compiles without their entity types: app.reasons, app.services,
// app.patients, app.payers, app.branches, saas.tenants (columns id, tenant_id, code, name_en, name_ar, kind, is_active,
// number, full_name). Verified against the merged wave-1 model by BillingModuleTests.

/// <summary>Catalogue B reasons of kind Refund (CP-24 "reason mandatory"; CP-53 reason lists).</summary>
internal sealed class RefundReasonLookup(AppDbContext context) : IRefundReasonLookup
{
    private const string ActiveSql = """
        SELECT r.id, r.name_en, r.name_ar
        FROM app.reasons r
        WHERE r.tenant_id = {0} AND r.id = {1} AND r.kind = 'Refund' AND r.is_active
        """;

    private const string ManySql = "SELECT r.id, r.name_en, r.name_ar FROM app.reasons r WHERE r.tenant_id = {0} AND r.id = ANY({1})";

    public async Task<RefundReasonView?> GetActiveAsync(Guid reasonId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<NameRow>(ActiveSql, context.CurrentTenantIdOrNil, reasonId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : new RefundReasonView(rows[0].Id, rows[0].NameEn, rows[0].NameAr);
    }

    public async Task<IReadOnlyDictionary<Guid, RefundReasonView>> GetManyAsync(IReadOnlyCollection<Guid> reasonIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reasonIds);
        var ids = reasonIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, RefundReasonView>();
        }

        var rows = await DisplayNames.TolerantAsync(() => context.Database.SqlQueryRaw<NameRow>(ManySql, context.CurrentTenantIdOrNil, ids).ToListAsync(cancellationToken)).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new RefundReasonView(row.Id, row.NameEn, row.NameAr));
    }
}

/// <summary>Letterhead, patient, service and payer names for documents and registers; a missing source renders blanks, never a refusal.</summary>
internal sealed class BillingDirectory(AppDbContext context) : IBillingDirectory
{
    public const string DefaultLabNameEn = "Laboratory";
    public const string DefaultLabNameAr = "المختبر";

    private const string TenantSql = "SELECT t.id, t.name_en, t.name_ar FROM saas.tenants t WHERE t.id = {0}";
    private const string BranchSql = "SELECT b.id, b.name_en, b.name_ar FROM app.branches b WHERE b.tenant_id = {0} AND b.id = {1}";
    private const string PatientsSql = "SELECT p.id, p.number, p.full_name FROM app.patients p WHERE p.tenant_id = {0} AND p.id = ANY({1})";
    private const string ServicesSql = "SELECT s.id, s.code, s.name_en, s.name_ar FROM app.services s WHERE s.tenant_id = {0} AND s.id = ANY({1})";
    private const string PayersSql = "SELECT p.id, NULL::text AS code, p.name_en, p.name_ar FROM app.payers p WHERE p.tenant_id = {0} AND p.id = ANY({1})";

    public async Task<DocumentLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var tenant = context.CurrentTenantIdOrNil;
        var labs = await DisplayNames.TolerantAsync(() => context.Database.SqlQueryRaw<NameRow>(TenantSql, tenant).ToListAsync(cancellationToken)).ConfigureAwait(false);
        var branches = await DisplayNames.TolerantAsync(() => context.Database.SqlQueryRaw<NameRow>(BranchSql, tenant, branchId).ToListAsync(cancellationToken)).ConfigureAwait(false);
        var lab = labs.FirstOrDefault();
        var branch = branches.FirstOrDefault();
        return new DocumentLetterhead(
            string.IsNullOrWhiteSpace(lab?.NameEn) ? DefaultLabNameEn : lab.NameEn,
            string.IsNullOrWhiteSpace(lab?.NameAr) ? DefaultLabNameAr : lab.NameAr,
            branch?.NameEn,
            branch?.NameAr);
    }

    public async Task<IReadOnlyDictionary<Guid, PatientCaption>> PatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = Ids(patientIds);
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PatientCaption>();
        }

        var rows = await DisplayNames.TolerantAsync(() => context.Database.SqlQueryRaw<PatientRow>(PatientsSql, context.CurrentTenantIdOrNil, ids).ToListAsync(cancellationToken)).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new PatientCaption(row.Id, row.Number, row.FullName));
    }

    public Task<IReadOnlyDictionary<Guid, NamedReference>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        NamedAsync(ServicesSql, serviceIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken) =>
        NamedAsync(PayersSql, payerIds, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, NamedReference>> NamedAsync(string sql, IReadOnlyCollection<Guid> wanted, CancellationToken cancellationToken)
    {
        var ids = Ids(wanted);
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, NamedReference>();
        }

        var rows = await DisplayNames.TolerantAsync(() => context.Database.SqlQueryRaw<CodedNameRow>(sql, context.CurrentTenantIdOrNil, ids).ToListAsync(cancellationToken)).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new NamedReference(row.Id, row.Code, row.NameEn ?? string.Empty, row.NameAr ?? string.Empty));
    }

    private static Guid[] Ids(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Where(id => id != Guid.Empty).Distinct().ToArray();
    }

    private sealed class PatientRow
    {
        public Guid Id { get; set; }

        public string? Number { get; set; }

        public string? FullName { get; set; }
    }

    private sealed class CodedNameRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string? NameEn { get; set; }

        public string? NameAr { get; set; }
    }
}

internal sealed class NameRow
{
    public Guid Id { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }
}

/// <summary>
/// Display names never block a document: when the owning module's table is not there yet (undefined table or column) or the
/// runtime role may not read it (insufficient privilege), the name is left blank. Validation reads never use this.
/// </summary>
internal static class DisplayNames
{
    public static async Task<List<T>> TolerantAsync<T>(Func<Task<List<T>>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
        {
            return [];
        }
    }
}
