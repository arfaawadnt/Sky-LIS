using System.Text;
using Platform.Application.Account;
using Platform.Application.Account.Queries;
using Platform.Contracts.Audit;
using Platform.Domain.Account;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Account;

/// <summary>The pure rules of the Account module: the password policy (02 §6.1 as decided on 2026-10-09), the CSV of the audit export and the dashboard's local-day arithmetic.</summary>
public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("short", 8, AccountCodes.PasswordLengthInvalid)]
    [InlineData("1234567", 8, AccountCodes.PasswordLengthInvalid)]
    [InlineData("nine-char", 10, AccountCodes.PasswordLengthInvalid)]
    [InlineData("xx-LAB.ADMIN-2026", 8, AccountCodes.PasswordContainsUsername)]
    public void The_password_rule_refuses_length_and_the_username(string password, int minimum, string code)
    {
        var refusal = Assert.Throws<DomainException>(() => PasswordRule.Check(password, "lab.admin", minimum));

        Assert.Equal((code, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("all lower case words without rules")]
    [InlineData("كلمة مرور عربية طويلة")]
    public void The_password_rule_has_no_composition_rules(string password) => PasswordRule.Check(password, "lab.admin", 8);

    [Fact]
    public void The_password_rule_refuses_more_than_128_characters()
    {
        PasswordRule.Check(new string('p', PasswordRule.MaxLength), "lab.admin", 8);
        Assert.Equal(AccountCodes.PasswordLengthInvalid, Assert.Throws<DomainException>(() => PasswordRule.Check(new string('p', PasswordRule.MaxLength + 1), "lab.admin", 8)).Code);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+20100", "'+20100")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    public void Csv_cells_are_quoted_and_formula_safe(string value, string expected) => Assert.Equal(expected, AuditCsv.Escape(value));

    [Fact]
    public void The_export_has_a_bom_a_header_row_and_one_row_per_entry_with_the_changes_as_details()
    {
        var change = new AuditEntryItem(
            Guid.CreateVersion7(), AuditEntryItem.KindFieldChange, new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero), Guid.CreateVersion7(), "Mona Adel",
            "Treasury", "t-1", "Modified", "corr-1", [new FieldChangeItem("Name.En", "Main", "Main drawer"), new FieldChangeItem("IsActive", "True", null)], "renamed", null, null);
        var trail = new AuditEntryItem(
            Guid.CreateVersion7(), AuditEntryItem.KindEvent, new DateTimeOffset(2026, 10, 9, 8, 31, 0, TimeSpan.Zero), null, null,
            "TreasuryAccessRequest", "r-1", "TreasuryAccessApproved", null, null, null, 42, "{\"number\":\"TRQ-2026-00001\"}");

        var bytes = AuditCsv.Write([change, trail], arabic: false);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Time (UTC),User,Action,Entity,Entity id,Details", lines[0], StringComparison.Ordinal);
        Assert.Equal("2026-10-09 08:30:00,Mona Adel,Modified,Treasury,t-1,Name.En: Main → Main drawer; IsActive: True → ∅,renamed,FieldChange,,corr-1", lines[1]);
        Assert.Equal("2026-10-09 08:31:00,,TreasuryAccessApproved,TreasuryAccessRequest,r-1,\"{\"\"number\"\":\"\"TRQ-2026-00001\"\"}\",,Event,42,", lines[2]);

        var arabic = Encoding.UTF8.GetString(AuditCsv.Write([], arabic: true)[3..]);
        Assert.StartsWith("الوقت (UTC),المستخدم", arabic, StringComparison.Ordinal);
    }

    [Fact]
    public void The_platform_export_names_the_client_of_each_entry_in_the_export_language()
    {
        var aboutClient = new AuditEntryItem(
            Guid.CreateVersion7(), AuditEntryItem.KindFieldChange, new DateTimeOffset(2026, 10, 11, 16, 40, 0, TimeSpan.Zero), null, "Platform Admin",
            "Tenant", "c-1", "Modified", null, [new FieldChangeItem("Address", "12 V12 Street", "14 V12 Street")], null, null, null,
            Guid.CreateVersion7(), "V12 Retest Lab", "مختبر إعادة الاختبار");
        var platformOwn = new AuditEntryItem(
            Guid.CreateVersion7(), AuditEntryItem.KindFieldChange, new DateTimeOffset(2026, 10, 11, 16, 41, 0, TimeSpan.Zero), null, "Platform Admin",
            "Plan", "p-1", "Modified", null, [], null, null, null);

        var english = Encoding.UTF8.GetString(AuditCsv.Write([aboutClient, platformOwn], arabic: false, includeClient: true)[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Time (UTC),User,Client,Action,Entity", english[0], StringComparison.Ordinal);
        Assert.StartsWith("2026-10-11 16:40:00,Platform Admin,V12 Retest Lab,Modified,Tenant,c-1,", english[1], StringComparison.Ordinal);
        Assert.StartsWith("2026-10-11 16:41:00,Platform Admin,,Modified,Plan,p-1,", english[2], StringComparison.Ordinal);

        var arabic = Encoding.UTF8.GetString(AuditCsv.Write([aboutClient], arabic: true, includeClient: true)[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("الوقت (UTC),المستخدم,العميل,الإجراء", arabic[0], StringComparison.Ordinal);
        Assert.Contains(",مختبر إعادة الاختبار,", arabic[1], StringComparison.Ordinal);

        // A laboratory's log has no client column.
        Assert.StartsWith("Time (UTC),User,Action", Encoding.UTF8.GetString(AuditCsv.Write([], arabic: false)[3..]), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_day_is_the_local_day_of_the_offset()
    {
        var window = DashboardDays.Window(new DateOnly(2026, 10, 9), 180);   // Cairo summer time, UTC+3

        Assert.Equal(new DateTimeOffset(2026, 10, 8, 21, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero), window.ToUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 21, 0, 0, TimeSpan.Zero), window.ChartFromUtc);
        Assert.Equal(180, window.UtcOffsetMinutes);
    }

    [Fact]
    public void The_chart_has_seven_bars_oldest_first_with_zero_for_quiet_days_and_none_without_a_source()
    {
        var bars = DashboardDays.Chart(new DateOnly(2026, 10, 9), new Dictionary<string, int> { ["2026-10-09"] = 12, ["2026-10-04"] = 3 });

        Assert.Equal(["2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-09"], bars.Select(bar => bar.Date).ToArray());
        Assert.Equal([0, 3, 0, 0, 0, 0, 12], bars.Select(bar => bar.Count).ToArray());
        Assert.Empty(DashboardDays.Chart(new DateOnly(2026, 10, 9), null));
    }
}
