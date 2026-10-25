using System.Globalization;
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
/// The cash ledger over the real API (CP-70, change request LIS-CR-011 point C-15; intake LIS-INT-BIL-001 §18): a cashier posts
/// cash in and out, reads the own ledger and its running balance; a supervisor voids an entry, reads one cashier of the branch,
/// closes the day from the ledger's close window, and the closed period becomes read-only (BIL-061).
/// </summary>
public sealed class CashLedgerFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Entries = "/api/v1/billing/cash-entries";
    private const string Ledger = "/api/v1/billing/cash-ledger";
    private const string Payments = "/api/v1/billing/payments";
    private const string Closings = "/api/v1/billing/cash-closings";

    private static readonly string[] CashierPermissions = ["billing.view", "billing.create"];
    private static readonly string[] SupervisorPermissions = ["billing.view", "billing.create", "billing.approve", "billing.cancel", "billing.manage", "billing.export"];

    private static readonly SpecialRights CashierRights = new(null, canPay: true, canRefund: false, canTransfer: false, canCancel: false, canModifyVerifiedResult: false, canPreviewWithoutVerify: false, canPrintInvoice: true, canSeeVip: false);

    [DatabaseFact]
    public async Task A_cashier_posts_cash_in_and_out_and_reads_the_own_ledger_with_its_running_balance()
    {
        var desk = await DeskAsync();
        var visitId = await InsertVisitAsync(desk.Tenant, 300m);
        Assert.Equal(HttpStatusCode.Created, (await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 100m, "Cash", null))).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));   // the ledger is in time order

        var floatIn = await desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashIn", "OpeningFloat", 200m, "Morning float"));
        Assert.Equal(HttpStatusCode.Created, floatIn.StatusCode);
        var created = (await floatIn.Content.ReadFromJsonAsync<CashEntryCreated>())!;
        Assert.StartsWith("CSH-", created.Number, StringComparison.Ordinal);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Created, (await desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("cashOut", "supplier", 50m, "Courier", Note: "invoice 12"))).StatusCode);

        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashIn", "Supplier", 5m, "x")), HttpStatusCode.UnprocessableEntity, "BIL-062");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashOut", "Other", 5m, "  ")), HttpStatusCode.UnprocessableEntity, "BIL-060");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashOut", "Other", 5m, "x", Method: "Card")), HttpStatusCode.UnprocessableEntity, "BIL-003");
        await ExpectAsync(desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("Sideways", "Other", 5m, "x")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(desk.Outsider.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashOut", "Other", 5m, "x")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);

        var ledger = (await desk.Cashier.GetFromJsonAsync<CashLedgerView>(Ledger))!;
        Assert.Equal(("User", (Guid?)desk.CashierId, "Cashier One", desk.Branch), (ledger.Scope, ledger.CashierUserId, ledger.CashierName, ledger.BranchId));
        Assert.Equal(ledger.FromDay, ledger.ToDay);
        Assert.Equal(["Receipt", "CashIn", "CashOut"], ledger.Rows.Select(row => row.Kind).ToArray());
        Assert.Equal([100m, 300m, 250m], ledger.Rows.Select(row => row.Balance).ToArray());
        Assert.Equal(("Supplier", "Courier", 50m), (ledger.Rows[2].Category, ledger.Rows[2].Text, ledger.Rows[2].MoneyOut));
        Assert.Equal((0m, 100m, 200m, 50m, 250m, 2), (ledger.Summary.OpeningBalance, ledger.Summary.Receipts.Cash, ledger.Summary.CashIn, ledger.Summary.CashOut, ledger.Summary.ClosingBalance, ledger.Summary.EntryCount));
        Assert.NotNull(ledger.CloseWindow);
        Assert.Empty(ledger.Cashiers);
        Assert.False(string.IsNullOrWhiteSpace(ledger.Letterhead.LabNameEn));

        await ExpectAsync(desk.Cashier.GetAsync($"{Ledger}?scope=Branch"), HttpStatusCode.Forbidden, "BIL-044");
        await ExpectAsync(desk.Cashier.GetAsync($"{Ledger}?scope=Everyone"), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        await ExpectAsync(desk.Outsider.GetAsync(Ledger), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        var today = ledger.FromDay;
        await ExpectAsync(desk.Supervisor.GetAsync($"{Ledger}?from={Day(today.AddDays(-31))}&to={Day(today)}"), HttpStatusCode.UnprocessableEntity, "BIL-033");
        await ExpectAsync(desk.Supervisor.GetAsync($"{Ledger}?from={Day(today)}&to={Day(today.AddDays(-1))}"), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
    }

    [DatabaseFact]
    public async Task A_supervisor_voids_an_entry_closes_the_cashiers_day_and_the_closed_period_is_read_only()
    {
        var desk = await DeskAsync();
        var visitId = await InsertVisitAsync(desk.Tenant, 300m);
        Assert.Equal(HttpStatusCode.Created, (await desk.Cashier.PostAsJsonAsync(Payments, new RecordPaymentRequest(visitId, 100m, "Cash", null))).StatusCode);
        var floatIn = (await (await desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashIn", "OpeningFloat", 200m, "Morning float"))).Content.ReadFromJsonAsync<CashEntryCreated>())!;
        var courier = (await (await desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashOut", "Supplier", 50m, "Courier"))).Content.ReadFromJsonAsync<CashEntryCreated>())!;

        // T7: the void belongs to billing.approve; twice is a state conflict.
        await ExpectAsync(desk.Cashier.PostAsJsonAsync($"{Entries}/{courier.Id}/void", new VoidCashEntryRequest("typed twice")), HttpStatusCode.Forbidden, ProblemCodes.Forbidden);
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Entries}/{courier.Id}/void", new VoidCashEntryRequest("")), HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed);
        Assert.Equal(HttpStatusCode.NoContent, (await desk.Supervisor.PostAsJsonAsync($"{Entries}/{courier.Id}/void", new VoidCashEntryRequest("typed twice"))).StatusCode);
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Entries}/{courier.Id}/void", new VoidCashEntryRequest("again")), HttpStatusCode.Conflict, "BIL-063");
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Entries}/{Guid.CreateVersion7()}/void", new VoidCashEntryRequest("x")), HttpStatusCode.NotFound, "BIL-411");

        // The branch book of one cashier (the right "view branch collections" is held by the supervisor).
        factory.Clock.Advance(TimeSpan.FromSeconds(2));   // JWT lifetime uses the machine clock (60 s skew): keep the test clock close
        var ledger = (await desk.Supervisor.GetFromJsonAsync<CashLedgerView>($"{Ledger}?scope=branch&cashierUserId={desk.CashierId}"))!;
        Assert.Equal(("Branch", (Guid?)desk.CashierId, 300m, 1, 50m), (ledger.Scope, ledger.CashierUserId, ledger.Summary.ClosingBalance, ledger.Summary.VoidedCount, ledger.Summary.VoidedAmount));
        Assert.Contains(ledger.Rows, row => row.Kind == "CashOut" && row.Status == "Voided" && row.MoneyOut == 0m);
        Assert.Contains(ledger.Cashiers, cashier => cashier.Name == "Cashier One");
        var window = ledger.CloseWindow!;

        // The day close from the ledger: the closing's expected cash is the ledger's balance (BR-16 with the cash entries).
        var preview = (await desk.Supervisor.GetFromJsonAsync<CashClosingPreview>($"{Closings}/preview?cashierUserId={desk.CashierId}&from={Iso(window.FromUtc)}&to={Iso(window.ToUtc)}"))!;
        Assert.Equal((300m, 200m, 0m, 1), (preview.Totals.ExpectedCash, preview.Totals.CashIn, preview.Totals.CashOut, preview.Totals.EntryCount));
        Assert.Contains(preview.Movements, movement => movement.Kind == "CashIn" && movement.Text == "Morning float");
        var closed = await desk.Supervisor.PostAsJsonAsync(Closings, new CloseCashDrawerRequest(desk.CashierId, window.FromUtc, window.ToUtc, 295m, "5 short"));
        Assert.Equal(HttpStatusCode.Created, closed.StatusCode);

        // Closed periods are read-only: the entry inside it can no longer be voided.
        await ExpectAsync(desk.Supervisor.PostAsJsonAsync($"{Entries}/{floatIn.Id}/void", new VoidCashEntryRequest("late")), HttpStatusCode.Conflict, "BIL-061");

        // After the close: the closing line resets the balance to the count; new money falls into the next period.
        factory.Clock.Advance(TimeSpan.FromSeconds(2));   // JWT lifetime uses the machine clock (60 s skew): keep the test clock close
        Assert.Equal(HttpStatusCode.Created, (await desk.Cashier.PostAsJsonAsync(Entries, new PostCashEntryRequest("CashOut", "BankDeposit", 295m, "Deposit"))).StatusCode);
        var after = (await desk.Cashier.GetFromJsonAsync<CashLedgerView>(Ledger))!;
        var closing = Assert.Single(after.Rows, row => row.Kind == "Closing");
        Assert.Equal((300m, 295m, -5m, 295m), (closing.Expected, closing.Counted, closing.Difference, closing.Balance));
        Assert.Equal((0m, -5m, 1), (after.Summary.ClosingBalance, after.Summary.CountDifferences, after.Summary.ClosingCount));
        Assert.Equal(window.ToUtc.AddTicks(-(window.ToUtc.Ticks % 10)), after.CloseWindow!.FromUtc);   // PostgreSQL keeps microseconds
    }

    // ---- desk -------------------------------------------------------------------------------------------------

    private sealed record Desk(Guid Tenant, Guid Branch, Guid CashierId, HttpClient Cashier, HttpClient Supervisor, HttpClient Outsider);

    private async Task<Desk> DeskAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (cashierName, _, cashierId) = await factory.SeedUserAsync(tenantId: tenant, permissions: CashierPermissions, displayName: "Cashier One", branchIds: [branch]);
        var (supervisorName, _, supervisorId) = await factory.SeedUserAsync(tenantId: tenant, permissions: SupervisorPermissions, displayName: "Shift Supervisor", branchIds: [branch]);
        await GrantAsync(tenant, cashierId, CashierRights);
        await GrantAsync(tenant, supervisorId, SpecialRights.All);
        var (outsiderName, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null, displayName: "No Role", branchIds: [branch]);
        return new Desk(tenant, branch, cashierId, await SignedInClientAsync(cashierName), await SignedInClientAsync(supervisorName), await SignedInClientAsync(outsiderName));
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset instant) => Uri.EscapeDataString(instant.ToString("O", CultureInfo.InvariantCulture));

    private static async Task ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await AuthClient.CodeOf(response));
    }

    private async Task<Guid> InsertVisitAsync(Guid tenant, decimal patientShare)
    {
        var id = Guid.CreateVersion7();
        var accession = $"ACC-CL-{id.ToString("N")[^8..].ToUpperInvariant()}";
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
        return id;
    }

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
