using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;

namespace Platform.Application.PlatformAdmin;

// Announcements of National Technology and its partners (LIS-CR-013, C-17; SA-22): the write side, the audience lookups and the
// atomic counters. saas.announcements is a platform table (no tenant, no RLS); the image lives in the platform tenant's file store.

/// <summary>The SA-22 register filter: search in both titles, placement, active flag; <see cref="Sort"/> from <see cref="AnnouncementSortKeys.Announcements"/>.</summary>
public sealed record AnnouncementFilter(string? Search, AnnouncementPlacement? Placement, bool? IsActive, ListSort Sort = default);

public interface IAnnouncementRepository : IRepository<Announcement>
{
    Task<Paged<AnnouncementView>> ListAsync(AnnouncementFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<AnnouncementView?> GetViewAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>The announcement read-only (image reads), or <see langword="null"/>.</summary>
    Task<Announcement?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every announcement of the placement that is live at <paramref name="nowUtc"/> (active, in schedule), newest start first; read-only.</summary>
    Task<IReadOnlyList<Announcement>> ListLiveAsync(AnnouncementPlacement placement, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

/// <summary>The client records an audience is matched and validated against (<c>saas.tenants</c>, <c>saas.countries</c>, <c>saas.plans</c>).</summary>
public interface IAnnouncementAudienceDirectory
{
    /// <summary>The laboratory's country, plan and status; <see langword="null"/> for the platform tenant or a tenant without a client record.</summary>
    Task<AnnouncementViewer?> FindViewerAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> CountryExistsAsync(Guid countryId, CancellationToken cancellationToken);

    Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>The ids of <paramref name="clientIds"/> that name no client record.</summary>
    Task<IReadOnlyList<Guid>> MissingClientsAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken);
}

/// <summary>
/// The counters a viewer's browser reports, as one atomic <c>UPDATE … SET n = n + 1</c> on a live announcement (anything else
/// counts nothing): no aggregate load, no row-version race on a busy sign-in page, no audit row per view. The increment is the
/// whole write, so no unit of work commits around it.
/// </summary>
public interface IAnnouncementCounters
{
    /// <summary>True when a live announcement was counted.</summary>
    Task<bool> IncrementAsync(Guid announcementId, AnnouncementCounter counter, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

/// <summary>The sortable columns of the SA-22 register (validation v0.5.0 B-03 rule: allow-list, server order before paging).</summary>
public static class AnnouncementSortKeys
{
    public static readonly SortKeys Announcements = new("title", "titleAr", "placement", "fromUtc", "toUtc", "impressions", "clicks");
}

/// <summary>What a laboratory is shown: the live announcements of a placement its audience admits, unless the client opted out.</summary>
public static class AnnouncementFeed
{
    /// <summary>At most this many announcements per answer (the sign-in hero shows the first, the welcome card pages through them).</summary>
    public const int MaxShown = 5;

    /// <summary>
    /// The live announcements of <paramref name="placement"/> for <paramref name="viewer"/> (null = no laboratory: open audiences
    /// only). A client whose <see cref="SettingKeys.HideAnnouncements"/> is on sees none; its setting is read in its own tenant
    /// scope (RLS, no elevation).
    /// </summary>
    public static async Task<IReadOnlyList<PublicAnnouncement>> ForAsync(
        IAnnouncementRepository announcements,
        ITenantSettingsStore settings,
        ICurrentTenantSetter tenantScope,
        AnnouncementPlacement placement,
        AnnouncementViewer? viewer,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcements);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tenantScope);
        if (viewer is not null && await HiddenForAsync(settings, tenantScope, viewer.TenantId, cancellationToken))
        {
            return [];
        }

        var live = await announcements.ListLiveAsync(placement, nowUtc, cancellationToken);
        return live.Where(announcement => announcement.IsShownTo(viewer, nowUtc)).Take(MaxShown).Select(ToPublic).ToList();
    }

    /// <summary>
    /// The client's opt-out: its <b>own</b> <c>hideAnnouncements</c> row only, read in its tenant scope (RLS, no elevation) — a client
    /// flag never inherits the platform tenant's rows (<see cref="SettingKeys.IsClientFlag"/>).
    /// </summary>
    public static async Task<bool> HiddenForAsync(ITenantSettingsStore settings, ICurrentTenantSetter tenantScope, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tenantScope);
        IReadOnlyList<StoredSetting> rows;
        using (tenantScope.ForTenant(tenantId))
        {
            rows = await settings.ListAsync(cancellationToken);
        }

        var own = rows.FirstOrDefault(row => row.Key == SettingKeys.HideAnnouncements.Key);
        return own is not null && SettingKeys.HideAnnouncements.Parse(own.Value);
    }

    public static PublicAnnouncement ToPublic(Announcement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        return new PublicAnnouncement(
            announcement.Id,
            announcement.Title.En,
            announcement.Title.Ar,
            announcement.BodyEn,
            announcement.BodyAr,
            announcement.LinkUrl,
            announcement.HasImage ? announcement.ImageVersion : null);
    }
}
