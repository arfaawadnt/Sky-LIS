using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Contracts.Settings;
using Platform.Contracts.Treasury;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// WF-09 over the real API (intake LIS-INT-BIL-001 §16): a cashier with an approved treasury on their device collects
/// partial and full payments, the refusals arrive as their codes, a supervisor voids, refunds a negative balance and
/// closes the drawer; permissions decide who may call what (D-04).
/// </summary>
public sealed class BillingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Payments = "/api/v1/billing/payments";
    private const string Refunds = "/api/v1/billing/refunds";
    private const string Closings = "/api/v1/billing/cash-closings";
    private const string Device = "device-cash-desk-1";

    private static readonly string[] CashierPermissions = ["billing.view", "billing.create", "treasury.view", "treasury.create", "treasury.manage"];
    private static readonly string[] SupervisorPermissions = ["billing.view", "billing.approve", "billing.cancel", "billing.manage", "billing.export", "treasury.view", "treasury.create"];
    private static readonly string[] ManagerPermissions = ["treasury.view", "treasury.approve"];

    // The cashier may take money and print invoices (CP-37 special rights); refunds and voids belong to the supervisor.
    private static readonly SpecialRights CashierRights = new(null, canPay: true, canRefund: false, canTransfer: false, canCancel: false, canModifyVerifiedResult: false, canPreviewWithoutVerify: false, canPrintInvoice: true, canSeeVip: false);

    [DatabaseFact]
    public async Task A_cashier_collects_partial_and_full_payments_and_every_refusal_has_its_code()
    {
        var desk = await DeskAsync();
        var (visitId, accession) = await InsertVisitAsync(desk.Tenant, 300m);

        var mine = await desk.Cashier.GetFromJsonAsync<List<BillingTreasuryItem>>($"/api/v1/billing/my-treasuries?deviceId={Device}");
        Assert.Equal([desk.TreasuryId], mine!.Select(item => item.Id).ToArray());
        Assert.Empty((await desk.Cashier.GetFromJsonAsync<List<BillingTreasuryItem>>("/api/v1/billing/my-treasuries?deviceId=unknown-device"))!);

        var account = await desk.Cashier.GetFromJsonAsync<VisitAccountView>($"/api/v1/billing/visits/by-accession/{accession}/account");
        Assert.Equal((visitId, 300m, "Unpaid"), (account!.VisitId, account.Remaining, account.BalanceStatus));

        var cash = await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 100m, "Cash", null, desk.TreasuryId, Device));
        Assert.Equal(HttpStatusCode.Created, cash.StatusCode);
        var receipt = (await cash.Content.ReadFromJsonAsync<PaymentCreated>())!;
        Assert.StartsWith("RCT-", receipt.ReceiptNumber, StringComparison.Ordinal);

        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 50m, "Card", null, desk.TreasuryId, Device)), HttpStatusCode.UnprocessableEntity, "BIL-003");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 250m, "Card", "SLIP-9", desk.TreasuryId, Device)), HttpStatusCode.Conflict, "BIL-010");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 10m, "Cash", null, desk.TreasuryId, "unknown-device")), HttpStatusCode.Forbidden, "BIL-020");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 10m, "Barter", null, desk.TreasuryId, Device)), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(Guid.CreateVersion7(), 10m, "Cash", null, desk.TreasuryId, Device)), HttpStatusCode.NotFound, "BIL-404");
        await ExpectAsync(desk.Outsider.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 10m, "Cash", null, desk.TreasuryId, Device)), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(desk.NoRight.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 10m, "Cash", null, desk.TreasuryId, Device)), HttpStatusCode.Forbidden, "BIL-040");

        var card = await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 200m, "Card", "SLIP-1", desk.TreasuryId, Device));
        Assert.Equal(HttpStatusCode.Created, card.StatusCode);

        var settled = await desk.Cashier.GetFromJsonAsync<VisitAccountView>($"/api/v1/billing/visits/{visitId}/account");
        Assert.Equal((300m, 0m, "Paid", 2), (settled!.Paid, settled.Remaining, settled.BalanceStatus, settled.Payments.Count));

        var document = await desk.Cashier.GetFromJsonAsync<PaymentReceiptDocument>($"{Payments}/{receipt.Id}/documents/receipt");
        Assert.Equal((receipt.ReceiptNumber, accession, 100m, "Cash", "Posted"), (document!.ReceiptNumber, document.Accession, document.Amount, document.Method, document.Status));
        Assert.False(string.IsNullOrWhiteSpace(document.Letterhead.LabNameEn));

        var invoice = await desk.Cashier.GetFromJsonAsync<InvoiceDocument>($"/api/v1/billing/visits/{visitId}/documents/invoice");
        Assert.Equal((300m, 0m, 2), (invoice!.Paid, invoice.Remaining, invoice.Payments.Count));

        var page = await desk.Cashier.GetFromJsonAsync<Paged<PaymentListItem>>($"{Payments}?accession={accession}&method=card");
        var listed = Assert.Single(page!.Items);
        Assert.Equal((accession, "SLIP-1", "Cashier One", false), (listed.Accession, listed.Reference, listed.CashierName, listed.IsFrozen));

        // T2: the supervisor voids with a reason; twice is a state conflict; an empty reason is a shape violation.
        await ExpectAsync(desk.Cashier.PostAsJsonAsync($"{Payments}/{receipt.Id}/void", new VoidPaymentRequest("mistake")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Payments}/{receipt.Id}/void", new VoidPaymentRequest("")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        Assert.Equal(HttpStatusCode.NoContent, (await desk.Supervisor.PostAsJsonAsync($"{Payments}/{receipt.Id}/void", new VoidPaymentRequest("wrong visit"))).StatusCode);
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Payments}/{receipt.Id}/void", new VoidPaymentRequest("again")), HttpStatusCode.Conflict, "BIL-012");
        Assert.Equal((100m, "Partial"), await RemainingAsync(desk.Cashier, visitId));

        // CSV export needs billing.export.
        var export = await desk.Supervisor.GetAsync($"{Payments}/export?visitId={visitId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        Assert.Contains("wrong visit", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await ExpectAsync(desk.Cashier.GetAsync($"{Payments}/export"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
    }

    [DatabaseFact]
    public async Task A_negative_balance_is_refunded_and_the_drawer_is_closed_and_frozen()
    {
        var desk = await DeskAsync();
        var (visitId, _) = await InsertVisitAsync(desk.Tenant, 100m);
        var paid = (await (await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 100m, "Cash", null, desk.TreasuryId, Device))).Content.ReadFromJsonAsync<PaymentCreated>())!;
        await SetShareAsync(desk.Tenant, visitId, 60m);   // a contract transfer re-priced the visit (VisitTransferred)
        var reason = await SeedRefundReasonAsync(desk.Tenant);

        var due = await desk.Supervisor.GetFromJsonAsync<VisitAccountView>($"/api/v1/billing/visits/{visitId}/account");
        Assert.Equal((-40m, 40m, "RefundDue"), (due!.Remaining, due.Refundable, due.BalanceStatus));

        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Refunds, new RecordRefundRequest(visitId, 40m, "Cash", null, reason, null, desk.TreasuryId, Device)), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync(Refunds, new RecordRefundRequest(visitId, 41m, "Cash", null, reason, null, desk.TreasuryId, Device)), HttpStatusCode.Conflict, "BIL-011");
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync(Refunds, new RecordRefundRequest(visitId, 40m, "Cash", null, Guid.CreateVersion7(), null, desk.TreasuryId, Device)), HttpStatusCode.UnprocessableEntity, "BIL-016");

        var refunded = await desk.Supervisor.PostAsJsonAsync(Refunds, new RecordRefundRequest(visitId, 40m, "Cash", null, reason, "transfer to contract", desk.TreasuryId, Device));
        Assert.Equal(HttpStatusCode.Created, refunded.StatusCode);
        var refund = (await refunded.Content.ReadFromJsonAsync<RefundCreated>())!;
        Assert.StartsWith("RFD-", refund.Number, StringComparison.Ordinal);
        var refundReceipt = await desk.Supervisor.GetFromJsonAsync<RefundReceiptDocument>($"{Refunds}/{refund.Id}/documents/receipt");
        Assert.Equal((40m, "Overpayment", "transfer to contract"), (refundReceipt!.Amount, refundReceipt.ReasonNameEn, refundReceipt.Note));
        Assert.Equal((0m, "Paid"), await RemainingAsync(desk.Supervisor, visitId));

        // T5: preview, a difference without a note, the close, the overlap, the frozen payment.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var from = factory.Clock.UtcNow.AddHours(-2);
        var to = factory.Clock.UtcNow;
        var preview = await desk.Supervisor.GetFromJsonAsync<CashClosingPreview>($"{Closings}/preview?treasuryId={desk.TreasuryId}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        Assert.Equal((100m, 40m, 60m, 2, false), (preview!.Totals.CashPayments, preview.Totals.CashRefunds, preview.Totals.ExpectedCash, preview.Movements.Count, preview.OverlapsClosedPeriod));

        await ExpectAsync(desk.Supervisor.PostAsJsonAsync(Closings, new CloseCashDrawerRequest(desk.TreasuryId, null, from, to, 55m, null)), HttpStatusCode.UnprocessableEntity, "BIL-034");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Closings, new CloseCashDrawerRequest(desk.TreasuryId, null, from, to, 60m, null)), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        var closed = await desk.Supervisor.PostAsJsonAsync(Closings, new CloseCashDrawerRequest(desk.TreasuryId, null, from, to, 55m, "5 short, reported"));
        Assert.Equal(HttpStatusCode.Created, closed.StatusCode);
        var closing = (await closed.Content.ReadFromJsonAsync<CashClosingCreated>())!;
        Assert.StartsWith("CLS-", closing.Number, StringComparison.Ordinal);

        await ExpectAsync(desk.Supervisor.PostAsJsonAsync(Closings, new CloseCashDrawerRequest(desk.TreasuryId, desk.CashierId, from.AddMinutes(30), to, 0m, "x")), HttpStatusCode.Conflict, "BIL-120");
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Payments}/{paid.Id}/void", new VoidPaymentRequest("too late")), HttpStatusCode.Conflict, "BIL-013");

        var sheet = await desk.Supervisor.GetFromJsonAsync<CashClosingSheetDocument>($"{Closings}/{closing.Id}/documents/sheet");
        Assert.Equal((60m, 55m, -5m, "5 short, reported", 2), (sheet!.Totals.ExpectedCash, sheet.CountedCash, sheet.Difference, sheet.Note, sheet.Movements.Count));
        var list = await desk.Supervisor.GetFromJsonAsync<Paged<CashClosingListItem>>($"{Closings}?treasuryId={desk.TreasuryId}");
        Assert.Equal(closing.Number, Assert.Single(list!.Items).Number);
    }

    [DatabaseFact]
    public async Task The_cash_desk_reads_the_receipt_print_parameter_without_settings_rights()
    {
        // UAT A-05: a cashier holds billing.view but not settings.view; the desk reads autoPrintReceipt through billing.
        var desk = await DeskAsync();
        await ExpectAsync(desk.Cashier.GetAsync("/api/v1/settings"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        var defaults = await desk.Cashier.GetFromJsonAsync<BillingPrintSettings>("/api/v1/billing/print-settings");
        Assert.True(defaults!.AutoPrintReceipt);   // the code default of CP-57

        var (adminName, _, _) = await factory.SeedUserAsync(tenantId: desk.Tenant, permissions: ["settings.view", "settings.manage"], displayName: "Lab Admin", branchIds: [desk.Branch]);
        var admin = await SignedInClientAsync(adminName);
        var saved = await admin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("autoPrintReceipt", "false")]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var off = await desk.Cashier.GetFromJsonAsync<BillingPrintSettings>("/api/v1/billing/print-settings");
        Assert.False(off!.AutoPrintReceipt);
        await ExpectAsync(desk.Outsider.GetAsync("/api/v1/billing/print-settings"), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
    }

    // ---- desk -------------------------------------------------------------------------------------------------

    private sealed record Desk(Guid Tenant, Guid Branch, Guid TreasuryId, Guid CashierId, HttpClient Cashier, HttpClient Supervisor, HttpClient Outsider, HttpClient NoRight);

    /// <summary>A branch with a treasury on which the cashier and the supervisor are both approved for <see cref="Device"/> (BR-27).</summary>
    private async Task<Desk> DeskAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (cashierName, _, cashierId) = await factory.SeedUserAsync(tenantId: tenant, permissions: CashierPermissions, displayName: "Cashier One", branchIds: [branch]);
        var (supervisorName, _, supervisorId) = await factory.SeedUserAsync(tenantId: tenant, permissions: SupervisorPermissions, displayName: "Shift Supervisor", branchIds: [branch]);
        await GrantAsync(tenant, cashierId, CashierRights);   // CP-37 special rights, before sign-in (the principal is cached)
        await GrantAsync(tenant, supervisorId, SpecialRights.All);
        var (managerName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ManagerPermissions, displayName: "Branch Manager", branchIds: [branch]);
        var (outsiderName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null, displayName: "No Role", branchIds: [branch]);
        var (noRightName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: CashierPermissions, displayName: "Cashier Without Rights", branchIds: [branch]);
        var cashier = await SignedInClientAsync(cashierName);
        var supervisor = await SignedInClientAsync(supervisorName);
        var manager = await SignedInClientAsync(managerName);
        var outsider = await SignedInClientAsync(outsiderName);
        var noRight = await SignedInClientAsync(noRightName);

        var created = await cashier.PostAsJsonAsync("/api/v1/treasuries", new CreateTreasuryRequest("DESK1", "Cash desk 1", "خزينة 1", branch));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var treasuryId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        foreach (var client in new[] { cashier, supervisor })
        {
            var requested = await client.PostAsJsonAsync("/api/v1/treasury-access-requests", new RequestTreasuryAccessRequest(treasuryId, Device, "Cash desk PC"));
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            var requestId = (await requested.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
            Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"/api/v1/treasury-access-requests/{requestId}/approve", null)).StatusCode);
        }

        return new Desk(tenant, branch, treasuryId, cashierId, cashier, supervisor, outsider, noRight);
    }

    private static async Task ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await AuthClient.CodeOf(response));
    }

    private static async Task<(decimal Remaining, string Status)> RemainingAsync(HttpClient client, Guid visitId)
    {
        var account = (await client.GetFromJsonAsync<VisitAccountView>($"/api/v1/billing/visits/{visitId}/account"))!;
        return (account.Remaining, account.BalanceStatus);
    }

    /// <summary>A walk-in visit row of Registration: raw SQL with the MODULES.md §5 columns and the NOT NULL columns of app.visits.</summary>
    private async Task<(Guid VisitId, string Accession)> InsertVisitAsync(Guid tenant, decimal patientShare)
    {
        var id = Guid.CreateVersion7();
        var accession = $"ACC-FT-{id.ToString("N")[^8..].ToUpperInvariant()}";
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

    private async Task SetShareAsync(Guid tenant, Guid visitId, decimal patientShare)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE app.visits SET patient_share_total = {0} WHERE tenant_id = {1} AND id = {2}", patientShare, tenant, visitId);
    }

    private async Task<Guid> SeedRefundReasonAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var reason = Reason.Create(ReasonKind.Refund, new LocalizedText("Overpayment", "مبلغ زائد"), factory.Clock.UtcNow);
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Reason>().Add(reason);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return reason.Id;
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

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
