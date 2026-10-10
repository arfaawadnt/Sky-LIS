using Platform.Application.Abstractions.Audit;
using Platform.Contracts.Common;
using Platform.WebApi.Middleware;
using Platform.WebApi.Startup;

namespace Platform.WebApi.Security;

/// <summary>
/// Anti-CSRF for the only two endpoints that read a cookie (02 §2.3): <c>POST /api/v1/auth/refresh</c> and
/// <c>POST /api/v1/auth/sign-out</c>. Three independent checks: <c>Sec-Fetch-Site</c> same-origin or none,
/// <c>Origin</c> (or <c>Referer</c>) equals a configured public origin, and the custom header
/// <c>X-Requested-With: Platform</c>, which a cross-site form cannot set. Every other endpoint is bearer-only and
/// ignores cookies, so CSRF cannot reach it. Machine clients send the token in the body and skip these checks.
/// </summary>
public sealed class AuthOriginCheckMiddleware(RequestDelegate next, HostSecuritySettings settings)
{
    public const string RequiredHeader = "X-Requested-With";
    public const string RequiredHeaderValue = "Platform";
    private static readonly PathString AuthPrefix = RefreshCookie.CookiePath;
    private static readonly string[] CookieEndpoints = ["/refresh", "/sign-out"];

    public async Task InvokeAsync(HttpContext context)
    {
        var isCookieEndpoint = HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments(AuthPrefix, out var rest)
            && CookieEndpoints.Contains(rest.Value, StringComparer.OrdinalIgnoreCase)
            && RefreshCookie.IsPresent(context);

        if (!isCookieEndpoint)
        {
            await next(context);
            return;
        }

        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite is not ("" or "same-origin" or "none"))
        {
            await RejectAsync(context, "fetch-site");
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            origin = OriginOf(context.Request.Headers.Referer.ToString());
        }

        if (!IsAllowedOrigin(context, origin))
        {
            await RejectAsync(context, "origin");
            return;
        }

        if (!string.Equals(context.Request.Headers[RequiredHeader].ToString(), RequiredHeaderValue, StringComparison.Ordinal))
        {
            await RejectAsync(context, "header");
            return;
        }

        await next(context);
    }

    private bool IsAllowedOrigin(HttpContext context, string origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        if (settings.AllowedOrigins.Length > 0)
        {
            return settings.AllowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);
        }

        // Same-origin deployment with no explicit list: the request's own origin, after forwarded headers.
        var own = $"{context.Request.Scheme}://{context.Request.Host}";
        return string.Equals(own, origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string OriginOf(string referer) =>
        Uri.TryCreate(referer, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : string.Empty;

    private static async Task RejectAsync(HttpContext context, string reason)
    {
        var events = context.RequestServices.GetRequiredService<ISecurityEventLog>();
        await events.WriteAsync(new SecurityEvent(SecurityEvents.CsrfRejected, SecurityEvent.Warning, Detail: $"{reason} {context.Request.Path}"), context.RequestAborted);
        await ProblemResponse.WriteAsync(context, StatusCodes.Status403Forbidden, "The request did not pass the same-origin checks.", ProblemCodes.CsrfRejected);
    }
}
