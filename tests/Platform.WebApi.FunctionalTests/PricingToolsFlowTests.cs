using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Change request LIS-CR-003 (2026-10-19) on Pricing, over the real API: payer office contacts (C19), the Excel import and
/// the bulk adjustment of a base price list (C21) and the service prices read (C22). The catalogue services the import
/// names are created through the catalogue API (an "Additional" service needs no group).
/// </summary>
public sealed class PricingToolsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Payers = "/api/v1/pricing/payers";
    private const string Lists = "/api/v1/pricing/base-price-lists";
    private const string Contracts = "/api/v1/pricing/contract-pricing";
    private const string ServicePrices = "/api/v1/pricing/service-prices";
    private const string CatalogueServices = "/api/v1/catalogue/services";

    private static readonly string[] AccountsPermissions = ["pricing.view", "pricing.create", "pricing.edit", "pricing.approve", "pricing.manage", "pricing.export", "catalogue.view", "catalogue.create"];
    private static readonly string[] ViewerPermissions = ["pricing.view"];

    [DatabaseFact]
    public async Task Accounts_imports_a_sheet_adjusts_the_list_in_bulk_and_reads_every_price_of_a_service()
    {
        var tenant = Guid.CreateVersion7();
        var (accounts, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: AccountsPermissions, displayName: "Accounts Officer");
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ViewerPermissions, displayName: "Reception Clerk");
        var api = await SignedInClientAsync(accounts);
        var reader = await SignedInClientAsync(viewer);

        var cbc = await CreateServiceAsync(api, "CBC");
        var lip = await CreateServiceAsync(api, "LIP");
        var tsh = await CreateServiceAsync(api, "TSH");
        var pl26 = await CreatedIdAsync(await api.PostAsJsonAsync(Lists, new CreateBasePriceListRequest("pl26", "Main price list 2026", "قائمة 2026", 2026)));

        // L8 import, dry run: every row is checked and nothing is written (all-or-nothing)
        var checkedRows = await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest(
            [
                new ImportPriceRowRequest(2, "CBC", 180m),
                new ImportPriceRowRequest(3, " lip ", 300m),
                new ImportPriceRowRequest(4, null, 10m),
                new ImportPriceRowRequest(5, "NOPE", 5m),
                new ImportPriceRowRequest(6, "TSH", null),
                new ImportPriceRowRequest(7, "TSH", -1m),
                new ImportPriceRowRequest(8, "cbc", 190m),
            ],
            DryRun: true));
        Assert.Equal(HttpStatusCode.OK, checkedRows.StatusCode);
        var check = (await checkedRows.Content.ReadFromJsonAsync<ImportPriceLinesResult>())!;
        Assert.Equal((2, 2, 0, false), (check.Accepted, check.Added, check.Changed, check.Applied));
        Assert.Equal(
            [(4, PricingCodes.ImportCodeMissing), (5, PricingCodes.ServiceUnknown), (6, PricingCodes.PriceInvalid), (7, PricingCodes.PriceInvalid), (8, PricingCodes.DuplicateServiceLine)],
            check.Errors.Select(error => (error.Row, error.Code)));
        Assert.Equal("TSH", check.Errors.Single(error => error.Row == 7).ServiceCode);
        Assert.Equal("CBC", check.Errors.Single(error => error.Row == 8).ServiceCode);   // codes are reported cleaned (trimmed, upper-case)
        Assert.Empty((await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!);

        // with an error nothing is applied even without a dry run
        var refusedImport = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest([new ImportPriceRowRequest(2, "CBC", 180m), new ImportPriceRowRequest(3, "NOPE", 5m)], DryRun: false))).Content.ReadFromJsonAsync<ImportPriceLinesResult>();
        Assert.False(refusedImport!.Applied);
        Assert.Empty((await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!);

        // a clean dry run reports the counts and still writes nothing; the real import upserts the lines in one save
        var validRows = new List<ImportPriceRowRequest> { new(2, "CBC", 180m), new(3, "lip", 300m), new(4, "TSH", 90.10m) };
        var dry = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest(validRows, DryRun: true))).Content.ReadFromJsonAsync<ImportPriceLinesResult>();
        Assert.Equal((3, 3, 0, false, 0), (dry!.Accepted, dry.Added, dry.Changed, dry.Applied, dry.Errors.Count));
        Assert.Empty((await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!);

        var imported = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest(validRows, DryRun: false))).Content.ReadFromJsonAsync<ImportPriceLinesResult>();
        Assert.Equal((3, 3, 0, true), (imported!.Accepted, imported.Added, imported.Changed, imported.Applied));
        var lines = (await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!;
        Assert.Equal([(cbc, 180m), (lip, 300m), (tsh, 90.10m)], lines.OrderBy(line => line.ServiceCode).Select(line => (line.ServiceId, line.Price)));
        Assert.All(lines, line => Assert.True(line.IsActive));

        // a second import changes a price and keeps the others
        var again = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest([new ImportPriceRowRequest(2, "CBC", 190m), new ImportPriceRowRequest(3, "LIP", 300m)], DryRun: false))).Content.ReadFromJsonAsync<ImportPriceLinesResult>();
        Assert.Equal((2, 0, 1, true), (again!.Accepted, again.Added, again.Changed, again.Applied));

        // shape: no rows, an unknown adjust mode, a value with three decimals
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest([], DryRun: true))).StatusCode);
        var badMode = await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Triple", 10m, "None", null, Preview: true));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (badMode.StatusCode, await AuthClient.CodeOf(badMode)));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 10.005m, "Whole", null, Preview: true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 10m, "Nearest7", null, Preview: true))).StatusCode);

        // L7 preview: every line priced, nothing written
        var preview = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("percent", 10m, "whole", null, Preview: true))).Content.ReadFromJsonAsync<AdjustBasePriceListResult>();
        Assert.Equal((3, 0, false), (preview!.Changed, preview.Clamped, preview.Applied));
        Assert.Equal([("CBC", 190m, 209m), ("LIP", 300m, 330m), ("TSH", 90.10m, 99m)], preview.Rows.OrderBy(row => row.Code).Select(row => (row.Code, row.OldPrice, row.NewPrice)));
        Assert.Equal("CBC service", preview.Rows.Single(row => row.ServiceId == cbc).NameEn);
        Assert.Equal(190m, (await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!.Single(line => line.ServiceId == cbc).Price);

        // L7 apply on a scope: a negative result is held at 0 and reported
        var applied = await (await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Amount", -250m, "None", [lip, tsh], Preview: false))).Content.ReadFromJsonAsync<AdjustBasePriceListResult>();
        Assert.Equal((2, 1, true), (applied!.Changed, applied.Clamped, applied.Applied));
        Assert.Equal([(lip, 50m, false), (tsh, 0m, true)], applied.Rows.Select(row => (row.ServiceId, row.NewPrice, row.Clamped)));
        lines = (await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl26}/lines"))!;
        Assert.Equal([(cbc, 190m), (lip, 50m), (tsh, 0m)], lines.OrderBy(line => line.ServiceCode).Select(line => (line.ServiceId, line.Price)));

        // refusals: a scope id the list does not hold, a percentage outside −100…1 000, a result above the maximum
        var unknownScope = await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 10m, "None", [cbc, Guid.CreateVersion7()], Preview: true));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.AdjustScopeUnknown), (unknownScope.StatusCode, await AuthClient.CodeOf(unknownScope)));
        var tooMuch = await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 2000m, "None", null, Preview: true));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.AdjustmentInvalid), (tooMuch.StatusCode, await AuthClient.CodeOf(tooMuch)));
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync($"{Lists}/{pl26}/lines", new UpsertPriceLinesRequest([new PriceLineRequest(cbc, PriceMath.MaxAmount, true)]))).StatusCode);
        var overflow = await api.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 1m, "None", [cbc], Preview: true));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.AdjustResultOutOfRange), (overflow.StatusCode, await AuthClient.CodeOf(overflow)));
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync($"{Lists}/{pl26}/lines", new UpsertPriceLinesRequest([new PriceLineRequest(cbc, 190m, true)]))).StatusCode);
        var unknownList = await api.PostAsJsonAsync($"{Lists}/{Guid.CreateVersion7()}/adjust", new AdjustBasePriceListRequest("Percent", 1m, "None", null, Preview: true));
        Assert.Equal((HttpStatusCode.NotFound, PricingCodes.BaseListNotFound), (unknownList.StatusCode, await AuthClient.CodeOf(unknownList)));

        // C22: a contract derived from the list, then every price of CBC
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync($"{Lists}/{pl26}/activate", null)).StatusCode);
        var axaId = await CreatedIdAsync(await api.PostAsJsonAsync(Payers, new CreatePayerRequest("AXA Insurance", "أكسا للتأمين", "axa", "Insurance", false, null)));
        var goldId = await CreatedIdAsync(await api.PostAsJsonAsync($"{Payers}/{axaId}/tiers", new ContractTierRequest("Corporate Gold", "الشركات الذهبي")));
        var today = DateOnly.FromDateTime(factory.Clock.UtcNow.UtcDateTime);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{goldId}", new SetContractFormulaRequest(today.AddDays(-1), null, [new PricingFormulaTermRequest(pl26, 0.1m, 0.9m)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync($"{Contracts}/{goldId}/apply", null)).StatusCode);

        var prices = await reader.GetFromJsonAsync<ServicePricesView>($"{ServicePrices}/{cbc}");
        Assert.Equal((cbc, "CBC", "CBC service", "Additional", true), (prices!.ServiceId, prices.Code, prices.NameEn, prices.Type, prices.IsActive));
        Assert.NotEmpty(prices.Currency);
        var basePrice = Assert.Single(prices.BasePrices);
        Assert.Equal((pl26, "PL26", 2026, true, 190m, true), (basePrice.ListId, basePrice.ListCode, basePrice.Year, basePrice.ListActive, basePrice.Price, basePrice.LineActive));
        var gold = Assert.Single(prices.ContractPrices, price => price.TierId == goldId);
        Assert.Equal((axaId, "AXA Insurance", "Corporate Gold", 190m, 19m, 171m, false, "PL26", true, true, true), (gold.PayerId, gold.PayerNameEn, gold.TierNameEn, gold.ListPrice, gold.PatientPrice, gold.PayerPrice, gold.IsExcluded, gold.SourceListCode, gold.TierActive, gold.PayerActive, gold.InEffect));

        var unknownService = await reader.GetAsync($"{ServicePrices}/{Guid.CreateVersion7()}");
        Assert.Equal((HttpStatusCode.NotFound, PricingCodes.ServiceNotFound), (unknownService.StatusCode, await AuthClient.CodeOf(unknownService)));

        // the viewer reads prices but never writes
        foreach (var response in new[]
                 {
                     await reader.PostAsJsonAsync($"{Lists}/{pl26}/import", new ImportPriceLinesRequest([new ImportPriceRowRequest(2, "CBC", 1m)], DryRun: true)),
                     await reader.PostAsJsonAsync($"{Lists}/{pl26}/adjust", new AdjustBasePriceListRequest("Percent", 10m, "None", null, Preview: true)),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(response));
        }
    }

    [DatabaseFact]
    public async Task A_payer_carries_its_office_contacts_and_refuses_malformed_ones()
    {
        var tenant = Guid.CreateVersion7();
        var (accounts, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: AccountsPermissions, displayName: "Accounts Officer");
        var api = await SignedInClientAsync(accounts);

        var created = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("Med Care", "ميد كير", "medcare", "Insurance", false, null, "  +20 (2)  2735-1234 ", null, "01001234567", " contracts@medcare.example.com "));
        var payerId = await CreatedIdAsync(created);
        var payer = await api.GetFromJsonAsync<PayerListItem>($"{Payers}/{payerId}");
        Assert.Equal(("+20 (2) 2735-1234", (string?)null, "01001234567", "contracts@medcare.example.com"), (payer!.Phone1, payer.Phone2, payer.WhatsApp, payer.Email));
        var listed = (await api.GetFromJsonAsync<Paged<PayerListItem>>($"{Payers}?search=medcare"))!.Items.Single();
        Assert.Equal("contracts@medcare.example.com", listed.Email);

        // the request shape of 0.14.0 (no contacts) still works and clears nothing it does not send on create
        var legacy = await CreatedIdAsync(await api.PostAsJsonAsync(Payers, new { nameEn = "Legacy", nameAr = "قديم", searchKey = "LEGACY", type = "Credit", webPortalEnabled = false }));
        Assert.Null((await api.GetFromJsonAsync<PayerListItem>($"{Payers}/{legacy}"))!.Phone1);

        // update the contacts alone
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Payers}/{payerId}", new UpdatePayerRequest("Med Care", "ميد كير", "medcare", "Insurance", false, null, "0100 123 4567", "0222", null, "a@medcare.example.com"))).StatusCode);
        payer = await api.GetFromJsonAsync<PayerListItem>($"{Payers}/{payerId}");
        Assert.Equal(("0100 123 4567", "0222", (string?)null, "a@medcare.example.com"), (payer!.Phone1, payer.Phone2, payer.WhatsApp, payer.Email));

        // PRC-017 / PRC-018 from the aggregate, 400 from the shape validator
        var badPhone = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("X", "س", "X1", "Credit", false, null, "0100 ABC", null, null, null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.ContactNumberInvalid), (badPhone.StatusCode, await AuthClient.CodeOf(badPhone)));
        var badWhatsApp = await api.PutAsJsonAsync($"{Payers}/{payerId}", new UpdatePayerRequest("Med Care", "ميد كير", "medcare", "Insurance", false, null, null, null, "+ - ( )", null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.ContactNumberInvalid), (badWhatsApp.StatusCode, await AuthClient.CodeOf(badWhatsApp)));
        var badEmail = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("X", "س", "X2", "Credit", false, null, null, null, null, "contracts@medcare"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, PricingCodes.EmailInvalid), (badEmail.StatusCode, await AuthClient.CodeOf(badEmail)));
        var tooLong = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("X", "س", "X3", "Credit", false, null, new string('1', 33), null, null, null));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (tooLong.StatusCode, await AuthClient.CodeOf(tooLong)));
        Assert.Equal("0100 123 4567", (await api.GetFromJsonAsync<PayerListItem>($"{Payers}/{payerId}"))!.Phone1);   // the refused update changed nothing
    }

    private static async Task<Guid> CreateServiceAsync(HttpClient client, string code)
    {
        var request = new CreateServiceRequest(code, "Additional", code, null, code + " service", "خدمة " + code, null, null, null, null, null, null, null, null, null, "Any", 0, 0, false, true, false, null, null);
        var response = await client.PostAsJsonAsync(CatalogueServices, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"service {code}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode} {await response.Content.ReadAsStringAsync()}");
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
