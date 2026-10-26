using Platform.WebApi.Startup;

namespace Platform.WebApi.Security;

/// <summary>
/// The refresh cookies of 02 §2.4 (D-01, pitfall P-24): <c>__Secure-rt</c> (laboratory portal) and <c>__Secure-rta</c> (Super
/// Admin portal, LIS-CR-014 G1), both <c>Path=/api/v1/auth; Secure; HttpOnly; SameSite=Strict</c>, no <c>Domain</c>, no
/// <c>Max-Age</c> (the server-side expiries are authoritative). Under <c>Security:AllowInsecureLocalDevelopment</c> they are
/// <c>rt</c> / <c>rta</c> without <c>Secure</c>, for loopback HTTP only. Sign-in writes the cookie of its target; refresh and
/// sign-out name their portal in the query (<c>?portal=admin|client</c>, anything else or nothing = client, so requests made
/// before the split keep working) and read or clear that cookie only. The only code that reads or writes them, besides the
/// auth controller, the origin check and the rate limiter's key reader.
/// </summary>
public static class RefreshCookie
{
    public const string SecureName = "__Secure-rt";
    public const string InsecureName = "rt";
    public const string AdminSecureName = "__Secure-rta";
    public const string AdminInsecureName = "rta";
    public const string CookiePath = "/api/v1/auth";

    /// <summary>The query parameter of <c>POST /api/v1/auth/refresh</c> and <c>/sign-out</c> that names the portal.</summary>
    public const string PortalParameter = "portal";
    public const string AdminPortalValue = "admin";
    public const string ClientPortalValue = "client";

    /// <summary>The portal the request names: <c>?portal=admin</c> (case-insensitive) is the Super Admin; anything else the laboratory portal.</summary>
    public static SessionPortal PortalOf(HttpContext context) =>
        string.Equals(context.Request.Query[PortalParameter].ToString(), AdminPortalValue, StringComparison.OrdinalIgnoreCase)
            ? SessionPortal.Admin
            : SessionPortal.Client;

    /// <summary>The portal of a sign-in: the Super Admin address (<c>admin: true</c>) or a laboratory address.</summary>
    public static SessionPortal PortalOfSignIn(bool? admin) => admin == true ? SessionPortal.Admin : SessionPortal.Client;

    /// <summary>The cookie of the portal the request names.</summary>
    public static string Name(HttpContext context) => Name(context, PortalOf(context));

    public static string Name(HttpContext context, SessionPortal portal) => NameOf(portal, IsInsecure(context));

    public static string NameOf(SessionPortal portal, bool insecure) => (portal, insecure) switch
    {
        (SessionPortal.Admin, false) => AdminSecureName,
        (SessionPortal.Admin, true) => AdminInsecureName,
        (_, false) => SecureName,
        _ => InsecureName,
    };

    /// <summary>True when the request carries a refresh cookie of either portal (the origin check guards every cookie-bearing call).</summary>
    public static bool IsPresent(HttpContext context)
    {
        var insecure = IsInsecure(context);
        return context.Request.Cookies.ContainsKey(NameOf(SessionPortal.Client, insecure))
            || context.Request.Cookies.ContainsKey(NameOf(SessionPortal.Admin, insecure));
    }

    /// <summary>The cookie of the portal the request names (<c>?portal=</c>).</summary>
    public static string? Read(HttpContext context) => Read(context, PortalOf(context));

    public static string? Read(HttpContext context, SessionPortal portal) =>
        context.Request.Cookies.TryGetValue(Name(context, portal), out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static void Write(HttpContext context, SessionPortal portal, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        context.Response.Cookies.Append(Name(context, portal), refreshToken, Options(IsInsecure(context)));
    }

    public static void Clear(HttpContext context, SessionPortal portal) =>
        context.Response.Cookies.Delete(Name(context, portal), Options(IsInsecure(context)));

    private static bool IsInsecure(HttpContext context) =>
        context.RequestServices.GetRequiredService<HostSecuritySettings>().AllowInsecureLocalDevelopment;

    private static CookieOptions Options(bool insecure) => new()
    {
        HttpOnly = true,
        Secure = !insecure,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Domain = null,
        IsEssential = true,
    };
}
