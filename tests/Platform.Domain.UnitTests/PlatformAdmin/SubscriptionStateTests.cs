using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.PlatformAdmin.PlatformAdminFixtures;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// LIS-CR-012 (C-16, D-3): the subscription state (Ok / Warning / Grace / Expired with the days left), the once-only reminder
/// keys and the wallet's zero mark. Scenario ids SUB-S1… of intake LIS-INT-PLT-001 §21.
/// </summary>
public sealed class SubscriptionStateTests
{
    private const int Grace = SubscriptionTerms.DefaultGraceDays;
    private const decimal Fee = 3000m;   // 100 a day

    private static SubscriptionStatus Trial(DateTimeOffset now, int graceDays = Grace) =>
        SubscriptionTerms.Evaluate(TenantStatus.Trial, Now.AddDays(14), null, graceDays, now);

    private static SubscriptionStatus Active(decimal balance, DateTimeOffset now, DateTimeOffset? zeroSince = null, decimal fee = Fee, int graceDays = Grace) =>
        SubscriptionTerms.Evaluate(TenantStatus.Active, null, new WalletPosition(balance, fee, "EGP", zeroSince, Now.AddMonths(1)), graceDays, now);

    [Fact]
    public void SUB_S1_a_trial_with_more_than_a_week_left_is_Ok_and_one_within_a_week_is_a_Warning_with_whole_days()
    {
        Assert.Equal(SubscriptionLevel.Ok, Trial(Now).Level);
        Assert.Equal(Now.AddDays(14), Trial(Now).TrialEndsAtUtc);

        var warning = Trial(Now.AddDays(7));
        Assert.Equal((SubscriptionLevel.Warning, SubscriptionReason.Trial, 7, Now.AddDays(14)), (warning.Level, warning.Reason, warning.DaysLeft, warning.EndsAtUtc));
        Assert.Equal(2, Trial(Now.AddDays(11.5)).DaysLeft);   // floor
        Assert.Equal(0, Trial(Now.AddDays(13.8)).DaysLeft);   // ends today
    }

    [Fact]
    public void SUB_S2_an_ended_trial_runs_the_grace_days_then_expires()
    {
        var grace = Trial(Now.AddDays(14));
        Assert.Equal((SubscriptionLevel.Grace, SubscriptionReason.Trial, 7, Now.AddDays(21)), (grace.Level, grace.Reason, grace.DaysLeft, grace.EndsAtUtc));
        Assert.Equal(1, Trial(Now.AddDays(19.5)).DaysLeft);

        var expired = Trial(Now.AddDays(21));
        Assert.Equal((SubscriptionLevel.Expired, 0), (expired.Level, expired.DaysLeft));
        Assert.True(expired.IsExpired);
    }

    [Fact]
    public void SUB_S3_the_grace_days_are_clamped_to_0_to_60_and_zero_grace_expires_at_the_trial_end()
    {
        Assert.Equal(SubscriptionLevel.Expired, Trial(Now.AddDays(14), graceDays: 0).Level);
        Assert.Equal(Now.AddDays(74), Trial(Now.AddDays(14), graceDays: 500).EndsAtUtc);
        Assert.Equal(SubscriptionLevel.Expired, Trial(Now.AddDays(15), graceDays: -3).Level);
        Assert.True(SubscriptionTerms.IsValidGraceDays(60));
        Assert.False(SubscriptionTerms.IsValidGraceDays(61));
    }

    [Fact]
    public void SUB_S4_an_active_wallet_covering_a_week_or_more_is_Ok_and_less_is_a_Warning_with_the_amount_to_one_month()
    {
        Assert.Equal(SubscriptionLevel.Ok, Active(700m, Now).Level);

        var low = Active(250m, Now);   // 2.5 days of 100
        Assert.Equal((SubscriptionLevel.Warning, SubscriptionReason.Wallet, 2, 2750m, "EGP"), (low.Level, low.Reason, low.DaysLeft, low.AmountToTopUp, low.CurrencyCode));
        Assert.Equal(Now.AddDays(2.5), low.EndsAtUtc);
    }

    [Fact]
    public void SUB_S5_a_wallet_at_zero_warns_for_one_plan_period_then_runs_the_grace_then_expires()
    {
        var since = Now;
        var warning = Active(-3000m, Now.AddDays(10), since);
        Assert.Equal((SubscriptionLevel.Warning, SubscriptionReason.WalletAtZero, since.AddMonths(1), 6000m), (warning.Level, warning.Reason, warning.EndsAtUtc, warning.AmountToTopUp));

        var grace = Active(-3000m, since.AddMonths(1).AddDays(2), since);
        Assert.Equal((SubscriptionLevel.Grace, SubscriptionReason.WalletAtZero, 5), (grace.Level, grace.Reason, grace.DaysLeft));

        Assert.Equal(SubscriptionLevel.Expired, Active(-3000m, since.AddMonths(1).AddDays(7), since).Level);
    }

