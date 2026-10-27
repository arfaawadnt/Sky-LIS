using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Patients;
using Platform.Application.Registration;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Registration;

namespace Platform.Infrastructure.Persistence.Registration;

/// <summary>The read port other modules use (Billing, Results, Reporting): a visit with its lines and tubes.</summary>
internal sealed class VisitLookup(AppDbContext context) : IVisitLookup
{
    public const int MaxIds = 500;

    public async Task<VisitView?> GetAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var visit = await Visits().FirstOrDefaultAsync(candidate => candidate.Id == visitId, cancellationToken).ConfigureAwait(false);
        return visit is null ? null : View(visit);
    }

    public async Task<VisitView?> GetByAccessionAsync(string accession, CancellationToken cancellationToken)
    {
        var typed = (accession ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        var upper = typed.ToUpperInvariant();
        var visit = await Visits().FirstOrDefaultAsync(candidate => candidate.Accession == typed || candidate.Accession == upper, cancellationToken).ConfigureAwait(false);
        return visit is null ? null : View(visit);
    }

    public async Task<IReadOnlyDictionary<Guid, VisitView>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitIds);
        var ids = visitIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Length, MaxIds, nameof(visitIds));
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, VisitView>();
        }

        var visits = await Visits().Where(visit => ids.Contains(visit.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return visits.ToDictionary(visit => visit.Id, View);
    }

    private IQueryable<Visit> Visits() =>
        context.Set<Visit>().AsNoTracking().Include(visit => visit.Lines).Include(visit => visit.Tubes).AsSplitQuery();

    private static VisitView View(Visit visit) =>
        new(
            visit.Id,
            visit.Accession,
            visit.PatientId,
            visit.OriginalPatientId,
            visit.BranchId,
            visit.LocationType.ToString(),
            visit.PayerId,
            visit.ContractTierId,
            visit.ReferringDoctorId,
            visit.DiagnosisId,
            visit.BookingId,
            visit.BatchId,
            visit.Status.ToString(),
            visit.Urgent,
            visit.Biohazard,
            visit.AnyCollectedOutside,
            visit.RegisteredAtUtc,
            visit.RegisteredByUserId,
            visit.CompletedAtUtc,
            visit.TotalListPrice,
            visit.DiscountTotal,
            visit.PatientShareTotal,
            visit.PayerShareTotal,
            visit.UtcOffsetMinutes,
            [.. visit.Lines.OrderBy(line => line.Sequence).Select(line => new VisitLineView(
                line.Id,
                line.ServiceId,
                line.ServiceCode,
                line.ServiceNameEn,
                line.ServiceNameAr,
                line.ServiceType,
                line.Kind.ToString(),
                line.ParentLineId,
                line.TubeId,
                line.Status.ToString(),
                line.ListPrice,
                line.PatientShare,
                line.PayerShare,
                line.Discount,
                line.ExpectedResultAtUtc,
                [.. line.TestIds],
                line.CollectorUserId,
                line.OrderedAtUtc,
                line.CancelledAtUtc))],
            [.. visit.Tubes.OrderBy(tube => tube.CreatedAtUtc).Select(tube => new VisitTubeView(tube.Id, tube.SampleNumber, tube.ClinicalGroupId, tube.SampleTypeId, tube.SampleConditionId, tube.UnitCode))]);
}

/// <summary>
/// Service entry (CP-06) by raw SQL on Catalogue A's <c>app.services</c> (code, short_name, shortcut_no, aliases, name_en,
/// name_ar, type, sequence, is_active, is_orderable): active orderable services only, the tenant named explicitly besides RLS.
/// </summary>
internal sealed class ServiceEntryFinder(AppDbContext context) : IServiceEntryFinder
{
    private const string Columns = "SELECT s.id, s.code, s.short_name, s.shortcut_no, s.name_en, s.name_ar, s.type FROM app.services s";

    // {0} tenant, {1} upper-cased text, {2} shortcut number (or -1)
    private const string ExactSql = Columns + """
         WHERE s.tenant_id = {0} AND s.is_active AND s.is_orderable
           AND (s.shortcut_no = {2} OR upper(s.code) = {1} OR upper(s.short_name) = {1} OR EXISTS (SELECT 1 FROM unnest(s.aliases) AS alias WHERE upper(alias) = {1}))
         ORDER BY CASE WHEN s.shortcut_no = {2} THEN 0 WHEN upper(s.code) = {1} THEN 1 ELSE 2 END, s.sequence, s.code
         LIMIT 10
        """;

    // {0} tenant, {1} ILIKE pattern, {2} shortcut number (or -1), {3} limit
    private const string SearchSql = Columns + """
         WHERE s.tenant_id = {0} AND s.is_active AND s.is_orderable
           AND (s.shortcut_no = {2} OR s.code ILIKE {1} OR s.short_name ILIKE {1} OR s.name_en ILIKE {1} OR s.name_ar ILIKE {1}
                OR EXISTS (SELECT 1 FROM unnest(s.aliases) AS alias WHERE alias ILIKE {1}))
         ORDER BY s.sequence, s.code
         LIMIT {3}
        """;

    private const string ManySql = Columns + " WHERE s.tenant_id = {0} AND s.id = ANY({1})";

