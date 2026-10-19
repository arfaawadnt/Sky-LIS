using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Change request 2026-10-19 on the patient file over the real API (intake LIS-INT-PAT-001 §18): the second phone (C3) is
/// stored cleaned, listed, exported and found by the similar check; the picked city / district / area (A2) must chain to
/// the governorate (PAT-014/015/016), the city's English name lands in the text city, and the detail, the register and the
/// CSV carry the names. Existing request shapes keep working (the new fields are optional).
/// </summary>
public sealed class PatientContactsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Patients = "/api/v1/patients";
    private const string Platform = "/api/v1/platform";

    private static readonly string[] ClerkPermissions = ["patients.view", "patients.create", "patients.edit", "patients.export"];
    private static readonly string[] AdminPermissions = ["tenancy.view", "tenancy.manage"];

    [DatabaseFact]
    public async Task A_file_carries_its_second_phone_and_its_location_chain_through_detail_register_similar_and_export()
    {
        var place = await BuildPlaceAsync();
        var clerk = await ClientAsync(Guid.CreateVersion7(), ClerkPermissions);

        var created = await clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId, "typed city") with { CityId = place.CityId, DistrictId = place.DistrictId, AreaId = place.AreaId, SecondPhone = "0111 222 3333" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<CreatedPatient>())!;

        var detail = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Equal(("01112223333", "Nasr City"), (detail.SecondPhone, detail.City));   // the picked city's English name replaces the typed one
        Assert.NotNull(detail.Location);
        Assert.Equal((place.CityId, "Nasr City", "مدينة نصر", place.DistrictId, "First Zone", place.AreaId, "Block 7", "بلوك 7"),
            (detail.Location!.CityId, detail.Location.CityNameEn, detail.Location.CityNameAr, detail.Location.DistrictId, detail.Location.DistrictNameEn, detail.Location.AreaId, detail.Location.AreaNameEn, detail.Location.AreaNameAr));

        var listed = Assert.Single((await clerk.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q={patient.Number}"))!.Items);
        Assert.Equal(("01112223333", "First Zone", "Block 7"), (listed.SecondPhone, listed.Location?.DistrictNameEn, listed.Location?.AreaNameEn));
        Assert.Equal(patient.Id, Assert.Single((await clerk.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=01112223333"))!.Items).Id);   // the register searches the second phone

        // the similar check: the typed mobile or second phone matches a file's mobile or second phone
        var bySecond = (await clerk.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?mobile=01112223333"))!;
        Assert.Equal(("Mobile", "01112223333"), (Assert.Single(bySecond.Candidates).Match, bySecond.Candidates[0].SecondPhone));
        var byTypedSecond = (await clerk.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?secondPhone=01001234567"))!;
        Assert.Equal(patient.Id, Assert.Single(byTypedSecond.Candidates).Id);

        var export = await clerk.GetAsync($"{Patients}/export?q={patient.Number}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csv = await export.Content.ReadAsByteArrayAsync();
        var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
        Assert.StartsWith("Number,Title,Name,Arabic name,Gender,Date of birth,Estimated,Age (years),Mobile,Second phone,National id,Nationality,E-mail,City,District,Area,Address,", text, StringComparison.Ordinal);
        Assert.Contains(",01001234567,01112223333,,EG,,Nasr City,First Zone,Block 7,,Active,", text, StringComparison.Ordinal);

        // T2: the chain can be shortened and the second phone cleared; a legacy request without the fields still works
        var update = Update(place.GovernorateId, detail.RowVersion) with { CityId = place.CityId, SecondPhone = null };
        Assert.Equal(HttpStatusCode.NoContent, (await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", update)).StatusCode);
        var shortened = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Equal((place.CityId, (Guid?)null, (Guid?)null, (string?)null, "Nasr City"), (shortened.Location?.CityId, shortened.Location?.DistrictId, shortened.Location?.AreaId, shortened.SecondPhone, shortened.City));
        Assert.Equal(HttpStatusCode.NoContent, (await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", Update(place.GovernorateId, shortened.RowVersion))).StatusCode);
        var legacy = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Null(legacy.Location);
        Assert.Equal("Cairo", legacy.City);   // the typed city again
    }

    [DatabaseFact]
    public async Task A_broken_chain_or_a_malformed_second_phone_is_refused_with_its_code_and_nothing_is_created()
    {
        var place = await BuildPlaceAsync();
        var clerk = await ClientAsync(Guid.CreateVersion7(), ClerkPermissions);

        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { CityId = place.OtherCityId }), "PAT-014");   // a city of another governorate
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(null) with { CityId = place.CityId }), "PAT-014");                       // a city without its governorate
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { CityId = Guid.CreateVersion7() }), "PAT-014");
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { CityId = place.CityId, DistrictId = Guid.CreateVersion7() }), "PAT-015");
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { DistrictId = place.DistrictId }), "PAT-015");   // a district without its city
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { CityId = place.CityId, DistrictId = place.DistrictId, AreaId = Guid.CreateVersion7() }), "PAT-016");
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { CityId = place.CityId, AreaId = place.AreaId }), "PAT-016");   // an area without its district
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { SecondPhone = "12ab" }), "PAT-017");
        await AssertRefusedAsync(clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { SecondPhone = "1234567" }), "PAT-017");

        var tooLong = await clerk.PostAsJsonAsync(Patients, Register(place.GovernorateId) with { SecondPhone = new string('1', 40) });
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (tooLong.StatusCode, await AuthClient.CodeOf(tooLong)));

        Assert.Empty((await clerk.GetFromJsonAsync<Paged<PatientListItem>>(Patients))!.Items);   // nothing was registered
    }

    private static RegisterPatientRequest Register(Guid? governorateId, string? city = "Cairo") =>
        new("Mr", "Located Person", null, "Male", new DateOnly(1990, 5, 1), null, null, "01001234567", null, "EG", null, governorateId, city, null, null, null);

    private static UpdatePatientRequest Update(Guid? governorateId, uint rowVersion) =>
        new("Mr", "Located Person", null, "Male", new DateOnly(1990, 5, 1), null, null, "01001234567", null, "EG", null, governorateId, "Cairo", null, null, null, rowVersion);

    private static async Task AssertRefusedAsync(Task<HttpResponseMessage> call, string code)
    {
        var response = await call;
        Assert.Equal((HttpStatusCode.UnprocessableEntity, code), (response.StatusCode, await AuthClient.CodeOf(response)));
    }

    /// <summary>A governorate with the chain Nasr City → First Zone → Block 7, and a city of another governorate.</summary>
    private async Task<Place> BuildPlaceAsync()
    {
        var admin = await ClientAsync(TenantIds.Platform, AdminPermissions);
        var iso = TestCountryCodes.Next();
        var countryId = await CreateAsync(admin, $"{Platform}/countries", new CountryRequest("Testland " + iso, "أرض الاختبار", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorateId = await CreateAsync(admin, $"{Platform}/governorates", new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null));
        var otherGovernorateId = await CreateAsync(admin, $"{Platform}/governorates", new GovernorateRequest(countryId, "Giza", "الجيزة", null, null));
        var cityId = await CreateAsync(admin, $"{Platform}/governorates/{governorateId}/cities", new LocationRequest("Nasr City", "مدينة نصر"));
        var otherCityId = await CreateAsync(admin, $"{Platform}/governorates/{otherGovernorateId}/cities", new LocationRequest("Dokki", "الدقي"));
        var districtId = await CreateAsync(admin, $"{Platform}/cities/{cityId}/districts", new LocationRequest("First Zone", "المنطقة الأولى"));
        var areaId = await CreateAsync(admin, $"{Platform}/districts/{districtId}/areas", new LocationRequest("Block 7", "بلوك 7"));
        return new Place(governorateId, cityId, districtId, areaId, otherCityId);
    }

    private static async Task<Guid> CreateAsync<T>(HttpClient client, string url, T body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Contacts Tester");
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private sealed record Place(Guid GovernorateId, Guid CityId, Guid DistrictId, Guid AreaId, Guid OtherCityId);
}
