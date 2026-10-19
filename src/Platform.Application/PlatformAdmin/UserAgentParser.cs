using System.Text.RegularExpressions;

namespace Platform.Application.PlatformAdmin;

/// <summary>The device class a browser session came from (SA-08b "Client login logs"); <see cref="Unknown"/> when the user agent says nothing usable.</summary>
public enum DeviceClass
{
    Unknown = 0,
    Computer = 1,
    Mobile = 2,
    Tablet = 3,
}

/// <summary>What the stored user-agent string tells about a session's browser (change request LIS-CR-003 §2).</summary>
/// <param name="Device">Computer, Mobile, Tablet or Unknown.</param>
/// <param name="OsFamily">Windows, macOS, iOS, Android, ChromeOS, Linux, Windows Phone or Unknown.</param>
/// <param name="OsVersion">The version as the agent states it (<c>10</c>, <c>14</c>, <c>17.5</c>, <c>10.15.7</c>); <see langword="null"/> when absent.</param>
/// <param name="BrowserFamily">Chrome, Edge, Firefox, Safari, Opera, Samsung Internet, Internet Explorer or Unknown.</param>
/// <param name="BrowserVersion">The major version; <see langword="null"/> when absent.</param>
public sealed record UserAgentInfo(DeviceClass Device, string OsFamily, string? OsVersion, string BrowserFamily, string? BrowserVersion)
{
    public const string UnknownName = "Unknown";

    public static UserAgentInfo Unknown { get; } = new(DeviceClass.Unknown, UnknownName, null, UnknownName, null);

    /// <summary>"Windows 10", "iOS 17.5", "Unknown".</summary>
    public string Os => OsVersion is null ? OsFamily : $"{OsFamily} {OsVersion}";

    /// <summary>"Chrome 129", "Safari 17", "Unknown".</summary>
    public string Browser => BrowserVersion is null ? BrowserFamily : $"{BrowserFamily} {BrowserVersion}";
}

