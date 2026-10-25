using Platform.Application.Abstractions.Security;
using Platform.Domain.PlatformAdmin;
using Platform.WebApi.Security;

namespace Platform.WebApi.Middleware;

/// <summary>
/// The expired subscription (change request LIS-CR-012, decision D-3 on its recommendation). Once a laboratory's grace period is
/// over only its administrators keep a live session (primary administrator or <c>organization.manage</c>; the principal of any
/// other member is no longer live and sign-in refuses it <c>SUB-020</c>), and they reach only what settles the subscription: the
/// <c>/api/v1/auth</c> surface, the own profile, password and preferences, the laboratory logo and privileges the shell reads,
/// the selectable branches, the wallet (<c>/api/v1/wallet/**</c>: balance, history, top-up request), <c>/api/v1/account/wallet</c>
/// and <c>/api/v1/account/subscription</c>, and support tickets (to contact National Technology). Every other API request is
/// <c>403 SUB-021</c>; a non-administrator that still carries an expired principal gets <c>403 SUB-020</c>. Non-API paths (health,
/// JWKS) are not gated; anonymous requests pass (they carry no principal).
/// <para>
/// Placed after <c>PasswordChangeGate</c> and before <c>Authorization</c> (proposal for <c>PipelineOrder</c>): a user who must
/// change the password does that first. The principal is cached for up to 10 s per session, so an "expired" answer is re-read
/// once before refusing — a top-up by the platform takes effect on the next request.
/// </para>
/// </summary>
public sealed class SubscriptionExpiredGateMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiRoot = "/api";

    private static readonly (string Method, PathString Path, bool Prefix)[] Allowed =
    [
        ("*", RefreshCookie.CookiePath, true),                                    // /api/v1/auth/** (challenge, refresh, sign-out, me, select-branch)
        (HttpMethods.Get, "/api/v1/account/profile", false),
        (HttpMethods.Post, "/api/v1/account/password", false),
        (HttpMethods.Put, "/api/v1/account/preferences", false),
        (HttpMethods.Get, "/api/v1/account/laboratory/logo", false),
        ("*", "/api/v1/account/wallet", true),
        ("*", "/api/v1/account/subscription", true),
        ("*", "/api/v1/wallet", true),                                            // CP-66: balance, transactions, top-up request
        ("*", "/api/v1/support/tickets", true),                                   // CP-67: contact National Technology
        (HttpMethods.Get, "/api/v1/organization/me/privileges", false),
        (HttpMethods.Get, "/api/v1/organization/branches/selectable", false),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items[EncryptedSubjectEvents.PrincipalItem] is SessionPrincipal { Subscription.IsExpired: true } principal
            && context.Request.Path.StartsWithSegments(ApiRoot)
            && Refusal(principal, context.Request.Method, context.Request.Path) is not null
            && await StillExpiredAsync(context, principal) is { } fresh
            && Refusal(fresh, context.Request.Method, context.Request.Path) is { } code)
        {
            await ProblemResponse.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                code == SubscriptionCodes.AdministratorOnlyBilling
                    ? "The laboratory's subscription has expired: only the wallet is available until it is topped up."
                    : "The laboratory's subscription has expired. Ask your laboratory administrator to top up the wallet.",
                code);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// The refusal code for a request of an expired principal, or null when the request may pass: a non-administrator is refused
    /// everything but the auth surface (<c>SUB-020</c>), an administrator everything outside the allow-list (<c>SUB-021</c>).
    /// </summary>
    public static string? Refusal(SessionPrincipal principal, string method, PathString path)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.Subscription.IsExpired || path.StartsWithSegments(RefreshCookie.CookiePath))
        {
            return null;
        }

        if (!principal.IsSubscriptionAdministrator)
        {
            return SubscriptionCodes.Expired;
        }

        return IsAllowed(method, path) ? null : SubscriptionCodes.AdministratorOnlyBilling;
    }

    /// <summary>Whether an administrator of an expired laboratory may still make the request (exact path or an allowed prefix).</summary>
    public static bool IsAllowed(string method, PathString path) =>
        Allowed.Any(entry =>
            (entry.Method == "*" || HttpMethods.Equals(entry.Method, method))
            && (entry.Prefix ? path.StartsWithSegments(entry.Path) : path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) || path.Equals(entry.Path + "/", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The principal re-read once (cache evicted); the cached one when the session no longer resolves.</summary>
    private static async Task<SessionPrincipal?> StillExpiredAsync(HttpContext context, SessionPrincipal cached)
    {
        var principals = context.RequestServices?.GetService<ISessionPrincipalQuery>();
        if (principals is null)
        {
            return cached;
        }

        principals.Evict(cached.SessionId);
        return await principals.ResolveAsync(cached.SessionId, cached.UserId, context.RequestAborted) ?? cached;
    }
}
