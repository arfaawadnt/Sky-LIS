using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-007 C-09 (Catalogue-B intake, change request 2026-10-24): <em>Add doctor</em> with a blank code takes the next
/// <c>DR-nnn</c> of the laboratory's own counter; a typed code is checked as before and a generated number it already
/// holds is skipped.
/// </summary>
public sealed class CatalogueBDoctorCodeFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Doctors = "/api/v1/catalogue/doctors";

    private static readonly string[] Editor = ["catalogue.view", "catalogue.create", "catalogue.edit"];

    [DatabaseFact]
    public async Task A_blank_code_is_generated_per_laboratory_and_skips_a_typed_one()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant);

        Assert.Equal("DR-001", await CodeOfAsync(editor, await editor.PostAsJsonAsync(Doctors, Doctor(""))));
        Assert.Equal("DR-002", await CodeOfAsync(editor, await editor.PostAsJsonAsync(Doctors, new { nameEn = "Dr. Hala", nameAr = "د. هالة", sharePercent = 0m })));
        Assert.Equal("DR-003", await CodeOfAsync(editor, await editor.PostAsJsonAsync(Doctors, Doctor("dr-003"))));
        Assert.Equal("DR-004", await CodeOfAsync(editor, await editor.PostAsJsonAsync(Doctors, Doctor(null))));

        var duplicate = await editor.PostAsJsonAsync(Doctors, Doctor("DR-001"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CAT-300", await AuthClient.CodeOf(duplicate));
        var invalid = await editor.PostAsJsonAsync(Doctors, Doctor("-x"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("CAT-200", await AuthClient.CodeOf(invalid));
        var unknownBranch = await editor.PostAsJsonAsync(Doctors, Doctor(" ") with { BranchIds = [Guid.CreateVersion7()] });
        Assert.Equal("CAT-205", await AuthClient.CodeOf(unknownBranch));

        // A refused request spends no number; another laboratory has its own counter.
        Assert.Equal("DR-005", await CodeOfAsync(editor, await editor.PostAsJsonAsync(Doctors, Doctor("   "))));
        var other = await ClientAsync(Guid.CreateVersion7());
        Assert.Equal("DR-001", await CodeOfAsync(other, await other.PostAsJsonAsync(Doctors, Doctor(""))));
    }

    private static CreateDoctorRequest Doctor(string? code) => new(code, "Dr. Amal", "د. أمل", null, null, null, 10m, null);

    private static async Task<string> CodeOfAsync(HttpClient client, HttpResponseMessage created)
    {
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        return (await client.GetFromJsonAsync<DoctorListItem>($"{Doctors}/{id}"))!.Code;
    }

    private async Task<HttpClient> ClientAsync(Guid tenant)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Editor);
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
