using Platform.WebApi.Startup;

namespace Platform.WebApi.Security;

/// <summary>
/// Security headers on every response, success and error alike (02 §2.2, pitfall P-42): a deny-all CSP for API
/// responses, nosniff, DENY framing, no referrer, no caching, and HSTS outside loopback development.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, HostSecuritySettings settings)
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    public const string StrictTransportSecurity = "max-age=63072000; includeSubDomains";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var (response, hsts) = ((HttpResponse, bool))state;
            var headers = response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.CacheControl = "no-store";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            if (hsts)
            {
                headers.StrictTransportSecurity = StrictTransportSecurity;
            }

            return Task.CompletedTask;
        }, (context.Response, !settings.AllowInsecureLocalDevelopment));

        return next(context);
    }
}
