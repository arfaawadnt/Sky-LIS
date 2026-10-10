using System.Text.RegularExpressions;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>Coded refusals for the module's value objects (aggregates use their own <c>Require</c>).</summary>
internal static class IntegrationGuard
{
    public static void Require(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainException(code, message, DomainFault.Validation);
        }
    }

    /// <summary>Trimmed text, or <see langword="null"/> when blank; refused over <paramref name="maxLength"/> characters.</summary>
    public static string? Optional(string? value, int maxLength, string code, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        Require(trimmed.Length <= maxLength && !trimmed.Any(c => char.IsControl(c) && c != '\t'), code, $"{field} is at most {maxLength} characters.");
        return trimmed;
    }

    /// <summary>Trimmed, required text of 1 to <paramref name="maxLength"/> characters.</summary>
    public static string Required(string? value, int maxLength, string code, string field)
    {
        var trimmed = (value ?? string.Empty).Trim();
        Require(trimmed.Length is >= 1 && trimmed.Length <= maxLength && !trimmed.Any(char.IsControl), code, $"{field} must be 1 to {maxLength} characters.");
        return trimmed;
    }
}

/// <summary>
/// The link the site agent uses to reach the instrument (CP-58; LDM manual §13.3): serial (<c>9600,n,8,1</c> on a COM
/// port), TCP/IP (host and port; the agent is the client or the server) or a shared folder. Only the fields of the kind
/// are kept; the others are cleared so a stale value never travels to the agent.
/// </summary>
public sealed partial class AnalyserConnection : ValueObject
{
    public const int MaxHostLength = 253;
    public const int MaxFolderLength = 260;
    public const int MaxSerialPortLength = 64;
    public const int MaxSerialSettingsLength = 20;

    private AnalyserConnection()
    {
    }

    private AnalyserConnection(ConnectionKind kind, string? serialSettings, string? serialPort, string? host, int? port, TcpRole? tcpRole, string? folderPath)
    {
        Kind = kind;
        SerialSettings = serialSettings;
        SerialPort = serialPort;
        Host = host;
        Port = port;
        TcpRole = tcpRole;
        FolderPath = folderPath;
    }

    public ConnectionKind Kind { get; private set; }

    /// <summary><c>&lt;baud&gt;,&lt;parity&gt;,&lt;data bits&gt;,&lt;stop bits&gt;</c>, normalised to lower-case parity: <c>9600,n,8,1</c>.</summary>
    public string? SerialSettings { get; private set; }

    /// <summary><c>COM3</c> or <c>/dev/ttyS0</c>.</summary>
    public string? SerialPort { get; private set; }

    public string? Host { get; private set; }

    public int? Port { get; private set; }

    public TcpRole? TcpRole { get; private set; }

    public string? FolderPath { get; private set; }

    public static AnalyserConnection Serial(string? settings, string? port)
    {
        var cleaned = (settings ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        var match = SerialPattern().Match(cleaned);
        IntegrationGuard.Require(match.Success, IntegrationCodes.SerialSettingsInvalid, "Serial settings must look like 9600,n,8,1 (baud, parity n/e/o/m/s, data bits 5–8, stop bits 1, 1.5 or 2).");
        var baud = int.Parse(match.Groups["baud"].Value, System.Globalization.CultureInfo.InvariantCulture);
        IntegrationGuard.Require(baud is >= 300 and <= 921600, IntegrationCodes.SerialSettingsInvalid, "The baud rate must be between 300 and 921600.");

        var cleanedPort = (port ?? string.Empty).Trim();
        IntegrationGuard.Require(PortPattern().IsMatch(cleanedPort), IntegrationCodes.SerialSettingsInvalid, "The serial port must look like COM3 or /dev/ttyS0.");
        if (cleanedPort.StartsWith("com", StringComparison.OrdinalIgnoreCase))
        {
            cleanedPort = cleanedPort.ToUpperInvariant();
        }

        return new AnalyserConnection(ConnectionKind.Serial, cleaned, cleanedPort, null, null, null, null);
    }

    public static AnalyserConnection Tcp(string? host, int? port, TcpRole role)
    {
        IntegrationGuard.Require(port is >= 1 and <= 65535, IntegrationCodes.TcpEndpointInvalid, "The TCP port must be between 1 and 65535.");
        var cleanedHost = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        IntegrationGuard.Require(cleanedHost is null || (cleanedHost.Length <= MaxHostLength && HostPattern().IsMatch(cleanedHost)), IntegrationCodes.TcpEndpointInvalid, "The host must be a host name or an IP address.");
        IntegrationGuard.Require(role == Integration.TcpRole.Server || cleanedHost is not null, IntegrationCodes.TcpEndpointInvalid, "A TCP client needs the instrument's host.");
        return new AnalyserConnection(ConnectionKind.Tcp, null, null, cleanedHost, port, role, null);
    }

    public static AnalyserConnection File(string? folderPath)
    {
        var cleaned = (folderPath ?? string.Empty).Trim();
        IntegrationGuard.Require(cleaned.Length is >= 1 and <= MaxFolderLength && !cleaned.Any(char.IsControl) && cleaned.IndexOfAny(['"', '<', '>', '|', '?', '*']) < 0, IntegrationCodes.FolderPathInvalid, $"The folder must be a path of 1 to {MaxFolderLength} characters.");
        return new AnalyserConnection(ConnectionKind.File, null, null, null, null, null, cleaned);
    }

    /// <summary>Builds the link of <paramref name="kind"/> from loose fields (the API shape); unrelated fields are ignored.</summary>
    public static AnalyserConnection From(ConnectionKind kind, string? serialSettings, string? serialPort, string? host, int? port, TcpRole? tcpRole, string? folderPath) => kind switch
    {
        ConnectionKind.Serial => Serial(serialSettings, serialPort),
        ConnectionKind.Tcp => Tcp(host, port, tcpRole ?? Integration.TcpRole.Client),
        ConnectionKind.File => File(folderPath),
        _ => throw new DomainException(IntegrationCodes.ModeConnectionMismatch, "Unknown connection kind.", DomainFault.Validation),
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return SerialSettings;
        yield return SerialPort;
        yield return Host;
        yield return Port;
        yield return TcpRole;
        yield return FolderPath;
    }

    [GeneratedRegex(@"^(?<baud>\d{3,6}),(?<parity>[noems]),(?<data>[5-8]),(?<stop>1|1\.5|2)$", RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    [GeneratedRegex(@"^(COM[1-9]\d{0,2}|/dev/[A-Za-z0-9/_.-]{1,58})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PortPattern();

    [GeneratedRegex(@"^[A-Za-z0-9.:\-\[\]]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}