/// <summary>
/// A pure, dependency-free reading of the user-agent strings browsers send (the stored <c>RefreshSession.UserAgent</c>,
/// at most 512 characters). It recognises the families the laboratories use (Chrome, Edge, Firefox, Safari, Opera,
/// Samsung Internet, Internet Explorer on Windows, macOS, iOS, Android, ChromeOS and Linux); anything else is "Unknown"
/// rather than a guess. Client hints are not stored, so Windows 11 reads as Windows 10 (the agent says <c>NT 10.0</c>
/// for both).
/// </summary>
public static partial class UserAgentParser
{
    public static UserAgentInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return UserAgentInfo.Unknown;
        }

        var agent = userAgent.Trim();
        var (osFamily, osVersion) = Os(agent);
        var (browserFamily, browserVersion) = Browser(agent);
        return new UserAgentInfo(Device(agent, osFamily), osFamily, osVersion, browserFamily, browserVersion);
    }

    public static DeviceClass Device(string agent, string osFamily)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (Has(agent, "iPad") || Has(agent, "Tablet") || (Has(agent, "Android") && !Has(agent, "Mobile")))
        {
            return DeviceClass.Tablet;
        }

        if (Has(agent, "Mobile") || Has(agent, "iPhone") || Has(agent, "iPod") || Has(agent, "Windows Phone") || Has(agent, "Android"))
        {
            return DeviceClass.Mobile;
        }

        return osFamily is "Windows" or "macOS" or "Linux" or "ChromeOS" ? DeviceClass.Computer : DeviceClass.Unknown;
    }

    public static (string Family, string? Version) Os(string agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (WindowsPhone().Match(agent) is { Success: true } phone)
        {
            return ("Windows Phone", Dotted(phone.Groups[1].Value));
        }

        if (WindowsNt().Match(agent) is { Success: true } windows)
        {
            return ("Windows", windows.Groups[1].Value switch
            {
                "10.0" => "10",
                "6.3" => "8.1",
                "6.2" => "8",
                "6.1" => "7",
                "6.0" => "Vista",
                "5.1" => "XP",
                var other => other,
            });
        }

        if (Has(agent, "Windows"))
        {
            return ("Windows", null);
        }

        if (Has(agent, "iPhone") || Has(agent, "iPad") || Has(agent, "iPod"))
        {
            var ios = IosVersion().Match(agent);
            return ("iOS", ios.Success ? Dotted(ios.Groups[1].Value) : null);
        }

        if (Has(agent, "Android"))
        {
            var android = AndroidVersion().Match(agent);
            return ("Android", android.Success ? Dotted(android.Groups[1].Value) : null);
        }

        if (Has(agent, "CrOS"))
        {
            return ("ChromeOS", null);
        }

        if (Has(agent, "Mac OS X") || Has(agent, "Macintosh"))
        {
            var mac = MacVersion().Match(agent);
            return ("macOS", mac.Success ? Dotted(mac.Groups[1].Value) : null);
        }

        if (Has(agent, "Linux") || Has(agent, "X11"))
        {
            return ("Linux", null);
        }

        return (UserAgentInfo.UnknownName, null);
    }

    public static (string Family, string? Version) Browser(string agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (Token(agent, EdgeVersion()) is { } edge)
        {
            return ("Edge", edge);
        }

        if (Token(agent, OperaVersion()) is { } opera)
        {
            return ("Opera", opera);
        }

        if (Token(agent, SamsungVersion()) is { } samsung)
        {
            return ("Samsung Internet", samsung);
        }

        if (Token(agent, FirefoxVersion()) is { } firefox)
        {
            return ("Firefox", firefox);
        }

        if (Token(agent, ChromeVersion()) is { } chrome)
        {
            return ("Chrome", chrome);
        }

        if (Has(agent, "Safari") && Token(agent, SafariVersion()) is { } safari)
        {
            return ("Safari", safari);
        }

        if (Has(agent, "Safari") && (Has(agent, "iPhone") || Has(agent, "iPad") || Has(agent, "Macintosh")))
        {
            return ("Safari", null);
        }

        if (Token(agent, MsieVersion()) is { } msie)
        {
            return ("Internet Explorer", msie);
        }

        if (Has(agent, "Trident/"))
        {
            return ("Internet Explorer", Token(agent, TridentRv()));
        }

        return (UserAgentInfo.UnknownName, null);
    }

    private static bool Has(string agent, string token) => agent.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>The major number of the first capture, or null.</summary>
    private static string? Token(string agent, Regex pattern)
    {
        var match = pattern.Match(agent);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? value[..dot] : value;
    }

    private static string Dotted(string value) => value.Replace('_', '.');

    [GeneratedRegex(@"Windows Phone(?: OS)? (\d+(?:[._]\d+)*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPhone();

    [GeneratedRegex(@"Windows NT (\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsNt();

    [GeneratedRegex(@"(?:iPhone |CPU )OS (\d+(?:[._]\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex IosVersion();

    [GeneratedRegex(@"Android (\d+(?:[._]\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex AndroidVersion();

    [GeneratedRegex(@"Mac OS X (\d+(?:[._]\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex MacVersion();

    [GeneratedRegex(@"(?:Edg|EdgA|EdgiOS|Edge)/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeVersion();

    [GeneratedRegex(@"(?:OPR|Opera)/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex OperaVersion();

    [GeneratedRegex(@"SamsungBrowser/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex SamsungVersion();

    [GeneratedRegex(@"(?:Firefox|FxiOS)/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex FirefoxVersion();

    [GeneratedRegex(@"(?:Chrome|CriOS)/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex ChromeVersion();

    [GeneratedRegex(@"Version/(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex SafariVersion();

    [GeneratedRegex(@"MSIE (\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex MsieVersion();

    [GeneratedRegex(@"rv:(\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex TridentRv();
}
