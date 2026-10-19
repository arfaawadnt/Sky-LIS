using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The location hierarchy over the real API (change request 2026-10-19 A2, intake LIS-INT-PLT-001 §17): the Locations
/// drawer's calls from the platform tenant, the refusal codes of every rule, the permission fence (tenancy.view may list,
/// tenancy.manage may change, a client tenant never reaches it: PLT-403) and the anonymous pick lists of the patient form.
/// </summary>
public sealed class LocationsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Platform = "/api/v1/platform";
    private const string Public = "/api/v1/public";

    private static readonly string[] ManagerPermissions = ["tenancy.view", "tenancy.manage"];
    private static readonly string[] ViewerPermissions = ["tenancy.view"];

    [DatabaseFact]
    public async Task A_platform_administrator_builds_the_hierarchy_under_a_governorate_and_every_rule_answers_with_its_code()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        var governorateId = await CreateGovernorateAsync(admin);

        // cities → districts → areas
        var nasr = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest(" Nasr City ", "مدينة نصر"));
        var maadi = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Maadi", "المعادي"));
        var zone1 = await CreateAsync(admin, $"{Platform}/cities/{nasr}/districts", new LocationRequest("First Zone", "المنطقة الأولى"));
        var block7 = await CreateAsync(admin, $"{Platform}/districts/{zone1}/areas", new LocationRequest("Block 7", "بلوك 7"));

        var cities = (await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{governorateId}/cities"))!;
        Assert.Equal(["Maadi", "Nasr City"], cities.Select(city => city.NameEn));   // by English name, trimmed
        Assert.All(cities, city => Assert.Equal((governorateId, true), (city.ParentId, city.IsActive)));
        Assert.Equal([zone1], (await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/cities/{nasr}/districts?search=%D8%A7%D9%84%D8%A3%D9%88%D9%84%D9%89"))!.Select(district => district.Id));
        Assert.Equal([block7], (await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/districts/{zone1}/areas"))!.Select(area => area.Id));

        // duplicates within the parent (case-insensitive), on create and on rename
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/governorates/{governorateId}/cities", new LocationRequest("nasr city", "أخرى")), HttpStatusCode.Conflict, "GEO-102");
        await AssertRefusedAsync(admin.PutAsJsonAsync($"{Platform}/cities/{maadi}", new LocationRequest("NASR CITY", "المعادي")), HttpStatusCode.Conflict, "GEO-102");
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/cities/{nasr}/districts", new LocationRequest("first zone", "أخرى")), HttpStatusCode.Conflict, "GEO-103");
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/districts/{zone1}/areas", new LocationRequest("BLOCK 7", "أخرى")), HttpStatusCode.Conflict, "GEO-104");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Platform}/governorates/{(await CreateGovernorateAsync(admin))}/cities", new LocationRequest("Nasr City", "مدينة نصر"))).StatusCode);   // another governorate may have the same name

        // rename
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Platform}/cities/{maadi}", new LocationRequest("New Maadi", "المعادي الجديدة"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Platform}/districts/{zone1}", new LocationRequest("Zone 1", "المنطقة 1"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Platform}/areas/{block7}", new LocationRequest("Block Seven", "بلوك سبعة"))).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{governorateId}/cities"))!, city => city.Id == maadi && city.NameEn == "New Maadi");

        // deactivation: children first (GEO-112); reactivation only under an active parent (GEO-015)
        await AssertRefusedAsync(admin.PostAsync($"{Platform}/cities/{nasr}/deactivate", null), HttpStatusCode.Conflict, "GEO-112");
        await AssertRefusedAsync(admin.PostAsync($"{Platform}/districts/{zone1}/deactivate", null), HttpStatusCode.Conflict, "GEO-112");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/areas/{block7}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/districts/{zone1}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/cities/{nasr}/deactivate", null)).StatusCode);
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/cities/{nasr}/districts", new LocationRequest("Second Zone", "المنطقة الثانية")), HttpStatusCode.Conflict, "GEO-015");
        await AssertRefusedAsync(admin.PostAsync($"{Platform}/areas/{block7}/reactivate", null), HttpStatusCode.Conflict, "GEO-015");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/cities/{nasr}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/districts/{zone1}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/areas/{block7}/reactivate", null)).StatusCode);
        var inactiveOnly = await admin.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{governorateId}/cities?isActive=false");
        Assert.Empty(inactiveOnly!);

        // not found and shape
        await AssertRefusedAsync(admin.GetAsync($"{Platform}/governorates/{Guid.CreateVersion7()}/cities"), HttpStatusCode.NotFound, "GEO-405");
        await AssertRefusedAsync(admin.PutAsJsonAsync($"{Platform}/cities/{Guid.CreateVersion7()}", new LocationRequest("X", "س")), HttpStatusCode.NotFound, "GEO-406");
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/cities/{Guid.CreateVersion7()}/districts", new LocationRequest("X", "س")), HttpStatusCode.NotFound, "GEO-406");
        await AssertRefusedAsync(admin.PostAsync($"{Platform}/districts/{Guid.CreateVersion7()}/deactivate", null), HttpStatusCode.NotFound, "GEO-407");
        await AssertRefusedAsync(admin.PutAsJsonAsync($"{Platform}/areas/{Guid.CreateVersion7()}", new LocationRequest("X", "س")), HttpStatusCode.NotFound, "GEO-408");
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/governorates/{governorateId}/cities", new LocationRequest("", "بلا اسم")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await AssertRefusedAsync(admin.PostAsJsonAsync($"{Platform}/governorates/{governorateId}/cities", new LocationRequest(new string('x', 101), "طويل")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
    }

    [DatabaseFact]
    public async Task A_viewer_lists_but_does_not_change_and_a_client_tenant_is_fenced_out()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        var governorateId = await CreateGovernorateAsync(admin);
        var nasr = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Nasr City", "مدينة نصر"));

        var viewer = await ClientAsync(TenantIds.Platform, ViewerPermissions);
        Assert.Single((await viewer.GetFromJsonAsync<List<LocationListItem>>($"{Platform}/governorates/{governorateId}/cities"))!);
        await AssertRefusedAsync(viewer.PostAsJsonAsync($"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Maadi", "المعادي")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await AssertRefusedAsync(viewer.PutAsJsonAsync($"{Platform}/cities/{nasr}", new LocationRequest("Maadi", "المعادي")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await AssertRefusedAsync(viewer.PostAsync($"{Platform}/cities/{nasr}/deactivate", null), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        // INV-PLT-070: a laboratory role holding tenancy keys never reaches platform administration. The authorization behaviour decides
        // first (D-04): tenancy.* is platform-only, so the laboratory principal never holds it and the refusal is AUTHZ-001, not the
        // handler's PLT-403 (as PlatformAdminFlowTests.Laboratory_users_never_reach_platform_administration).
        var laboratory = await ClientAsync(Guid.CreateVersion7(), ManagerPermissions);
        await AssertRefusedAsync(laboratory.GetAsync($"{Platform}/governorates/{governorateId}/cities"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await AssertRefusedAsync(laboratory.PostAsJsonAsync($"{Platform}/cities/{nasr}/districts", new LocationRequest("Zone", "منطقة")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
    }

    [DatabaseFact]
    public async Task The_anonymous_pick_lists_answer_active_rows_of_the_asked_parent()
    {
        var admin = await ClientAsync(TenantIds.Platform, ManagerPermissions);
        var governorateId = await CreateGovernorateAsync(admin);
        var nasr = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Nasr City", "مدينة نصر"));
        var sleeping = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Sleeping City", "مدينة نائمة"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Platform}/cities/{sleeping}/deactivate", null)).StatusCode);
        var zone1 = await CreateAsync(admin, $"{Platform}/cities/{nasr}/districts", new LocationRequest("First Zone", "المنطقة الأولى"));
        var block7 = await CreateAsync(admin, $"{Platform}/districts/{zone1}/areas", new LocationRequest("Block 7", "بلوك 7"));

        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var cities = (await anonymous.GetFromJsonAsync<List<PublicCity>>($"{Public}/cities?governorateId={governorateId}"))!;
        Assert.Equal([nasr], cities.Select(city => city.Id));   // the sleeping city is hidden
        Assert.Equal((governorateId, "Nasr City", "مدينة نصر"), (cities[0].GovernorateId, cities[0].NameEn, cities[0].NameAr));
        Assert.Contains((await anonymous.GetFromJsonAsync<List<PublicCity>>($"{Public}/cities"))!, city => city.Id == nasr);
        Assert.Equal([zone1], (await anonymous.GetFromJsonAsync<List<PublicDistrict>>($"{Public}/districts?cityId={nasr}"))!.Select(district => district.Id));
        Assert.Equal([block7], (await anonymous.GetFromJsonAsync<List<PublicArea>>($"{Public}/areas?districtId={zone1}"))!.Select(area => area.Id));
        Assert.Empty((await anonymous.GetFromJsonAsync<List<PublicArea>>($"{Public}/areas?districtId={Guid.CreateVersion7()}"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync($"{Public}/cities?governorateId={Guid.Empty}")).StatusCode);
    }

    private static async Task<Guid> CreateGovernorateAsync(HttpClient admin)
    {
        var iso = TestCountryCodes.Next();
        var countryId = await CreateAsync(admin, "/api/v1/platform/countries", new CountryRequest("Testland " + iso, "أرض الاختبار", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        return await CreateAsync(admin, "/api/v1/platform/governorates", new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null));
    }

    private static async Task<Guid> CreateAsync<T>(HttpClient client, string url, T body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static async Task AssertRefusedAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        Assert.Equal((status, code), (response.StatusCode, await AuthClient.CodeOf(response)));
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Locations Tester");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
