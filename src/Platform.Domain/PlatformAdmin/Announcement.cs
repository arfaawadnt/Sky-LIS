using Platform.Domain.Account;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>Where an announcement is shown (LIS-CR-013, decision D-4: no in-app strip).</summary>
public enum AnnouncementPlacement
{
    /// <summary>The hero of a laboratory's sign-in page (<c>/lab/&lt;slug&gt;</c>), instead of the slogan.</summary>
    SignIn = 0,

    /// <summary>One dismissible card on the client dashboard (CP-05) after sign-in.</summary>
    Welcome = 1,
}

/// <summary>The client life-cycle states an announcement addresses (an inactive client never sees one: it cannot sign in).</summary>
public enum AnnouncementAudienceStatus
{
    Any = 0,
    Trial = 1,
    Active = 2,
}

/// <summary>The counters a viewer's browser reports (anonymous, rate-limited by the global chain).</summary>
public enum AnnouncementCounter
{
    Impression = 0,
    Click = 1,
}

/// <summary>The texts of an announcement: title in both languages (≤ 120), optional body per language (≤ 600), optional https link (≤ 500).</summary>
public sealed record AnnouncementContent(string TitleEn, string TitleAr, string? BodyEn, string? BodyAr, string? LinkUrl);

/// <summary>When an announcement is shown: from <see cref="FromUtc"/> (inclusive) until <see cref="ToUtc"/> (exclusive, open when null).</summary>
public sealed record AnnouncementSchedule(DateTimeOffset FromUtc, DateTimeOffset? ToUtc);

/// <summary>
/// Who sees an announcement. Every given criterion must match: the client's country, its plan, its status (Any = Trial or
/// Active), and — when the list is not empty — the client must be on <see cref="ClientIds"/>. An audience with no criterion
/// (<see cref="IsOpen"/>) reaches every client and the anonymous sign-in page without a laboratory.
/// </summary>
public sealed record AnnouncementAudience(Guid? CountryId, Guid? PlanId, AnnouncementAudienceStatus Status, IReadOnlyList<Guid> ClientIds)
{
    public static readonly AnnouncementAudience Everyone = new(null, null, AnnouncementAudienceStatus.Any, []);

    public bool IsOpen => CountryId is null && PlanId is null && Status == AnnouncementAudienceStatus.Any && ClientIds.Count == 0;
}

/// <summary>The laboratory looking at an announcement: its client record's country, plan and status.</summary>
public sealed record AnnouncementViewer(Guid TenantId, Guid CountryId, Guid PlanId, TenantStatus Status);

