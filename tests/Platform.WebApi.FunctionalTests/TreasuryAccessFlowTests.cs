using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Contracts.Treasury;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The treasury access workflow over the real API (intake LIS-INT-TRS-001 §16): permissions from roles decide who
/// may call what (D-04), the aggregate decides the rest, and every refusal arrives as its code.
/// </summary>
public sealed class TreasuryAccessFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Treasuries = "/api/v1/treasuries";
    private const string Requests = "/api/v1/treasury-access-requests";

    private static readonly string[] RequesterPermissions = ["treasury.view", "treasury.create", "treasury.cancel", "treasury.manage"];
    private static readonly string[] ApproverPermissions = ["treasury.view", "treasury.approve"];
    private static readonly string[] SelfApproverPermissions = ["treasury.view", "treasury.create", "treasury.approve"];

    [DatabaseFact]
    public async Task The_treasury_list_names_the_branch_for_a_reader_without_organization_rights()
    {
        var tenant = Guid.CreateVersion7();
        var (organizer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["organization.view", "organization.create"], displayName: "Organizer");
        var (manager, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: RequesterPermissions, displayName: "Treasury Manager");
        var (reader, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["treasury.view"], displayName: "Reception");
        var organizerClient = await SignedInClientAsync(organizer);
        var managerClient = await SignedInClientAsync(manager);
        var readerClient = await SignedInClientAsync(reader);

        var branch = await organizerClient.PostAsJsonAsync("/api/v1/organization/branches", new CreateBranchRequest("NORTH", "North", "الشمال", null, null, null, null, "EGP"));
        Assert.Equal(HttpStatusCode.Created, branch.StatusCode);
        var branchId = (await branch.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        Assert.Equal(HttpStatusCode.Created, (await managerClient.PostAsJsonAsync(Treasuries, new CreateTreasuryRequest("NORTH-CASH", "North cash", "خزينة الشمال", branchId))).StatusCode);

        // UAT A-04: the reader holds no organization.view, yet sees CODE · name from the list itself
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.GetAsync("/api/v1/organization/branches")).StatusCode);
        var listed = await readerClient.GetFromJsonAsync<Paged<TreasuryListItem>>(Treasuries);
        var item = Assert.Single(listed!.Items);
        Assert.Equal((branchId, "NORTH", "North", "الشمال"), (item.BranchId, item.BranchCode, item.BranchNameEn, item.BranchNameAr));
    }

    [DatabaseFact]
    public async Task A_cashier_requests_access_an_approver_decides_and_the_outsider_is_refused()
    {
        var tenant = Guid.CreateVersion7();
        var (cashier, _, cashierId) = await factory.SeedUserAsync(tenantId: tenant, permissions: RequesterPermissions, displayName: "Cashier One");
        var (approver, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ApproverPermissions, displayName: "Shift Manager");
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null, displayName: "No Role");

        var cashierClient = await SignedInClientAsync(cashier);
        var approverClient = await SignedInClientAsync(approver);
        var outsiderClient = await SignedInClientAsync(outsider);

        // T6: the cashier (with treasury.manage) creates a treasury; the outsider cannot even list them.
        var created = await cashierClient.PostAsJsonAsync(Treasuries, new CreateTreasuryRequest(" main ", "Main drawer", "الخزينة الرئيسية", Guid.CreateVersion7()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var treasuryId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var duplicate = await cashierClient.PostAsJsonAsync(Treasuries, new CreateTreasuryRequest("MAIN", "Again", "مرة أخرى", Guid.CreateVersion7()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("TRS-100", await AuthClient.CodeOf(duplicate));

        var forbidden = await outsiderClient.GetAsync(Treasuries);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));

        // T1: request access; INV-TRS-002 refuses a second live request on the same device.
        var requested = await cashierClient.PostAsJsonAsync(Requests, new RequestTreasuryAccessRequest(treasuryId, "device-reception-1", "Reception PC 1"));
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var requestId = (await requested.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var again = await cashierClient.PostAsJsonAsync(Requests, new RequestTreasuryAccessRequest(treasuryId, "device-reception-1", "Reception PC 1"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("TRS-012", await AuthClient.CodeOf(again));

        var mine = await cashierClient.GetFromJsonAsync<Paged<TreasuryAccessRequestListItem>>(Requests + "?mine=true&status=pending");
        var item = Assert.Single(mine!.Items);
        Assert.Equal(("Pending", "Cashier One", "MAIN", cashierId), (item.Status, item.RequestedByName, item.TreasuryCode, item.RequestedByUserId));
        Assert.StartsWith("TRQ-", item.Number, StringComparison.Ordinal);

        // The cashier holds no treasury.approve: deny-by-default, not the aggregate, refuses.
        var cashierApproves = await cashierClient.PostAsync($"{Requests}/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, cashierApproves.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(cashierApproves));

        // T2: the approver approves; the detail carries the recorded approval; approving twice is a state conflict.
        var approved = await approverClient.PostAsJsonAsync($"{Requests}/{requestId}/approve", new ApproveTreasuryAccessRequest("verified device"));
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        var detail = await approverClient.GetFromJsonAsync<TreasuryAccessRequestDetail>($"{Requests}/{requestId}");
        Assert.Equal("Approved", detail!.Status);
        Assert.Equal(("Shift Manager", "verified device"), (detail.Approval!.ActorName, detail.Approval.Comment));

        var twice = await approverClient.PostAsync($"{Requests}/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("TRS-013", await AuthClient.CodeOf(twice));

        // T4: revoke needs a reason (shape, 400) and then succeeds.
        var noReason = await approverClient.PostAsJsonAsync($"{Requests}/{requestId}/revoke", new RevokeTreasuryAccessRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(noReason));

        var revoked = await approverClient.PostAsJsonAsync($"{Requests}/{requestId}/revoke", new RevokeTreasuryAccessRequest("device reassigned"));
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal("Revoked", (await cashierClient.GetFromJsonAsync<TreasuryAccessRequestDetail>($"{Requests}/{requestId}"))!.Status);
    }

    [DatabaseFact]
    public async Task The_requester_cannot_approve_their_own_request_even_with_the_permission()
    {
        var tenant = Guid.CreateVersion7();
        var (self, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: SelfApproverPermissions);
        var (manager, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: RequesterPermissions);
        var managerClient = await SignedInClientAsync(manager);
        var selfClient = await SignedInClientAsync(self);

        var treasuryId = (await (await managerClient.PostAsJsonAsync(Treasuries, new CreateTreasuryRequest("sod", "SoD drawer", "خزينة", Guid.CreateVersion7()))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var requestId = (await (await selfClient.PostAsJsonAsync(Requests, new RequestTreasuryAccessRequest(treasuryId, "device-self", null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var selfApproval = await selfClient.PostAsync($"{Requests}/{requestId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        Assert.Equal("SOD-TRS-001", await AuthClient.CodeOf(selfApproval));
        Assert.Equal("Pending", (await selfClient.GetFromJsonAsync<TreasuryAccessRequestDetail>($"{Requests}/{requestId}"))!.Status);
    }

    [DatabaseFact]
    public async Task Only_the_requester_cancels_a_pending_request()
    {
        var tenant = Guid.CreateVersion7();
        var (cashier, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: RequesterPermissions);
        var (other, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: RequesterPermissions);
        var cashierClient = await SignedInClientAsync(cashier);
        var otherClient = await SignedInClientAsync(other);

        var treasuryId = (await (await cashierClient.PostAsJsonAsync(Treasuries, new CreateTreasuryRequest("cxl", "Cancel drawer", "خزينة", Guid.CreateVersion7()))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var requestId = (await (await cashierClient.PostAsJsonAsync(Requests, new RequestTreasuryAccessRequest(treasuryId, "device-cxl", null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var byOther = await otherClient.PostAsync($"{Requests}/{requestId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal("TRS-030", await AuthClient.CodeOf(byOther));

        var byRequester = await cashierClient.PostAsJsonAsync($"{Requests}/{requestId}/cancel", new CancelTreasuryAccessRequest("wrong device"));
        Assert.Equal(HttpStatusCode.NoContent, byRequester.StatusCode);
        var detail = await cashierClient.GetFromJsonAsync<TreasuryAccessRequestDetail>($"{Requests}/{requestId}");
        Assert.Equal(("Cancelled", "wrong device"), (detail!.Status, detail.DecisionReason));

        var unknownStatus = await cashierClient.GetAsync(Requests + "?status=flying");
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
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
