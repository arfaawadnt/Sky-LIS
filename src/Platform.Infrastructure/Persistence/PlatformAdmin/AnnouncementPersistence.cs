using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// Announcements (LIS-CR-013, C-17; SA-22): saas.announcements — a platform table (no tenant, no RLS, single-column key). The
// audience's country and plan are logical references (no FK: SA-10 may delete an unreferenced country, and an audience naming a
// deleted country simply matches nobody); the client list is a uuid[] (precedent: tenant_memberships.branch_ids). Grants in
// PlatformAdmin.migration.sql (block 6). The counters are atomic increments (ExecuteUpdate: no tracking, no interceptors).

/// <summary><c>saas.announcements</c>.</summary>
internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, announcement => announcement.Title, "title", Announcement.MaxTitleLength);
        builder.Property(announcement => announcement.BodyEn).HasMaxLength(Announcement.MaxBodyLength);
        builder.Property(announcement => announcement.BodyAr).HasMaxLength(Announcement.MaxBodyLength);
        builder.Property(announcement => announcement.LinkUrl).HasMaxLength(Announcement.MaxLinkLength);
        builder.Property(announcement => announcement.Placement).IsRequired();
        builder.Property(announcement => announcement.FromUtc).IsRequired();
        builder.Property(announcement => announcement.AudienceStatus).IsRequired();
        builder.Property(announcement => announcement.ClientIds).IsRequired();   // uuid[]; empty = every client the other criteria admit
        builder.Property(announcement => announcement.ImageContentType).HasMaxLength(Announcement.MaxContentTypeLength);
        builder.Property(announcement => announcement.ImageStorageKey).HasMaxLength(Announcement.MaxStorageKeyLength);
        builder.Ignore(announcement => announcement.HasImage);
        builder.Ignore(announcement => announcement.Audience);

        builder.HasIndex(announcement => new { announcement.Placement, announcement.IsActive, announcement.FromUtc });
    }
}

internal sealed class AnnouncementRepository(AppDbContext context) : Repository<Announcement>(context), IAnnouncementRepository
{
    /// <summary>More live announcements of one placement than this are not read (the feed shows at most five).</summary>
    private const int LiveReadLimit = 50;

    private static readonly SortColumns<Announcement> Sorts = new SortColumns<Announcement>(AnnouncementSortKeys.Announcements)
        .Column("title", announcement => announcement.Title.En)
        .Column("titleAr", announcement => announcement.Title.Ar)
        .Column("placement", announcement => announcement.Placement)
        .Column("fromUtc", announcement => announcement.FromUtc)
        .Column("toUtc", announcement => announcement.ToUtc)
        .Column("impressions", announcement => announcement.Impressions)
        .Column("clicks", announcement => announcement.Clicks)
        .ThenBy(announcement => announcement.Id);

    public async Task<Paged<AnnouncementView>> ListAsync(AnnouncementFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Search is { Length: > 0 } search)
        {
            var pattern = Like.Contains(search);
            query = query.Where(row => EF.Functions.ILike(row.Title.En, pattern, Like.Escape) || EF.Functions.ILike(row.Title.Ar, pattern, Like.Escape));
        }

        if (filter.Placement is { } placement)
        {
            query = query.Where(row => row.Placement == placement);
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(row => row.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Sorts.Apply(query, filter.Sort, rows => rows.OrderByDescending(row => row.FromUtc).ThenByDescending(row => row.Id))
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<AnnouncementView>(rows.Select(row => ToView(row, nowUtc)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<AnnouncementView?> GetViewAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToView(row, nowUtc);
    }

    public Task<Announcement?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Announcement>> ListLiveAsync(AnnouncementPlacement placement, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await Set.AsNoTracking()
            .Where(row => row.Placement == placement && row.IsActive && row.FromUtc <= nowUtc && (row.ToUtc == null || row.ToUtc > nowUtc))
            .OrderByDescending(row => row.FromUtc)
            .ThenByDescending(row => row.Id)
            .Take(LiveReadLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static AnnouncementView ToView(Announcement row, DateTimeOffset nowUtc) => new(
        row.Id,
        row.Title.En,
        row.Title.Ar,
        row.BodyEn,
        row.BodyAr,
        row.LinkUrl,
        row.Placement.ToString(),
        row.FromUtc,
        row.ToUtc,
        row.AudienceCountryId,
        row.AudiencePlanId,
        row.AudienceStatus.ToString(),
        row.ClientIds,
        row.IsActive,
        row.IsLiveAt(nowUtc),
        row.HasImage ? row.ImageVersion : null,
        row.Impressions,
        row.Clicks,
        row.CreatedAtUtc);
}

/// <summary>The client records an announcement audience is matched and validated against; platform tables, no elevation.</summary>
internal sealed class AnnouncementAudienceDirectory(AppDbContext context) : IAnnouncementAudienceDirectory
{
    public async Task<AnnouncementViewer?> FindViewerAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var row = await context.Set<Tenant>().AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new { tenant.CountryId, tenant.PlanId, tenant.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new AnnouncementViewer(tenantId, row.CountryId, row.PlanId, row.Status);
    }

    public Task<bool> CountryExistsAsync(Guid countryId, CancellationToken cancellationToken) =>
        context.Set<Country>().AnyAsync(country => country.Id == countryId, cancellationToken);

    public Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken) =>
        context.Set<Plan>().AnyAsync(plan => plan.Id == planId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> MissingClientsAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientIds);
        var wanted = clientIds.Distinct().ToList();
        var found = await context.Set<Tenant>().AsNoTracking()
            .Where(tenant => wanted.Contains(tenant.Id))
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return wanted.Except(found).ToList();
    }
}

/// <summary>One atomic increment on a live announcement (<c>UPDATE … SET impressions = impressions + 1 WHERE …</c>).</summary>
internal sealed class AnnouncementCounters(AppDbContext context) : IAnnouncementCounters
{
    public async Task<bool> IncrementAsync(Guid announcementId, AnnouncementCounter counter, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var live = context.Set<Announcement>()
            .Where(row => row.Id == announcementId && row.IsActive && row.FromUtc <= nowUtc && (row.ToUtc == null || row.ToUtc > nowUtc));
        var counted = counter == AnnouncementCounter.Click
            ? await live.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Clicks, row => row.Clicks + 1), cancellationToken).ConfigureAwait(false)
            : await live.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Impressions, row => row.Impressions + 1), cancellationToken).ConfigureAwait(false);
        return counted == 1;
    }
}
