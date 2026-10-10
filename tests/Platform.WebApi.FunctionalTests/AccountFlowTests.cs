using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Platform.Contracts.Account;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The Account &amp; onboarding module over the real API (intake LIS-INT-ACC-001 §16): support tickets between a
/// laboratory and the platform desk, self-service preferences and password change, the branch dashboard, the profile
/// and the audit helpers. Permissions from roles decide who may call what (D-04); every refusal arrives as its code.
/// </summary>
public sealed class AccountFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Tickets = "/api/v1/support/tickets";
    private const string Desk = "/api/v1/support/desk/tickets";
    private const string Profile = "/api/v1/account/profile";
    private const string Preferences = "/api/v1/account/preferences";
    private const string PasswordPath = "/api/v1/account/password";
    private const string Dashboard = "/api/v1/dashboard/branch";
    private const string AuditActors = "/api/v1/account/audit/actors";
    private const string AuditExport = "/api/v1/account/audit/export";

    private static readonly string[] LabSupport = ["support.view", "support.create"];
    private static readonly string[] DeskSupport = ["support.view", "support.manage"];

    [DatabaseFact]
    public async Task S_S1_to_S7_a_laboratory_opens_a_ticket_and_the_platform_desk_answers_resolves_and_closes_it()
    {
        var lab = Guid.CreateVersion7();
        var (labAdmin, _, labAdminId) = await factory.SeedUserAsync(tenantId: lab, permissions: LabSupport, displayName: "Lab Admin");
        var (reader, _, _) = await factory.SeedUserAsync(tenantId: lab, permissions: ["support.view"]);
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: lab, permissions: null);
        var (deskAgent, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: DeskSupport, displayName: "NT Support");
        var labClient = await SignedInClientAsync(labAdmin);
        var readerClient = await SignedInClientAsync(reader);
        var outsiderClient = await SignedInClientAsync(outsider);
        var deskClient = await SignedInClientAsync(deskAgent);

        // S1 open; shape refusals are 400; the outsider holds no support key.
        var invalid = await labClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("", "Details", "High"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(invalid));
        var badPriority = await labClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("Subject", "Details", "Critical"));
        Assert.Equal(HttpStatusCode.BadRequest, badPriority.StatusCode);

        var opened = await labClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest(" Labels print cut off ", "Since this morning the labels are cut.", "high"));
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var ticketId = (await opened.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync(Tickets)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("x", "y", "Low"))).StatusCode);

        var mine = await labClient.GetFromJsonAsync<Paged<SupportTicketListItem>>(Tickets + "?mine=true&status=open");
        var listed = Assert.Single(mine!.Items);
        Assert.Equal(("Labels print cut off", "High", "Open", labAdminId, "Lab Admin"), (listed.Subject, listed.Priority, listed.Status, listed.OpenedByUserId, listed.OpenedByName));
        Assert.StartsWith("TKT-", listed.Number, StringComparison.Ordinal);
        Assert.Single((await readerClient.GetFromJsonAsync<Paged<SupportTicketListItem>>(Tickets))!.Items);   // every member with support.view sees the laboratory's tickets

        // The platform desk opens nothing for itself (SUP-020); a laboratory role never reaches the desk (SUP-403).
        var deskOpens = await deskClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("Desk", "Desk", "Low"));
        Assert.Equal(HttpStatusCode.Forbidden, deskOpens.StatusCode);
        var (labWithManage, _, _) = await factory.SeedUserAsync(tenantId: lab, permissions: ["support.manage"]);
        var labDesk = await (await SignedInClientAsync(labWithManage)).GetAsync(Desk);
        Assert.Equal(HttpStatusCode.Forbidden, labDesk.StatusCode);
        Assert.Equal("SUP-403", await AuthClient.CodeOf(labDesk));

        // S3: the desk sees it among every laboratory's tickets and answers; the ticket moves to InProgress.
        var deskList = await deskClient.GetFromJsonAsync<Paged<SupportTicketListItem>>($"{Desk}?tenantId={lab}");
        Assert.Equal(ticketId, Assert.Single(deskList!.Items).Id);
        var answered = await deskClient.PostAsJsonAsync($"{Desk}/{ticketId}/replies", new SupportTicketReplyRequest("Please update the label printer driver."));
        Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        var detail = await labClient.GetFromJsonAsync<SupportTicketDetail>($"{Tickets}/{ticketId}");
        Assert.Equal("InProgress", detail!.Status);
        var reply = Assert.Single(detail.Replies);
        Assert.Equal((true, "NT Support"), (reply.FromPlatform, reply.AuthorName));

        // S5 resolve with a note; S2 the laboratory replies and reopens it; S5 again; S7 the laboratory closes it.
        Assert.Equal(HttpStatusCode.NoContent, (await deskClient.PostAsJsonAsync($"{Desk}/{ticketId}/resolve", new ResolveSupportTicketRequest("Driver 4.2 fixes it."))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await labClient.PostAsJsonAsync($"{Tickets}/{ticketId}/replies", new SupportTicketReplyRequest("Still cut on the second printer."))).StatusCode);
        Assert.Equal("Open", (await labClient.GetFromJsonAsync<SupportTicketDetail>($"{Tickets}/{ticketId}"))!.Status);
        var reopenOpen = await labClient.PostAsync($"{Tickets}/{ticketId}/reopen", null);
        Assert.Equal(HttpStatusCode.Conflict, reopenOpen.StatusCode);
        Assert.Equal("SUP-011", await AuthClient.CodeOf(reopenOpen));
        Assert.Equal(HttpStatusCode.NoContent, (await deskClient.PostAsync($"{Desk}/{ticketId}/resolve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await labClient.PostAsync($"{Tickets}/{ticketId}/close", null)).StatusCode);

        var lateReply = await deskClient.PostAsJsonAsync($"{Desk}/{ticketId}/replies", new SupportTicketReplyRequest("late"));
        Assert.Equal(HttpStatusCode.Conflict, lateReply.StatusCode);
        Assert.Equal("SUP-010", await AuthClient.CodeOf(lateReply));
        var closed = await deskClient.GetFromJsonAsync<SupportTicketDetail>($"{Desk}/{ticketId}");
        Assert.Equal(("Closed", 3), (closed!.Status, closed.Replies.Count));
    }

    [DatabaseFact]
    public async Task S_ISO_another_laboratory_cannot_read_or_answer_a_ticket()
    {
        var (labA, labB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (userA, _, _) = await factory.SeedUserAsync(tenantId: labA, permissions: LabSupport);
        var (userB, _, _) = await factory.SeedUserAsync(tenantId: labB, permissions: LabSupport);
        var clientA = await SignedInClientAsync(userA);
        var clientB = await SignedInClientAsync(userB);
        var ticketId = (await (await clientA.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("Private", "Only for lab A", "Normal"))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var read = await clientB.GetAsync($"{Tickets}/{ticketId}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal("SUP-404", await AuthClient.CodeOf(read));
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsJsonAsync($"{Tickets}/{ticketId}/replies", new SupportTicketReplyRequest("hello"))).StatusCode);
        Assert.Empty((await clientB.GetFromJsonAsync<Paged<SupportTicketListItem>>(Tickets))!.Items);
    }

    [DatabaseFact]
    public async Task S_A1_the_language_preference_is_saved_and_the_next_refresh_profile_carries_it()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (signedIn, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        Assert.Equal("en", signedIn.Profile!.Language);
        var client = Authorized(auth.Http, signedIn.AccessToken!);

        var invalid = await client.PutAsJsonAsync(Preferences, new UpdatePreferencesRequest("fr"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(Preferences, new UpdatePreferencesRequest("ar"))).StatusCode);

        var refreshed = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("ar", (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.Language);
        Assert.Equal("ar", (await client.GetFromJsonAsync<MyAccountProfile>(Profile))!.Language);
    }

    [DatabaseFact]
    public async Task S_A2_a_password_change_needs_the_current_password_applies_the_policy_and_ends_the_other_sessions()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var current = new AuthClient(factory.CreateClient());
        var (signedIn, currentCookie, _) = await current.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var other = new AuthClient(factory.CreateClient());
        var (_, otherCookie, _) = await other.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var client = Authorized(current.Http, signedIn.AccessToken!);

        var plaintext = await ChangePasswordAsync(current, client, ApiFactory.Password, "Brand-New-Secret-2026", seal: false);
        Assert.Equal(HttpStatusCode.BadRequest, plaintext.StatusCode);
        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(plaintext));

        var wrong = await ChangePasswordAsync(current, client, "not-my-password", "Brand-New-Secret-2026");
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("ACC-010", await AuthClient.CodeOf(wrong));

        var containsUsername = await ChangePasswordAsync(current, client, ApiFactory.Password, "x" + username + "-2026");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, containsUsername.StatusCode);
        Assert.Equal("ACC-013", await AuthClient.CodeOf(containsUsername));

        var tooShort = await ChangePasswordAsync(current, client, ApiFactory.Password, "short");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        Assert.Equal("ACC-012", await AuthClient.CodeOf(tooShort));

        var unchanged = await ChangePasswordAsync(current, client, ApiFactory.Password, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unchanged.StatusCode);
        Assert.Equal("ACC-014", await AuthClient.CodeOf(unchanged));

        const string newPassword = "Brand-New-Secret-2026";
        Assert.Equal(HttpStatusCode.NoContent, (await ChangePasswordAsync(current, client, ApiFactory.Password, newPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.RefreshAsync(otherCookie)).StatusCode);   // the other session ended
        Assert.Equal(HttpStatusCode.OK, (await current.RefreshAsync(currentCookie)).StatusCode);         // this one stays signed in
        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthClient(factory.CreateClient()).SignInAsync(username, ApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await new AuthClient(factory.CreateClient()).SignInAsync(username, newPassword)).StatusCode);
    }

    [DatabaseFact]
    public async Task S_A2_five_wrong_current_passwords_lock_the_account()
    {
        var (username, _, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (signedIn, _, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var client = Authorized(auth.Http, signedIn.AccessToken!);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal("ACC-010", await AuthClient.CodeOf(await ChangePasswordAsync(auth, client, "wrong-" + attempt, "Another-Secret-2026")));
        }

        Assert.Equal("ACC-011", await AuthClient.CodeOf(await ChangePasswordAsync(auth, client, "wrong-5", "Another-Secret-2026")));
        Assert.Equal("ACC-011", await AuthClient.CodeOf(await ChangePasswordAsync(auth, client, ApiFactory.Password, "Another-Secret-2026")));
        Assert.True((await factory.LoadUserAsync(userId)).IsLockedOut);
    }

    [DatabaseFact]
    public async Task S_D1_the_dashboard_answers_for_the_session_branch_and_refuses_without_one()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (withBranch, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["dashboard.view"], branchIds: [branch]);
        var (twoBranches, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["dashboard.view"], branchIds: [branch, Guid.CreateVersion7()]);
        var (noPermission, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["support.view"], branchIds: [branch]);

        var client = await SignedInClientAsync(withBranch);
        var dashboard = await client.GetFromJsonAsync<BranchDashboard>($"{Dashboard}?date=2026-10-09&utcOffsetMinutes=180");
        Assert.Equal((branch, "2026-10-09"), (dashboard!.BranchId, dashboard.Date));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 21, 0, 0, TimeSpan.Zero), dashboard.FromUtc);
        Assert.Equal("EGP", dashboard.CurrencyCode);
        Assert.True(dashboard.Kpis.PendingResults is null or 0);   // null until app.results is migrated, then 0 for an empty branch (LIS-INT-REP-001 §14)
        Assert.Null(dashboard.Kpis.WalletBalance);   // no wallet.view
        Assert.True(dashboard.RegistrationsByDay.Count is 0 or 7);
        Assert.Empty(dashboard.DelayedTests);

        var badDate = await client.GetAsync($"{Dashboard}?date=09/10/2026");
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);

        var unselected = await (await SignedInClientAsync(twoBranches)).GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.Forbidden, unselected.StatusCode);
        Assert.Equal(ProblemCodes.BranchRequired, await AuthClient.CodeOf(unselected));

        var forbidden = await (await SignedInClientAsync(noPermission)).GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));
    }

    [DatabaseFact]
    public async Task The_profile_returns_the_own_account_the_password_rule_and_the_laboratory_placeholder()
    {
        var tenant = Guid.CreateVersion7();
        var (username, _, userId) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["support.view"], displayName: "Profile Person", mobile: "+201012345678");
        var client = await SignedInClientAsync(username);

        var profile = await client.GetFromJsonAsync<MyAccountProfile>(Profile);

        Assert.Equal((userId, username, "Profile Person", "+201012345678"), (profile!.UserId, profile.Username, profile.DisplayName, profile.Mobile));
        Assert.Equal((tenant, false, "en"), (profile.TenantId, profile.IsPlatform, profile.Language));
        Assert.Equal((8, 128), (profile.PasswordPolicy.MinLength, profile.PasswordPolicy.MaxLength));
        Assert.Equal(tenant, profile.Laboratory!.TenantId);   // no client record: the tenant id only (intake §16 Q11)
        Assert.Null(profile.Laboratory.NameEn);
        Assert.NotNull(profile.RoleNameEn);

        var (staff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["support.manage"]);
        var staffProfile = await (await SignedInClientAsync(staff)).GetFromJsonAsync<MyAccountProfile>(Profile);
        Assert.True(staffProfile!.IsPlatform);
        Assert.Null(staffProfile.Laboratory);
    }

    [DatabaseFact]
    public async Task S_AU1_the_audit_export_is_csv_for_audit_export_and_refused_without_it()
    {
        var tenant = Guid.CreateVersion7();
        var (exporter, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["audit.view", "audit.export", "support.create"], displayName: "Exporter");
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["audit.view"], displayName: "Viewer");
        var exporterClient = await SignedInClientAsync(exporter);
        var viewerClient = await SignedInClientAsync(viewer);
        Assert.Equal(HttpStatusCode.Created, (await exporterClient.PostAsJsonAsync(Tickets, new OpenSupportTicketRequest("Audited", "A ticket for the trail", "Low"))).StatusCode);

        var actors = await viewerClient.GetFromJsonAsync<List<AuditActorItem>>(AuditActors);
        Assert.Equal(["Exporter", "Viewer"], actors!.Select(actor => actor.DisplayName).Order(StringComparer.Ordinal).ToArray());

        var export = await exporterClient.GetAsync($"{AuditExport}?language=en");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.StartsWith("Time (UTC),User,Action", Encoding.UTF8.GetString(bytes[3..]), StringComparison.Ordinal);

        var refused = await viewerClient.GetAsync(AuditExport);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(refused));
        Assert.Equal(HttpStatusCode.BadRequest, (await exporterClient.GetAsync($"{AuditExport}?language=fr")).StatusCode);
    }

    /// <summary>Seals both fields under one challenge, as the SPA's interceptor does (02 §3.3: one challenge per request, the same nonce on every field).</summary>
    private static async Task<HttpResponseMessage> ChangePasswordAsync(AuthClient auth, HttpClient client, string currentPassword, string newPassword, bool seal = true)
    {
        if (!seal)
        {
            return await client.PostAsJsonAsync(PasswordPath, new { currentPassword, newPassword });
        }

        var challenge = await auth.ChallengeAsync();
        var audience = "POST " + PasswordPath;
        return await client.PostAsJsonAsync(PasswordPath, new
        {
            currentPassword = TestSealer.Seal(challenge, "currentPassword", currentPassword, audience),
            newPassword = TestSealer.Seal(challenge, "newPassword", newPassword, audience),
        });
    }

    private static HttpClient Authorized(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        return Authorized(client, body.AccessToken!);
    }
}
