using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-012 (C-16): the reminder run per client (due event, idempotency, channels, placeholders, the race on the unique key),
/// the subscription filters of SA-03 / SA-04 and the profile notice.
/// </summary>
public sealed class SubscriptionReminderTests
{
    private const string Base = "https://lis.example.test";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SUB_RUN_1_a_due_trial_reminder_is_recorded_once_then_sent_by_sms_and_email_in_the_client_language()
    {
        var (run, log, sender, unitOfWork) = Run();
        var client = Trial("Al Noor", Now.AddDays(3).AddHours(2), language: "ar", email: "lab@example.test");

        var item = await run.RemindAsync(client, 7, Now, CancellationToken.None);

        Assert.Equal((SubscriptionReminderRun.Sent, NotificationTemplateCodes.SubscriptionExpiry), (item.Outcome, item.TemplateCode));
        Assert.StartsWith("trial-3:", item.Key, StringComparison.Ordinal);
        Assert.Equal(["Sms", "Email"], item.Channels);
        Assert.Equal(1, unitOfWork.Saves);
        var row = Assert.Single(log.Rows);
        Assert.Equal((client.TenantId, item.Key, "Sms,Email"), (row.TenantId, row.DueKey, row.Channels));

        Assert.Equal(2, sender.Sent.Count);
        var sms = sender.Sent[0];
        Assert.Equal((NotificationChannel.Sms, "+201000000001", "ar"), (sms.Channel, sms.To, sms.Language));
        Assert.Equal(("مختبر Al Noor", "3", $"{Base}/lab/al-noor"), (sms.Placeholders["labName"], sms.Placeholders["days"], sms.Placeholders["url"]));
        Assert.Equal((NotificationChannel.Email, "lab@example.test"), (sender.Sent[1].Channel, sender.Sent[1].To));

        // the next poll (an hour later) finds the key recorded: nothing more is sent
        var again = await run.RemindAsync(client, 7, Now.AddHours(1), CancellationToken.None);
        Assert.Equal((SubscriptionReminderRun.Skipped, "already-sent"), (again.Outcome, again.Reason));
        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task SUB_RUN_2_nothing_is_due_for_a_healthy_client_and_nothing_is_written()
    {
        var (run, log, sender, unitOfWork) = Run();
        var item = await run.RemindAsync(Active("Busy Lab", balance: 10_000m), 7, Now, CancellationToken.None);

        Assert.Equal((SubscriptionReminderRun.Skipped, "nothing-due"), (item.Outcome, item.Reason));
        Assert.Empty(log.Rows);
        Assert.Empty(sender.Sent);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task SUB_RUN_3_a_low_wallet_sends_wallet_low_with_the_amount_to_one_month_and_no_email_without_an_address()
    {
        var (run, _, sender, _) = Run();
        var item = await run.RemindAsync(Active("Low Lab", balance: 250m), 7, Now, CancellationToken.None);

        Assert.Equal((SubscriptionReminderRun.Sent, NotificationTemplateCodes.WalletLow), (item.Outcome, item.TemplateCode));
        var sms = Assert.Single(sender.Sent);
        Assert.Equal(("2,750.00 EGP", "2"), (sms.Placeholders["amount"], sms.Placeholders["days"]));
    }

    [Fact]
    public async Task SUB_RUN_4_a_grace_ending_soon_sends_subscription_grace()
    {
        var (run, _, _, _) = Run();
        var item = await run.RemindAsync(Trial("Late Lab", Now.AddDays(-5)), 7, Now, CancellationToken.None);   // 2 days of grace left
        Assert.Equal((SubscriptionReminderRun.Sent, NotificationTemplateCodes.SubscriptionGrace), (item.Outcome, item.TemplateCode));
        Assert.StartsWith("grace-3:", item.Key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SUB_RUN_5_a_concurrent_run_that_recorded_the_key_first_wins_and_this_one_sends_nothing()
    {
        var (run, _, sender, unitOfWork) = Run();
        unitOfWork.Refusal = new DomainException(ProblemCodes.UniqueViolation, "duplicate", DomainFault.Conflict);

        var item = await run.RemindAsync(Trial("Raced Lab", Now.AddDays(1).AddHours(3)), 7, Now, CancellationToken.None);

        Assert.Equal((SubscriptionReminderRun.Skipped, "already-sent"), (item.Outcome, item.Reason));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task SUB_RUN_6_the_run_refuses_outside_the_platform_tenant()
    {
        var (run, _, _, _) = Run(Guid.CreateVersion7());
        await Assert.ThrowsAsync<DomainException>(() => run.RemindAsync(Trial("Lab", Now.AddDays(2)), 7, Now, CancellationToken.None));
    }

    [Fact]
    public void SUB_RUN_7_the_summary_counts_every_client_and_lists_only_those_that_did_something()
    {
        var summary = SubscriptionReminderRun.Summarise(Now,
        [
            new(Guid.CreateVersion7(), SubscriptionReminderRun.Sent, null, "trial-3:x", "subscription-expiry", ["Sms"]),
            new(Guid.CreateVersion7(), SubscriptionReminderRun.Skipped, "nothing-due", null, null, []),
            new(Guid.CreateVersion7(), SubscriptionReminderRun.Failed, "error", null, null, []),
        ]);
        Assert.Equal((3, 1, 1, 1, 2), (summary.Clients, summary.Sent, summary.Skipped, summary.Failed, summary.Items.Count));
    }

    [Fact]
    public void SUB_FLT_1_the_filters_select_expiring_within_the_window_grace_and_expired()
    {
        var rows = new[]
        {
            Trial("Fine Lab", Now.AddDays(20)),
            Trial("Soon Lab", Now.AddDays(5)),
            Trial("Grace Lab", Now.AddDays(-2)),
            Trial("Gone Lab", Now.AddDays(-30)),
            Active("Low Lab", balance: 250m),
        };
        var states = rows.Select(row => (row, row.StateAt(Now, 7))).ToList();

        Assert.Equal(["Low Lab", "Grace Lab", "Soon Lab"], SubscriptionClientFinder.Filter(states, "expiring", 7, Base).Select(item => item.NameEn));   // soonest end first, then name
        Assert.Equal(["Low Lab"], SubscriptionClientFinder.Filter(states, "expiring", 3, Base).Select(item => item.NameEn));
        var grace = Assert.Single(SubscriptionClientFinder.Filter(states, "grace", 7, Base));
        Assert.Equal(("Grace Lab", "Grace", "Trial", 5, $"{Base}/lab/grace-lab"), (grace.NameEn, grace.Level, grace.Reason, grace.DaysLeft, grace.PortalUrl));
        Assert.Equal(["Gone Lab"], SubscriptionClientFinder.Filter(states, "EXPIRED", 7, Base).Select(item => item.NameEn));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("expiring", 7, true)]
    [InlineData("grace", 60, true)]
    [InlineData("expired", 1, true)]
    [InlineData("soon", 7, false)]
    [InlineData("expiring", 0, false)]
    [InlineData("expiring", 61, false)]
    public void SUB_FLT_2_state_and_days_are_checked_by_shape(string? state, int? days, bool valid) =>
        Assert.Equal(valid, new ListSubscriptionClientsValidator().Validate(new ListSubscriptionClientsQuery(state, days)).IsValid);

    [Fact]
    public void SUB_PRF_1_the_profile_carries_a_notice_only_when_there_is_something_to_say()
    {
        var ok = Principal(SubscriptionStatus.None, administrator: true);
        Assert.Null(Profiles.ToProfile(ok, SessionScope.Full).SubscriptionNotice);

        var grace = SubscriptionTerms.Evaluate(TenantStatus.Trial, Now.AddDays(-1), new WalletPosition(0m, 3000m, "EGP", null), 7, Now);
        var notice = Profiles.ToProfile(Principal(grace, administrator: false), SessionScope.Full).SubscriptionNotice!;
        Assert.Equal(("Grace", "Trial", 6, 3000m, "EGP", false, true), (notice.Level, notice.Reason, notice.DaysLeft, notice.AmountToTopUp, notice.CurrencyCode, notice.CanManage, notice.ShowModal));
        Assert.Equal(Now.AddDays(-1), notice.TrialEndsAtUtc);
    }

    private static SessionPrincipal Principal(SubscriptionStatus state, bool administrator) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "User", true, Guid.CreateVersion7(), SessionKind.Browser, SessionScope.Full, Now.AddHours(1), Now.AddHours(8), false, null, true, "en", "Role",
            new HashSet<string>(StringComparer.Ordinal), [], [], null)
        {
            Subscription = state,
            IsSubscriptionAdministrator = administrator,
        };

    private static ClientSubscriptionRow Trial(string name, DateTimeOffset trialEnd, string language = "en", string? email = null) =>
        new(Guid.CreateVersion7(), name, "مختبر " + name, Slug(name), TenantStatus.Trial, trialEnd, language, "Manager of " + name, "+201000000001", email, 3000m, 0m, "EGP", null, Now.AddDays(30));

    private static ClientSubscriptionRow Active(string name, decimal balance) =>
        new(Guid.CreateVersion7(), name, "مختبر " + name, Slug(name), TenantStatus.Active, null, "en", "Manager of " + name, "+201000000002", null, 3000m, balance, "EGP", balance <= 0 ? Now.AddDays(-3) : null, Now.AddDays(20));

    private static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');

    private static (SubscriptionReminderRun Run, FakeLog Log, RecordingSender Sender, FakeUnitOfWork UnitOfWork) Run(Guid? tenant = null)
    {
        var log = new FakeLog();
        var sender = new RecordingSender();
        var unitOfWork = new FakeUnitOfWork(log);
        var run = new SubscriptionReminderRun(log, sender, new Portal(), new FakeTenant(tenant ?? TenantIds.Platform), unitOfWork);
        return (run, log, sender, unitOfWork);
    }

    private sealed class FakeLog : ISubscriptionReminderRepository
    {
        public List<SubscriptionReminder> Rows { get; } = [];

        public List<SubscriptionReminder> Staged { get; } = [];

        public Task<bool> ExistsAsync(Guid tenantId, string dueKey, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Any(row => row.TenantId == tenantId && row.DueKey == dueKey));

        public Task<SubscriptionReminder?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

        public void Add(SubscriptionReminder aggregate) => Staged.Add(aggregate);
    }

    private sealed class FakeUnitOfWork(FakeLog log) : IUnitOfWork
    {
        public int Saves { get; private set; }

        public DomainException? Refusal { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (Refusal is not null)
            {
                log.Staged.Clear();
                throw Refusal;
            }

            Saves++;
            log.Rows.AddRange(log.Staged);
            var count = log.Staged.Count;
            log.Staged.Clear();
            return Task.FromResult(count);
        }
    }

    private sealed class RecordingSender : INotificationSender
    {
        public List<NotificationMessage> Sent { get; } = [];

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class Portal : IPortalAddressOptions
    {
        public string PortalBaseUrl => Base;
    }

    private sealed class FakeTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;

        public bool IsElevated => false;
    }
}
