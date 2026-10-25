using FluentValidation;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// SA-22 Announcements (LIS-CR-013, C-17): the Super Admin register, one announcement, its image preview and a client's opt-out;
// and the anonymous reads of a laboratory's sign-in page (the SignIn list and the image of a live announcement).

[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ListAnnouncementsQuery(string? Search, string? Placement, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null)
    : IQuery<Paged<AnnouncementView>>, IPlatformAdministrationRequest;

public sealed class ListAnnouncementsValidator : AbstractValidator<ListAnnouncementsQuery>
{
    public ListAnnouncementsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Placement).IsEnumName(typeof(AnnouncementPlacement), caseSensitive: false).When(query => !string.IsNullOrWhiteSpace(query.Placement));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(AnnouncementSortKeys.Announcements);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListAnnouncementsHandler(IAnnouncementRepository announcements, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<ListAnnouncementsQuery, Paged<AnnouncementView>>
{
    public Task<Paged<AnnouncementView>> Handle(ListAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var placement = string.IsNullOrWhiteSpace(query.Placement) ? (AnnouncementPlacement?)null : Enum.Parse<AnnouncementPlacement>(query.Placement, ignoreCase: true);
        var filter = new AnnouncementFilter(SearchText.Of(query.Search), placement, query.IsActive, ListSort.Of(query.Sort, query.Direction));
        return announcements.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), clock.UtcNow, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record GetAnnouncementQuery(Guid AnnouncementId) : IQuery<AnnouncementView>, IPlatformAdministrationRequest;

public sealed class GetAnnouncementHandler(IAnnouncementRepository announcements, ICurrentTenant currentTenant, IClock clock) : IQueryHandler<GetAnnouncementQuery, AnnouncementView>
{
    public async Task<AnnouncementView> Handle(GetAnnouncementQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return await announcements.GetViewAsync(query.AnnouncementId, clock.UtcNow, cancellationToken) ?? throw AnnouncementImageContent.NotFound();
    }
}

/// <summary>The bytes of an announcement image (the controller streams and disposes them).</summary>
public sealed record AnnouncementImageContent(Stream Content, string ContentType, int Version)
{
    internal static DomainException NotFound() => new(AnnouncementCodes.AnnouncementNotFound, "Announcement not found.", DomainFault.NotFound);

    internal static DomainException NoImage() => new(AnnouncementCodes.ImageMissing, "The announcement has no image.", DomainFault.NotFound);

    /// <summary>Opens the image of <paramref name="announcement"/> from the platform tenant's file store, or refuses (ANN-011).</summary>
    internal static async Task<AnnouncementImageContent> OpenAsync(Announcement announcement, IFileStore files, ICurrentTenantSetter tenantScope, CancellationToken cancellationToken)
    {
        if (announcement.ImageStorageKey is not { } key || announcement.ImageContentType is not { } contentType)
        {
            throw NoImage();
        }

        Stream? content;
        using (tenantScope.ForTenant(TenantIds.Platform))   // the store opens only the current tenant's keys; images are the platform's
        {
            content = await files.OpenReadAsync(key, cancellationToken);
        }

        return content is null ? throw NoImage() : new AnnouncementImageContent(content, contentType, announcement.ImageVersion);
    }
}

/// <summary>The image of any announcement (active or not) for the editor's preview.</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record GetAnnouncementImageQuery(Guid AnnouncementId) : IQuery<AnnouncementImageContent>, IPlatformAdministrationRequest;

public sealed class GetAnnouncementImageHandler(IAnnouncementRepository announcements, IFileStore files, ICurrentTenantSetter tenantScope, ICurrentTenant currentTenant)
    : IQueryHandler<GetAnnouncementImageQuery, AnnouncementImageContent>
{
    public async Task<AnnouncementImageContent> Handle(GetAnnouncementImageQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var announcement = await announcements.FindAsync(query.AnnouncementId, cancellationToken) ?? throw AnnouncementImageContent.NotFound();
        return await AnnouncementImageContent.OpenAsync(announcement, files, tenantScope, cancellationToken);
    }
}

/// <summary>SA-04: whether the client opted out of the announcements (its <c>hideAnnouncements</c> setting). CLI-404 for no client record.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record GetClientAnnouncementsSettingQuery(Guid ClientId) : IQuery<ClientAnnouncementsSetting>, IPlatformAdministrationRequest;

public sealed class GetClientAnnouncementsSettingHandler(ITenantRepository tenants, ITenantSettingsStore settings, ICurrentTenantSetter tenantScope, ICurrentTenant currentTenant)
    : IQueryHandler<GetClientAnnouncementsSettingQuery, ClientAnnouncementsSetting>
{
    public async Task<ClientAnnouncementsSetting> Handle(GetClientAnnouncementsSettingQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        if (TenantIds.IsPlatform(query.ClientId) || await tenants.FindStatusAsync(query.ClientId, cancellationToken) is null)
        {
            throw new DomainException(PlatformAdminCodes.ClientNotFound, "Client not found.", DomainFault.NotFound);
        }

        return new ClientAnnouncementsSetting(await AnnouncementFeed.HiddenForAsync(settings, tenantScope, query.ClientId, cancellationToken));
    }
}

/// <summary>
/// The anonymous SignIn announcements of a laboratory's sign-in page: live, in schedule, admitted by the audience of the laboratory
/// of <paramref name="Laboratory"/> (its address) and not opted out; without an address, open audiences only. An address that names
/// no usable laboratory answers an empty list (the card already says "not found"). Only the <c>SignIn</c> placement is public.
/// </summary>
[AllowUnauthenticated]
public sealed record ListPublicAnnouncementsQuery(string? Placement, string? Laboratory) : IQuery<IReadOnlyList<PublicAnnouncement>>;

public sealed class ListPublicAnnouncementsValidator : AbstractValidator<ListPublicAnnouncementsQuery>
{
    public ListPublicAnnouncementsValidator()
    {
        RuleFor(query => query.Placement)
            .Must(placement => string.IsNullOrWhiteSpace(placement) || string.Equals(placement.Trim(), nameof(AnnouncementPlacement.SignIn), StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only the SignIn placement is public.");
        RuleFor(query => query.Laboratory).MaximumLength(100);
    }
}

public sealed class ListPublicAnnouncementsHandler(
    IAnnouncementRepository announcements,
    IAnnouncementAudienceDirectory directory,
    IPublicLaboratoryDirectory laboratories,
    ITenantSettingsStore settings,
    ICurrentTenantSetter tenantScope,
    IClock clock) : IQueryHandler<ListPublicAnnouncementsQuery, IReadOnlyList<PublicAnnouncement>>
{
    public async Task<IReadOnlyList<PublicAnnouncement>> Handle(ListPublicAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        AnnouncementViewer? viewer = null;
        if (!string.IsNullOrWhiteSpace(query.Laboratory))
        {
            if (!Tenant.IsAddressShape(query.Laboratory))
            {
                return [];
            }

            var laboratory = await laboratories.FindUsableAsync(Tenant.NormaliseAddress(query.Laboratory), cancellationToken);
            viewer = laboratory is null ? null : await directory.FindViewerAsync(laboratory.TenantId, cancellationToken);
            if (viewer is null)
            {
                return [];
            }
        }

        return await AnnouncementFeed.ForAsync(announcements, settings, tenantScope, AnnouncementPlacement.SignIn, viewer, clock.UtcNow, cancellationToken);
    }
}

/// <summary>The image of a live announcement (sign-in hero, welcome card); ANN-404 / ANN-011 otherwise, so a draft's image stays private.</summary>
[AllowUnauthenticated]
public sealed record GetPublicAnnouncementImageQuery(Guid AnnouncementId) : IQuery<AnnouncementImageContent>;

public sealed class GetPublicAnnouncementImageHandler(IAnnouncementRepository announcements, IFileStore files, ICurrentTenantSetter tenantScope, IClock clock)
    : IQueryHandler<GetPublicAnnouncementImageQuery, AnnouncementImageContent>
{
    public async Task<AnnouncementImageContent> Handle(GetPublicAnnouncementImageQuery query, CancellationToken cancellationToken)
    {
        var announcement = await announcements.FindAsync(query.AnnouncementId, cancellationToken);
        if (announcement is null || !announcement.IsLiveAt(clock.UtcNow))
        {
            throw AnnouncementImageContent.NotFound();
        }

        return await AnnouncementImageContent.OpenAsync(announcement, files, tenantScope, cancellationToken);
    }
}
