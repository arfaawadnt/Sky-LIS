using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Report-template images over the real API (validation F-02, intake LIS-INT-CTB-001 A10): the logo and the signature are
/// uploaded, replaced and removed by a holder of <c>catalogue.edit</c>, stored by the Files module as the template's file
/// ids, read by every signed-in member of the tenant (the printed result report carries them) and by nobody else; the
/// file's signature decides the type (PNG, JPEG, WebP), 512 KB at most; a client provisioned from the platform
/// catalogue gets its own copy of the images.
/// </summary>
public sealed class ReportTemplateImageFlowTests : IClassFixture<ApiFactory>
{
    private const string Templates = "/api/v1/catalogue/report-templates";
    private const string Clients = "/api/v1/platform/clients";
    private static readonly string FilesRoot = Path.Combine(Path.GetTempPath(), "lis-functional-files");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8.ToArray(), 26, 0, 0, 0, .. "WEBPVP8 "u8.ToArray()];
    private static readonly byte[] SvgBytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();
    private static readonly string[] Editor = ["catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.manage"];

    private static int _isoCounter;
    private readonly ApiFactory _factory;

    public ReportTemplateImageFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Overrides["Lis:Files:Root"] = FilesRoot;   // images of the tests never land in the development folder
    }

    [DatabaseFact]
    public async Task The_editor_uploads_replaces_and_removes_the_logo_and_signature_and_every_member_reads_them()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);
        var viewer = await ClientAsync(tenant, ["catalogue.view"]);
        var member = await ClientAsync(tenant, ["results.view"]);   // a results user prints reports, holds no catalogue key
        var id = await CreateTemplateAsync(editor, "Results A4");

        // nothing attached yet
        var none = await member.GetAsync($"{Templates}/{id}/images/logo");
        Assert.Equal((HttpStatusCode.NotFound, "CAT-394"), (none.StatusCode, await AuthClient.CodeOf(none)));

        // catalogue.view alone may not change them
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, id, "logo", "logo.png", PngBytes)).StatusCode);

        var logo = await UploadAsync(editor, id, "logo", "logo.png", PngBytes);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        var logoView = (await logo.Content.ReadFromJsonAsync<ReportTemplateImageView>())!;
        Assert.Equal(("Logo", "image/png", (long)PngBytes.Length), (logoView.Image, logoView.ContentType, logoView.SizeBytes));
        var signature = (await (await UploadAsync(editor, id, "Signature", "sign.webp", WebpBytes)).Content.ReadFromJsonAsync<ReportTemplateImageView>())!;
        Assert.Equal("image/webp", signature.ContentType);

        // the register carries the file ids; every member reads the images inline
        var item = (await viewer.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{id}"))!;
        Assert.Equal((logoView.FileId, signature.FileId), (item.LogoFileId, item.SignatureFileId));
        var image = await member.GetAsync($"{Templates}/{id}/images/logo");
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PngBytes, await image.Content.ReadAsByteArrayAsync());
        Assert.Null(image.Content.Headers.ContentDisposition);   // shown inline, not downloaded
        Assert.Equal(WebpBytes, await (await member.GetAsync($"{Templates}/{id}/images/signature")).Content.ReadAsByteArrayAsync());

        // replaced: a JPEG named .png is a JPEG, with a new file id; editing the texts keeps the images
        var replaced = (await (await UploadAsync(editor, id, "logo", "logo.png", JpegBytes)).Content.ReadFromJsonAsync<ReportTemplateImageView>())!;
        Assert.Equal("image/jpeg", replaced.ContentType);
        Assert.NotEqual(logoView.FileId, replaced.FileId);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PutAsJsonAsync($"{Templates}/{id}", new ReportTemplateRequest("Results A4", "النتائج", "Results", false, "En", null, null, null, null))).StatusCode);
        item = (await viewer.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{id}"))!;
        Assert.Equal((replaced.FileId, signature.FileId), (item.LogoFileId, item.SignatureFileId));
        Assert.Equal("image/jpeg", (await member.GetAsync($"{Templates}/{id}/images/logo")).Content.Headers.ContentType!.MediaType);

        // removed: only by the editor, once
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Templates}/{id}/images/signature")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"{Templates}/{id}/images/signature")).StatusCode);
        Assert.Null((await viewer.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{id}"))!.SignatureFileId);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{Templates}/{id}/images/signature")).StatusCode);
        var twice = await editor.DeleteAsync($"{Templates}/{id}/images/signature");
        Assert.Equal((HttpStatusCode.NotFound, "CAT-394"), (twice.StatusCode, await AuthClient.CodeOf(twice)));

        // an unknown image name or template
        var unknownImage = await editor.DeleteAsync($"{Templates}/{id}/images/stamp");
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (unknownImage.StatusCode, await AuthClient.CodeOf(unknownImage)));
        var unknownTemplate = await UploadAsync(editor, Guid.CreateVersion7(), "logo", "logo.png", PngBytes);
        Assert.Equal((HttpStatusCode.NotFound, "CAT-388"), (unknownTemplate.StatusCode, await AuthClient.CodeOf(unknownTemplate)));
    }

    [DatabaseFact]
    public async Task Only_a_small_PNG_JPEG_or_WebP_is_accepted()
    {
        var editor = await ClientAsync(Guid.CreateVersion7(), Editor);
        var id = await CreateTemplateAsync(editor, "Culture A4");

        var svg = await UploadAsync(editor, id, "logo", "logo.svg", SvgBytes);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "CAT-272"), (svg.StatusCode, await AuthClient.CodeOf(svg)));
        var empty = await UploadAsync(editor, id, "logo", "logo.png", []);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "CAT-270"), (empty.StatusCode, await AuthClient.CodeOf(empty)));
        var tooLarge = await UploadAsync(editor, id, "signature", "sign.png", [.. PngBytes, .. new byte[512 * 1024]]);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "CAT-271"), (tooLarge.StatusCode, await AuthClient.CodeOf(tooLarge)));

        var item = (await editor.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{id}"))!;
        Assert.Equal((null, null), (item.LogoFileId, item.SignatureFileId));   // nothing was kept
    }

    [DatabaseFact]
    public async Task The_images_are_served_only_to_signed_in_users_of_the_same_tenant()
    {
        var editor = await ClientAsync(Guid.CreateVersion7(), Editor);
        var id = await CreateTemplateAsync(editor, "Receipt");
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(editor, id, "logo", "logo.png", PngBytes)).StatusCode);

        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Templates}/{id}/images/logo")).StatusCode);

        var other = await ClientAsync(Guid.CreateVersion7(), Editor);
        var foreign = await other.GetAsync($"{Templates}/{id}/images/logo");
        Assert.Equal((HttpStatusCode.NotFound, "CAT-388"), (foreign.StatusCode, await AuthClient.CodeOf(foreign)));
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Templates}/{id}/images/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(other, id, "logo", "logo.png", JpegBytes)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_provisioned_client_gets_its_own_copy_of_the_platform_templates_images()
    {
        var platform = await ClientAsync(TenantIds.Platform, [.. Editor, "platform.view", "platform.manage", "tenancy.view", "tenancy.create", "tenancy.edit", "tenancy.manage"]);
        var name = "Images " + Guid.CreateVersion7().ToString("N")[^10..];
        var sourceId = await CreateTemplateAsync(platform, name);
        var sourceLogo = (await (await UploadAsync(platform, sourceId, "logo", "logo.png", PngBytes)).Content.ReadFromJsonAsync<ReportTemplateImageView>())!;
        var sourceSignature = (await (await UploadAsync(platform, sourceId, "signature", "sign.jpg", JpegBytes)).Content.ReadFromJsonAsync<ReportTemplateImageView>())!;

        var place = await CreatePlaceAsync(platform);
        var slug = "img-" + Guid.CreateVersion7().ToString("N")[^8..];
        var created = await ProvisionAsync(platform, place, slug, "img.admin." + slug[^8..]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var clientId = (await created.Content.ReadFromJsonAsync<ProvisionedClient>())!.Id;

        var laboratory = await ClientAsync(clientId, ["catalogue.view"]);
        var copied = Assert.Single((await laboratory.GetFromJsonAsync<Paged<ReportTemplateListItem>>($"{Templates}?search={Uri.EscapeDataString(name)}"))!.Items);
        Assert.NotEqual(sourceId, copied.Id);
        Assert.NotNull(copied.LogoFileId);
        Assert.NotNull(copied.SignatureFileId);
        Assert.NotEqual(sourceLogo.FileId, copied.LogoFileId);   // copied, not shared: the laboratory owns its own files
        Assert.NotEqual(sourceSignature.FileId, copied.SignatureFileId);
        Assert.Equal(PngBytes, await (await laboratory.GetAsync($"{Templates}/{copied.Id}/images/logo")).Content.ReadAsByteArrayAsync());
        Assert.Equal(JpegBytes, await (await laboratory.GetAsync($"{Templates}/{copied.Id}/images/signature")).Content.ReadAsByteArrayAsync());

        // a later change on the platform leaves the laboratory's copy alone; the laboratory never reads the platform's template
        Assert.Equal(HttpStatusCode.NoContent, (await platform.DeleteAsync($"{Templates}/{sourceId}/images/logo")).StatusCode);
        Assert.Equal(PngBytes, await (await laboratory.GetAsync($"{Templates}/{copied.Id}/images/logo")).Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await laboratory.GetAsync($"{Templates}/{sourceId}/images/signature")).StatusCode);
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient editor, string name)
    {
        var created = await editor.PostAsJsonAsync(Templates, new ReportTemplateRequest(name, "قالب", "Results", true, "Both", null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid templateId, string image, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync($"{Templates}/{templateId}/images/{image}", form);
    }

    private async Task<HttpResponseMessage> ProvisionAsync(HttpClient admin, Place place, string slug, string adminUsername)
    {
        var challenge = await new AuthClient(admin).ChallengeAsync();
        return await admin.PostAsJsonAsync(Clients, new
        {
            nameEn = "Images Lab " + slug,
            nameAr = "معمل الصور",
            countryId = place.CountryId,
            governorateId = place.GovernorateId,
            address = "12 Tahrir St.",
            mobile = "01012345678",
            licenceNo = "LIC-1",
            managerName = "Dr. Mona",
            managerMobile = "011" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            email = "info@images.example",
            planId = place.PlanId,
            portalSlug = slug,
            language = "en",
            adminUsername,
            adminPassword = TestSealer.Seal(challenge, "adminPassword", "a long enough password", "POST " + Clients),
        });
    }

    private static async Task<Place> CreatePlaceAsync(HttpClient admin)
    {
        var n = Interlocked.Increment(ref _isoCounter);
        var iso = $"U{(char)('A' + (n % 26))}";   // U? is used by no other test
        var country = await admin.PostAsJsonAsync("/api/v1/platform/countries", new CountryRequest("Imageland " + iso, "أرض الصور", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        Assert.Equal(HttpStatusCode.Created, country.StatusCode);
        var countryId = (await country.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var governorateId = (await (await admin.PostAsJsonAsync("/api/v1/platform/governorates", new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var planCode = "I" + Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        var planId = (await (await admin.PostAsJsonAsync("/api/v1/platform/plans", new PlanRequest(planCode, "Image plan", "باقة", 10m, "EGP", 1, 5, 100, 1))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        return new Place(countryId, governorateId, planId);
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await _factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Template Image Tester");
        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private sealed record Place(Guid CountryId, Guid GovernorateId, Guid PlanId);
}
