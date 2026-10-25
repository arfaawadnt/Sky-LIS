namespace Platform.Contracts.PlatformAdmin;

// Announcements of National Technology and its partners (change request LIS-CR-013, C-17; SA-22). Times are UTC.

/// <summary>
/// Create or edit an announcement (SA-22). <see cref="Placement"/> is <c>SignIn</c> or <c>Welcome</c>; <see cref="AudienceStatus"/>
/// <c>Any</c> (default), <c>Trial</c> or <c>Active</c>; an empty <see cref="AudienceClientIds"/> (or null) = every client the other
/// criteria admit. The image is uploaded separately (<c>POST …/{id}/image</c>).
/// </summary>
public sealed record AnnouncementRequest(
    string TitleEn,
    string TitleAr,
    string? BodyEn,
    string? BodyAr,
    string? LinkUrl,
    string Placement,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    Guid? AudienceCountryId,
    Guid? AudiencePlanId,
    string? AudienceStatus,
    IReadOnlyList<Guid>? AudienceClientIds);

/// <summary>
/// An announcement as the Super Admin register and editor show it. <see cref="IsLive"/> = active and inside its schedule now;
/// <see cref="ImageVersion"/> is null without an image (the preview is <c>GET /api/v1/platform/announcements/{id}/image</c>).
/// </summary>
public sealed record AnnouncementView(
    Guid Id,
    string TitleEn,
    string TitleAr,
    string? BodyEn,
    string? BodyAr,
    string? LinkUrl,
    string Placement,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    Guid? AudienceCountryId,
    Guid? AudiencePlanId,
    string AudienceStatus,
    IReadOnlyList<Guid> AudienceClientIds,
    bool IsActive,
    bool IsLive,
    int? ImageVersion,
    long Impressions,
    long Clicks,
    DateTimeOffset CreatedAtUtc);

/// <summary>The image of an announcement after an upload: its version (the cache key), detected type and size.</summary>
public sealed record AnnouncementImageView(int ImageVersion, string ContentType, long SizeBytes);

/// <summary>
/// What a laboratory sees (anonymous sign-in hero, <c>GET /api/v1/public/announcements</c>; dashboard welcome card,
/// <c>GET /api/v1/account/announcements</c>). The image is <c>GET /api/v1/public/announcements/{id}/image?v={imageVersion}</c>
/// when <see cref="ImageVersion"/> is not null. Nothing about the audience is published.
/// </summary>
public sealed record PublicAnnouncement(Guid Id, string TitleEn, string TitleAr, string? BodyEn, string? BodyAr, string? LinkUrl, int? ImageVersion);

/// <summary>SA-04 "Hide announcements for this client" (setting <c>hideAnnouncements</c> of the client).</summary>
public sealed record ClientAnnouncementsSetting(bool Hidden);
