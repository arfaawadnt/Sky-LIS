using Platform.Application.Identity;
using Platform.Infrastructure.Jobs;
using Platform.Infrastructure.Notifications;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.Infrastructure.Security;

namespace Platform.WebApi.Startup;

/// <summary>Rate-limit matrix of 02 §5.2; every value is configuration under <c>Lis:Security:RateLimits</c>.</summary>
public sealed class RateLimitSettings
{
    public int GlobalPerMinute { get; init; } = 600;

    public int TenantPerMinute { get; init; } = 3000;

    public int CredentialAccountPer10Minutes { get; init; } = 20;

    public int CredentialClientPerMinute { get; init; } = 60;

    public int CredentialChallengePerMinute { get; init; } = 120;

    public int RefreshPerMinute { get; init; } = 30;

    public int MutationBurst { get; init; } = 120;

    public int MutationPerSecond { get; init; } = 2;
}

/// <summary>Host-level security switches (02 §2). One behaviour in every environment; relaxations are explicit, named flags (C-07).</summary>
public sealed class HostSecuritySettings
{
    /// <summary>
    /// Loopback HTTP development only: the refresh cookie becomes <c>rt</c> without <c>Secure</c> and HSTS is not
    /// emitted. The bind guard refuses a non-loopback bind regardless. Writes <c>SECURITY_RELAXATION_ACTIVE</c> at start-up.
    /// </summary>
    public bool AllowInsecureLocalDevelopment { get; init; }

    /// <summary>The public origins of the deployment, for the cookie-bearing endpoints' origin check. Empty = the request's own origin (same-origin deployment).</summary>
    public string[] AllowedOrigins { get; init; } = [];

    public RateLimitSettings RateLimits { get; init; } = new();
}

/// <summary>
/// Reads the LIS configuration (02 §6.4). Every key lives under the <c>Lis</c> root: this host carries other
/// applications' generic variables (<c>ConnectionStrings__Postgres</c>, <c>Jwt__Secret</c>, <c>ASPNETCORE_URLS</c>),
/// and a generic key name would silently read theirs. Secrets come from user-secrets or the environment
/// (<c>Lis__Jwt__SigningKey</c> …), never from appsettings.
/// </summary>
public static class SecurityConfiguration
{
    public const string Root = "Lis";
    public const string ConnectionStringKey = Root + ":Database:ConnectionString";

    public static string? ReadConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[ConnectionStringKey];
    }

    public static HostSecuritySettings ReadHost(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var security = configuration.GetSection(Root + ":Security");
        return new HostSecuritySettings
        {
            AllowInsecureLocalDevelopment = security.GetValue("AllowInsecureLocalDevelopment", false),
            AllowedOrigins = security.GetSection("AllowedOrigins").Get<string[]>() ?? [],
            RateLimits = security.GetSection("RateLimits").Get<RateLimitSettings>() ?? new RateLimitSettings(),
        };
    }

    public static SecurityOptions ReadOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaults = new SecurityOptions();
        var jwt = configuration.GetSection(Root + ":Jwt");
        var envelope = configuration.GetSection(Root + ":Security:Envelope");
        return new SecurityOptions
        {
            Issuer = jwt["Issuer"] ?? string.Empty,
            Audience = jwt["Audience"] ?? defaults.Audience,
            SigningKeyPem = jwt["SigningKey"] ?? string.Empty,
            SigningKeyId = jwt["SigningKeyId"],
            SubjectEncryptionKey = jwt["SubjectEncryptionKey"] ?? string.Empty,
            PreviousSubjectEncryptionKeys = jwt.GetSection("PreviousSubjectEncryptionKeys").Get<string[]>() ?? [],
            MasterKey = configuration[Root + ":DataProtection:MasterKey"] ?? string.Empty,
            ChallengeLifetime = envelope.GetValue("ChallengeLifetime", defaults.ChallengeLifetime),
            EnvelopeKeyRotation = envelope.GetValue("KeyRotation", defaults.EnvelopeKeyRotation),
            EnvelopeKeyOverlap = envelope.GetValue("KeyOverlap", defaults.EnvelopeKeyOverlap),
            EnvelopeRsaKeySize = envelope.GetValue("RsaKeySize", defaults.EnvelopeRsaKeySize),
            RevealOtpInDevelopmentLog = configuration.GetSection(Root + ":Security").GetValue("AllowInsecureLocalDevelopment", false),
        };
    }

    public static SessionPolicy ReadSessionPolicy(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(Root + ":Security:Session").Get<SessionPolicy>() ?? new SessionPolicy();
    }

    /// <summary><c>Lis:Outbox</c> (01 §7): <c>Enabled</c> (default true), <c>PollInterval</c>, <c>BatchSize</c>, <c>Lease</c>, <c>MaxAttempts</c>.</summary>
    public static OutboxOptions ReadOutbox(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(Root + ":Outbox").Get<OutboxOptions>() ?? new OutboxOptions();
    }

    /// <summary><c>Lis:Jobs:WalletCharge</c> (W2 monthly subscription charge): <c>Enabled</c> (default true), <c>PollInterval</c> (default one hour).</summary>
    public static WalletChargeOptions ReadWalletCharge(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(Root + ":Jobs:WalletCharge").Get<WalletChargeOptions>() ?? new WalletChargeOptions();
    }

    /// <summary><c>Lis:Notifications</c>: pickup directory, SMTP and HTTP SMS gateway (NotificationOptions); nothing configured = the logging sender.</summary>
    public static NotificationOptions ReadNotifications(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(Root + ":Notifications").Get<NotificationOptions>() ?? new NotificationOptions();
    }
}