    [Fact]
    public void SUB_S6_a_free_plan_a_missing_wallet_and_a_deactivated_client_have_nothing_to_say()
    {
        Assert.Equal(SubscriptionLevel.Ok, Active(-10m, Now, Now.AddYears(-1), fee: 0m).Level);
        Assert.Equal(SubscriptionStatus.None, SubscriptionTerms.Evaluate(TenantStatus.Active, null, null, Grace, Now));
        Assert.Equal(SubscriptionStatus.None, SubscriptionTerms.Evaluate(TenantStatus.Inactive, Now.AddDays(-90), null, Grace, Now));
        Assert.Equal(SubscriptionLevel.Ok, SubscriptionTerms.Evaluate(TenantStatus.Trial, null, null, Grace, Now).Level);
    }

    [Fact]
    public void SUB_S7_the_modal_shows_under_three_days_in_grace_and_expired()
    {
        Assert.False(Trial(Now.AddDays(10)).CallsForModal);    // 4 days left
        Assert.True(Trial(Now.AddDays(11.5)).CallsForModal);   // 2 days left
        Assert.True(Trial(Now.AddDays(15)).CallsForModal);     // grace
        Assert.True(Trial(Now.AddDays(30)).CallsForModal);     // expired
        Assert.False(SubscriptionStatus.None.CallsForModal);
    }

    [Fact]
    public void SUB_S8_the_tenant_exposes_its_state_from_its_own_trial()
    {
        var client = Client(TenantSource.Subscription, trialDays: 14);
        Assert.Equal(SubscriptionLevel.Warning, client.SubscriptionState(Now.AddDays(12), Grace, null).Level);
        client.Activate(Now.AddDays(12));
        Assert.Equal(SubscriptionLevel.Ok, client.SubscriptionState(Now.AddDays(12), Grace, null).Level);
        Assert.True(client.IsUsableAt(Now.AddDays(40)));
        client.Deactivate("unpaid", Now.AddDays(40));
        Assert.False(client.IsUsableAt(Now.AddDays(40)));
    }

    [Theory]
    [InlineData(7.0, "trial-7", 7)]
    [InlineData(10.2, "trial-3", 3)]
    [InlineData(12.5, "trial-1", 1)]
    [InlineData(13.5, "trial-0", 0)]
    public void SUB_R1_a_trial_warning_is_due_at_the_tightest_threshold_once_per_trial_end(double daysAfter, string prefix, int threshold)
    {
        var state = Trial(Now.AddDays(daysAfter));
        var due = SubscriptionReminders.Due(state, null)!;
        Assert.Equal((SubscriptionReminderKind.TrialEnding, threshold), (due.Kind, due.Threshold));
        Assert.StartsWith(prefix + ":", due.Key, StringComparison.Ordinal);
        Assert.Equal(due.Key, SubscriptionReminders.Due(Trial(Now.AddDays(daysAfter).AddHours(1)), null)!.Key);   // the same key an hour later
    }

    [Fact]
    public void SUB_R2_an_ended_trial_announces_the_grace_once_then_its_last_three_days_and_last_day()
    {
        Assert.Equal(SubscriptionReminderKind.TrialEnded, SubscriptionReminders.Due(Trial(Now.AddDays(14.1)), null)!.Kind);
        var three = SubscriptionReminders.Due(Trial(Now.AddDays(18)), null)!;   // 3 days of grace left
        Assert.Equal((SubscriptionReminderKind.GraceEnding, 3), (three.Kind, three.Threshold));
        var one = SubscriptionReminders.Due(Trial(Now.AddDays(20)), null)!;
        Assert.Equal((SubscriptionReminderKind.GraceEnding, 1), (one.Kind, one.Threshold));
        Assert.StartsWith("grace-1:", one.Key, StringComparison.Ordinal);
        Assert.Null(SubscriptionReminders.Due(Trial(Now.AddDays(25)), null));   // expired: nothing more
        Assert.Null(SubscriptionReminders.Due(Trial(Now), null));               // ok: nothing
    }

    [Fact]
    public void SUB_R3_a_low_wallet_is_due_per_threshold_and_charge_period_and_a_zero_wallet_once_per_zero_episode()
    {
        var wallet = new WalletPosition(250m, Fee, "EGP", null, Now.AddMonths(1));
        var low = SubscriptionReminders.Due(Active(250m, Now), wallet)!;
        Assert.Equal((SubscriptionReminderKind.WalletLow, 3), (low.Kind, low.Threshold));
        Assert.Equal("wallet-3:" + Now.AddMonths(1).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), low.Key);
        Assert.Equal(7, SubscriptionReminders.Due(Active(500m, Now), wallet with { Balance = 500m })!.Threshold);
        Assert.Equal(1, SubscriptionReminders.Due(Active(50m, Now), wallet with { Balance = 50m })!.Threshold);

