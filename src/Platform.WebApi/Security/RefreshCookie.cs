using Platform.WebApi.Startup;

namespace Platform.WebApi.Security;

/// <summary>
/// The refresh cookie of 02 §2.4 (D-01, pitfall P-24): <c>__Secure-rt; Path=/api/v1/auth; Secure; HttpOnly;
/// SameSite=Strict</c>, no <c>Domain</c>, no <c>Max-Age</c> (the server-side expiries are authoritative). Under
/// <c>Security:AllowInsecureLocalDevelopment</c> the cookie is <c>rt</c> without <c>Secure</c>, for loopback HTTP only.
/// The only code that reads or writes it, besides the auth controller and the origin check.
/// </summary>
public static class RefreshCookie
{
    public const string SecureName = "__Secure-rt";
    public const string InsecureName = "rt";
    public const string CookiePath = "/api/v1/auth";

    public static string Name(HttpContext context) => IsInsecure(context) ? InsecureName : SecureName;

    public static bool IsPresent(HttpContext context) => context.Request.Cookies.ContainsKey(Name(context));

    public static string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Name(context), out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static void Write(HttpContext context, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        context.Response.Cookies.Append(Name(context), refreshToken, Options(IsInsecure(context)));
    }

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(Name(context), Options(IsInsecure(context)));

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
