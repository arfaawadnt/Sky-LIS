using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The payment methods set-up list over the real API (change request 2026-10-19, LIS-CR-003 P-C10; intake
/// LIS-INT-BIL-001 §17): the defaults, the CRUD of CP-30b with its refusal codes, the pick list every cashier reads, the
/// CSV, and a payment that names a method code: the definition decides the kind and the reference rule, and the receipt,
/// the register and the invoice show the method's name.
/// </summary>
public sealed class PaymentMethodsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Methods = "/api/v1/billing/payment-methods";
    private const string Payments = "/api/v1/billing/payments";

    private static readonly string[] ManagerPermissions = ["billing.view", "billing.manage", "billing.export"];
    private static readonly string[] ViewerPermissions = ["billing.view"];
    private static readonly string[] CashierPermissions = ["billing.view", "billing.create"];

    [DatabaseFact]
    public async Task A_laboratory_seeds_the_defaults_defines_its_own_methods_and_every_refusal_has_its_code()
    {
        var tenant = Guid.CreateVersion7();
        var manager = await SignedInClientAsync(tenant, ManagerPermissions);
        var viewer = await SignedInClientAsync(tenant, ViewerPermissions);
        var outsider = await SignedInClientAsync(tenant, null);

        // A new tenant that was not provisioned through the platform has no methods yet: the set-up page asks for the defaults.
        var empty = await viewer.GetFromJsonAsync<Paged<PaymentMethodListItem>>(Methods);
        Assert.Empty(empty!.Items);
        await ExpectAsync(viewer.PostAsync($"{Methods}/defaults", null), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(outsider.GetAsync(Methods), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        // T14: the defaults, idempotent.
        var seeded = await manager.PostAsync($"{Methods}/defaults", null);
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        Assert.Equal(4, (await seeded.Content.ReadFromJsonAsync<PaymentMethodDefaultsResult>())!.Created);
        Assert.Equal(0, (await (await manager.PostAsync($"{Methods}/defaults", null)).Content.ReadFromJsonAsync<PaymentMethodDefaultsResult>())!.Created);

        // The pick list is read with billing.view (the reception role holds it to pay from the registration page).
        var options = (await viewer.GetFromJsonAsync<List<PaymentMethodOption>>($"{Methods}/options"))!;
        Assert.Equal(["CASH", "CARD", "POS", "TRANSFER"], options.Select(option => option.Code).ToArray());
        Assert.Equal([false, true, true, true], options.Select(option => option.RequiresReference).ToArray());
        Assert.Equal(("Cash", "نقدي", "Cash"), (options[0].NameEn, options[0].NameAr, options[0].Kind));
        await ExpectAsync(outsider.GetAsync($"{Methods}/options"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        // T10: define; the code is upper-cased and goes after the last option; duplicates and shapes are refused.
        await ExpectAsync(viewer.PostAsJsonAsync(Methods, new PaymentMethodRequest("wallet", "Wallet", "محفظة", "Card", false, null)), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        var created = await manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("wallet", "Wallet", "محفظة", "Card", false, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var walletId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var wallet = await viewer.GetFromJsonAsync<PaymentMethodListItem>($"{Methods}/{walletId}");
        Assert.Equal(("WALLET", "Wallet", "Card", false, true, 5), (wallet!.Code, wallet.NameEn, wallet.Kind, wallet.RequiresReference, wallet.IsActive, wallet.Sequence));

        await ExpectAsync(manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("WALLET", "Wallet 2", "محفظة 2", "Card", true, null)), HttpStatusCode.Conflict, "BIL-121");
        await ExpectAsync(manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("CHEQUE", "Cheque", "شيك", "Barter", true, null)), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("", "Cheque", "شيك", "Transfer", true, null)), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("CHQ 1", "Cheque", "شيك", "Transfer", true, null)), HttpStatusCode.UnprocessableEntity, "BIL-051");
        await ExpectAsync(manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("CHEQUE", "Cheque", "شيك", "Transfer", true, 10000)), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);

        // T11: update everything but the code.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"{Methods}/{walletId}", new UpdatePaymentMethodRequest("E-wallet", "محفظة إلكترونية", "Transfer", true, 2))).StatusCode);
        var updated = await viewer.GetFromJsonAsync<PaymentMethodListItem>($"{Methods}/{walletId}");
        Assert.Equal(("WALLET", "E-wallet", "محفظة إلكترونية", "Transfer", true, 2), (updated!.Code, updated.NameEn, updated.NameAr, updated.Kind, updated.RequiresReference, updated.Sequence));
        await ExpectAsync(viewer.PutAsJsonAsync($"{Methods}/{walletId}", new UpdatePaymentMethodRequest("X", "س", "Card", false, 1)), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(manager.PutAsJsonAsync($"{Methods}/{walletId}", new UpdatePaymentMethodRequest("", "س", "Card", false, 1)), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(manager.PutAsJsonAsync($"{Methods}/{Guid.CreateVersion7()}", new UpdatePaymentMethodRequest("X", "س", "Card", false, 1)), HttpStatusCode.NotFound, "BIL-410");
        await ExpectAsync(viewer.GetAsync($"{Methods}/{Guid.CreateVersion7()}"), HttpStatusCode.NotFound, "BIL-410");

        // The sort allow-list and the search (code and both names) of the set-up list.
        var sorted = await viewer.GetFromJsonAsync<Paged<PaymentMethodListItem>>($"{Methods}?sort=sequence&direction=asc");
        Assert.Equal(["CASH", "CARD", "WALLET", "POS", "TRANSFER"], sorted!.Items.Select(item => item.Code).ToArray());
        var found = await viewer.GetFromJsonAsync<Paged<PaymentMethodListItem>>($"{Methods}?search=%D9%85%D8%AD%D9%81%D8%B8%D8%A9");   // "محفظة"
        Assert.Equal("WALLET", Assert.Single(found!.Items).Code);
        await ExpectAsync(viewer.GetAsync($"{Methods}?sort=colour"), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);

        // T12 / T13: the last active cash method stays (BIL-052); a second one frees it; the pick list follows.
        var cashId = sorted.Items.Single(item => item.Code == "CASH").Id;
        await ExpectAsync(manager.PostAsync($"{Methods}/{cashId}/deactivate", null), HttpStatusCode.Conflict, "BIL-052");
        await ExpectAsync(manager.PutAsJsonAsync($"{Methods}/{cashId}", new UpdatePaymentMethodRequest("Cash", "نقدي", "Card", true, 1)), HttpStatusCode.Conflict, "BIL-052");
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("PETTY", "Petty cash", "نثرية", "Cash", false, 9))).StatusCode);
        await ExpectAsync(viewer.PostAsync($"{Methods}/{cashId}/deactivate", null), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"{Methods}/{cashId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"{Methods}/{cashId}/deactivate", null)).StatusCode);   // idempotent
        Assert.DoesNotContain("CASH", (await viewer.GetFromJsonAsync<List<PaymentMethodOption>>($"{Methods}/options"))!.Select(option => option.Code));
        var inactive = await viewer.GetFromJsonAsync<Paged<PaymentMethodListItem>>($"{Methods}?isActive=false");
        Assert.Equal("CASH", Assert.Single(inactive!.Items).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"{Methods}/{cashId}/reactivate", null)).StatusCode);
        Assert.Contains("CASH", (await viewer.GetFromJsonAsync<List<PaymentMethodOption>>($"{Methods}/options"))!.Select(option => option.Code));
        await ExpectAsync(manager.PostAsync($"{Methods}/{Guid.CreateVersion7()}/reactivate", null), HttpStatusCode.NotFound, "BIL-410");

        // CSV needs billing.export and follows the grid's filter.
        var export = await manager.GetAsync($"{Methods}/export?search=wallet");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("WALLET", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("PETTY", csv, StringComparison.Ordinal);
        await ExpectAsync(viewer.GetAsync($"{Methods}/export"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
    }

    [DatabaseFact]
    public async Task A_payment_with_a_method_code_takes_the_definition_s_kind_and_reference_rule_and_the_documents_name_the_method()
    {
        var desk = await DeskAsync();
        var (visitId, accession) = await InsertVisitAsync(desk.Tenant, 300m);
        Assert.Equal(HttpStatusCode.OK, (await desk.Manager.PostAsync($"{Methods}/defaults", null)).StatusCode);
        var walletId = (await (await desk.Manager.PostAsJsonAsync(Methods, new PaymentMethodRequest("WALLET", "Wallet", "محفظة", "Card", false, null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        // The definition's kind (Card) replaces the request's kind and its rule needs no reference.
        var wallet = await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 100m, "Cash", null, "wallet"));
        Assert.Equal(HttpStatusCode.Created, wallet.StatusCode);
        var receipt = (await wallet.Content.ReadFromJsonAsync<PaymentCreated>())!;

        // CARD requires a reference (BIL-003); an unknown code is refused (BIL-053); so is a deactivated one.
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", null, "CARD")), HttpStatusCode.UnprocessableEntity, "BIL-003");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", "SLIP-1", "CHEQUE")), HttpStatusCode.UnprocessableEntity, "BIL-053");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", "SLIP-1", new string('X', 21))), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        Assert.Equal(HttpStatusCode.NoContent, (await desk.Manager.PostAsync($"{Methods}/{walletId}/deactivate", null)).StatusCode);
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", null, "WALLET")), HttpStatusCode.UnprocessableEntity, "BIL-053");

        // Without a code the legacy rule still applies (an older client): every kind but cash needs a reference.
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", null)), HttpStatusCode.UnprocessableEntity, "BIL-003");
        Assert.Equal(HttpStatusCode.Created, (await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", "SLIP-2", "CARD"))).StatusCode);

        // The receipt, the register, the account and the invoice carry the method's code and names; a deactivated definition still names history.
        var document = await desk.Cashier.GetFromJsonAsync<PaymentReceiptDocument>($"{Payments}/{receipt.Id}/documents/receipt");
        Assert.Equal(("Card", "WALLET", "Wallet", "محفظة", 100m), (document!.Method, document.MethodCode, document.MethodNameEn, document.MethodNameAr, document.Amount));

        var register = await desk.Cashier.GetFromJsonAsync<Paged<PaymentListItem>>($"{Payments}?accession={accession}&method=card");
        Assert.Equal(2, register!.Items.Count);
        var listed = register.Items.Single(item => item.ReceiptNumber == receipt.ReceiptNumber);
        Assert.Equal(("WALLET", "Wallet", "محفظة", null), (listed.MethodCode, listed.MethodNameEn, listed.MethodNameAr, listed.Reference));
        Assert.Equal(("CARD", "Card", "SLIP-2"), register.Items.Single(item => item.ReceiptNumber != receipt.ReceiptNumber) is { } card ? (card.MethodCode, card.MethodNameEn, card.Reference) : default);

        var account = await desk.Cashier.GetFromJsonAsync<VisitAccountView>($"/api/v1/billing/visits/{visitId}/account");
        Assert.Equal(["WALLET", "CARD"], account!.Payments.OrderBy(payment => payment.PaidAtUtc).ThenBy(payment => payment.ReceiptNumber).Select(payment => payment.MethodCode ?? "").ToArray());

        var invoice = await desk.Cashier.GetFromJsonAsync<InvoiceDocument>($"/api/v1/billing/visits/{visitId}/documents/invoice");
        Assert.Contains(invoice!.Payments, movement => movement.MethodCode == "WALLET" && movement.MethodNameEn == "Wallet" && movement.Method == "Card");

        var export = await desk.Manager.GetAsync($"{Payments}/export?visitId={visitId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("WALLET", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed record Desk(Guid Tenant, HttpClient Cashier, HttpClient Manager);

    /// <summary>A branch with a cashier signed in on it and a billing manager who defines the methods (no treasury since LIS-CR-004).</summary>
    private async Task<Desk> DeskAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (cashierName, _, cashierId) = await factory.SeedUserAsync(tenantId: tenant, permissions: CashierPermissions, displayName: "Cashier One", branchIds: [branch]);
        await GrantAsync(tenant, cashierId, new SpecialRights(null, canPay: true, canRefund: false, canTransfer: false, canCancel: false, canModifyVerifiedResult: false, canPreviewWithoutVerify: false, canPrintInvoice: true, canSeeVip: false));
        var cashier = await SignedInClientAsync(cashierName);
        var manager = await SignedInClientAsync(tenant, ManagerPermissions, branch);

        return new Desk(tenant, cashier, manager);
    }

    private static async Task ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await AuthClient.CodeOf(response));
    }

    private async Task<(Guid VisitId, string Accession)> InsertVisitAsync(Guid tenant, decimal patientShare)
    {
        var id = Guid.CreateVersion7();
        var accession = $"ACC-PM-{id.ToString("N")[^8..].ToUpperInvariant()}";
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            """
            INSERT INTO app.visits (id, tenant_id, accession, patient_id, branch_id, location_type, payer_id, contract_tier_id, batch_id, urgent, biohazard,
                                    collected_outside, utc_offset_minutes, status, registered_at_utc, registered_by_user_id, total_list_price, discount_total,
                                    patient_share_total, payer_share_total, discount_value, revision, created_at_utc)
            VALUES ({0}, {1}, {2}, {3}, {4}, 'Walkin', {8}, {9}, NULL, false, false, false, 0, 'Open', {5}, {6}, {7}, 0, {7}, 0, 0, 1, {5})
            """,
            id, tenant, accession, Guid.CreateVersion7(), Guid.CreateVersion7(), factory.Clock.UtcNow, Guid.CreateVersion7(), patientShare, Guid.CreateVersion7(), Guid.CreateVersion7());
        return (id, accession);
    }

    /// <summary>Gives the role of the user's membership the special rights of CP-37 (SeedUserAsync creates roles without any).</summary>
    private async Task GrantAsync(Guid tenant, Guid userId, SpecialRights rights)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleId = await context.Set<TenantMembership>().Where(membership => membership.PlatformUserId == userId).Select(membership => membership.RoleId).SingleAsync();
        var role = await context.Set<Role>().SingleAsync(candidate => candidate.Id == roleId);
        role.SetSpecialRights(rights, factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<HttpClient> SignedInClientAsync(Guid tenant, IReadOnlyList<string>? permissions, Guid? branch = null)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, branchIds: branch is { } id ? [id] : null);
        return await SignedInClientAsync(username);
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
