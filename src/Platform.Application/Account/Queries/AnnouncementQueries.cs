using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Application.Settings;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.Account.Queries;

/// <summary>
/// LIS-CR-013 (C-17): the Welcome announcements of the dashboard card (CP-05) for the session's laboratory — live, in schedule,
/// admitted by the audience of the laboratory's client record (country, plan, status, client list) — or none when the client
/// opted out (<c>hideAnnouncements</c>, set on SA-04). The platform tenant and a tenant without a client record get none.
/// </summary>
[RequireAuthenticatedActor]
public sealed record GetMyAnnouncementsQuery : IQuery<IReadOnlyList<PublicAnnouncement>>;

public sealed class GetMyAnnouncementsHandler(
    IAnnouncementRepository announcements,
    IAnnouncementAudienceDirectory directory,
    ITenantSettingsStore settings,
    ICurrentTenantSetter tenantScope,
    ICurrentTenant tenant,
    IClock clock) : IQueryHandler<GetMyAnnouncementsQuery, IReadOnlyList<PublicAnnouncement>>
{
    public async Task<IReadOnlyList<PublicAnnouncement>> Handle(GetMyAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId || TenantIds.IsPlatform(tenantId))
        {
            return [];
        }

        var viewer = await directory.FindViewerAsync(tenantId, cancellationToken);
        return viewer is null
            ? []
            : await AnnouncementFeed.ForAsync(announcements, settings, tenantScope, AnnouncementPlacement.Welcome, viewer, clock.UtcNow, cancellationToken);
    }
}
