using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.Samples;
using Platform.Domain.Samples;

namespace Platform.Infrastructure.Persistence.Samples;

/// <summary>The read port Results uses for the Received gate (BR-03): untracked reads of this module's own table.</summary>
internal sealed class SampleStatusLookup(AppDbContext context) : ISampleStatusLookup
{
    private IQueryable<Sample> Live => context.Set<Sample>().AsNoTracking().Where(sample => sample.Status != SampleStatus.Replaced);

    public async Task<SampleStatusView?> ByTubeAsync(Guid tubeId, CancellationToken cancellationToken)
    {
        var rows = await Project(Live.Where(sample => sample.TubeId == tubeId).OrderByDescending(sample => sample.Generation).Take(1)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : ToView(rows[0]);
    }

    public async Task<IReadOnlyDictionary<Guid, SampleStatusView>> ByTubesAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tubeIds);
        if (tubeIds.Count == 0)
        {
            return new Dictionary<Guid, SampleStatusView>();
        }

        var ids = tubeIds.Distinct().ToArray();
        var rows = await Project(Live.Where(sample => ids.Contains(sample.TubeId))).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.TubeId).ToDictionary(group => group.Key, group => ToView(group.OrderByDescending(row => row.Generation).First()));
    }

    public async Task<SampleStatusView?> BySampleNumberAsync(string sampleNumber, CancellationToken cancellationToken)
    {
        var number = (sampleNumber ?? string.Empty).Trim().ToUpperInvariant();
        var rows = await Project(context.Set<Sample>().AsNoTracking().Where(sample => sample.SampleNumber == number)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : ToView(rows[0]);
    }

    public async Task<SampleStatusView?> BySampleIdAsync(Guid sampleId, CancellationToken cancellationToken)
    {
        var rows = await Project(context.Set<Sample>().AsNoTracking().Where(sample => sample.Id == sampleId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : ToView(rows[0]);
    }

    public async Task<IReadOnlyList<SampleStatusView>> ByVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var rows = await Project(Live.Where(sample => sample.VisitId == visitId).OrderBy(sample => sample.SampleNumber)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(ToView)];
    }

    public async Task<bool> IsResultEntryAllowedAsync(Guid tubeId, CancellationToken cancellationToken) =>
        (await ByTubeAsync(tubeId, cancellationToken).ConfigureAwait(false))?.IsResultEntryAllowed ?? false;

    private static IQueryable<StatusRow> Project(IQueryable<Sample> query) =>
        query.Select(sample => new StatusRow(sample.Id, sample.TubeId, sample.Generation, sample.SampleNumber, sample.VisitId, sample.Status, sample.CollectedAtUtc, sample.ReceivedAtUtc));

    private static SampleStatusView ToView(StatusRow row) =>
        new(row.Id, row.TubeId, row.SampleNumber, row.VisitId, row.Status.ToString(), row.CollectedAtUtc, row.ReceivedAtUtc);

    private sealed record StatusRow(Guid Id, Guid TubeId, int Generation, string SampleNumber, Guid VisitId, SampleStatus Status, DateTimeOffset? CollectedAtUtc, DateTimeOffset? ReceivedAtUtc);
}

/// <summary>Catalogue rejection reasons by id through <c>app.reasons</c> (Catalogue B, wave 1), with the availability fallback of the reader.</summary>
internal sealed class RejectionReasonLookup(AppDbContext context, ITenantContextAccessor tenant) : IRejectionReasonLookup
{
    public async Task<RejectionReasonCheck> FindAsync(Guid reasonId, CancellationToken cancellationToken)
    {
        var rows = await new SampleReferenceReader(context, tenant).ReasonsAsync([reasonId], cancellationToken).ConfigureAwait(false);
        if (rows is null)
        {
            return new RejectionReasonCheck(false, null);
        }

        return rows.TryGetValue(reasonId, out var row)
            ? new RejectionReasonCheck(true, new RejectionReasonRow(row.Id, row.Kind, row.NameEn, row.NameAr, row.IsActive))
            : new RejectionReasonCheck(true, null);
    }
}

/// <summary>
/// The lines of a tube in Registration's <c>app.visit_lines</c> (MODULES.md §5: <c>id, tenant_id, tube_id, status</c>),
/// read by raw SQL because Registration is built in the same wave. While the table is not installed the answer is
/// "live lines remain", so nothing is cancelled by mistake.
/// </summary>
internal sealed class VisitLineLookup(AppDbContext context, ITenantContextAccessor tenant) : IVisitLineLookup
{
    private const string InstalledSql = """
        SELECT count(*)::int AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'visit_lines' AND column_name::text IN ('id', 'tenant_id', 'tube_id', 'status')
        """;

    private const string LiveLinesSql = """
        SELECT count(*)::int AS "Value"
        FROM app.visit_lines
        WHERE tenant_id = {0} AND tube_id = {1} AND id <> {2} AND status IN ('Ordered', 'Reserved')
        """;

    public async Task<bool> TubeHasLiveLinesAsync(Guid tubeId, Guid excludingLineId, CancellationToken cancellationToken)
    {
        if (tenant.CurrentTenantId is not { } tenantId)
        {
            return true;
        }

        var installed = await context.Database.SqlQueryRaw<int>(InstalledSql).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (installed.Count == 0 || installed[0] < 4)
        {
            return true;
        }

        var live = await context.Database.SqlQueryRaw<int>(LiveLinesSql, tenantId, tubeId, excludingLineId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return live.Count > 0 && live[0] > 0;
    }
}