    public async Task<IReadOnlyList<ServiceEntryItem>> FindExactAsync(string text, CancellationToken cancellationToken)
    {
        var typed = (text ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return [];
        }

        var rows = await context.Database.SqlQueryRaw<ServiceRow>(ExactSql, context.CurrentTenantIdOrNil, typed.ToUpperInvariant(), Number(typed)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var exactNumber = rows.Where(row => row.ShortcutNo == Number(typed)).ToList();
        return [.. (exactNumber.Count > 0 ? exactNumber : rows).Select(Item)];
    }

    public async Task<IReadOnlyList<ServiceEntryItem>> SearchAsync(string text, int limit, CancellationToken cancellationToken)
    {
        var typed = (text ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return [];
        }

        var pattern = "%" + typed.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var rows = await context.Database.SqlQueryRaw<ServiceRow>(SearchSql, context.CurrentTenantIdOrNil, pattern, Number(typed), Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(Item)];
    }

    public async Task<IReadOnlyList<ServiceEntryItem>> GetManyAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var ids = serviceIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await context.Database.SqlQueryRaw<ServiceRow>(ManySql, context.CurrentTenantIdOrNil, ids).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(Item)];
    }

    private static int Number(string text) => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0 ? value : -1;

    private static ServiceEntryItem Item(ServiceRow row) => new(row.Id, row.Code, row.ShortName, row.ShortcutNo, row.NameEn, row.NameAr, row.Type);

    private sealed class ServiceRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string ShortName { get; set; } = string.Empty;

        public int? ShortcutNo { get; set; }

        public string NameEn { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }
}

/// <summary>Sending laboratories (Catalogue B, wave 1) for CP-09.</summary>
internal sealed class SendingLabLookup(AppDbContext context) : ISendingLabLookup
{
    public Task<SendingLabOption?> GetAsync(Guid sendingLabId, CancellationToken cancellationToken) =>
        context.Set<SendingLab>().AsNoTracking()
            .Where(lab => lab.Id == sendingLabId)
            .Select(lab => new SendingLabOption(lab.Id, lab.Name.En, lab.Name.Ar, lab.PayerId, lab.ContractTierId, lab.Courier.En, lab.Courier.Ar, lab.Phone, lab.DoctorId, lab.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SendingLabOption>> ListActiveAsync(CancellationToken cancellationToken) =>
        await context.Set<SendingLab>().AsNoTracking()
            .Where(lab => lab.IsActive)
            .OrderBy(lab => lab.Name.En)
            .Select(lab => new SendingLabOption(lab.Id, lab.Name.En, lab.Name.Ar, lab.PayerId, lab.ContractTierId, lab.Courier.En, lab.Courier.Ar, lab.Phone, lab.DoctorId, lab.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>The letterhead of the module's documents: the laboratory's names (platform client record, raw SQL, tolerant) and the branch (Organization).</summary>
internal sealed class RegistrationDirectory(AppDbContext context) : IRegistrationDirectory
{
    public const string DefaultLabNameEn = "Laboratory";
    public const string DefaultLabNameAr = "المختبر";

    private const string TenantSql = "SELECT t.name_en, t.name_ar FROM saas.tenants t WHERE t.id = {0} AND t.kind = 'Laboratory'";

    public async Task<RegistrationLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken)
    {
        List<LabRow> labs;
        try
        {
            labs = await context.Database.SqlQueryRaw<LabRow>(TenantSql, context.CurrentTenantIdOrNil).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
        {
            labs = [];
        }

        var branch = branchId == Guid.Empty
            ? null
            : await context.Set<Branch>().AsNoTracking().Where(candidate => candidate.Id == branchId).Select(candidate => new { candidate.Name.En, candidate.Name.Ar }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lab = labs.FirstOrDefault();
        return new RegistrationLetterhead(
            string.IsNullOrWhiteSpace(lab?.NameEn) ? DefaultLabNameEn : lab.NameEn,
            string.IsNullOrWhiteSpace(lab?.NameAr) ? DefaultLabNameAr : lab.NameAr,
            branch?.En,
            branch?.Ar);
    }

    private sealed class LabRow
    {
        public string? NameEn { get; set; }

        public string? NameAr { get; set; }
    }
}

/// <summary>
/// The visit figures of the patient register (CP-11; UAT A-09): one grouped query over <c>app.visits</c> of the current
/// tenant for the files of a register page, counted on the file each visit belongs to now (merges re-point visits).
/// </summary>
internal sealed class PatientVisitStatistics(AppDbContext context) : IPatientVisitStatistics
{
    public async Task<IReadOnlyDictionary<Guid, PatientVisitFigures>> ForPatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        var ids = patientIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Length, IPatientVisitStatistics.MaxIds, nameof(patientIds));
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PatientVisitFigures>();
        }

        var rows = await context.Set<Visit>().AsNoTracking()
            .Where(visit => ids.Contains(visit.PatientId))
            .GroupBy(visit => visit.PatientId)
            .Select(group => new { PatientId = group.Key, Count = group.Count(), Last = group.Max(visit => visit.RegisteredAtUtc) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.PatientId, row => new PatientVisitFigures(row.Count, row.Last));
    }
}
