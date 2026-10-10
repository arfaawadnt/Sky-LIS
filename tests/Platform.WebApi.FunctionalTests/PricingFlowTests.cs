using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.Infrastructure.Security;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// WF-10 (pricing steps) over the real API (intake LIS-INT-PRC-001 §16): payers and tiers, base lists with copy and Apply,
/// a tier's formula, negotiated prices and alerts, copy between tiers, and the CP-12 quote a reception clerk reads.
/// Catalogue services cannot be created by this module, so the test seeds price lines through the aggregate and
/// checks the API's catalogue refusal (PRC-015) separately.
/// </summary>
public sealed class PricingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Payers = "/api/v1/pricing/payers";
    private const string Lists = "/api/v1/pricing/base-price-lists";
    private const string Contracts = "/api/v1/pricing/contract-pricing";
    private const string Quote = "/api/v1/pricing/quote";

    private static readonly string[] AccountsPermissions = ["pricing.view", "pricing.create", "pricing.edit", "pricing.approve", "pricing.manage", "pricing.export"];
    private static readonly string[] ClerkPermissions = ["pricing.view"];

    private static readonly Guid S1 = Guid.CreateVersion7();
    private static readonly Guid S2 = Guid.CreateVersion7();

    [DatabaseFact]
    public async Task Accounts_maintains_payers_lists_and_contract_pricing_and_reception_quotes()
    {
        var tenant = Guid.CreateVersion7();
        var (accounts, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: AccountsPermissions, displayName: "Accounts Officer");
        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions, displayName: "Reception Clerk");
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null);
        var api = await SignedInClientAsync(accounts);
        var reception = await SignedInClientAsync(clerk);
        var stranger = await SignedInClientAsync(outsider);

        // deny by default
        var forbidden = await stranger.GetAsync(Payers);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));

        // P8: the cash payer is provisioned once
        var cash = await (await api.PostAsync($"{Payers}/default", null)).Content.ReadFromJsonAsync<DefaultPayerIds>();
        Assert.True(cash!.Created);
        var again = await (await api.PostAsync($"{Payers}/default", null)).Content.ReadFromJsonAsync<DefaultPayerIds>();
        Assert.Equal((cash.PayerId, cash.ContractTierId, false), (again!.PayerId, again.ContractTierId, again.Created));
        var lockedDefault = await api.PostAsync($"{Payers}/{cash.PayerId}/deactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, lockedDefault.StatusCode);
        Assert.Equal(PricingCodes.DefaultPayerLocked, await AuthClient.CodeOf(lockedDefault));

        // P1: shape first (portal without username), then the payer, then the duplicate search key
        var noUsername = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("AXA Insurance", "أكسا للتأمين", "axa", "Insurance", true, null));
        Assert.Equal(HttpStatusCode.BadRequest, noUsername.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(noUsername));

        var created = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("AXA Insurance", "أكسا للتأمين", " axa ", "Insurance", true, "AXA.Portal"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var axaId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var duplicate = await api.PostAsJsonAsync(Payers, new CreatePayerRequest("Other", "أخرى", "AXA", "Credit", false, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(PricingCodes.SearchKeyExists, await AuthClient.CodeOf(duplicate));

        // P5: tiers
        var goldId = await CreatedIdAsync(await api.PostAsJsonAsync($"{Payers}/{axaId}/tiers", new ContractTierRequest("Corporate Gold", "الشركات الذهبي")));
        var silverId = await CreatedIdAsync(await api.PostAsJsonAsync($"{Payers}/{axaId}/tiers", new ContractTierRequest("Corporate Silver", "الشركات الفضي")));
        var sameTier = await api.PostAsJsonAsync($"{Payers}/{axaId}/tiers", new ContractTierRequest("corporate gold", "اسم"));
        Assert.Equal(PricingCodes.TierNameExists, await AuthClient.CodeOf(sameTier));
        var axa = await api.GetFromJsonAsync<PayerListItem>($"{Payers}/{axaId}");
        Assert.Equal(("AXA", "axa.portal", 2), (axa!.SearchKey, axa.WebUsername, axa.Tiers.Count));

        // L1: a list (inactive), its duplicate code, the catalogue refusal, then lines seeded through the aggregate
        var pl26 = await CreatedIdAsync(await api.PostAsJsonAsync(Lists, new CreateBasePriceListRequest("pl26", "Main price list 2026", "قائمة 2026", 2026)));
        Assert.Equal(PricingCodes.ListCodeExists, await AuthClient.CodeOf(await api.PostAsJsonAsync(Lists, new CreateBasePriceListRequest("PL26", "Again", "مرة", 2026))));
        var unknownService = await api.PutAsJsonAsync($"{Lists}/{pl26}/lines", new UpsertPriceLinesRequest([new PriceLineRequest(Guid.CreateVersion7(), 10m, true)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownService.StatusCode);
        Assert.Equal(PricingCodes.ServiceUnknown, await AuthClient.CodeOf(unknownService));
        var negative = await api.PutAsJsonAsync($"{Lists}/{pl26}/lines", new UpsertPriceLinesRequest([new PriceLineRequest(S1, -1m, true)]));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        await SeedLinesAsync(tenant, pl26, (S1, 180m), (S2, 300m));
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync($"{Lists}/{pl26}/activate", null)).StatusCode);

        // L4: next year's list ×1.1, inactive, rounded to 1
        var copied = await api.PostAsJsonAsync($"{Lists}/{pl26}/copy", new CopyBasePriceListRequest("pl27", "Main price list 2027", "قائمة 2027", 2027, "Multiply", 1.1m, 1m));
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        var pl27 = (await copied.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var pl27Lines = await api.GetFromJsonAsync<List<PriceLineItem>>($"{Lists}/{pl27}/lines");
        Assert.Equal([198m, 330m], pl27Lines!.OrderBy(line => line.Price).Select(line => line.Price));
        Assert.False((await api.GetFromJsonAsync<BasePriceListDetail>($"{Lists}/{pl27}"))!.IsActive);

        // C1: formula (shape and rule refusals first), C2 Apply
        var today = DateOnly.FromDateTime(factory.Clock.UtcNow.UtcDateTime);
        var tooBig = await api.PutAsJsonAsync($"{Contracts}/{goldId}", new SetContractFormulaRequest(today.AddDays(-1), null, [new PricingFormulaTermRequest(pl26, 11m, 0m)]));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        var twice = await api.PutAsJsonAsync($"{Contracts}/{goldId}", new SetContractFormulaRequest(today.AddDays(-1), null, [new PricingFormulaTermRequest(pl26, 1m, 0m), new PricingFormulaTermRequest(pl26, 0m, 1m)]));
        Assert.Equal(PricingCodes.FormulaDuplicateList, await AuthClient.CodeOf(twice));
        Assert.Equal(PricingCodes.ContractPricingNotFound, await AuthClient.CodeOf(await api.PostAsync($"{Contracts}/{goldId}/apply", null)));

        var formula = await api.PutAsJsonAsync($"{Contracts}/{goldId}", new SetContractFormulaRequest(today.AddDays(-1), null, [new PricingFormulaTermRequest(pl26, 0.1m, 0.9m)]));
        Assert.Equal(HttpStatusCode.NoContent, formula.StatusCode);
        var applied = await (await api.PostAsync($"{Contracts}/{goldId}/apply", null)).Content.ReadFromJsonAsync<ContractApplyResult>();
        Assert.Equal((2, 2), (applied!.Priced, applied.Added));

        // C3 negotiate S1, C5 alert on S2, L6 Apply the list: the negotiated price survives (BR-07)
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{goldId}/prices/{S1}", new NegotiatePriceRequest(15m, 80m))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsJsonAsync($"{Contracts}/{goldId}/prices/{S2}/alert", new SetPriceAlertRequest("Prior approval required"))).StatusCode);
        var unpricedAlert = await api.PutAsJsonAsync($"{Contracts}/{goldId}/prices/{Guid.CreateVersion7()}/alert", new SetPriceAlertRequest("x"));
        Assert.Equal(PricingCodes.ServiceNotPriced, await AuthClient.CodeOf(unpricedAlert));
        var listApply = await (await api.PostAsync($"{Lists}/{pl26}/apply", null)).Content.ReadFromJsonAsync<ApplyBasePriceListResult>();
        Assert.Equal(1, listApply!.ContractsApplied);

        var gold = await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{goldId}");
        Assert.True(gold!.Exists && gold.InEffect);
        var s1 = gold.Prices.Single(price => price.ServiceId == S1);
        Assert.Equal((true, 15m, 80m), (s1.IsExcluded, s1.PatientShare, s1.PayerShare));
        var s2 = gold.Prices.Single(price => price.ServiceId == S2);
        Assert.Equal((300m, 30m, 270m, "Prior approval required", "PL26"), (s2.ListPrice, s2.PatientShare, s2.PayerShare, s2.AlertCondition, s2.SourceListCode));

        // CP-12: the reception clerk quotes the tier and the cash payer; an unknown service is not priced
        var unknown = Guid.CreateVersion7();
        var tierQuote = await reception.GetFromJsonAsync<PriceQuote>($"{Quote}?tierId={goldId}&serviceIds={S1}&serviceIds={S2}&serviceIds={unknown}");
        Assert.Equal((axaId, goldId, 3), (tierQuote!.PayerId, tierQuote.ContractTierId, tierQuote.Lines.Count));
        Assert.Equal((45m, 350m), (tierQuote.TotalPatientShare, tierQuote.TotalPayerShare));
        Assert.False(tierQuote.Lines.Single(line => line.ServiceId == unknown).Priced);
        Assert.Equal("Prior approval required", tierQuote.Lines.Single(line => line.ServiceId == S2).AlertCondition);

        var cashQuote = await reception.GetFromJsonAsync<PriceQuote>($"{Quote}?serviceIds={S1}");
        Assert.Equal((cash.PayerId, cash.ContractTierId), (cashQuote!.PayerId, cashQuote.ContractTierId));
        var cashLine = Assert.Single(cashQuote.Lines);
        Assert.Equal((180m, 180m, 0m, true), (cashLine.ListPrice, cashLine.PatientShare, cashLine.PayerShare, cashLine.Priced));

        var mismatch = await reception.GetAsync($"{Quote}?payerId={cash.PayerId}&tierId={goldId}&serviceIds={S1}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal(PricingCodes.TierPayerMismatch, await AuthClient.CodeOf(mismatch));
        var axaTwoTiers = await reception.GetAsync($"{Quote}?payerId={axaId}&serviceIds={S1}");
        Assert.Equal(PricingCodes.TierRequired, await AuthClient.CodeOf(axaTwoTiers));

        var clerkWrites = await reception.PostAsJsonAsync(Payers, new CreatePayerRequest("X", "س", "X", "Credit", false, null));
        Assert.Equal(HttpStatusCode.Forbidden, clerkWrites.StatusCode);

        var review = await reception.GetFromJsonAsync<ContractPricingReviewDocument>($"{Contracts}/{goldId}/review");
        Assert.Equal(("Reception Clerk", "AXA", 2), (review!.PrintedBy, review.PayerSearchKey, review.Prices.Count));

        // C4 restore S1 from the formula; C6 copy gold onto silver at 50 %; a tier onto itself is refused
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"{Contracts}/{goldId}/prices/{S1}/exclusion")).StatusCode);
        var restored = (await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{goldId}"))!.Prices.Single(price => price.ServiceId == S1);
        Assert.Equal((false, 18m, 162m), (restored.IsExcluded, restored.PatientShare, restored.PayerShare));

        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsJsonAsync($"{Contracts}/{silverId}/copy-from/{goldId}", new CopyContractPricingRequest(0.5m))).StatusCode);
        var silver = await api.GetFromJsonAsync<ContractPricingDetail>($"{Contracts}/{silverId}");
        Assert.Equal((0.05m, 0.45m), (silver!.Terms[0].PatientFactor, silver.Terms[0].PayerFactor));
        Assert.Equal(15m, silver.Prices.Single(price => price.ServiceId == S2).PatientShare);
        var self = await api.PostAsync($"{Contracts}/{goldId}/copy-from/{goldId}", null);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(PricingCodes.CopySameTier, await AuthClient.CodeOf(self));

        var options = await api.GetFromJsonAsync<List<TierOption>>($"{Payers}/tiers?activeOnly=true");
        Assert.Contains(options!, option => option.Id == silverId && option.HasPricing);
        var registers = await api.GetFromJsonAsync<Paged<BasePriceListItem>>($"{Lists}?search=pl2");
        Assert.Equal(2, registers!.TotalCount);
    }

    [DatabaseFact]
    public async Task A_user_without_the_permission_is_refused_on_every_surface()
    {
        var tenant = Guid.CreateVersion7();
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions);
        var client = await SignedInClientAsync(viewer);

        foreach (var response in new[]
                 {
                     await client.PostAsJsonAsync(Lists, new CreateBasePriceListRequest("PLX", "X", "س", 2026)),
                     await client.PutAsJsonAsync($"{Contracts}/{Guid.CreateVersion7()}", new SetContractFormulaRequest(new DateOnly(2026, 1, 1), null, [])),
                     await client.PostAsync($"{Contracts}/{Guid.CreateVersion7()}/apply", null),
                     await client.PostAsync($"{Lists}/{Guid.CreateVersion7()}/activate", null),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(response));
        }

        var unknownTier = await client.GetAsync($"{Contracts}/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, unknownTier.StatusCode);
        Assert.Equal(PricingCodes.TierNotFound, await AuthClient.CodeOf(unknownTier));
        var badType = await client.GetAsync($"{Payers}?type=gold");
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
    }

    private async Task SeedLinesAsync(Guid tenantId, Guid listId, params (Guid ServiceId, decimal Price)[] lines)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContextAccessor>().SetTenant(tenantId);
        var list = (await scope.ServiceProvider.GetRequiredService<IBasePriceListRepository>().GetForUpdateAsync(listId, CancellationToken.None))!;
        list.UpsertLines(lines.Select(line => new PriceLineInput(line.ServiceId, line.Price, true)).ToList(), factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
