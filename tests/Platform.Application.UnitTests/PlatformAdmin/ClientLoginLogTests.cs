using System.Text;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Queries;
using Platform.Contracts.PlatformAdmin;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>SA-08b client login logs (change request LIS-CR-003 §2): the user-agent reading, the shape rules and the CSV, none of which needs a database.</summary>
public sealed class ClientLoginLogTests
{
    private const string ChromeWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
    private const string EdgeWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.2792.79";
    private const string SafariIphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
    private const string ChromeIpad = "Mozilla/5.0 (iPad; CPU OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/128.0.6613.98 Mobile/15E148 Safari/604.1";
    private const string ChromeAndroidPhone = "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36";
    private const string SamsungAndroidTablet = "Mozilla/5.0 (Linux; Android 13; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Safari/537.36";
    private const string FirefoxMac = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:130.0) Gecko/20100101 Firefox/130.0";
    private const string SafariMac = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15";
    private const string OperaLinux = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 OPR/114.0.0.0";
    private const string InternetExplorer = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";

    [Theory]
    [InlineData(ChromeWindows, DeviceClass.Computer, "Windows 10", "Chrome 129")]
    [InlineData(EdgeWindows, DeviceClass.Computer, "Windows 10", "Edge 129")]
    [InlineData(SafariIphone, DeviceClass.Mobile, "iOS 17.5", "Safari 17")]
    [InlineData(ChromeIpad, DeviceClass.Tablet, "iOS 16.6", "Chrome 128")]
    [InlineData(ChromeAndroidPhone, DeviceClass.Mobile, "Android 14", "Chrome 128")]
    [InlineData(SamsungAndroidTablet, DeviceClass.Tablet, "Android 13", "Samsung Internet 25")]
    [InlineData(FirefoxMac, DeviceClass.Computer, "macOS 10.15", "Firefox 130")]
    [InlineData(SafariMac, DeviceClass.Computer, "macOS 10.15.7", "Safari 17")]
    [InlineData(OperaLinux, DeviceClass.Computer, "Linux", "Opera 114")]
    [InlineData(InternetExplorer, DeviceClass.Computer, "Windows 7", "Internet Explorer 11")]
    public void The_user_agent_parser_reads_device_os_and_browser(string agent, DeviceClass device, string os, string browser)
    {
        var info = UserAgentParser.Parse(agent);
        Assert.Equal((device, os, browser), (info.Device, info.Os, info.Browser));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curl/8.4.0")]
    [InlineData("Platform.Tests/1.0")]
    public void An_absent_or_unrecognised_agent_reads_as_Unknown(string? agent)
    {
        var info = UserAgentParser.Parse(agent);
        Assert.Equal((DeviceClass.Unknown, "Unknown", "Unknown"), (info.Device, info.Os, info.Browser));
    }

    [Fact]
    public void The_parser_never_throws_on_a_hostile_agent()
    {
        var hostile = new string('(', 600) + "Chrome/" + new string('9', 100) + " Windows NT";
        var info = UserAgentParser.Parse(hostile);
        Assert.Equal(("Windows", "Chrome"), (info.OsFamily, info.BrowserFamily));
    }

    [Fact]
    public void The_list_validator_refuses_reversed_windows_negative_or_crossed_minutes_and_unknown_names()
    {
        var validator = new ListClientLoginLogsValidator();
        var at = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
        var valid = new ListClientLoginLogsQuery(null, "noor", at, at.AddHours(1), at, at.AddHours(2), "mobile", 0, 60, "signedout", 1, 50, "duration", "desc");
        Assert.True(validator.Validate(valid).IsValid);

        Assert.Contains("LoginToUtc", Failures(validator, valid with { LoginFromUtc = at.AddHours(2) }));
        Assert.Contains("LogoutToUtc", Failures(validator, valid with { LogoutFromUtc = at.AddHours(3) }));
        Assert.Contains("MinMinutes", Failures(validator, valid with { MinMinutes = -1 }));
        Assert.Contains("MaxMinutes", Failures(validator, valid with { MinMinutes = 90, MaxMinutes = 60 }));
        Assert.Contains("Device", Failures(validator, valid with { Device = "Watch" }));
        Assert.Contains("Status", Failures(validator, valid with { Status = "Open" }));
        Assert.Contains("Sort", Failures(validator, valid with { Sort = "ipAddress" }));
        Assert.Contains("Direction", Failures(validator, valid with { Direction = "down" }));
        Assert.Contains("PageSize", Failures(validator, valid with { PageSize = 500 }));
        Assert.Contains("Search", Failures(validator, valid with { Search = new string('x', 101) }));
    }

    [Fact]
    public void The_export_validator_shares_the_rules_and_knows_two_languages()
    {
        var validator = new ExportClientLoginLogsValidator();
        var query = new ExportClientLoginLogsQuery(null, null, null, null, null, null, null, null, null, null, "ar");
        Assert.True(validator.Validate(query).IsValid);
        Assert.Contains("Language", Failures(validator, query with { Language = "fr" }));
        Assert.Contains("MaxMinutes", Failures(validator, query with { MinMinutes = 10, MaxMinutes = 5 }));
    }

    [Fact]
    public void Every_sort_key_of_the_login_logs_is_accepted_and_the_filter_canonicalises_names()
    {
        var validator = new ListClientLoginLogsValidator();
        foreach (var key in PlatformAdminSortKeys.ClientLoginLogs.Names)
        {
            Assert.True(validator.Validate(new ListClientLoginLogsQuery(null, null, null, null, null, null, null, null, null, null, null, null, key, ListSort.Ascending)).IsValid, key);
        }

        var filter = ClientLoginLogRules.FilterOf(new ListClientLoginLogsQuery(null, "  noor ", null, null, null, null, "TABLET", null, null, "expired", null, null));
        Assert.Equal(("noor", "Expired"), (filter.Search, filter.Status));
        Assert.Equal("Tablet", ClientLoginLogRules.DeviceOf("TABLET"));
        Assert.Null(ClientLoginLogRules.DeviceOf(" "));
    }

    [Fact]
    public void The_row_becomes_an_item_with_the_parsed_agent()
    {
        var at = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
        var row = new ClientLoginLogRow(Guid.CreateVersion7(), Guid.CreateVersion7(), "Al Noor", "النور", Guid.CreateVersion7(), "noor.admin", "Noor Admin", at, at.AddMinutes(42), at.AddMinutes(40), "10.0.0.7", SafariIphone, 42, "SignedOut", null);
        var item = ClientLoginLogRules.ToItem(row);
        Assert.Equal(("Mobile", "iOS 17.5", "Safari 17", 42, "SignedOut", "10.0.0.7"), (item.Device, item.Os, item.Browser, item.DurationMinutes, item.Status, item.IpAddress));
    }

    [Fact]
    public void The_csv_carries_a_byte_order_mark_the_headers_of_the_language_and_formula_safe_cells()
    {
        var at = new DateTimeOffset(2026, 10, 19, 9, 5, 0, TimeSpan.Zero);
        var item = new ClientLoginLogItem(Guid.CreateVersion7(), Guid.CreateVersion7(), "Al Noor, Cairo", "النور", Guid.CreateVersion7(), "=cmd", "Noor \"Admin\"", at, at.AddMinutes(90), at.AddMinutes(85), "10.0.0.7", "Computer", "Windows 10", "Chrome 129", 90, "Revoked", "Admin");

        var english = ClientLoginLogCsv.Write([item], arabic: false);
        Assert.Equal(Encoding.UTF8.GetPreamble(), english[..3]);
        var lines = Encoding.UTF8.GetString(english[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Client,User,Username,Login (UTC),Logout (UTC),IP address,Device,OS,Browser,Duration (min),Status,Reason", lines[0]);
        Assert.Equal("\"Al Noor, Cairo\",\"Noor \"\"Admin\"\"\",'=cmd,2026-10-19 09:05:00,2026-10-19 10:35:00,10.0.0.7,Computer,Windows 10,Chrome 129,90,Revoked,Admin", lines[1]);

        var arabic = Encoding.UTF8.GetString(ClientLoginLogCsv.Write([item with { ClientNameAr = null }], arabic: true)[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("العميل,المستخدم", arabic[0], StringComparison.Ordinal);
        Assert.StartsWith("\"Al Noor, Cairo\"", arabic[1], StringComparison.Ordinal);   // the English name when the Arabic one is missing
    }

    private static List<string> Failures<T>(FluentValidation.AbstractValidator<T> validator, T query) =>
        validator.Validate(query).Errors.Select(error => error.PropertyName).ToList();
}