        var zero = wallet with { Balance = -10m, AtOrBelowZeroSinceUtc = Now };
        var atZero = SubscriptionReminders.Due(Active(-10m, Now.AddDays(1), Now), zero)!;
        Assert.Equal(SubscriptionReminderKind.WalletAtZero, atZero.Kind);
        Assert.Equal(atZero.Key, SubscriptionReminders.Due(Active(-10m, Now.AddDays(20), Now), zero)!.Key);   // same episode, same key
        Assert.Equal(atZero.Key, SubscriptionReminders.Due(Active(-10m, Now.AddMonths(1).AddDays(1), Now), zero)!.Key);   // grace, > 3 days left: the episode's reminder again (already sent)
        Assert.Equal(SubscriptionReminderKind.GraceEnding, SubscriptionReminders.Due(Active(-10m, Now.AddMonths(1).AddDays(5), Now), zero)!.Kind);
        Assert.Null(SubscriptionReminders.Due(Active(700m, Now), wallet with { Balance = 700m }));
    }

    [Fact]
    public void SUB_R4_a_reminder_row_records_the_key_template_and_channels()
    {
        var due = SubscriptionReminders.Due(Trial(Now.AddDays(11)), null)!;
        var row = SubscriptionReminder.Record(Guid.CreateVersion7(), due, "Subscription-Expiry", ["Sms", "Email", "Sms"], Now);
        Assert.Equal((due.Key, "subscription-expiry", "Sms,Email", SubscriptionReminderKind.TrialEnding, Now), (row.DueKey, row.TemplateCode, row.Channels, row.Kind, row.SentAtUtc));
        Assert.Equal(SubscriptionReminder.NoChannel, SubscriptionReminder.Record(Guid.CreateVersion7(), due, "wallet-low", [], Now).Channels);
        Assert.Throws<DomainException>(() => SubscriptionReminder.Record(Guid.Empty, due, "wallet-low", [], Now));
    }

    [Fact]
    public void SUB_W1_the_wallet_marks_the_debit_that_reaches_zero_keeps_the_first_mark_and_a_top_up_above_zero_clears_it()
    {
        var wallet = Wallet.Open(Guid.CreateVersion7(), "EGP", Now);
        Assert.Null(wallet.AtOrBelowZeroSinceUtc);   // a new empty wallet was never debited

        wallet.ChargeSubscription(1000m, "SUB-1", Now);
        Assert.Equal(Now, wallet.AtOrBelowZeroSinceUtc);
        wallet.ChargeSubscription(1000m, "SUB-2", Now.AddMonths(1));
        Assert.Equal(Now, wallet.AtOrBelowZeroSinceUtc);   // the first mark stays

        wallet.TopUp(500m, "INV-1", null, null, Now.AddMonths(1).AddDays(1));
        Assert.Equal(Now, wallet.AtOrBelowZeroSinceUtc);   // still below zero
        wallet.TopUp(2000m, "INV-2", null, null, Now.AddMonths(1).AddDays(2));
        Assert.Null(wallet.AtOrBelowZeroSinceUtc);

        var position = wallet.PositionFor(3000m);
        Assert.Equal((500m, 3000m, "EGP", 2500m), (position.Balance, position.MonthlyFee, position.CurrencyCode, position.AmountToOneMonth));
    }

    [Fact]
    public void SUB_W2_a_free_charge_never_marks_the_wallet()
    {
        var wallet = Wallet.Open(Guid.CreateVersion7(), "EGP", Now);
        wallet.ChargeSubscription(0m, "SUB-0", Now);
        Assert.Null(wallet.AtOrBelowZeroSinceUtc);
    }

    [Fact]
    public void SUB_P1_the_grace_days_are_a_platform_parameter_of_7_days_between_0_and_60()
    {
        Assert.Equal(7, SettingKeys.SubscriptionGraceDays.Default);
        Assert.True(SettingKeys.SubscriptionGraceDays.IsValid("0"));
        Assert.True(SettingKeys.SubscriptionGraceDays.IsValid("60"));
        Assert.False(SettingKeys.SubscriptionGraceDays.IsValid("61"));
        Assert.Contains(SettingKeys.SubscriptionGraceDays, SettingKeys.All);
    }

    [Fact]
    public void SUB_A1_an_administrator_is_the_primary_administrator_or_a_member_managing_the_organization()
    {
        Assert.True(SubscriptionAdministrators.Includes(true, []));
        Assert.True(SubscriptionAdministrators.Includes(false, ["registration.view", "organization.manage"]));
        Assert.False(SubscriptionAdministrators.Includes(false, ["organization.view", "wallet.manage"]));
    }

    [Fact]
    public void SUB_C1_the_expiry_codes_do_not_reuse_the_wizard_codes()
    {
        var wizard = typeof(PlatformAdminCodes).GetFields().Where(field => field.FieldType == typeof(string)).Select(field => (string)field.GetValue(null)!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(SubscriptionCodes.Expired, wizard);
        Assert.DoesNotContain(SubscriptionCodes.AdministratorOnlyBilling, wizard);
    }
}
