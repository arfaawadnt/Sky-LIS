using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Jobs;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-012 (C-16, D-3) over the real API: the session profile's subscription notice, the sign-in refusal of a member who is
/// not an administrator of an expired laboratory (<c>SUB-020</c>), the administrator's wallet-only access (<c>SUB-021</c>, needs
/// <c>SubscriptionExpiredGate</c> in <c>PipelineOrder</c>), the SA-03 counts and SA-04 list, and the reminder job sending once.
/// The platform's grace days stay at the default (7) here: the parameter is global and other classes run beside this one.
/// </summary>
public sealed class SubscriptionRemindersFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ClientSubscriptions = "/api/v1/platform/clients/subscriptions";
    private const string Dashboard = "/api/v1/platform/dashboard";

    [DatabaseFact]
    public async Task A_member_of_a_trial_ending_in_two_days_reads_a_warning_notice_that_calls_for_the_modal()
    {
        var lab = await LaboratoryAsync(trialStartedDaysAgo: 11.5);   // 14-day trial: 2.5 days left
        var (member, _, _) = await factory.SeedUserAsync(tenantId: lab.Id);

        var (body, _, _) = await new AuthClient(factory.CreateClient()).SignInExpectingSuccessAsync(member, ApiFactory.Password, SignInTarget.At(lab.Slug));

        var notice = body.Profile!.SubscriptionNotice!;
        Assert.Equal(("Warning", "Trial", false, true), (notice.Level, notice.Reason, notice.CanManage, notice.ShowModal));
        Assert.InRange(notice.DaysLeft!.Value, 1, 2);
        Assert.InRange((notice.TrialEndsAtUtc!.Value - lab.TrialEndsAtUtc!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));   // PostgreSQL keeps microseconds
    }

    [DatabaseFact]
    public async Task An_active_laboratory_with_a_full_wallet_carries_no_notice()
    {
        var lab = await LaboratoryAsync(trialStartedDaysAgo: null, walletBalance: 10_000m);
        var (member, _, _) = await factory.SeedUserAsync(tenantId: lab.Id);

        var (body, _, _) = await new AuthClient(factory.CreateClient()).SignInExpectingSuccessAsync(member, ApiFactory.Password, SignInTarget.At(lab.Slug));

        Assert.Null(body.Profile!.SubscriptionNotice);
    }

    [DatabaseFact]
    public async Task An_expired_laboratory_refuses_its_members_and_lets_its_administrators_reach_only_the_wallet()
    {
        var lab = await LaboratoryAsync(trialStartedDaysAgo: 30);   // ended 16 days ago, 7 days of grace: expired
        var (member, _, _) = await factory.SeedUserAsync(tenantId: lab.Id, permissions: ["registration.view"]);
        var (administrator, _, _) = await factory.SeedUserAsync(tenantId: lab.Id, permissions: ["organization.manage", "wallet.view", "settings.view"]);
        var auth = new AuthClient(factory.CreateClient());

        var refused = await auth.SignInAsync(member, ApiFactory.Password, SignInTarget.At(lab.Slug));
        Assert.Equal((HttpStatusCode.Forbidden, SubscriptionCodes.Expired), (refused.StatusCode, await AuthClient.CodeOf(refused)));

        var (body, _, _) = await auth.SignInExpectingSuccessAsync(administrator, ApiFactory.Password, SignInTarget.At(lab.Slug));
        var notice = body.Profile!.SubscriptionNotice!;
        Assert.Equal(("Expired", "Trial", true, true, 0), (notice.Level, notice.Reason, notice.CanManage, notice.ShowModal, notice.DaysLeft));

        var client = Bearer(body.AccessToken!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
        var settings = await client.GetAsync("/api/v1/settings");
        Assert.Equal((HttpStatusCode.Forbidden, SubscriptionCodes.AdministratorOnlyBilling), (settings.StatusCode, await AuthClient.CodeOf(settings)));
    }

    [DatabaseFact]
    public async Task The_super_admin_lists_the_expiring_grace_and_expired_clients_and_reads_the_counts_on_the_dashboard()
    {
        var soon = await LaboratoryAsync(trialStartedDaysAgo: 9.5);    // 4.5 days left: 4 whole days (never on a day boundary: the database keeps microseconds)
        var grace = await LaboratoryAsync(trialStartedDaysAgo: 16.5);   // ended 2.5 days ago: 4.5 days of grace left
        var gone = await LaboratoryAsync(trialStartedDaysAgo: 40);
        var admin = await PlatformAdminAsync();

        var expiring = (await admin.GetFromJsonAsync<SubscriptionClientList>($"{ClientSubscriptions}?state=expiring"))!;
        Assert.Equal(("expiring", 7, 7), (expiring.State, expiring.Days, expiring.GraceDays));
        Assert.Contains(expiring.Items, item => item.Id == soon.Id && item.Level == "Warning" && item.DaysLeft == 4);
        Assert.Contains(expiring.Items, item => item.Id == grace.Id && item.Level == "Grace");
        Assert.DoesNotContain(expiring.Items, item => item.Id == gone.Id);

        var inGrace = (await admin.GetFromJsonAsync<SubscriptionClientList>($"{ClientSubscriptions}?state=grace"))!;
        Assert.Contains(inGrace.Items, item => item.Id == grace.Id && item.DaysLeft == 4 && item.PortalUrl.EndsWith("/lab/" + grace.Slug, StringComparison.Ordinal));
        var expired = (await admin.GetFromJsonAsync<SubscriptionClientList>($"{ClientSubscriptions}?state=expired"))!;
        Assert.Contains(expired.Items, item => item.Id == gone.Id && item.Level == "Expired");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{ClientSubscriptions}?days=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{ClientSubscriptions}?days=61")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{ClientSubscriptions}?state=soon")).StatusCode);

        var dashboard = (await admin.GetFromJsonAsync<PlatformDashboard>(Dashboard))!;
        Assert.True(dashboard.ExpiringSoonClients >= 2, $"expiring soon: {dashboard.ExpiringSoonClients}");
        Assert.True(dashboard.GraceClients >= 1 && dashboard.ExpiredClients >= 1);

        // a laboratory user and a platform user without platform.view are refused (403)
        var (labUser, _, _) = await factory.SeedUserAsync(tenantId: soon.Id, permissions: ["registration.view"]);
        var labClient = await SignedInAsync(labUser, SignInTarget.At(soon.Slug));
        Assert.Equal(HttpStatusCode.Forbidden, (await labClient.GetAsync(ClientSubscriptions)).StatusCode);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["tenancy.view"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignedInAsync(viewer, SignInTarget.Administration)).GetAsync(ClientSubscriptions)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_reminder_job_sends_a_due_reminder_once_by_sms_and_email_to_the_manager()
    {
        var lab = await LaboratoryAsync(trialStartedDaysAgo: 10.5, email: "owner@reminder.example");   // 3.5 days left: threshold 3
        var job = ActivatorUtilities.CreateInstance<SubscriptionReminderJob>(factory.Services, new SubscriptionReminderOptions { Enabled = false });
        var before = factory.Notifications.Messages.Count(message => message.To == lab.ManagerMobile);

        var summary = await job.RunOnceAsync(factory.Clock.UtcNow, CancellationToken.None);

        var sent = Assert.Single(summary.Items, item => item.TenantId == lab.Id);
        Assert.Equal(("Sent", NotificationTemplateCodes.SubscriptionExpiry), (sent.Outcome, sent.TemplateCode));
        Assert.StartsWith("trial-3:", sent.Key, StringComparison.Ordinal);
        var sms = factory.Notifications.LastTo(lab.ManagerMobile)!;
        Assert.Equal((NotificationChannel.Sms, NotificationTemplateCodes.SubscriptionExpiry, "3"), (sms.Channel, sms.TemplateCode, sms.Placeholders["days"]));
        Assert.Equal(NotificationChannel.Email, factory.Notifications.LastTo("owner@reminder.example")!.Channel);

        var again = await job.RunOnceAsync(factory.Clock.UtcNow.AddHours(1), CancellationToken.None);
        Assert.DoesNotContain(again.Items, item => item.TenantId == lab.Id);   // already sent: only skips, which the summary does not list
        Assert.Equal(before + 1, factory.Notifications.Messages.Count(message => message.To == lab.ManagerMobile));
    }

    private sealed record Laboratory(Guid Id, string Slug, string ManagerMobile, DateTimeOffset? TrialEndsAtUtc);

    /// <summary>
    /// A client laboratory of its own reference data: a 14-day trial started <paramref name="trialStartedDaysAgo"/> days ago, or an
    /// Active client when null; its wallet in EGP with <paramref name="walletBalance"/>.
    /// </summary>
    private async Task<Laboratory> LaboratoryAsync(double? trialStartedDaysAgo, decimal walletBalance = 0m, string? email = null)
    {
        var iso = TestCountryCodes.Next();
        var suffix = Guid.CreateVersion7().ToString("N");
        var country = Country.Create(new CountryData("Reminderland " + iso, "أرض التذكير", iso, "+20", "Pound", "EGP", "en", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("R" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 3000m, "EGP", 1, 5, 100, 1));
        var now = factory.Clock.UtcNow;
        var slug = "rem-" + suffix[^10..];
        var manager = "011" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345670", null, "Manager", manager, email);
        var tenant = trialStartedDaysAgo is { } days
            ? Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Subscription, "en", 14, now.AddDays(-days))
            : Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 0, now);
        var wallet = Wallet.Open(tenant.Id, "EGP", now.AddDays(20));
        if (walletBalance > 0)
        {
            wallet.TopUp(walletBalance, "INV-" + suffix[..6], null, null, now);
        }

        using var scope = factory.Services.CreateScope();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(TenantIds.Platform))
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(wallet);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        return new Laboratory(tenant.Id, slug, tenant.ManagerMobile, tenant.TrialEndsAtUtc);
    }

    private async Task<HttpClient> PlatformAdminAsync()
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["platform.view", "tenancy.view"]);
        return await SignedInAsync(username, SignInTarget.Administration);
    }

    private async Task<HttpClient> SignedInAsync(string username, SignInTarget target)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password, target);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private HttpClient Bearer(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
