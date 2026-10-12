using Platform.Application.Abstractions.Security;
using Platform.Contracts.Identity;
using Platform.WebApi.Security;

namespace Platform.WebApi.Middleware;

/// <summary>
/// The forced password change (validation 2026-10-11 C-04; intake ORG-001 A13 "enforced by the sign-in flow"; 02 §6.1).
/// A session whose user must change the password may reach only what the change needs: the <c>/api/v1/auth</c> surface
/// (credential challenge for the envelope, refresh, sign-out, me, branch selection), the own profile, privileges and
/// language, the selectable branches of the session (CP-04) and the password change itself. Every other API request is
/// <c>403 AUTH-016</c>. Non-API paths (health, JWKS) are not gated; anonymous requests pass (they carry no principal).
/// <para>
/// The principal is cached for up to 10 s per session, so a "must change" answer is re-read once before refusing: the
/// password change evicts only the session that made it, and another tab's token may name an earlier session of the family.
/// </para>
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiRoot = "/api";

    private static readonly (string Method, PathString Path, bool Prefix)[] Allowed =
    [
        ("*", RefreshCookie.CookiePath, true),                                    // /api/v1/auth/** (challenge, refresh, sign-out, me, select-branch)
        (HttpMethods.Get, "/api/v1/account/profile", false),
        (HttpMethods.Post, "/api/v1/account/password", false),
        (HttpMethods.Put, "/api/v1/account/preferences", false),
        (HttpMethods.Get, "/api/v1/organization/me/privileges", false),
        (HttpMethods.Get, "/api/v1/organization/branches/selectable", false),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items[EncryptedSubjectEvents.PrincipalItem] is SessionPrincipal { MustChangePassword: true } principal
            && context.Request.Path.StartsWithSegments(ApiRoot)
            && !IsAllowed(context.Request.Method, context.Request.Path)
            && await StillMustChangeAsync(context, principal))
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status403Forbidden, "The password must be changed before anything else.", AuthProblemCodes.PasswordChangeRequired);
            return;
        }

        await next(context);
    }

    /// <summary>Whether the request is one a user who must change the password may still make (exact path or the auth prefix).</summary>
    public static bool IsAllowed(string method, PathString path) =>
        Allowed.Any(entry =>
            (entry.Method == "*" || HttpMethods.Equals(entry.Method, method))
            && (entry.Prefix ? path.StartsWithSegments(entry.Path) : path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) || path.Equals(entry.Path + "/", StringComparison.OrdinalIgnoreCase)));

    private static async Task<bool> StillMustChangeAsync(HttpContext context, SessionPrincipal cached)
    {
        var principals = context.RequestServices.GetRequiredService<ISessionPrincipalQuery>();
        principals.Evict(cached.SessionId);
        var fresh = await principals.ResolveAsync(cached.SessionId, cached.UserId, context.RequestAborted);
        return fresh?.MustChangePassword ?? true;
    }
}
