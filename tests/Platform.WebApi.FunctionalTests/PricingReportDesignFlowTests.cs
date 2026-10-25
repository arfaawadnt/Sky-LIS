using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// C7 Report design of a contract tier (LIS-CR-010, C-14; intake PRC §20): <c>PUT …/contract-pricing/{tier}/report-design</c>
/// sets and clears the override, the CP-34 page reads it back, the tier needs its pricing (PRC-407), the shape is checked and
/// the permission is <c>pricing.edit</c>. The precedence over the template and the laboratory is the Results module's.
/// </summary>
public sealed class PricingReportDesignFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Payers = "/api/v1/pricing/payers";
    private const string Lists = "/api/v1/pricing/base-price-lists";
    private const string Contracts = "/api/v1/pricing/contract-pricing";

    [DatabaseFact]
    public async Task A_tier_sets_reads_and_clears_its_report_design()
    {
        var tenant = Guid.CreateVersion7();
        var (accounts, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["pricing.view", "pricing.create", "pricing.edit"]);
        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["pricing.view"]);
        var api = await SignedInClientAsync(accounts);
        var reader = await SignedInClientAsync(clerk);

        var cash = (await (await api.PostAsync($"{Payers}/default", null)).Content.ReadFromJsonAsync<DefaultPayerIds>())!;
        var tier = cash.ContractTierId;

        var beforeFormula = await api.PutAsJsonAsync($"{Contracts}/{tier}/report-design", new SetContractReportDesignRequest("Modern"));
        Assert.Equal((HttpStatusCode.NotFound, PricingCodes.ContractPricingNotFound), (beforeFormula.StatusCode, await AuthClient.CodeOf(beforeFormula)));

        var created = await api.PostAsJsonAsync(Lists, new CreateBasePriceListRequest("RD26", "Design list", "قائمة", 2026));
        var list = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var today = DateOnly.FromDateTime(factory.Clock.UtcNow.UtcDateTime);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{tier}", new SetContractFormulaRequest(today, null, [new PricingFormulaTermRequest(list, 1m, 0m)]))).StatusCode);
        Assert.Null((await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{tier}"))!.ReportDesign);

        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{tier}/report-design", new SetContractReportDesignRequest("letterhead"))).StatusCode);
        Assert.Equal("Letterhead", (await reader.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{tier}"))!.ReportDesign);

        // The formula is not the design: setting it again keeps the override.
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{tier}", new SetContractFormulaRequest(today, null, [new PricingFormulaTermRequest(list, 0.5m, 0.5m)]))).StatusCode);
        Assert.Equal("Letterhead", (await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{tier}"))!.ReportDesign);

        var unknown = await api.PutAsJsonAsync($"{Contracts}/{tier}/report-design", new SetContractReportDesignRequest("Fancy"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var forbidden = await reader.PutAsJsonAsync($"{Contracts}/{tier}/report-design", new SetContractReportDesignRequest("Modern"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{tier}/report-design", new SetContractReportDesignRequest(null))).StatusCode);
        Assert.Null((await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{tier}"))!.ReportDesign);
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
