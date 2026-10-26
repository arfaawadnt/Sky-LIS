using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-015 (M3b / M3c) over the real API: the formula check answers, per reference, the services that may be ordered for it and
/// the default; the formula is saved with its operands (service to order, visible in receipt); a service that does not carry the
/// test is refused (CAT-067); a save without operands keeps the stored choices.
/// </summary>
public sealed class CatalogueFormulaOperandsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Root = "/api/v1/catalogue";

    private static readonly string[] Administrator = ["catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.manage", "catalogue.export"];

    [DatabaseFact]
    public async Task A_formula_is_saved_with_the_service_to_order_and_the_receipt_flag_of_each_test_it_reads()
    {
        var tenant = Guid.CreateVersion7();
        var (admin, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: Administrator);
        var client = await SignedInClientAsync(admin);

        var groupId = await CreateAsync(client, "clinical-groups", new CreateClinicalGroupRequest("CHEM", "C", "Chemistry", "الكيمياء", null, null, 1));
        var serumId = await CreateAsync(client, "sample-types", new CreateSampleTypeRequest("SERUM", "SER", "Serum", "مصل", []));
        var cholId = await CreateAsync(client, "tests", TestRequest("CHOL", "Numeric", groupId, serumId));
        var hdlId = await CreateAsync(client, "tests", TestRequest("HDL", "Numeric", groupId, serumId));
        var ldlId = await CreateAsync(client, "tests", TestRequest("LDL", "Formula", groupId, null));

        var cholService = await CreateAsync(client, "services", ServiceRequest("CHOL", "Test", groupId, serumId, [new ServiceMemberItem("Test", cholId, 1, false, true)]));
        var hdlService = await CreateAsync(client, "services", ServiceRequest("HDL", "Test", groupId, serumId, [new ServiceMemberItem("Test", hdlId, 1, false, true)]));
        var lipid = await CreateAsync(client, "services", ServiceRequest("LIPID", "Profile", groupId, serumId, [new ServiceMemberItem("Test", cholId, 1, false, true), new ServiceMemberItem("Test", hdlId, 2, false, true)]));
        await CreateAsync(client, "services", ServiceRequest("HDLX", "Test", groupId, serumId, [new ServiceMemberItem("Test", hdlId, 1, false, true)]) with { IsOrderable = false });

        // the check answers the choices per reference, in reference order: active orderable carriers, the single-test service as default
        var check = (await (await client.PostAsJsonAsync($"{Root}/tests/formula/check", new CheckFormulaRequest("[hdl] - [chol]", ldlId))).Content.ReadFromJsonAsync<FormulaCheckResult>())!;
        Assert.True(check.Valid);
        Assert.Equal(["HDL", "CHOL"], check.Operands.Select(operand => operand.TestCode).ToArray());
        var hdl = check.Operands[0];
        Assert.Equal((hdlId, "HDL test", hdlService), (hdl.TestId!.Value, hdl.TestNameEn, hdl.DefaultServiceId!.Value));
        Assert.Equal(["HDL", "LIPID"], hdl.Services.Select(service => service.Code).ToArray());   // HDLX is not orderable
        Assert.Equal(["Test", "Profile"], hdl.Services.Select(service => service.Type).ToArray());
        Assert.Empty((await (await client.PostAsJsonAsync($"{Root}/tests/formula/check", new CheckFormulaRequest("[NOPE] + 1", ldlId))).Content.ReadFromJsonAsync<FormulaCheckResult>())!.Operands);

        // saved with one chosen operand; the other takes its default off the receipt
        var saved = await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest("[CHOL] - [HDL]", [new FormulaOperandItem("hdl", lipid, true)]));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var detail = (await client.GetFromJsonAsync<LabTestDetail>($"{Root}/tests/{ldlId}"))!;
        Assert.Equal([new FormulaOperandItem("CHOL", cholService, false), new FormulaOperandItem("HDL", lipid, true)], detail.FormulaOperands.ToArray());

        // CAT-067: the HDL service does not carry CHOL; nothing changes
        var refused = await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest("[CHOL] - [HDL]", [new FormulaOperandItem("CHOL", hdlService, false)]));
        Assert.Equal(("CAT-067", HttpStatusCode.UnprocessableEntity), (await AuthClient.CodeOf(refused), refused.StatusCode));

        // "None": nothing is ordered for CHOL; a save without operands keeps the stored choices of the codes still read
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest("[CHOL] - [HDL]", [new FormulaOperandItem("CHOL", null, true), new FormulaOperandItem("HDL", lipid, true)]))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest("[HDL] * 2"))).StatusCode);
        detail = (await client.GetFromJsonAsync<LabTestDetail>($"{Root}/tests/{ldlId}"))!;
        Assert.Equal([new FormulaOperandItem("HDL", lipid, true)], detail.FormulaOperands.ToArray());

        // clearing the formula clears the operands; a shape error names the field
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest(null))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<LabTestDetail>($"{Root}/tests/{ldlId}"))!.FormulaOperands);
        var blank = await client.PutAsJsonAsync($"{Root}/tests/{ldlId}/formula", new SetFormulaRequest("[HDL]", [new FormulaOperandItem("", null, false)]));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (blank.StatusCode, await AuthClient.CodeOf(blank)));
    }

    private static CreateLabTestRequest TestRequest(string code, string type, Guid groupId, Guid? sampleTypeId) =>
        new(code, code + " test", "فحص " + code, null, null, null, groupId, type, sampleTypeId, null, null, "Any", null, 1, false, true, 1, null);

    private static CreateServiceRequest ServiceRequest(string code, string type, Guid groupId, Guid? sampleTypeId, IReadOnlyList<ServiceMemberItem> members) =>
        new(code, type, code, null, code + " service", "خدمة " + code, null, null, null, groupId, sampleTypeId, null, null, [], null, "Any", 24, 1, false, true, false, null, members);

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
