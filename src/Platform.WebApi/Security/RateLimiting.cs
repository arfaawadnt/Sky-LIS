using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Contracts.Common;
using Platform.WebApi.Middleware;
using Platform.WebApi.Startup;

namespace Platform.WebApi.Security;

/// <summary>
/// The rate-limit matrix of 02 §5 (C-13, pitfall P-40). Sliding windows, partitioned by what an attack targets: the
/// account for credential guessing (independent of the client address, with a per-address backstop), the session
/// for refresh, the client address for the challenge and the global budget, the tenant for everything authenticated,
/// the actor for mutations. A 429 never reaches a handler, so it never counts toward a lockout. Counters are
/// in-process; with more than one replica the documented limits are per replica until the shared store lands.
/// </summary>
public static class RateLimiting
{
    public const string CredentialAccount = "credential-account";
    public const string CredentialChallenge = "credential-challenge";
    public const string Refresh = "refresh";
    public const string Global = "global";
    public const string PolicyHeader = "RateLimit-Policy";
    public const string AccountKeyItem = "platform.credential-account-key";
    public const string RefreshSessionItem = "platform.refresh-session-id";

    /// <summary>02 §5.2 <c>machine-token</c>: <c>POST /auth/token</c> and <c>/auth/token/refresh</c>, keyed by the client id (the refresh token's session for a refresh), sliding 1 min, 30 permits.</summary>
    public const string MachineToken = "machine-token";
    public const string MachineClientItem = "platform.machine-client-key";
    public const int MachineTokenPerMinute = 30;

    private static readonly string[] CredentialSegments = ["sign-in", "reauthenticate", "mfa/verify", "password", "recovery"];

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetSlidingWindowLimiter("global:" + ClientIp(context), _ => Sliding(Settings(context).GlobalPerMinute, TimeSpan.FromMinutes(1), 6))),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsCredentialEndpoint(context)
                    ? RateLimitPartition.GetSlidingWindowLimiter("credential-client:" + ClientIp(context), _ => Sliding(Settings(context).CredentialClientPerMinute, TimeSpan.FromMinutes(1), 6))
                    : RateLimitPartition.GetNoLimiter("none")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => TenantOf(context) is { } tenant
                    ? RateLimitPartition.GetSlidingWindowLimiter("tenant:" + tenant, _ => Sliding(Settings(context).TenantPerMinute, TimeSpan.FromMinutes(1), 6))
                    : RateLimitPartition.GetNoLimiter("none")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsMutation(context) && UserOf(context) is { } user
                    ? RateLimitPartition.GetTokenBucketLimiter("mutation:" + user, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = Settings(context).MutationBurst,
                        TokensPerPeriod = Settings(context).MutationPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                    : RateLimitPartition.GetNoLimiter("none")));

            options.AddPolicy(CredentialAccount, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    "credential-account:" + (context.Items[AccountKeyItem] as string ?? "address:" + ClientIp(context)),
                    _ => Sliding(Settings(context).CredentialAccountPer10Minutes, TimeSpan.FromMinutes(10), 10)));

            options.AddPolicy(CredentialChallenge, context =>
                RateLimitPartition.GetSlidingWindowLimiter("credential-challenge:" + ClientIp(context), _ => Sliding(Settings(context).CredentialChallengePerMinute, TimeSpan.FromMinutes(1), 6)));

            options.AddPolicy(Refresh, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    "refresh:" + (context.Items[RefreshSessionItem] as string ?? "address:" + ClientIp(context)),
                    _ => Sliding(Settings(context).RefreshPerMinute, TimeSpan.FromMinutes(1), 6)));

            options.AddPolicy(MachineToken, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    "machine-token:" + (context.Items[MachineClientItem] as string ?? "address:" + ClientIp(context)),
                    _ => Sliding(MachineTokenPerMinute, TimeSpan.FromMinutes(1), 6)));
        });

        return services;
    }

    /// <summary>
    /// <c>sha256(lower(username))</c>, never the username itself (02 §5.3). LIS-CR-005: keyed on the account alone, whatever
    /// laboratory address the request names, so cycling addresses never buys an account more guesses (the lockout is per user too).
    /// </summary>
    public static string AccountKey(string? username) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(":" + (username ?? string.Empty).ToLowerInvariant()))).ToLowerInvariant();

    public static bool IsCredentialEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.StartsWithSegments(RefreshCookie.CookiePath, out var rest)
        && CredentialSegments.Any(segment => rest.StartsWithSegments("/" + segment));

    private static bool IsMutation(HttpContext context) =>
        !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        && !context.Request.Path.StartsWithSegments(RefreshCookie.CookiePath);

    private static RateLimitSettings Settings(HttpContext context) => context.RequestServices.GetRequiredService<HostSecuritySettings>().RateLimits;

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string? TenantOf(HttpContext context) => context.User.FindFirst("tid")?.Value;

    private static string? UserOf(HttpContext context) => context.User.FindFirst("sub")?.Value;

    private static SlidingWindowRateLimiterOptions Sliding(int permits, TimeSpan window, int segments) => new()
    {
        PermitLimit = Math.Max(1, permits),
        Window = window,
        SegmentsPerWindow = segments,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "60";
        response.Headers[PolicyHeader] = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? Global;
        await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "Too many requests.", ProblemCodes.RateLimited);
    }
}

