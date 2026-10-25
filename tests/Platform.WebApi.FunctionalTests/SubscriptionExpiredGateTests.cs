using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.WebApi.Middleware;
using Platform.WebApi.Security;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-012 (D-3): <see cref="SubscriptionExpiredGateMiddleware"/> as a unit (no database, no host): an administrator of an
/// expired laboratory reaches only the auth surface, the own profile, the wallet and support (<c>SUB-021</c> elsewhere); any other
/// member of it gets <c>SUB-020</c>; a laboratory that is not expired, anonymous requests and non-API paths pass; a principal that
/// the re-read finds renewed (a top-up landed) passes.
/// </summary>
public sealed class SubscriptionExpiredGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly SubscriptionStatus Expired = SubscriptionTerms.Evaluate(TenantStatus.Trial, Now.AddDays(-30), null, 7, Now);
    private static readonly SubscriptionStatus Grace = SubscriptionTerms.Evaluate(TenantStatus.Trial, Now.AddDays(-1), null, 7, Now);

    [Theory]
    [InlineData("GET", "/api/v1/auth/me")]
    [InlineData("POST", "/api/v1/auth/refresh")]
    [InlineData("GET", "/api/v1/account/profile")]
    [InlineData("PUT", "/api/v1/account/preferences")]
    [InlineData("GET", "/api/v1/account/laboratory/logo")]
    [InlineData("GET", "/api/v1/wallet")]
    [InlineData("GET", "/api/v1/wallet/transactions")]
    [InlineData("POST", "/api/v1/wallet/top-up-requests")]
    [InlineData("GET", "/api/v1/account/subscription")]
    [InlineData("POST", "/api/v1/support/tickets")]
    [InlineData("GET", "/api/v1/organization/me/privileges")]
    public void An_administrator_of_an_expired_laboratory_reaches_the_billing_surface(string method, string path) =>
        Assert.Null(SubscriptionExpiredGateMiddleware.Refusal(Principal(Expired, administrator: true), method, path));

    [Theory]
    [InlineData("GET", "/api/v1/settings")]
    [InlineData("POST", "/api/v1/registration/visits")]
    [InlineData("GET", "/api/v1/results/worklist")]
    [InlineData("DELETE", "/api/v1/account/laboratory/logo")]
    [InlineData("POST", "/api/v1/account/profile")]
    public void An_administrator_of_an_expired_laboratory_is_refused_everything_else_SUB_021(string method, string path) =>
        Assert.Equal(SubscriptionCodes.AdministratorOnlyBilling, SubscriptionExpiredGateMiddleware.Refusal(Principal(Expired, administrator: true), method, path));

    [Fact]
    public void Another_member_of_an_expired_laboratory_reaches_only_the_auth_surface_SUB_020()
    {
        var member = Principal(Expired, administrator: false);
        Assert.Equal(SubscriptionCodes.Expired, SubscriptionExpiredGateMiddleware.Refusal(member, "GET", "/api/v1/wallet"));
        Assert.Null(SubscriptionExpiredGateMiddleware.Refusal(member, "POST", "/api/v1/auth/sign-out"));
    }

    [Fact]
    public void A_laboratory_in_grace_is_not_gated()
    {
        Assert.Null(SubscriptionExpiredGateMiddleware.Refusal(Principal(Grace, administrator: false), "POST", "/api/v1/registration/visits"));
        Assert.Null(SubscriptionExpiredGateMiddleware.Refusal(Principal(SubscriptionStatus.None, administrator: false), "GET", "/api/v1/settings"));
    }

    [Fact]
    public async Task The_middleware_answers_403_SUB_021_and_does_not_call_the_next_step()
    {
        var principal = Principal(Expired, administrator: true);
        var (context, nextCalled) = await InvokeAsync(principal, "GET", "/api/v1/settings", renewed: null);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"code\":\"" + SubscriptionCodes.AdministratorOnlyBilling + "\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/api/v1/wallet")]
    [InlineData("GET", "/health/ready")]
    public async Task Allowed_and_non_API_requests_pass_to_the_next_step(string method, string path)
    {
        var (context, nextCalled) = await InvokeAsync(Principal(Expired, administrator: true), method, path, renewed: null);
        Assert.True(nextCalled());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_principal_renewed_by_a_top_up_passes_after_the_re_read()
    {
        var cached = Principal(Expired, administrator: true);
        var renewed = cached with { Subscription = SubscriptionStatus.None };
        var (_, nextCalled) = await InvokeAsync(cached, "GET", "/api/v1/settings", renewed);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task An_anonymous_request_passes()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/settings";
        var called = false;
        await new SubscriptionExpiredGateMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(called);
    }

    private static async Task<(DefaultHttpContext Context, Func<bool> NextCalled)> InvokeAsync(SessionPrincipal principal, string method, string path, SessionPrincipal? renewed)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionPrincipalQuery>(new FakePrincipals(renewed ?? principal));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Items[EncryptedSubjectEvents.PrincipalItem] = principal;
        var called = false;
        await new SubscriptionExpiredGateMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        return (context, () => called);
    }

    private static SessionPrincipal Principal(SubscriptionStatus state, bool administrator) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "User", true, Guid.CreateVersion7(), SessionKind.Browser, SessionScope.Full, Now.AddHours(1), Now.AddHours(8), false, null, true, "en", "Role",
            new HashSet<string>(StringComparer.Ordinal), [], [], null)
        {
            Subscription = state,
            IsSubscriptionAdministrator = administrator,
        };

    private sealed class FakePrincipals(SessionPrincipal answer) : ISessionPrincipalQuery
    {
        public Task<SessionPrincipal?> ResolveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<SessionPrincipal?>(answer);

        public void Evict(Guid sessionId)
        {
        }
    }
}
