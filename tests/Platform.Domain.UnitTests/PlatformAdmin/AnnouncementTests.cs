using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// Announcements (LIS-CR-013, C-17; intake LIS-INT-PLT-001 §19): content rules, the schedule, the audience (country, plan,
/// status, client list), activation, the image and its signature check.
/// </summary>
public sealed class AnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Egypt = Guid.CreateVersion7();
    private static readonly Guid Saudi = Guid.CreateVersion7();
    private static readonly Guid Starter = Guid.CreateVersion7();
    private static readonly Guid Standard = Guid.CreateVersion7();

    private static AnnouncementContent Content(string? link = "https://nationaltechnology.net/offer") =>
        new(" New analyser offer ", "عرض الأجهزة الجديد", "Ten percent off this month.", null, link);

    private static Announcement Live(AnnouncementAudience? audience = null, AnnouncementPlacement placement = AnnouncementPlacement.SignIn)
    {
        var announcement = Announcement.Create(Content(), placement, new AnnouncementSchedule(Now.AddDays(-1), Now.AddDays(7)), audience ?? AnnouncementAudience.Everyone);
        announcement.Activate(Now);
        return announcement;
    }

    private static AnnouncementViewer Lab(TenantStatus status = TenantStatus.Active, Guid? country = null, Guid? plan = null, Guid? tenant = null) =>
        new(tenant ?? Guid.CreateVersion7(), country ?? Egypt, plan ?? Starter, status);

    [Fact]
    public void S_ANN_create_A_new_announcement_is_a_trimmed_inactive_draft_without_image_or_counts()
    {
        var announcement = Announcement.Create(Content(), AnnouncementPlacement.Welcome, new AnnouncementSchedule(Now, null), AnnouncementAudience.Everyone);

        Assert.Equal(("New analyser offer", "عرض الأجهزة الجديد", null), (announcement.Title.En, announcement.Title.Ar, announcement.BodyAr));
        Assert.Equal((AnnouncementPlacement.Welcome, false, false, 0, 0L, 0L), (announcement.Placement, announcement.IsActive, announcement.HasImage, announcement.ImageVersion, announcement.Impressions, announcement.Clicks));
        Assert.False(announcement.IsLiveAt(Now));
        Assert.Empty(announcement.DomainEvents);
    }

    [Theory]
    [InlineData("", "عنوان", null)]
    [InlineData("Title", " ", null)]
    [InlineData("Title", "عنوان", "x")]
    public void S_ANN_text_A_title_in_both_languages_is_required_and_bodies_are_bounded_ANN_001(string titleEn, string titleAr, string? bodyMarker)
    {
        var body = bodyMarker is null ? null : new string('x', Announcement.MaxBodyLength + 1);
        var refusal = Assert.Throws<DomainException>(() => Announcement.Create(new AnnouncementContent(titleEn, titleAr, body, null, null), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, null), AnnouncementAudience.Everyone));
        Assert.Equal((AnnouncementCodes.TextInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("ftp://example.com/file")]
    public void S_ANN_link_Only_an_absolute_https_address_without_credentials_is_a_link_ANN_002(string link)
    {
        var refusal = Assert.Throws<DomainException>(() => Announcement.Create(Content(link), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, null), AnnouncementAudience.Everyone));
        Assert.Equal(AnnouncementCodes.LinkInvalid, refusal.Code);
    }

    [Fact]
    public void S_ANN_link_A_blank_link_is_none_and_an_https_link_is_kept()
    {
        Assert.Null(Announcement.CleanLink("  "));
        Assert.Equal("https://nationaltechnology.net/a?b=1", Announcement.CleanLink(" https://nationaltechnology.net/a?b=1 "));
    }

    [Fact]
    public void S_ANN_schedule_The_schedule_must_end_after_it_starts_ANN_003()
    {
        var refusal = Assert.Throws<DomainException>(() => Announcement.Create(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, Now), AnnouncementAudience.Everyone));
        Assert.Equal(AnnouncementCodes.ScheduleInvalid, refusal.Code);
    }

    [Fact]
    public void S_ANN_schedule_An_active_announcement_is_live_from_its_start_until_before_its_end()
    {
        var announcement = Live();

        Assert.True(announcement.IsLiveAt(Now));
        Assert.True(announcement.IsLiveAt(Now.AddDays(-1)));
        Assert.False(announcement.IsLiveAt(Now.AddDays(-1).AddTicks(-1)));
        Assert.False(announcement.IsLiveAt(Now.AddDays(7)));

        announcement.Deactivate();
        Assert.False(announcement.IsLiveAt(Now));
    }

    [Fact]
    public void S_ANN_activate_An_ended_schedule_cannot_be_activated_ANN_010_but_an_open_one_can_twice()
    {
        var ended = Announcement.Create(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now.AddDays(-10), Now.AddDays(-1)), AnnouncementAudience.Everyone);
        var refusal = Assert.Throws<DomainException>(() => ended.Activate(Now));
        Assert.Equal((AnnouncementCodes.ScheduleEnded, DomainFault.Conflict), (refusal.Code, refusal.Fault));

        var open = Announcement.Create(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now.AddDays(1), null), AnnouncementAudience.Everyone);
        open.Activate(Now);
        open.Activate(Now);
        Assert.True(open.IsActive);
        Assert.False(open.IsLiveAt(Now));   // active, but its schedule starts tomorrow
    }

    [Fact]
    public void S_ANN_audience_An_open_audience_reaches_every_usable_client_and_the_anonymous_sign_in_page()
    {
        var announcement = Live();

        Assert.True(announcement.IsShownTo(null, Now));
        Assert.True(announcement.IsShownTo(Lab(TenantStatus.Trial), Now));
        Assert.True(announcement.IsShownTo(Lab(TenantStatus.Active, Saudi, Standard), Now));
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Inactive), Now));
    }

    [Fact]
    public void S_ANN_audience_Every_given_criterion_must_match_and_a_restricted_audience_never_reaches_the_anonymous_page()
    {
        var target = Guid.CreateVersion7();
        var announcement = Live(new AnnouncementAudience(Egypt, Standard, AnnouncementAudienceStatus.Trial, [target]));

        Assert.True(announcement.IsShownTo(Lab(TenantStatus.Trial, Egypt, Standard, target), Now));
        Assert.False(announcement.IsShownTo(null, Now));
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Active, Egypt, Standard, target), Now));   // status
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Trial, Saudi, Standard, target), Now));    // country
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Trial, Egypt, Starter, target), Now));     // plan
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Trial, Egypt, Standard), Now));            // not on the list
    }

    [Fact]
    public void S_ANN_audience_Active_status_admits_active_clients_only()
    {
        var announcement = Live(new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Active, []));

        Assert.True(announcement.IsShownTo(Lab(TenantStatus.Active), Now));
        Assert.False(announcement.IsShownTo(Lab(TenantStatus.Trial), Now));
    }

    [Fact]
    public void S_ANN_audience_A_client_list_is_deduplicated_and_refuses_an_empty_id_or_more_than_200_clients_ANN_004()
    {
        var client = Guid.CreateVersion7();
        var announcement = Announcement.Create(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, null), new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Any, [client, client]));
        Assert.Equal([client], announcement.ClientIds);

        Assert.Equal(AnnouncementCodes.AudienceInvalid, Assert.Throws<DomainException>(() => announcement.Update(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, null), new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Any, [Guid.Empty]))).Code);
        var many = Enumerable.Range(0, Announcement.MaxClients + 1).Select(_ => Guid.CreateVersion7()).ToList();
        Assert.Equal(AnnouncementCodes.AudienceInvalid, Assert.Throws<DomainException>(() => announcement.Update(Content(), AnnouncementPlacement.SignIn, new AnnouncementSchedule(Now, null), new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Any, many))).Code);
    }

    [Fact]
    public void S_ANN_update_An_edit_keeps_the_active_flag_the_image_and_the_counters()
    {
        var announcement = Live();
        announcement.AttachImage(new AnnouncementImage("image/png", 1024), "key/1");

        announcement.Update(new AnnouncementContent("Renamed", "معدل", null, "نص", null), AnnouncementPlacement.Welcome, new AnnouncementSchedule(Now.AddDays(-2), null), AnnouncementAudience.Everyone);

        Assert.Equal(("Renamed", "نص", null, AnnouncementPlacement.Welcome), (announcement.Title.En, announcement.BodyAr, announcement.LinkUrl, announcement.Placement));
        Assert.True(announcement.IsActive);
        Assert.Equal((true, 1), (announcement.HasImage, announcement.ImageVersion));
    }

    [Fact]
    public void S_ANN_image_Every_upload_and_removal_raises_the_version_and_removing_nothing_is_ANN_011()
    {
        var announcement = Live();
        announcement.AttachImage(new AnnouncementImage("image/png", 2048), "key/1");
        announcement.AttachImage(new AnnouncementImage("image/webp", 4096), "key/2");
        Assert.Equal(("image/webp", 4096L, "key/2", 2), (announcement.ImageContentType, announcement.ImageSizeBytes, announcement.ImageStorageKey, announcement.ImageVersion));

        announcement.RemoveImage();
        Assert.Equal((false, null, 3), (announcement.HasImage, announcement.ImageStorageKey, announcement.ImageVersion));
        Assert.Equal((AnnouncementCodes.ImageMissing, DomainFault.NotFound), Refusal(() => announcement.RemoveImage()));
    }

    [Fact]
    public void S_ANN_image_The_signature_decides_PNG_JPEG_WebP_up_to_512_KB_ANN_006_007_008()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] webp = [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray()];
        byte[] svg = [.. "<svg xmlns="u8.ToArray()];

        Assert.Equal("image/png", AnnouncementImages.Inspect(100, png).ContentType);
        Assert.Equal("image/jpeg", AnnouncementImages.Inspect(100, jpeg).ContentType);
        Assert.Equal("image/webp", AnnouncementImages.Inspect(Announcement.MaxImageBytes, webp).ContentType);
        Assert.Equal(AnnouncementCodes.ImageEmpty, Assert.Throws<DomainException>(() => AnnouncementImages.Inspect(0, png)).Code);
        Assert.Equal(AnnouncementCodes.ImageTooLarge, Assert.Throws<DomainException>(() => AnnouncementImages.Inspect(Announcement.MaxImageBytes + 1, png)).Code);
        Assert.Equal(AnnouncementCodes.ImageTypeNotAllowed, Assert.Throws<DomainException>(() => AnnouncementImages.Inspect(100, svg)).Code);
    }

    [Fact]
    public void S_ANN_optout_The_client_opt_out_is_a_client_flag_off_by_default_that_the_Parameters_pages_never_write()
    {
        Assert.Same(SettingKeys.HideAnnouncements, SettingKeys.Find("hideAnnouncements"));
        Assert.True(SettingKeys.IsClientFlag(SettingKeys.HideAnnouncements.Key));   // PUT /api/v1/settings answers SET-004
        Assert.DoesNotContain(SettingKeys.All, descriptor => descriptor != SettingKeys.HideAnnouncements && SettingKeys.IsClientFlag(descriptor.Key));
        Assert.False(SettingKeys.HideAnnouncements.Default);
        Assert.True(SettingKeys.HideAnnouncements.Parse("true"));
    }

    private static (string Code, DomainFault Fault) Refusal(Action action)
    {
        var refusal = Assert.Throws<DomainException>(action);
        return (refusal.Code, refusal.Fault);
    }
}
