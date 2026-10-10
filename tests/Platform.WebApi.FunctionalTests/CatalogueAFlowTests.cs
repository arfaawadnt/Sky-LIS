using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The catalogue-A pages over the real API (intake LIS-INT-CAT-001 §16): the lab administrator builds groups, sample
/// types, units, conditions, tests and services; the policies refuse what the role lacks (403), the validators the
/// wrong shapes (400) and the aggregates the broken rules (their codes).
/// </summary>
public sealed class CatalogueAFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Root = "/api/v1/catalogue";

    private static readonly string[] Administrator = ["catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.manage", "catalogue.export"];
    private static readonly string[] Viewer = ["catalogue.view"];

    [DatabaseFact]
    public async Task The_lab_administrator_builds_the_catalogue_from_groups_to_services()
    {
        var tenant = Guid.CreateVersion7();
        var (admin, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Administrator);
        var client = await SignedInClientAsync(admin);

        // CP-39 / CP-43 / CP-44 / CP-45
        var groupId = await CreateAsync(client, "clinical-groups", new CreateClinicalGroupRequest(" chem ", "c", "Chemistry", "الكيمياء", "Clinical chemistry", null, 1));
        var duplicate = await client.PostAsJsonAsync($"{Root}/clinical-groups", new CreateClinicalGroupRequest("CHEM", "C", "Again", "مرة أخرى", null, null, 2));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CAT-100", await AuthClient.CodeOf(duplicate));

        var sampleTypeId = await CreateAsync(client, "sample-types", new CreateSampleTypeRequest("SERUM", "SER", "Serum", "مصل", [new SampleConditionItem(null, "FAST", "Fasting", "صائم", true)]));
        var sampleType = (await client.GetFromJsonAsync<SampleTypeDetail>($"{Root}/sample-types/{sampleTypeId}"))!;
        var fasting = Assert.Single(sampleType.Conditions);
        var saved = await client.PutAsJsonAsync($"{Root}/sample-types/{sampleTypeId}/conditions", new SaveSampleConditionsRequest([new SampleConditionItem(fasting.Id, "FAST", "Fasting 12 h", "صائم ١٢ ساعة", true), new SampleConditionItem(null, "RND", "Random", "عشوائي", true)]));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var unitId = await CreateAsync(client, "units", new MeasurementUnitRequest("mg/dL"));
        var unitAgain = await client.PostAsJsonAsync($"{Root}/units", new MeasurementUnitRequest("MG/DL"));
        Assert.Equal(("CAT-102", HttpStatusCode.Conflict), (await AuthClient.CodeOf(unitAgain), unitAgain.StatusCode));
        var pregnantId = await CreateAsync(client, "patient-conditions", new PatientConditionRequest("Pregnant", "حامل"));

        // CP-41: a numeric test with bands, a list test, a formula test
        var gluId = await CreateAsync(client, "tests", TestRequest("GLU", "Numeric", groupId, sampleTypeId, unitId));
        var bands = await client.PutAsJsonAsync($"{Root}/tests/{gluId}/range-bands", new ReplaceRangeBandsRequest(
        [
            new RangeBandItem("Any", 0, 150, "Years", null, 70m, 110m, 40m, 400m, null, null, null),
            new RangeBandItem("Female", 15, 50, "Years", pregnantId, 60m, 95m, null, null, null, null, null),
        ]));
        Assert.Equal(HttpStatusCode.NoContent, bands.StatusCode);
        var overlapping = await client.PutAsJsonAsync($"{Root}/tests/{gluId}/range-bands", new ReplaceRangeBandsRequest([new RangeBandItem("Any", 0, 20, "Years", null, 1m, 2m, null, null, null, null, null), new RangeBandItem("Any", 10, 30, "Years", null, 1m, 2m, null, null, null, null, null)]));
        Assert.Equal(("CAT-054", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(overlapping), overlapping.StatusCode));

        var timing = await client.PutAsJsonAsync($"{Root}/tests/{gluId}/timing", new LabTestTimingItem(48, 12, 30, "Prevent", 20m, 90, 62));
        Assert.Equal(HttpStatusCode.NoContent, timing.StatusCode);

        var urineId = await CreateAsync(client, "tests", TestRequest("URINE", "RestrictedList", groupId, null, null));
        var badDefault = await client.PutAsJsonAsync($"{Root}/tests/{urineId}/list-values", new ReplaceListValuesRequest([new ListValueItem("Negative", true, false, true, false, null)]));
        Assert.Equal("CAT-058", await AuthClient.CodeOf(badDefault));
        var values = await client.PutAsJsonAsync($"{Root}/tests/{urineId}/list-values", new ReplaceListValuesRequest([new ListValueItem(".", true, false, true, true, null), new ListValueItem("Negative", true, false, false, false, null)]));
        Assert.Equal(HttpStatusCode.NoContent, values.StatusCode);

        var ratioId = await CreateAsync(client, "tests", TestRequest("RATIO", "Formula", groupId, null, null));
        var check = await (await client.PostAsJsonAsync($"{Root}/tests/formula/check", new CheckFormulaRequest("round(glu / 18, 2)", ratioId))).Content.ReadFromJsonAsync<FormulaCheckResult>();
        Assert.Equal((true, "ROUND([GLU] / 18, 2)"), (check!.Valid, check.Normalized));
        var unknown = await (await client.PostAsJsonAsync($"{Root}/tests/formula/check", new CheckFormulaRequest("[NOPE] + 1", ratioId))).Content.ReadFromJsonAsync<FormulaCheckResult>();
        Assert.Equal((false, "CAT-064"), (unknown!.Valid, unknown.Code));
        var self = await client.PutAsJsonAsync($"{Root}/tests/{ratioId}/formula", new SetFormulaRequest("[RATIO] * 2"));
        Assert.Equal("CAT-065", await AuthClient.CodeOf(self));
        var formula = await client.PutAsJsonAsync($"{Root}/tests/{ratioId}/formula", new SetFormulaRequest("round(glu / 18, 2)"));
        Assert.Equal(HttpStatusCode.NoContent, formula.StatusCode);

        var glu = (await client.GetFromJsonAsync<LabTestDetail>($"{Root}/tests/{gluId}"))!;
        Assert.Equal((2, "Prevent", 62), (glu.RangeBands.Count, glu.Timing.FrequencyAction, glu.Timing.TestDays));
        var ratio = (await client.GetFromJsonAsync<LabTestDetail>($"{Root}/tests/{ratioId}"))!;
        Assert.Equal(("ROUND([GLU] / 18, 2)", "GLU"), (ratio.FormulaExpression, Assert.Single(ratio.FormulaReferences)));

        // CP-40: a test service, a profile and a package; the member rules refuse a test service with two tests
        var twoTests = await client.PostAsJsonAsync($"{Root}/services", ServiceRequest("BAD", "Test", groupId, sampleTypeId, [new ServiceMemberItem("Test", gluId, 1, false, true), new ServiceMemberItem("Test", urineId, 2, false, true)]));
        Assert.Equal(("CAT-021", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(twoTests), twoTests.StatusCode));
        var wrongCondition = await client.PostAsJsonAsync($"{Root}/services", ServiceRequest("BAD2", "Test", groupId, sampleTypeId, [new ServiceMemberItem("Test", gluId, 1, false, true)]) with { SampleConditionId = Guid.CreateVersion7() });
        Assert.Equal("CAT-030", await AuthClient.CodeOf(wrongCondition));

        var gluService = await CreateAsync(client, "services", ServiceRequest("GLU", "Test", groupId, sampleTypeId, [new ServiceMemberItem("Test", gluId, 1, false, true)]) with { SampleConditionId = fasting.Id, ShortcutNo = 1 });
        var profile = await CreateAsync(client, "services", ServiceRequest("DIAB", "Profile", groupId, sampleTypeId, [new ServiceMemberItem("Test", gluId, 1, false, true), new ServiceMemberItem("Test", ratioId, 2, false, true)]));
        var package = await CreateAsync(client, "services", ServiceRequest("CHECKUP", "Package", groupId, null, [new ServiceMemberItem("Service", gluService, 1, false, true), new ServiceMemberItem("Service", profile, 2, false, true)]));
        var shortcutTaken = await client.PostAsJsonAsync($"{Root}/services", ServiceRequest("GLU2", "Test", groupId, sampleTypeId, [new ServiceMemberItem("Test", gluId, 1, false, true)]) with { ShortcutNo = 1 });
        Assert.Equal("CAT-106", await AuthClient.CodeOf(shortcutTaken));

        var detail = (await client.GetFromJsonAsync<ServiceDetail>($"{Root}/services/{package}"))!;
        Assert.Equal(["GLU", "DIAB"], detail.Members.Select(member => member.Code).ToArray());
        Assert.Equal(3, detail.Sequence);

        // CP-42: swap two sequences in one save; a duplicate is refused
        var swapped = await client.PutAsJsonAsync($"{Root}/services/sequence", new SaveServiceSequenceRequest([new ServiceSequenceChange(gluService, 2), new ServiceSequenceChange(profile, 1)]));
        Assert.Equal(HttpStatusCode.NoContent, swapped.StatusCode);
        var clash = await client.PutAsJsonAsync($"{Root}/services/sequence", new SaveServiceSequenceRequest([new ServiceSequenceChange(package, 1)]));
        Assert.Equal(("CAT-105", HttpStatusCode.Conflict), (await AuthClient.CodeOf(clash), clash.StatusCode));
        var sequence = (await client.GetFromJsonAsync<List<ServiceSequenceRow>>($"{Root}/services/sequence"))!;
        Assert.Equal(["DIAB", "GLU", "CHECKUP"], sequence.Select(row => row.Code).ToArray());

        // CP-55: an additional service with the reduced form
        var additional = await CreateAsync(client, "services", new CreateServiceRequest("HOME", "Additional", "Home visit", 900, "Home visit", "زيارة منزلية", null, null, null, null, null, null, null, null, null, "Any", 0, 0, false, true, false, null, null));
        var additionals = (await client.GetFromJsonAsync<Paged<ServiceListItem>>($"{Root}/services?type=additional"))!;
        Assert.Equal(additional, Assert.Single(additionals.Items).Id);

        // lookups for other modules, exports, deactivation
        var lookup = (await client.GetFromJsonAsync<Paged<ServiceLookupItem>>($"{Root}/lookups/services?search=chec&pageSize=50"))!;
        Assert.Equal("CHECKUP", Assert.Single(lookup.Items).Code);
        var tests = (await client.GetFromJsonAsync<Paged<TestLookupItem>>($"{Root}/lookups/tests?type=formula"))!;
        Assert.Equal("RATIO", Assert.Single(tests.Items).Code);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Root}/lookups/tests?pageSize=51")).StatusCode);
        Assert.Equal(4, (await client.GetFromJsonAsync<List<ServiceListItem>>($"{Root}/services/export"))!.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Root}/tests/{urineId}/deactivate", null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<Paged<TestLookupItem>>($"{Root}/lookups/tests?search=urine"))!.Items);
        var typeChange = await client.PutAsJsonAsync($"{Root}/services/{gluService}", UpdateRequest("Profile", groupId, sampleTypeId));
        Assert.Equal(("CAT-035", HttpStatusCode.Conflict), (await AuthClient.CodeOf(typeChange), typeChange.StatusCode));
    }

    [DatabaseFact]
    public async Task A_viewer_reads_but_cannot_create_nor_export_and_shapes_are_refused_with_400()
    {
        var tenant = Guid.CreateVersion7();
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Viewer);
        var (admin, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Administrator);
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null);
        var viewerClient = await SignedInClientAsync(viewer);
        var adminClient = await SignedInClientAsync(admin);
        var outsiderClient = await SignedInClientAsync(outsider);

        Assert.Equal(HttpStatusCode.OK, (await viewerClient.GetAsync($"{Root}/clinical-groups")).StatusCode);
        var create = await viewerClient.PostAsJsonAsync($"{Root}/clinical-groups", new CreateClinicalGroupRequest("HEM", "H", "Haematology", "أمراض الدم", null, null, 0));
        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.Forbidden), (create.StatusCode, await AuthClient.CodeOf(create)));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync($"{Root}/units/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"{Root}/lookups/services")).StatusCode);

        var shape = await adminClient.PostAsJsonAsync($"{Root}/clinical-groups", new CreateClinicalGroupRequest("", "H", "Haematology", "أمراض الدم", null, null, 0));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (shape.StatusCode, await AuthClient.CodeOf(shape)));
        var badType = await adminClient.GetAsync($"{Root}/services?type=flying");
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        var badCode = await adminClient.PostAsJsonAsync($"{Root}/clinical-groups", new CreateClinicalGroupRequest("H M", "H", "Haematology", "أمراض الدم", null, null, 0));
        Assert.Equal(("CAT-001", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(badCode), badCode.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync($"{Root}/tests/{Guid.CreateVersion7()}")).StatusCode);
    }

    private static CreateLabTestRequest TestRequest(string code, string type, Guid groupId, Guid? sampleTypeId, Guid? unitId) =>
        new(code, code + " test", "فحص " + code, null, null, null, groupId, type, sampleTypeId, null, unitId, "Any", null, 1, false, true, 1, null);

    private static CreateServiceRequest ServiceRequest(string code, string type, Guid groupId, Guid? sampleTypeId, IReadOnlyList<ServiceMemberItem> members) =>
        new(code, type, code, null, code + " service", "خدمة " + code, null, null, null, groupId, sampleTypeId, null, null, ["alias-" + code], null, "Any", 24, 1, false, true, false, null, members);

    private static UpdateServiceRequest UpdateRequest(string type, Guid groupId, Guid sampleTypeId) =>
        new(type, "GLU", 1, "Glucose", "جلوكوز", null, null, null, groupId, sampleTypeId, null, null, null, null, "Any", 24, 1, false, true, false, null);

    private static async Task<Guid> CreateAsync<TBody>(HttpClient client, string resource, TBody body)
    {
        var response = await client.PostAsJsonAsync($"{Root}/{resource}", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{resource}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