/// <summary>
/// An announcement of National Technology or a partner (change request LIS-CR-013, C-17; decision D-4), managed by the Super
/// Admin (SA-22, <c>/admin/announcements</c>) and shown to client laboratories in one placement — the sign-in hero or the
/// dashboard welcome card — within its schedule, to its audience. Platform reference data (<c>saas.announcements</c>, no
/// tenant). A new announcement is a draft (inactive) until the Super Admin activates it; an ended schedule cannot be
/// activated (ANN-010). The optional image (PNG, JPEG or WebP ≤ 512 KB, decided by its signature like a laboratory logo)
/// lives in the file store of the platform tenant (area <c>announcements</c>); every change raises <see cref="ImageVersion"/>
/// so browsers reload it. <see cref="Impressions"/> and <see cref="Clicks"/> are counted by the store with an atomic
/// increment (never through this aggregate: a busy sign-in page must not fight over the row version).
/// </summary>
public sealed class Announcement : AggregateRoot
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 600;
    public const int MaxLinkLength = 500;
    public const int MaxClients = 200;
    public const int MaxContentTypeLength = 50;
    public const int MaxStorageKeyLength = 400;
    public const long MaxImageBytes = 512L * 1024;

    private Announcement()
    {
        Title = null!;
        ClientIds = [];
    }

    private Announcement(Guid id) : base(id)
    {
        Title = null!;
        ClientIds = [];
    }

    public LocalizedText Title { get; private set; }

    public string? BodyEn { get; private set; }

    public string? BodyAr { get; private set; }

    /// <summary>An absolute https address the announcement opens in a new tab (clicks are counted).</summary>
    public string? LinkUrl { get; private set; }

    public AnnouncementPlacement Placement { get; private set; }

    public DateTimeOffset FromUtc { get; private set; }

    public DateTimeOffset? ToUtc { get; private set; }

    public Guid? AudienceCountryId { get; private set; }

    public Guid? AudiencePlanId { get; private set; }

    public AnnouncementAudienceStatus AudienceStatus { get; private set; }

    /// <summary>The clients the announcement is limited to (<c>uuid[]</c>); empty = every client the other criteria admit.</summary>
    public Guid[] ClientIds { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Detected content type of the image (image/png, image/jpeg, image/webp); null without an image.</summary>
    public string? ImageContentType { get; private set; }

    public long? ImageSizeBytes { get; private set; }

    /// <summary>Opaque key of the file store (<c>&lt;platform&gt;/announcements/&lt;announcement&gt;/&lt;guid&gt;</c>); null without an image.</summary>
    public string? ImageStorageKey { get; private set; }

    /// <summary>0 before the first image; +1 on every upload or removal (the browsers' cache key).</summary>
    public int ImageVersion { get; private set; }

    public long Impressions { get; private set; }

    public long Clicks { get; private set; }

    public uint RowVersion { get; private set; }

    public bool HasImage => ImageStorageKey is not null;

    public AnnouncementAudience Audience => new(AudienceCountryId, AudiencePlanId, AudienceStatus, ClientIds);

    /// <summary>A draft: inactive until <see cref="Activate"/>.</summary>
    public static Announcement Create(AnnouncementContent content, AnnouncementPlacement placement, AnnouncementSchedule schedule, AnnouncementAudience audience)
    {
        var announcement = new Announcement(Guid.CreateVersion7());
        announcement.Apply(content, placement, schedule, audience);
        return announcement;
    }

    /// <summary>Edits every field but the image and the counters; an active announcement stays active.</summary>
    public void Update(AnnouncementContent content, AnnouncementPlacement placement, AnnouncementSchedule schedule, AnnouncementAudience audience) =>
        Apply(content, placement, schedule, audience);

    /// <summary>Shown from now on, within its schedule. Refused once the schedule has ended (ANN-010). Idempotent.</summary>
    public void Activate(DateTimeOffset nowUtc)
    {
        Require(ToUtc is null || ToUtc > nowUtc, AnnouncementCodes.ScheduleEnded, "The schedule of this announcement has ended; extend it before activating.", DomainFault.Conflict);
        IsActive = true;
    }

    /// <summary>No longer shown. Idempotent.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>A new image (also after a removal); the content was accepted by <see cref="AnnouncementImages.Inspect"/>.</summary>
    public void AttachImage(AnnouncementImage image, string storageKey)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(storageKey.Length, MaxStorageKeyLength, nameof(storageKey));
        Require(image.SizeBytes is > 0 and <= MaxImageBytes, AnnouncementCodes.ImageTooLarge, "The image size is not allowed.", DomainFault.Validation);
        ImageContentType = image.ContentType;
        ImageSizeBytes = image.SizeBytes;
        ImageStorageKey = storageKey;
        ImageVersion++;
    }

    /// <summary>Clears the image (ANN-011 when there is none); the stored file is kept (BR-15).</summary>
    public void RemoveImage()
    {
        Require(HasImage, AnnouncementCodes.ImageMissing, "The announcement has no image.", DomainFault.NotFound);
        ImageContentType = null;
        ImageSizeBytes = null;
        ImageStorageKey = null;
        ImageVersion++;
    }

    /// <summary>Active and inside its schedule at <paramref name="nowUtc"/>.</summary>
    public bool IsLiveAt(DateTimeOffset nowUtc) => IsActive && FromUtc <= nowUtc && (ToUtc is null || nowUtc < ToUtc);

    /// <summary>
    /// Whether the audience admits <paramref name="viewer"/>: every given criterion matches; an inactive client matches
    /// nothing. Without a viewer (the sign-in page of no laboratory) only an open audience matches.
    /// </summary>
    public bool Admits(AnnouncementViewer? viewer) => AdmitsViewer(Audience, viewer);

    /// <summary>Live at <paramref name="nowUtc"/> and admitted by the audience.</summary>
    public bool IsShownTo(AnnouncementViewer? viewer, DateTimeOffset nowUtc) => IsLiveAt(nowUtc) && Admits(viewer);

    /// <summary>The audience rule on its own, for readers that hold the columns rather than the aggregate.</summary>
    public static bool AdmitsViewer(AnnouncementAudience audience, AnnouncementViewer? viewer)
    {
        ArgumentNullException.ThrowIfNull(audience);
        if (viewer is null)
        {
            return audience.IsOpen;
        }

        if (viewer.Status == TenantStatus.Inactive)
        {
            return false;
        }

        var statusMatches = audience.Status switch
        {
            AnnouncementAudienceStatus.Trial => viewer.Status == TenantStatus.Trial,
            AnnouncementAudienceStatus.Active => viewer.Status == TenantStatus.Active,
            _ => true,
        };

        return statusMatches
               && (audience.CountryId is null || audience.CountryId == viewer.CountryId)
               && (audience.PlanId is null || audience.PlanId == viewer.PlanId)
               && (audience.ClientIds.Count == 0 || audience.ClientIds.Contains(viewer.TenantId));
    }

    /// <summary>An absolute https address without credentials, ≤ 500 characters (ANN-002); null for blank.</summary>
    public static string? CleanLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        Require(
            cleaned.Length <= MaxLinkLength
            && Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && !string.IsNullOrEmpty(uri.Host),
            AnnouncementCodes.LinkInvalid,
            $"The link must be an https:// address of at most {MaxLinkLength} characters.",
            DomainFault.Validation);
        return cleaned;
    }

    private void Apply(AnnouncementContent content, AnnouncementPlacement placement, AnnouncementSchedule schedule, AnnouncementAudience audience)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(audience);
        Require(Enum.IsDefined(placement), AnnouncementCodes.PlacementInvalid, "The placement is SignIn or Welcome.", DomainFault.Validation);
        Require(schedule.ToUtc is null || schedule.ToUtc > schedule.FromUtc, AnnouncementCodes.ScheduleInvalid, "The schedule must end after it starts.", DomainFault.Validation);
        Require(Enum.IsDefined(audience.Status), AnnouncementCodes.AudienceInvalid, "The audience status is Any, Trial or Active.", DomainFault.Validation);
        var clients = (audience.ClientIds ?? []).Distinct().ToArray();
        Require(clients.Length <= MaxClients && !clients.Contains(Guid.Empty), AnnouncementCodes.AudienceInvalid, $"The audience names at most {MaxClients} clients, none empty.", DomainFault.Validation);
        Require(audience.CountryId != Guid.Empty && audience.PlanId != Guid.Empty, AnnouncementCodes.AudienceInvalid, "The audience country and plan are an id or none.", DomainFault.Validation);

        Title = DomainText.Localized(content.TitleEn, content.TitleAr, MaxTitleLength, AnnouncementCodes.TextInvalid, "The title");
        BodyEn = DomainText.Optional(content.BodyEn, MaxBodyLength, AnnouncementCodes.TextInvalid, "The English body");
        BodyAr = DomainText.Optional(content.BodyAr, MaxBodyLength, AnnouncementCodes.TextInvalid, "The Arabic body");
        LinkUrl = CleanLink(content.LinkUrl);
        Placement = placement;
        FromUtc = schedule.FromUtc;
        ToUtc = schedule.ToUtc;
        AudienceCountryId = audience.CountryId;
        AudiencePlanId = audience.PlanId;
        AudienceStatus = audience.Status;
        ClientIds = clients;
    }
}

