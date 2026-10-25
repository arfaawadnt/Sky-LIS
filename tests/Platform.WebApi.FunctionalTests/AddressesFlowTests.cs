using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-008 C-04 over the real API (intakes PLT-001 §22, ORG-001 §17, ACC-001 §20): the city / district / area of a
/// laboratory through the subscription wizard and the SA-04 drawer, the address line on the clients register and the public
/// laboratory card, the branch locality of CP-36, and the SA-11 locality import (dry run, real run, a refused line, idempotent
/// re-run, permission). Every chain refusal arrives as its code (GEO-020…022, 422).
/// </summary>
public sealed class AddressesFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Platform = "/api/v1/platform";
    private const string Clients = "/api/v1/platform/clients";
    private const string Subscriptions = "/api/v1/public/subscriptions";
    private const string Card = "/api/v1/public/laboratories";
    private const string Branches = "/api/v1/organization/branches";

    private static readonly string[] AdminPermissions = ["platform.view", "platform.manage", "tenancy.view", "tenancy.create", "tenancy.edit", "tenancy.manage"];

    [DatabaseFact]
    public async Task The_wizard_keeps_the_picked_locality_and_the_register_and_card_print_the_address()
    {
        var admin = await SignedInAsync(TenantIds.Platform, AdminPermissions);
        var place = await PlaceAsync(admin);
        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        // a district of another city is refused before any code is sent
        var wrong = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, NewMobile(), place.CityId, place.OtherDistrictId, null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "GEO-021"), (wrong.StatusCode, await AuthClient.CodeOf(wrong)));

        var managerMobile = NewMobile();
        var start = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, managerMobile, place.CityId, place.DistrictId, place.AreaId));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = (await start.Content.ReadFromJsonAsync<SubscriptionStarted>())!;
        var code = factory.Notifications.LastTo("+2" + managerMobile)!.Placeholders["code"];
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(started.SubscriptionId, code))).StatusCode);
        var username = "addr.owner." + Guid.CreateVersion7().ToString("N")[^8..];
        var completed = await CompleteAsync(anonymous, started.SubscriptionId, username);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var result = (await completed.Content.ReadFromJsonAsync<SubscriptionCompletedResponse>())!;

        var detail = (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{result.TenantId}"))!;
        Assert.Equal((place.CityId, place.DistrictId, place.AreaId), (detail.CityId, detail.DistrictId, detail.AreaId));
        Assert.Equal(("Block 7, First Zone, Nasr City, Cairo", "بلوك 7، المنطقة الأولى، مدينة نصر، القاهرة"), (detail.AddressLineEn, detail.AddressLineAr));

        var slug = result.PortalUrl[(result.PortalUrl.LastIndexOf('/') + 1)..];
        var item = Assert.Single((await admin.GetFromJsonAsync<Paged<ClientListItem>>($"{Clients}?search={slug}"))!.Items);
        Assert.Equal("Block 7, First Zone, Nasr City, Cairo", item.AddressLineEn);

        var card = (await anonymous.GetFromJsonAsync<PublicLaboratory>($"{Card}/{slug}"))!;
        Assert.Equal(("Block 7, First Zone, Nasr City, Cairo", "بلوك 7، المنطقة الأولى، مدينة نصر، القاهرة"), (card.AddressEn, card.AddressAr));
    }

    [DatabaseFact]
    public async Task The_SA_04_drawer_sets_changes_and_clears_the_locality_of_a_client()
    {
        var admin = await SignedInAsync(TenantIds.Platform, AdminPermissions);
        var place = await PlaceAsync(admin);
        var slug = "addr-" + Guid.CreateVersion7().ToString("N")[^8..];
        var provision = await ProvisionAsync(admin, place, slug, place.CityId, null, null);
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);
        var clientId = (await provision.Content.ReadFromJsonAsync<ProvisionedClient>())!.Id;
        var detail = (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{clientId}"))!;
        Assert.Equal((place.CityId, (Guid?)null, "Nasr City, Cairo"), (detail.CityId, detail.DistrictId, detail.AddressLineEn));

        // an area of another district is GEO-022; a city of another governorate GEO-020; nothing changes
        var area = await admin.PutAsJsonAsync($"{Clients}/{clientId}", Update(detail, place.CityId, place.DistrictId, Guid.CreateVersion7()));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "GEO-022"), (area.StatusCode, await AuthClient.CodeOf(area)));
        var city = await admin.PutAsJsonAsync($"{Clients}/{clientId}", Update(detail, place.OtherGovernorateCityId, null, null));
        Assert.Equal("GEO-020", await AuthClient.CodeOf(city));
        Assert.Equal(place.CityId, (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{clientId}"))!.CityId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Clients}/{clientId}", Update(detail, place.CityId, place.DistrictId, place.AreaId))).StatusCode);
        Assert.Equal(place.AreaId, (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{clientId}"))!.AreaId);

        // a body without the fields (the 0.21 shape) clears them
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Clients}/{clientId}", Update(detail, null, null, null))).StatusCode);
        var cleared = (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{clientId}"))!;
        Assert.Equal(((Guid?)null, (string?)null), (cleared.CityId, cleared.AddressLineEn));
    }

    [DatabaseFact]
    public async Task A_branch_takes_a_locality_of_its_governorate_only()
    {
        var platform = await SignedInAsync(TenantIds.Platform, AdminPermissions);
        var place = await PlaceAsync(platform);
        var laboratory = await SignedInAsync(Guid.CreateVersion7(), ["organization.view", "organization.create", "organization.edit"]);

        var created = await laboratory.PostAsJsonAsync(Branches, new CreateBranchRequest("EAST", "East", "الشرق", place.CountryId, place.GovernorateId, null, null, "EGP", place.CityId, place.DistrictId, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var east = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var stored = (await laboratory.GetFromJsonAsync<BranchListItem>($"{Branches}/{east}"))!;
        Assert.Equal((place.CityId, place.DistrictId, (Guid?)null), (stored.CityId, stored.DistrictId, stored.AreaId));

        var noGovernorate = await laboratory.PostAsJsonAsync(Branches, new CreateBranchRequest("WEST", "West", "الغرب", null, null, null, null, "EGP", place.CityId, null, null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "GEO-020"), (noGovernorate.StatusCode, await AuthClient.CodeOf(noGovernorate)));
        var orphanArea = await laboratory.PutAsJsonAsync($"{Branches}/{east}", new UpdateBranchRequest("East", "الشرق", place.CountryId, place.GovernorateId, null, null, "EGP", place.CityId, null, place.AreaId));
        Assert.Equal("GEO-022", await AuthClient.CodeOf(orphanArea));

        Assert.Equal(HttpStatusCode.NoContent, (await laboratory.PutAsJsonAsync($"{Branches}/{east}", new UpdateBranchRequest("East", "الشرق", place.CountryId, place.GovernorateId, null, null, "EGP", place.CityId, place.DistrictId, place.AreaId))).StatusCode);
        Assert.Equal(place.AreaId, (await laboratory.GetFromJsonAsync<BranchListItem>($"{Branches}/{east}"))!.AreaId);
        Assert.Equal(HttpStatusCode.NoContent, (await laboratory.PutAsJsonAsync($"{Branches}/{east}", new UpdateBranchRequest("East", "الشرق", place.CountryId, place.GovernorateId, null, null, "EGP"))).StatusCode);
        Assert.Null((await laboratory.GetFromJsonAsync<BranchListItem>($"{Branches}/{east}"))!.CityId);
    }

    [DatabaseFact]
    public async Task The_locality_import_checks_first_writes_all_or_nothing_and_runs_again_without_duplicates()
    {
        var admin = await SignedInAsync(TenantIds.Platform, AdminPermissions);
        var place = await PlaceAsync(admin);
        var import = $"{Platform}/governorates/{place.GovernorateId}/localities/import";
        const string File = "city_en,city_ar,district_en,district_ar,area_en,area_ar\nNasr City,مدينة نصر,First Zone,المنطقة الأولى,Block 9,بلوك 9\nShubra,شبرا,Rod El Farag,روض الفرج,,\nShubra,شبرا,Rod El Farag,روض الفرج,Kafr,كفر\n";

        var dry = await PostCsvAsync(admin, import + "?dryRun=true", File);
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        var preview = (await dry.Content.ReadFromJsonAsync<LocalityImportResult>())!;
        Assert.Equal((3, true, false), (preview.Rows, preview.DryRun, preview.Applied));
        Assert.Equal((new LocalityImportLevel(1, 1, 0), new LocalityImportLevel(1, 1, 0), new LocalityImportLevel(2, 0, 0)), (preview.Cities, preview.Districts, preview.Areas));
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{place.GovernorateId}/cities"))!, row => row.NameEn == "Shubra");

        // one refused line (an area without its district): nothing is written
        var refused = (await (await PostCsvAsync(admin, import, File + "Maadi,المعادي,,,Orphan,يتيم\n")).Content.ReadFromJsonAsync<LocalityImportResult>())!;
        var error = Assert.Single(refused.Errors);
        Assert.Equal((5, "Area", "GEO-025", false), (error.Line, error.Level, error.Code, refused.Applied));
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{place.GovernorateId}/cities"))!, row => row.NameEn is "Shubra" or "Maadi");

        var applied = (await (await PostCsvAsync(admin, import, File)).Content.ReadFromJsonAsync<LocalityImportResult>())!;
        Assert.True(applied.Applied);
        var shubra = Assert.Single((await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{place.GovernorateId}/cities"))!, row => row.NameEn == "Shubra");
        Assert.Equal("شبرا", shubra.NameAr);

        var again = (await (await PostCsvAsync(admin, import, File)).Content.ReadFromJsonAsync<LocalityImportResult>())!;
        Assert.Equal((true, 0, 0, 0), (again.Applied, again.Cities.Created, again.Districts.Created, again.Areas.Created));
        Assert.Equal((2, 2, 2), (again.Cities.Existing, again.Districts.Existing, again.Areas.Existing));

        var unreadable = await PostCsvAsync(admin, import, "name\nNasr\n");
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "GEO-023"), (unreadable.StatusCode, await AuthClient.CodeOf(unreadable)));
        var missing = await PostCsvAsync(admin, $"{Platform}/governorates/{Guid.CreateVersion7()}/localities/import", File);
        Assert.Equal((HttpStatusCode.NotFound, "GEO-405"), (missing.StatusCode, await AuthClient.CodeOf(missing)));

        var viewer = await SignedInAsync(TenantIds.Platform, ["tenancy.view"]);
        var forbidden = await PostCsvAsync(viewer, import, File);
        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.Forbidden), (forbidden.StatusCode, await AuthClient.CodeOf(forbidden)));
    }

    private static StartSubscriptionRequest Start(Place place, string managerMobile, Guid? cityId, Guid? districtId, Guid? areaId) =>
        new("Address Lab", "معمل العنوان", place.CountryId, place.GovernorateId, "5 Nile St.", "01012345678", null, "Dr. Ahmed", managerMobile, null, "en", cityId, districtId, areaId);

    private static UpdateClientRequest Update(ClientDetail detail, Guid? cityId, Guid? districtId, Guid? areaId) =>
        new(detail.NameEn, detail.NameAr, detail.CountryId, detail.GovernorateId, detail.Address, detail.Mobile, detail.LicenceNo, detail.ManagerName, detail.ManagerMobile, detail.Email, detail.PlanId, detail.Language, cityId, districtId, areaId);

    private static string NewMobile() => "010" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> PostCsvAsync(HttpClient client, string url, string csv)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "localities.csv");
        return await client.PostAsync(url, form);
    }

    private async Task<HttpResponseMessage> CompleteAsync(HttpClient anonymous, Guid subscriptionId, string username)
    {
        var path = $"{Subscriptions}/complete";
        var challenge = await new AuthClient(anonymous).ChallengeAsync();
        return await anonymous.PostAsJsonAsync(path, new { subscriptionId, username, password = TestSealer.Seal(challenge, "password", "a long enough password", "POST " + path) });
    }

    private async Task<HttpResponseMessage> ProvisionAsync(HttpClient admin, Place place, string slug, Guid? cityId, Guid? districtId, Guid? areaId)
    {
        var challenge = await new AuthClient(admin).ChallengeAsync();
        return await admin.PostAsJsonAsync(Clients, new
        {
            nameEn = "Address " + slug,
            nameAr = "معمل العنوان",
            countryId = place.CountryId,
            governorateId = place.GovernorateId,
            cityId,
            districtId,
            areaId,
            address = "12 Tahrir St.",
            mobile = "01012345678",
            managerName = "Dr. Mona",
            managerMobile = NewMobile(),
            planId = place.PlanId,
            portalSlug = slug,
            language = "en",
            adminUsername = "addr.admin." + slug[^8..],
            adminPassword = TestSealer.Seal(challenge, "adminPassword", "a long enough password", "POST " + Clients),
        });
    }

    /// <summary>A country, two governorates, a plan and the chain Nasr City → First Zone → Block 7 (plus a second district and a city elsewhere).</summary>
    private static async Task<Place> PlaceAsync(HttpClient admin)
    {
        var iso = TestCountryCodes.Next();
        var countryId = await CreateAsync(admin, $"{Platform}/countries", new CountryRequest("Testland " + iso, "أرض الاختبار", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorateId = await CreateAsync(admin, $"{Platform}/governorates", new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null));
        var otherGovernorateId = await CreateAsync(admin, $"{Platform}/governorates", new GovernorateRequest(countryId, "Giza", "الجيزة", null, null));
        var planCode = "A" + Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        var planId = await CreateAsync(admin, $"{Platform}/plans", new PlanRequest(planCode, "Test plan", "باقة", 1m, "EGP", 1, 5, 100, 1));
        var cityId = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Nasr City", "مدينة نصر"));
        var districtId = await CreateAsync(admin, $"{Platform}/cities/{cityId}/districts", new LocationRequest("First Zone", "المنطقة الأولى"));
        var areaId = await CreateAsync(admin, $"{Platform}/districts/{districtId}/areas", new LocationRequest("Block 7", "بلوك 7"));
        var otherCityId = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Heliopolis", "مصر الجديدة"));
        var otherDistrictId = await CreateAsync(admin, $"{Platform}/cities/{otherCityId}/districts", new LocationRequest("Korba", "الكوربة"));
        var elsewhereCityId = await CreateAsync(admin, $"{Platform}/governorates/{otherGovernorateId}/cities", new LocationRequest("Dokki", "الدقي"));
        return new Place(countryId, governorateId, planId, cityId, districtId, areaId, otherDistrictId, elsewhereCityId);
    }

    private static async Task<Guid> CreateAsync<T>(HttpClient client, string url, T body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private async Task<HttpClient> SignedInAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Addresses Tester");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private sealed record Place(Guid CountryId, Guid GovernorateId, Guid PlanId, Guid CityId, Guid DistrictId, Guid AreaId, Guid OtherDistrictId, Guid OtherGovernorateCityId);
}
