using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Contracts.Settings;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Announcements over the real API (change request LIS-CR-013, C-17; intake LIS-INT-PLT-001 §19, LIS-INT-ACC-001): the SA-22
/// register and editor (platform.manage writes, platform.view reads, a laboratory is fenced out), the anonymous sign-in list
/// (schedule, audience, opt-out, five-minute public cache), the dashboard's Welcome list, the image and the counters.
/// </summary>
public sealed class AnnouncementsFlowTests : IClassFixture<ApiFactory>
{
    private const string Admin = "/api/v1/platform/announcements";
    private const string Public = "/api/v1/public/announcements";
    private const string Mine = "/api/v1/account/announcements";
    private static readonly string FilesRoot = Path.Combine(Path.GetTempPath(), "lis-functional-files");
    private static readonly string[] ManagerPermissions = ["platform.view", "platform.manage", "tenancy.view", "tenancy.edit"];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] SvgBytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private readonly ApiFactory factory;

    public AnnouncementsFlowTests(ApiFactory factory)
    {
        this.factory = factory;
        this.factory.Overrides["Lis:Files:Root"] = FilesRoot;   // images of the tests never land in the development folder
    }

    private DateTimeOffset Now => factory.Clock.UtcNow;

    private AnnouncementRequest Request(string title, string placement = "SignIn", int startMinutesAgo = 5, DateTimeOffset? to = null, IReadOnlyList<Guid>? clients = null, string? status = null, string? link = "https://nationaltechnology.net/offer") =>
        new(title, title + " ع", "Body " + title, null, link, placement, Now.AddMinutes(-startMinutesAgo), to, null, null, status, clients);

    [DatabaseFact]
    public async Task The_Super_Admin_creates_edits_activates_and_deactivates_an_announcement_and_every_rule_answers_with_its_code()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);

        var id = await CreateAsync(admin, Request("Spring offer"));
        var draft = (await admin.GetFromJsonAsync<AnnouncementView>($"{Admin}/{id}"))!;
        Assert.Equal(("Spring offer", "SignIn", "Any", false, false, null, 0L), (draft.TitleEn, draft.Placement, draft.AudienceStatus, draft.IsActive, draft.IsLive, draft.ImageVersion, draft.Impressions));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Admin}/{id}", Request("Summer offer", "Welcome"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{id}/activate", null)).StatusCode);
        var live = (await admin.GetFromJsonAsync<AnnouncementView>($"{Admin}/{id}"))!;
        Assert.Equal(("Summer offer", "Welcome", true, true), (live.TitleEn, live.Placement, live.IsActive, live.IsLive));

        var page = (await admin.GetFromJsonAsync<Paged<AnnouncementView>>($"{Admin}?search=summer&placement=Welcome&isActive=true"))!;
        Assert.Contains(page.Items, item => item.Id == id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{id}/deactivate", null)).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<Paged<AnnouncementView>>($"{Admin}?isActive=true"))!.Items, item => item.Id == id);

        foreach (var key in AnnouncementSortKeys.Announcements.Names)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Admin}?sort={key}&direction=desc")).StatusCode);
        }

        await AssertRefusedAsync(admin.GetAsync($"{Admin}?sort=bogus"), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("") with { TitleEn = "" }), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("Banner", "Banner")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("Plain link", link: "http://example.com")), HttpStatusCode.UnprocessableEntity, "ANN-002");
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("Backwards", to: Now.AddDays(-1))), HttpStatusCode.UnprocessableEntity, "ANN-003");
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("Nowhere") with { AudienceCountryId = Guid.CreateVersion7() }), HttpStatusCode.UnprocessableEntity, "ANN-012");
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("No plan") with { AudiencePlanId = Guid.CreateVersion7() }), HttpStatusCode.UnprocessableEntity, "ANN-013");
        await AssertRefusedAsync(admin.PostAsJsonAsync(Admin, Request("Ghost", clients: [Guid.CreateVersion7()])), HttpStatusCode.UnprocessableEntity, "ANN-014");
        await AssertRefusedAsync(admin.GetAsync($"{Admin}/{Guid.CreateVersion7()}"), HttpStatusCode.NotFound, "ANN-404");
        await AssertRefusedAsync(admin.PostAsync($"{Admin}/{Guid.CreateVersion7()}/activate", null), HttpStatusCode.NotFound, "ANN-404");

        var ended = await CreateAsync(admin, Request("Ended", startMinutesAgo: 120, to: Now.AddMinutes(-60)));
        await AssertRefusedAsync(admin.PostAsync($"{Admin}/{ended}/activate", null), HttpStatusCode.Conflict, "ANN-010");
    }

    [DatabaseFact]
    public async Task A_viewer_reads_but_does_not_change_and_a_laboratory_is_fenced_out()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        var id = await CreateAsync(admin, Request("Read only"));

        var viewer = await ClientAsync(TenantIds.Platform, ["platform.view"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Admin}/{id}")).StatusCode);
        await AssertRefusedAsync(viewer.PostAsJsonAsync(Admin, Request("Nope")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await AssertRefusedAsync(viewer.PostAsync($"{Admin}/{id}/activate", null), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        var laboratory = await ClientAsync(Guid.CreateVersion7(), ["settings.manage"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await laboratory.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await laboratory.PostAsJsonAsync(Admin, Request("Nope"))).StatusCode);
    }

    [DatabaseFact]
    public async Task The_sign_in_list_honours_schedule_audience_and_opt_out_and_may_be_cached_for_five_minutes()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        await DeactivateEverythingAsync(admin);
        var labA = Guid.CreateVersion7();
        var labB = Guid.CreateVersion7();
        await ClientAsync(labA, ["patients.view"]);   // gives each laboratory its client record (slug t<id>)
        await ClientAsync(labB, ["patients.view"]);

        var open = await LiveAsync(admin, Request("Open to all"));
        var forA = await LiveAsync(admin, Request("Only for A", clients: [labA]));
        var forB = await LiveAsync(admin, Request("Only for B", clients: [labB]));
        var trialOnly = await LiveAsync(admin, Request("Trial only", status: "Trial"));
        var future = await CreateAsync(admin, Request("Tomorrow", startMinutesAgo: -24 * 60));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{future}/activate", null)).StatusCode);
        var welcome = await LiveAsync(admin, Request("Welcome card", "Welcome"));

        var anonymous = Anonymous();
        var response = await anonymous.GetAsync($"{Public}?placement=SignIn&laboratory={ApiFactory.SlugOf(labA)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl!.ToString());
        var ids = (await response.Content.ReadFromJsonAsync<List<PublicAnnouncement>>())!.Select(item => item.Id).ToList();
        Assert.Contains(open, ids);
        Assert.Contains(forA, ids);
        Assert.DoesNotContain(forB, ids);
        Assert.DoesNotContain(trialOnly, ids);   // the seeded laboratory is Active
        Assert.DoesNotContain(future, ids);
        Assert.DoesNotContain(welcome, ids);

        // no laboratory: open audiences only; an unknown address: nothing; the Welcome placement is not public
        Assert.Equal([open], (await anonymous.GetFromJsonAsync<List<PublicAnnouncement>>(Public))!.Select(item => item.Id));
        Assert.Empty((await anonymous.GetFromJsonAsync<List<PublicAnnouncement>>($"{Public}?laboratory=no-such-laboratory"))!);
        await AssertRefusedAsync(anonymous.GetAsync($"{Public}?placement=Welcome"), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);

        // SA-04 opt-out: the client sees none, the other still does; the laboratory itself cannot change the setting (SET-004)
        Assert.False((await admin.GetFromJsonAsync<ClientAnnouncementsSetting>($"/api/v1/platform/clients/{labA}/announcements"))!.Hidden);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/platform/clients/{labA}/announcements", new ClientAnnouncementsSetting(true))).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<ClientAnnouncementsSetting>($"/api/v1/platform/clients/{labA}/announcements"))!.Hidden);
        Assert.Empty((await anonymous.GetFromJsonAsync<List<PublicAnnouncement>>($"{Public}?laboratory={ApiFactory.SlugOf(labA)}"))!);
        Assert.Contains(forB, (await anonymous.GetFromJsonAsync<List<PublicAnnouncement>>($"{Public}?laboratory={ApiFactory.SlugOf(labB)}"))!.Select(item => item.Id));
        await AssertRefusedAsync(admin.GetAsync($"/api/v1/platform/clients/{Guid.CreateVersion7()}/announcements"), HttpStatusCode.NotFound, "CLI-404");

        var labAdmin = await ClientAsync(labA, ["settings.view", "settings.manage"]);
        await AssertRefusedAsync(labAdmin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("hideAnnouncements", "false")])), HttpStatusCode.Forbidden, "SET-004");
        await AssertRefusedAsync(labAdmin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("hideAnnouncements", null)])), HttpStatusCode.Forbidden, "SET-004");
        Assert.True((await admin.GetFromJsonAsync<ClientAnnouncementsSetting>($"/api/v1/platform/clients/{labA}/announcements"))!.Hidden);   // the refused writes changed nothing

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/platform/clients/{labA}/announcements", new ClientAnnouncementsSetting(false))).StatusCode);
        Assert.Contains(forA, (await anonymous.GetFromJsonAsync<List<PublicAnnouncement>>($"{Public}?laboratory={ApiFactory.SlugOf(labA)}"))!.Select(item => item.Id));
    }

    [DatabaseFact]
    public async Task The_dashboard_reads_the_Welcome_announcements_of_its_laboratory_and_honours_the_opt_out()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        await DeactivateEverythingAsync(admin);
        var lab = Guid.CreateVersion7();
        var member = await ClientAsync(lab, ["patients.view"]);

        var welcome = await LiveAsync(admin, Request("Welcome aboard", "Welcome", clients: [lab]));
        var signIn = await LiveAsync(admin, Request("Hero only"));
        var other = await LiveAsync(admin, Request("Someone else", "Welcome", clients: [await OtherClientAsync()]));

        var ids = (await member.GetFromJsonAsync<List<PublicAnnouncement>>(Mine))!.Select(item => item.Id).ToList();
        Assert.Equal([welcome], ids);
        Assert.DoesNotContain(signIn, ids);
        Assert.DoesNotContain(other, ids);

        Assert.Empty((await admin.GetFromJsonAsync<List<PublicAnnouncement>>(Mine))!);   // the platform tenant has no welcome card

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/platform/clients/{lab}/announcements", new ClientAnnouncementsSetting(true))).StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<List<PublicAnnouncement>>(Mine))!);
    }

    [DatabaseFact]
    public async Task The_image_is_sniffed_previewed_by_the_admin_and_public_only_while_live_and_the_counters_count_live_announcements_only()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        var id = await CreateAsync(admin, Request("With picture"));

        var uploaded = await UploadAsync(admin, id, "offer.png", PngBytes);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.Equal(new AnnouncementImageView(1, "image/png", PngBytes.Length), await uploaded.Content.ReadFromJsonAsync<AnnouncementImageView>());
        await AssertRefusedAsync(UploadAsync(admin, id, "offer.png", SvgBytes), HttpStatusCode.UnprocessableEntity, "ANN-008");
        await AssertRefusedAsync(UploadAsync(admin, id, "big.png", [.. PngBytes, .. new byte[512 * 1024]]), HttpStatusCode.UnprocessableEntity, "ANN-007");
        Assert.Equal(PngBytes, await (await admin.GetAsync($"{Admin}/{id}/image")).Content.ReadAsByteArrayAsync());

        var anonymous = Anonymous();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"{Public}/{id}/image")).StatusCode);   // a draft's image stays private

        // the counters ignore a draft
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync($"{Public}/{id}/impression", null)).StatusCode);
        Assert.Equal(0L, (await admin.GetFromJsonAsync<AnnouncementView>($"{Admin}/{id}"))!.Impressions);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{id}/activate", null)).StatusCode);
        var image = await anonymous.GetAsync($"{Public}/{id}/image?v=1");
        Assert.Equal((HttpStatusCode.OK, "image/png", "public, max-age=300"), (image.StatusCode, image.Content.Headers.ContentType!.MediaType, image.Headers.CacheControl!.ToString()));

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync($"{Public}/{id}/impression", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync($"{Public}/{id}/impression", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync($"{Public}/{id}/click", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync($"{Public}/{Guid.CreateVersion7()}/click", null)).StatusCode);   // unknown: nothing to count, same answer
        var counted = (await admin.GetFromJsonAsync<AnnouncementView>($"{Admin}/{id}"))!;
        Assert.Equal((2L, 1L, 1), (counted.Impressions, counted.Clicks, counted.ImageVersion));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Admin}/{id}/image")).StatusCode);
        await AssertRefusedAsync(admin.DeleteAsync($"{Admin}/{id}/image"), HttpStatusCode.NotFound, "ANN-011");
        Assert.Null((await admin.GetFromJsonAsync<AnnouncementView>($"{Admin}/{id}"))!.ImageVersion);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{id}/deactivate", null)).StatusCode);
    }

    /// <summary>Other tests of this class leave live announcements behind; the lists are asserted on a clean slate.</summary>
    private static async Task DeactivateEverythingAsync(HttpClient admin)
    {
        var active = (await admin.GetFromJsonAsync<Paged<AnnouncementView>>($"{Admin}?isActive=true&pageSize=200"))!;
        foreach (var item in active.Items)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{item.Id}/deactivate", null)).StatusCode);
        }
    }

    private async Task<Guid> OtherClientAsync()
    {
        var other = Guid.CreateVersion7();
        await factory.EnsureClientRecordAsync(other);
        return other;
    }

    private static async Task<Guid> LiveAsync(HttpClient admin, AnnouncementRequest request)
    {
        var id = await CreateAsync(admin, request);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/{id}/activate", null)).StatusCode);
        return id;
    }

    private static async Task<Guid> CreateAsync(HttpClient admin, AnnouncementRequest request)
    {
        var response = await admin.PostAsJsonAsync(Admin, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid id, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync($"{Admin}/{id}/image", form);
    }

    private static async Task AssertRefusedAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        Assert.Equal((status, code), (response.StatusCode, await AuthClient.CodeOf(response)));
    }

    private HttpClient Anonymous()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Announcements Tester");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
