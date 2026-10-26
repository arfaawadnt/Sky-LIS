using System.Net;
using System.Net.Http.Json;
using Platform.Contracts.Identity;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;
using Platform.WebApi.FunctionalTests.Support;
using Platform.WebApi.Security;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-014 G1 (one browser, two portals): the laboratory portal keeps its session in <c>__Secure-rt</c>, the Super Admin
/// in <c>__Secure-rta</c> — same path and attributes. Sign-in writes the cookie of its target and revokes only the presented
/// cookie of its own portal; refresh and sign-out read and clear the cookie the query names (<c>?portal=admin|client</c>,
/// default client). The browser sends both cookies on every <c>/api/v1/auth</c> call, as these tests do.
/// </summary>
public sealed class AuthPortalSessionsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LabCookie = RefreshCookie.SecureName;
    private const string AdminCookie = RefreshCookie.AdminSecureName;

    [Fact]
    public void The_portals_have_their_own_cookie_names_secure_and_for_loopback_development()
    {
        Assert.Equal("__Secure-rt", RefreshCookie.NameOf(SessionPortal.Client, insecure: false));
        Assert.Equal("__Secure-rta", RefreshCookie.NameOf(SessionPortal.Admin, insecure: false));
        Assert.Equal("rt", RefreshCookie.NameOf(SessionPortal.Client, insecure: true));
        Assert.Equal("rta", RefreshCookie.NameOf(SessionPortal.Admin, insecure: true));
        Assert.Equal(SessionPortal.Admin, RefreshCookie.PortalOfSignIn(true));
        Assert.Equal(SessionPortal.Client, RefreshCookie.PortalOfSignIn(null));
        Assert.Equal(SessionPortal.Client, RefreshCookie.PortalOfSignIn(false));
    }

    [DatabaseFact]
    public async Task A_laboratory_session_and_a_Super_Admin_session_coexist_in_one_browser()
    {
        var (member, labTenant, _) = await factory.SeedUserAsync();
        var (staff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(factory.CreateClient());

        var (lab, labCookie, labSetCookie) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password);
        Assert.StartsWith(LabCookie + "=", labSetCookie, StringComparison.Ordinal);
        Assert.Equal(labTenant, lab.Profile!.TenantId);

        // the Super Admin tab signs in while the browser still holds the laboratory cookie
        var adminSignIn = await auth.SignInAsync(staff, ApiFactory.Password, SignInTarget.Administration, cookie: labCookie);
        Assert.Equal(HttpStatusCode.OK, adminSignIn.StatusCode);
        var adminSetCookie = Assert.Single(adminSignIn.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(AdminCookie + "=", adminSetCookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/v1/auth", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", adminSetCookie, StringComparison.OrdinalIgnoreCase);
        var adminCookie = AuthClient.CookieValue(adminSetCookie);

        // each portal's refresh reads its own cookie of the two the browser sends, and answers its own identity
        var adminRefresh = await RefreshAsync(auth, "admin", labCookie, adminCookie);
        Assert.Equal(HttpStatusCode.OK, adminRefresh.StatusCode);
        Assert.Equal(TenantIds.Platform, (await adminRefresh.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.TenantId);
        var adminRotated = AuthClient.CookieValue(Assert.Single(adminRefresh.Headers.GetValues("Set-Cookie")));
        Assert.StartsWith(AdminCookie + "=", Assert.Single(adminRefresh.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);

        var labRefresh = await RefreshAsync(auth, null, labCookie, adminRotated);
        Assert.Equal(HttpStatusCode.OK, labRefresh.StatusCode);
        Assert.Equal(labTenant, (await labRefresh.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.TenantId);
        Assert.StartsWith(LabCookie + "=", Assert.Single(labRefresh.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_refresh_for_the_Super_Admin_rotates_only_its_cookie_and_leaves_the_laboratory_session_untouched()
    {
        var (member, _, _) = await factory.SeedUserAsync();
        var (staff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(factory.CreateClient());
        var (_, labCookie, _) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password);
        var (_, adminCookie, _) = await auth.SignInExpectingSuccessAsync(staff, ApiFactory.Password, SignInTarget.Administration);

        var refreshed = await RefreshAsync(auth, "admin", labCookie, adminCookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var setCookies = refreshed.Headers.GetValues("Set-Cookie").ToList();
        Assert.Single(setCookies);
        Assert.StartsWith(AdminCookie + "=", setCookies[0], StringComparison.Ordinal);
        Assert.DoesNotContain(setCookies, value => value.StartsWith(LabCookie + "=", StringComparison.Ordinal));

        // the laboratory cookie was not rotated: it is still the live token and rotates on its own refresh (a grace answer sets no cookie)
        var labRefresh = await RefreshAsync(auth, "client", labCookie, AuthClient.CookieValue(setCookies[0]));
        Assert.Equal(HttpStatusCode.OK, labRefresh.StatusCode);
        Assert.StartsWith(LabCookie + "=", Assert.Single(labRefresh.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);

        // a Super Admin refresh in a browser that holds only the laboratory cookie reads nothing
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth, "admin", labCookie, null)).StatusCode);
    }

    [DatabaseFact]
    public async Task Signing_out_of_one_portal_leaves_the_other_portal_signed_in()
    {
        var (member, _, _) = await factory.SeedUserAsync();
        var (staff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(factory.CreateClient());
        var (_, labCookie, _) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password);
        var (_, adminCookie, _) = await auth.SignInExpectingSuccessAsync(staff, ApiFactory.Password, SignInTarget.Administration);

        var adminOut = await CookiePostAsync(auth, AuthClient.SignOutPath, "admin", labCookie, adminCookie);
        Assert.Equal(HttpStatusCode.NoContent, adminOut.StatusCode);
        var cleared = Assert.Single(adminOut.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(AdminCookie + "=;", cleared, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth, "admin", null, adminCookie)).StatusCode);
        var labStill = await RefreshAsync(auth, null, labCookie, adminCookie);
        Assert.Equal(HttpStatusCode.OK, labStill.StatusCode);
        labCookie = AuthClient.CookieValue(Assert.Single(labStill.Headers.GetValues("Set-Cookie")));

        // and the other way round: the laboratory sign-out (no portal named) ends only the laboratory session
        var (_, adminAgain, _) = await auth.SignInExpectingSuccessAsync(staff, ApiFactory.Password, SignInTarget.Administration);
        var labOut = await CookiePostAsync(auth, AuthClient.SignOutPath, null, labCookie, adminAgain);
        Assert.Equal(HttpStatusCode.NoContent, labOut.StatusCode);
        Assert.StartsWith(LabCookie + "=;", Assert.Single(labOut.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth, null, labCookie, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(auth, "admin", null, adminAgain)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_sign_in_revokes_the_presented_cookie_of_its_own_portal_only()
    {
        var (member, _, _) = await factory.SeedUserAsync();
        var (staff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var (otherStaff, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);
        var auth = new AuthClient(factory.CreateClient());
        var (_, labCookie, _) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password);
        var (_, adminCookie, _) = await auth.SignInExpectingSuccessAsync(staff, ApiFactory.Password, SignInTarget.Administration);

        // a second Super Admin sign-in in the same browser (both cookies sent) ends the previous Super Admin session (R2-04) ...
        var challenge = await auth.ChallengeAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthClient.SignInPath)
        {
            Content = JsonContent.Create(new { admin = true, username = otherStaff, password = TestSealer.Seal(challenge, "password", ApiFactory.Password, "POST " + AuthClient.SignInPath) }),
        };
        request.Headers.Add("X-Requested-With", "Platform");
        request.Headers.Add("Cookie", $"{LabCookie}={labCookie}; {AdminCookie}={adminCookie}");
        var second = await auth.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var newAdmin = AuthClient.CookieValue(Assert.Single(second.Headers.GetValues("Set-Cookie")));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth, "admin", null, adminCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(auth, "admin", null, newAdmin)).StatusCode);

        // ... and leaves the laboratory session of the same browser alone
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(auth, null, labCookie, null)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_request_without_a_portal_or_with_portal_client_is_the_laboratory_portal()
    {
        var (member, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie, _) = await auth.SignInExpectingSuccessAsync(member, ApiFactory.Password);

        var explicitClient = await RefreshAsync(auth, "client", cookie, null);
        Assert.Equal(HttpStatusCode.OK, explicitClient.StatusCode);
        cookie = AuthClient.CookieValue(Assert.Single(explicitClient.Headers.GetValues("Set-Cookie")));

        var unknownPortal = await RefreshAsync(auth, "elsewhere", cookie, null);
        Assert.Equal(HttpStatusCode.OK, unknownPortal.StatusCode);
        Assert.StartsWith(LabCookie + "=", Assert.Single(unknownPortal.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> RefreshAsync(AuthClient auth, string? portal, string? labCookie, string? adminCookie) =>
        CookiePostAsync(auth, AuthClient.RefreshPath, portal, labCookie, adminCookie);

    /// <summary>A cookie endpoint called the way the SPA calls it, with whichever of the two cookies the browser holds.</summary>
    private static async Task<HttpResponseMessage> CookiePostAsync(AuthClient auth, string path, string? portal, string? labCookie, string? adminCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, portal is null ? path : $"{path}?portal={portal}");
        var cookies = new List<string>();
        if (labCookie is not null)
        {
            cookies.Add($"{LabCookie}={labCookie}");
        }

        if (adminCookie is not null)
        {
            cookies.Add($"{AdminCookie}={adminCookie}");
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies));
        }

        request.Headers.Add("X-Requested-With", "Platform");
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return await auth.Http.SendAsync(request);
    }
}
