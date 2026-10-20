using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Identity;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.Settings;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The foundation over the real API (intake LIS-INT-FND-001): the platform tenant principal, the branch of the
/// session (CP-04), password recovery with a one-time code (CP-03), the audit log and chain check (CP-68 / SA-09)
/// and the tenant parameters (CP-57).
/// </summary>
public sealed class FoundationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SelectBranchPath = "/api/v1/auth/select-branch";
    private const string RecoveryStartPath = "/api/v1/auth/recovery/start";
    private const string RecoveryCompletePath = "/api/v1/auth/recovery/complete";
    private const string AuditEntries = "/api/v1/audit/entries";
    private const string VerifyChain = "/api/v1/audit/verify-chain";
    private const string Settings = "/api/v1/settings";

    [DatabaseFact]
    public async Task A_platform_administrator_signs_into_the_platform_tenant_with_every_permission()
    {
        var username = $"nt.{Guid.CreateVersion7():N}"[..20];
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<PlatformAdministratorSeeder>();
            var first = await seeder.SeedAsync(username, ApiFactory.Password, CancellationToken.None);
            var again = await seeder.SeedAsync(username, ApiFactory.Password, CancellationToken.None);   // idempotent
            Assert.True(first.UserCreated);
            Assert.False(again.UserCreated);
            Assert.Equal((first.UserId, first.MembershipId, first.RoleId), (again.UserId, again.MembershipId, again.RoleId));
        }

        var auth = new AuthClient(factory.CreateClient());
        var (body, _, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        Assert.Equal(TenantIds.Platform, body.Profile!.TenantId);
        Assert.Equal(PlatformAdministratorSeeder.RoleName, body.Profile.RoleName);
        Assert.Equal(PermissionCatalog.Keys.Order(StringComparer.Ordinal), body.Profile.Permissions.Order(StringComparer.Ordinal));

        var me = await auth.MeAsync(body.AccessToken);
        Assert.Equal(TenantIds.Platform, (await me.Content.ReadFromJsonAsync<SessionResponse>())!.TenantId);
    }

    [DatabaseFact]
    public async Task The_session_binds_to_an_assigned_branch_keeps_it_through_refresh_and_refuses_a_foreign_one()
    {
        var (branchA, branchB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (username, _, _) = await factory.SeedUserAsync(branchIds: [branchA, branchB]);
        var client = factory.CreateClient();
        var auth = new AuthClient(client);
        var (signedIn, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        Assert.Null(signedIn.Profile!.BranchId);
        Assert.Equal(new[] { branchA, branchB }.Order(), signedIn.Profile.BranchIds.Order());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);
        var selected = await client.PostAsJsonAsync(SelectBranchPath, new SelectBranchRequest(branchA));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal(branchA, (await selected.Content.ReadFromJsonAsync<AuthProfile>())!.BranchId);

        var me = await client.GetFromJsonAsync<SessionResponse>(AuthClient.MePath);
        Assert.Equal(branchA, me!.BranchId);   // the cache was evicted: the next request sees the branch

        var refreshed = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(branchA, (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.BranchId);   // successors inherit it

        var foreign = await client.PostAsJsonAsync(SelectBranchPath, new SelectBranchRequest(Guid.CreateVersion7()));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(ProblemCodes.BranchNotAssigned, await AuthClient.CodeOf(foreign));

        var empty = await client.PostAsJsonAsync(SelectBranchPath, new SelectBranchRequest(Guid.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [DatabaseFact]
    public async Task A_single_assigned_branch_is_selected_at_sign_in()
    {
        var branch = Guid.CreateVersion7();
        var (username, _, _) = await factory.SeedUserAsync(branchIds: [branch]);

        var (signedIn, _, _) = await new AuthClient(factory.CreateClient()).SignInExpectingSuccessAsync(username, ApiFactory.Password);

        Assert.Equal(branch, signedIn.Profile!.BranchId);
        Assert.Equal([branch], signedIn.Profile.BranchIds);
    }

    [DatabaseFact]
    public async Task Password_recovery_sends_a_code_to_the_mobile_replaces_the_password_and_revokes_the_sessions()
    {
        var mobile = "+2010" + Random.Shared.NextInt64(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (username, _, _) = await factory.SeedUserAsync(mobile: mobile);
        var auth = new AuthClient(factory.CreateClient());
        var (_, oldCookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        var unknown = await StartRecoveryAsync(auth, "nobody." + Guid.CreateVersion7().ToString("N")[..8]);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);   // never reveals whether the account exists
        Assert.Null(factory.Notifications.LastTo("nobody"));

        var started = await StartRecoveryAsync(auth, mobile);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var message = factory.Notifications.LastTo(mobile);
        Assert.NotNull(message);
        Assert.Equal(("password-recovery", "ar"), (message.TemplateCode, message.Language));
        var code = message.Placeholders["code"];
        Assert.Equal(6, code.Length);
        Assert.All(code, digit => Assert.True(char.IsAsciiDigit(digit)));

        const string newPassword = "Recovered-Password-2026!";
        var wrong = await CompleteRecoveryAsync(auth, username, "000000", newPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(ProblemCodes.OtpRejected, await AuthClient.CodeOf(wrong));

        var plaintext = await CompleteRecoveryAsync(auth, username, code, newPassword, seal: false);
        Assert.Equal(HttpStatusCode.BadRequest, plaintext.StatusCode);
        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(plaintext));

        var completed = await CompleteRecoveryAsync(auth, username, code, newPassword);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);

        var replay = await CompleteRecoveryAsync(auth, username, code, newPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);   // consumed

        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.SignInAsync(username, ApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auth.SignInAsync(username, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(oldCookie)).StatusCode);   // the earlier session family is revoked
    }

    [DatabaseFact]
    public async Task Five_wrong_codes_close_the_challenge()
    {
        var mobile = "+2011" + Random.Shared.NextInt64(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (username, _, _) = await factory.SeedUserAsync(mobile: mobile);
        var auth = new AuthClient(factory.CreateClient());
        Assert.Equal(HttpStatusCode.Accepted, (await StartRecoveryAsync(auth, username)).StatusCode);
        var code = factory.Notifications.LastTo(mobile)!.Placeholders["code"];

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteRecoveryAsync(auth, username, "111111", "Another-Password-2026!")).StatusCode);
        }

        var late = await CompleteRecoveryAsync(auth, username, code, "Another-Password-2026!");
        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
        Assert.Equal(ProblemCodes.OtpRejected, await AuthClient.CodeOf(late));
    }

    [DatabaseFact]
    public async Task The_audit_log_lists_field_changes_and_events_and_the_chain_verifies()
    {
        var tenant = Guid.CreateVersion7();
        var (auditor, _, auditorId) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["audit.view", "billing.manage", "billing.view"], displayName: "Auditor One");
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["billing.view"]);
        var client = await SignedInClientAsync(auditor);

        var created = await client.PostAsJsonAsync("/api/v1/billing/payment-methods", new PaymentMethodRequest("AUD", "Audited card", "بطاقة مدققة", "Card", true, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var methodId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var page = await client.GetFromJsonAsync<Paged<AuditEntryItem>>($"{AuditEntries}?entityType=PaymentMethodDefinition&entityId={methodId}&pageSize=10");
        Assert.Equal(2, page!.TotalCount);
        var change = Assert.Single(page.Items, item => item.Kind == AuditEntryItem.KindFieldChange);
        Assert.Equal(("Created", auditorId, "Auditor One"), (change.Action, change.ActorUserId, change.ActorName));
        Assert.Contains(change.Changes!, field => field.Field == "Code" && field.After == "AUD");
        Assert.NotNull(change.CorrelationId);
        var trail = Assert.Single(page.Items, item => item.Kind == AuditEntryItem.KindEvent);
        Assert.Equal(("PaymentMethodDefined", "Auditor One"), (trail.Action, trail.ActorName));
        Assert.NotNull(trail.Sequence);

        var verified = await client.GetFromJsonAsync<ChainVerificationResult>(VerifyChain);
        Assert.True(verified!.Ok);
        Assert.True(verified.Checked >= 1);

        var byUser = await client.GetFromJsonAsync<Paged<AuditEntryItem>>($"{AuditEntries}?userId={Guid.CreateVersion7()}");
        Assert.Equal(0, byUser!.TotalCount);

        var badRange = await client.GetAsync($"{AuditEntries}?from=2026-10-09T10:00:00Z&to=2026-10-09T09:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        var outsiderClient = await SignedInClientAsync(outsider);
        var forbidden = await outsiderClient.GetAsync(AuditEntries);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync(VerifyChain)).StatusCode);
    }

    [DatabaseFact]
    public async Task Tenant_parameters_read_as_defaults_are_edited_with_validation_and_need_the_manage_permission()
    {
        var tenant = Guid.CreateVersion7();
        var (admin, _, adminId) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["settings.view", "settings.manage"]);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["settings.view"]);
        var adminClient = await SignedInClientAsync(admin);
        var viewerClient = await SignedInClientAsync(viewer);

        var initial = await viewerClient.GetFromJsonAsync<TenantSettingsResponse>(Settings);
        Assert.Equal(26, initial!.Settings.Count);   // timeZone joined in round 2 (R2-01)
        Assert.All(initial.Settings, item => Assert.True(item.IsDefault));
        Assert.Equal(("Int", "1"), (initial.Settings.Single(item => item.Key == "labelCopies").Type, initial.Settings.Single(item => item.Key == "labelCopies").Value));

        var updated = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("labelCopies", "3"), new SettingValueItem("autoCollect", "false")]));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = (await updated.Content.ReadFromJsonAsync<TenantSettingsResponse>())!;
        var copies = body.Settings.Single(item => item.Key == "labelCopies");
        Assert.Equal(("3", false, adminId), (copies.Value, copies.IsDefault, copies.UpdatedByUserId));
        Assert.Equal("false", body.Settings.Single(item => item.Key == "autoCollect").Value);
        Assert.Equal("true", body.Settings.Single(item => item.Key == "autoReceive").Value);

        var unknown = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("noSuchKey", "1")]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal("SET-001", await AuthClient.CodeOf(unknown));

        var invalid = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("labelCopies", "99")]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("SET-002", await AuthClient.CodeOf(invalid));

        // LIS-VR-002 round 4: an unknown time zone names its reason, so the field can say "This time zone is not known."
        var unknownZone = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("timeZone", "Mars/Olympus")]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "SET-003"), (unknownZone.StatusCode, await AuthClient.CodeOf(unknownZone)));
        var blankZone = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("timeZone", " ")]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "SET-002"), (blankZone.StatusCode, await AuthClient.CodeOf(blankZone)));

        var empty = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var forbidden = await viewerClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("labelCopies", "2")]));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        Assert.Equal("3", (await viewerClient.GetFromJsonAsync<TenantSettingsResponse>(Settings))!.Settings.Single(item => item.Key == "labelCopies").Value);
    }

    /// <summary>
    /// Validation F-13: "Use default" sends a null value, which deletes the tenant's own row; the key then reads the inherited
    /// value again (<c>isDefault</c> true, no update stamp) and follows a later platform default. An unknown key is still
    /// SET-001; removing a row that does not exist changes nothing.
    /// </summary>
    [DatabaseFact]
    public async Task Use_default_removes_the_tenant_row_instead_of_storing_the_default()
    {
        var tenant = Guid.CreateVersion7();
        var (admin, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["settings.view", "settings.manage"]);
        var adminClient = await SignedInClientAsync(admin);

        // the state F-13 left behind: a row that holds the default value
        var stored = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("sessionTimeoutMinutes", "30"), new SettingValueItem("labelCopies", "3")]));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        var before = (await stored.Content.ReadFromJsonAsync<TenantSettingsResponse>())!.Settings.Single(item => item.Key == "sessionTimeoutMinutes");
        Assert.Equal(("30", false), (before.Value, before.IsDefault));
        Assert.NotNull(before.UpdatedAtUtc);

        var reset = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("sessionTimeoutMinutes", null), new SettingValueItem("labelCopies", null), new SettingValueItem("otpLength", null)]));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        foreach (var key in new[] { "sessionTimeoutMinutes", "labelCopies", "otpLength" })
        {
            var item = (await adminClient.GetFromJsonAsync<TenantSettingsResponse>(Settings))!.Settings.Single(setting => setting.Key == key);
            Assert.True(item.IsDefault, key);
            Assert.Null(item.UpdatedAtUtc);
            Assert.Null(item.UpdatedByUserId);
            Assert.Equal(item.InheritedValue, item.Value);
        }

        var unknown = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("noSuchKey", null)]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "SET-001"), (unknown.StatusCode, await AuthClient.CodeOf(unknown)));
    }

    /// <summary>F-13 on the Super Admin parameters (SA-19): the platform tenant's own row is removed the same way.</summary>
    [DatabaseFact]
    public async Task Use_default_on_the_platform_parameters_removes_the_platform_row()
    {
        var (admin, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["settings.view", "settings.manage"]);
        var adminClient = await SignedInClientAsync(admin);

        // a row equal to the code default: no other test sees a different value while it exists
        var stored = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("masterDataUpdatePolicy", "propose")]));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        Assert.False((await stored.Content.ReadFromJsonAsync<TenantSettingsResponse>())!.Settings.Single(item => item.Key == "masterDataUpdatePolicy").IsDefault);

        var reset = await adminClient.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("masterDataUpdatePolicy", null)]));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var item = (await reset.Content.ReadFromJsonAsync<TenantSettingsResponse>())!.Settings.Single(setting => setting.Key == "masterDataUpdatePolicy");
        Assert.Equal(("propose", true, (DateTimeOffset?)null, TenantSettingSources.Default), (item.Value, item.IsDefault, item.UpdatedAtUtc, item.Source));
    }

    private static Task<HttpResponseMessage> StartRecoveryAsync(AuthClient auth, string identifier) =>
        auth.Http.PostAsJsonAsync(RecoveryStartPath, new StartPasswordRecoveryRequest(identifier, "ar"));

    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(AuthClient auth, string identifier, string code, string newPassword, bool seal = true)
    {
        var sealedPassword = seal ? TestSealer.Seal(await auth.ChallengeAsync(), "newPassword", newPassword, "POST " + RecoveryCompletePath) : newPassword;
        return await auth.Http.PostAsJsonAsync(RecoveryCompletePath, new { usernameOrMobile = identifier, code, newPassword = sealedPassword });
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