/// <summary>An image accepted by <see cref="AnnouncementImages.Inspect"/>: the detected content type and the size.</summary>
public sealed record AnnouncementImage(string ContentType, long SizeBytes);

/// <summary>
/// The content rules of an announcement image, the same as a laboratory logo's: PNG, JPEG or WebP decided by the file's
/// signature only (the client's name and content type are ignored), 1 byte to 512 KB; SVG is refused (it can carry script).
/// </summary>
public static class AnnouncementImages
{
    public const int HeaderLength = LaboratoryLogoFiles.HeaderLength;

    /// <summary>Validates size and signature; returns the accepted image or refuses with ANN-006…008.</summary>
    public static AnnouncementImage Inspect(long sizeBytes, ReadOnlySpan<byte> header)
    {
        if (sizeBytes <= 0)
        {
            throw new DomainException(AnnouncementCodes.ImageEmpty, "The file is empty.", DomainFault.Validation);
        }

        if (sizeBytes > Announcement.MaxImageBytes)
        {
            throw new DomainException(AnnouncementCodes.ImageTooLarge, $"An announcement image may not exceed {Announcement.MaxImageBytes / 1024} KB.", DomainFault.Validation);
        }

        var contentType = LaboratoryLogoFiles.DetectContentType(header)
            ?? throw new DomainException(AnnouncementCodes.ImageTypeNotAllowed, "The image must be a PNG, JPEG or WebP file.", DomainFault.Validation);
        return new AnnouncementImage(contentType, sizeBytes);
    }
}