/// <summary>
/// Reads, before the limiter, the identifiers the account- and session-keyed partitions need: a size-capped (4 KB)
/// buffered copy of the sign-in body for the account hash, and the session id prefix of the presented refresh token.
/// The body is rewound for model binding; nothing is logged.
/// </summary>
public sealed class CredentialAccountKeyMiddleware(RequestDelegate next)
{
    public const int MaxBodyBytes = 4096;
    private static readonly PathString SignInPath = RefreshCookie.CookiePath + "/sign-in";
    private static readonly PathString RecoveryPath = RefreshCookie.CookiePath + "/recovery";
    private static readonly PathString RefreshPath = RefreshCookie.CookiePath + "/refresh";
    private static readonly PathString SignOutPath = RefreshCookie.CookiePath + "/sign-out";
    private static readonly PathString MachineTokenPath = RefreshCookie.CookiePath + "/token";
    private static readonly PathString MachineRefreshPath = RefreshCookie.CookiePath + "/token/refresh";

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (context.Request.Path.Equals(MachineTokenPath) || context.Request.Path.Equals(MachineRefreshPath))
            {
                // machine-token (02 §5.2): the client id of the exchange, or the session of the presented refresh token (one client per session).
                if (MachineKey(await ReadBodyAsync(context)) is { } key)
                {
                    context.Items[RateLimiting.MachineClientItem] = key;
                }
            }
            else if (context.Request.Path.Equals(SignInPath) || context.Request.Path.StartsWithSegments(RecoveryPath))
            {
                // Sign-in keys on the username; recovery (CP-03) on the identifier the caller presents (username or mobile).
                var body = await ReadBodyAsync(context);
                if (body is not null && TryReadAccount(body.Value, out var username))
                {
                    context.Items[RateLimiting.AccountKeyItem] = RateLimiting.AccountKey(username);
                }
            }
            else if (context.Request.Path.Equals(RefreshPath) || context.Request.Path.Equals(SignOutPath))
            {
                var token = RefreshCookie.Read(context);
                if (token is null)
                {
                    var body = await ReadBodyAsync(context);
                    token = body is not null && body.Value.ValueKind == JsonValueKind.Object && body.Value.TryGetProperty("refreshToken", out var property) ? property.GetString() : null;
                }

                if (token is { Length: > 32 } && token[32] == '.' && Guid.TryParseExact(token.AsSpan(0, 32), "N", out var sessionId))
                {
                    context.Items[RateLimiting.RefreshSessionItem] = sessionId.ToString("N");
                }
            }
        }

        await next(context);
    }

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.ContentLength is null or > MaxBodyBytes || !request.HasJsonContentType())
        {
            return null;
        }

        request.EnableBuffering(MaxBodyBytes);
        var buffer = new byte[MaxBodyBytes];
        var read = await request.Body.ReadAtLeastAsync(buffer, MaxBodyBytes, throwOnEndOfStream: false, context.RequestAborted);
        request.Body.Position = 0;

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, read));
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary><c>client:&lt;client id&gt;</c> for an exchange, <c>session:&lt;session id&gt;</c> for a refresh, capped at 80 characters; never the secret.</summary>
    private static string? MachineKey(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        string? key = null;
        if (root.TryGetProperty("clientId", out var clientId) && clientId.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(clientId.GetString()))
        {
            key = "client:" + clientId.GetString();
        }
        else if (root.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String && refresh.GetString() is { Length: > 32 } token && token[32] == '.')
        {
            key = "session:" + token[..32];
        }

        return key is null ? null : key.Length <= 80 ? key : key[..80];
    }

    private static bool TryReadAccount(JsonElement root, out string? username)
    {
        username = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if ((property.NameEquals("username") || property.NameEquals("usernameOrMobile")) && property.Value.ValueKind == JsonValueKind.String)
            {
                username = property.Value.GetString();
            }
        }

        return !string.IsNullOrWhiteSpace(username);
    }
}
