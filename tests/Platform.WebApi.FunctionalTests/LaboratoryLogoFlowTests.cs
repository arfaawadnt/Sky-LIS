using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Account;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The laboratory logo over the real API (top bar, user decision 2026-10-12): uploaded, replaced and removed by a holder
/// of <c>settings.manage</c>, read by every member of the laboratory, never by another laboratory; the file's signature
/// decides the type (PNG, JPEG, WebP), 512 KB at most; the profile carries the version the top bar reloads on.
/// </summary>
public sealed class LaboratoryLogoFlowTests : IClassFixture<ApiFactory>
{
    private const string Logo = "/api/v1/account/laboratory/logo";
    private const string Profile = "/api/v1/account/profile";
    private static readonly string FilesRoot = Path.Combine(Path.GetTempPath(), "lis-functional-files");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8.ToArray(), 26, 0, 0, 0, .. "WEBPVP8 "u8.ToArray()];
    private static readonly byte[] SvgBytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private readonly ApiFactory _factory;

    public LaboratoryLogoFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Overrides["Lis:Files:Root"] = FilesRoot;   // images of the tests never land in the development folder
    }

    [DatabaseFact]
    public async Task The_administrator_uploads_replaces_and_removes_the_logo_and_every_member_sees_it()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await ClientAsync(tenant, ["settings.view", "settings.manage"]);
        var member = await ClientAsync(tenant, ["patients.view"]);

        // no logo yet: the profile says so and the image is not found
        Assert.Null((await member.GetFromJsonAsync<MyAccountProfile>(Profile))!.Laboratory!.LogoVersion);
        var none = await member.GetAsync(Logo);
        Assert.Equal((HttpStatusCode.NotFound, "ACC-045"), (none.StatusCode, await AuthClient.CodeOf(none)));

        // a member without settings.manage may not change it
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(member, "logo.png", PngBytes)).StatusCode);

        var first = await UploadAsync(admin, "logo.png", PngBytes);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(new LaboratoryLogoView(1, "image/png", PngBytes.Length), await first.Content.ReadFromJsonAsync<LaboratoryLogoView>());
        Assert.Equal(1, (await member.GetFromJsonAsync<MyAccountProfile>(Profile))!.Laboratory!.LogoVersion);
        var image = await member.GetAsync(Logo);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PngBytes, await image.Content.ReadAsByteArrayAsync());
        Assert.Null(image.Content.Headers.ContentDisposition);   // shown inline, not downloaded

        // the signature decides, never the name: a JPEG named .png is a JPEG; WebP is accepted; the version moves on
        var replaced = await UploadAsync(admin, "logo.png", JpegBytes);
        Assert.Equal(new LaboratoryLogoView(2, "image/jpeg", JpegBytes.Length), await replaced.Content.ReadFromJsonAsync<LaboratoryLogoView>());
        Assert.Equal("image/jpeg", (await member.GetAsync(Logo)).Content.Headers.ContentType!.MediaType);
        Assert.Equal(3, (await (await UploadAsync(admin, "logo.webp", WebpBytes)).Content.ReadFromJsonAsync<LaboratoryLogoView>())!.Version);

        // removed: the initials come back; removing twice is refused
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync(Logo)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Logo)).StatusCode);
        Assert.Null((await member.GetFromJsonAsync<MyAccountProfile>(Profile))!.Laboratory!.LogoVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Logo)).StatusCode);
        var twice = await admin.DeleteAsync(Logo);
        Assert.Equal((HttpStatusCode.NotFound, "ACC-045"), (twice.StatusCode, await AuthClient.CodeOf(twice)));

        // and a new upload after a removal continues the version
        Assert.Equal(5, (await (await UploadAsync(admin, "again.png", PngBytes)).Content.ReadFromJsonAsync<LaboratoryLogoView>())!.Version);
    }

    [DatabaseFact]
    public async Task Only_a_small_PNG_JPEG_or_WebP_is_accepted()
    {
        var admin = await ClientAsync(Guid.CreateVersion7(), ["settings.manage"]);

        var svg = await UploadAsync(admin, "logo.svg", SvgBytes);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ACC-042"), (svg.StatusCode, await AuthClient.CodeOf(svg)));
        var empty = await UploadAsync(admin, "logo.png", []);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ACC-040"), (empty.StatusCode, await AuthClient.CodeOf(empty)));
        var tooLarge = await UploadAsync(admin, "logo.png", [.. PngBytes, .. new byte[512 * 1024]]);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ACC-041"), (tooLarge.StatusCode, await AuthClient.CodeOf(tooLarge)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Logo)).StatusCode);   // nothing was kept
    }

    [DatabaseFact]
    public async Task A_laboratory_never_sees_another_laboratorys_logo_and_the_platform_has_none()
    {
        var owner = await ClientAsync(Guid.CreateVersion7(), ["settings.manage"]);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner, "logo.png", PngBytes)).StatusCode);

        var other = await ClientAsync(Guid.CreateVersion7(), ["settings.manage"]);
        var foreign = await other.GetAsync(Logo);
        Assert.Equal((HttpStatusCode.NotFound, "ACC-045"), (foreign.StatusCode, await AuthClient.CodeOf(foreign)));
        Assert.Null((await other.GetFromJsonAsync<MyAccountProfile>(Profile))!.Laboratory!.LogoVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(Logo)).StatusCode);   // nothing of the first laboratory to remove

        var platform = await ClientAsync(TenantIds.Platform, ["settings.manage"]);
        Assert.Equal("ACC-403", await AuthClient.CodeOf(await UploadAsync(platform, "logo.png", PngBytes)));
        Assert.Equal("ACC-403", await AuthClient.CodeOf(await platform.GetAsync(Logo)));
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync(Logo, form);
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await _factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Logo Tester");
        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
