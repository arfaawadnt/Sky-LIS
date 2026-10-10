using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Tokens;
using Platform.Application.Identity.Commands;
using Platform.Contracts.Common;
using Platform.SharedKernel.Abstractions;
using Platform.WebApi.Middleware;

namespace Platform.WebApi.Security;

/// <summary>
/// The only code that decrypts <c>sub</c> (D-02, 01 §2.3). After signature and lifetime checks: decrypt the user
/// id with the token's signing <c>kid</c> as AAD (<c>AUTH-010</c> on failure), read <c>sid</c>, resolve the session
/// in one query (<c>AUTH-011</c> on mismatch, <c>AUTH-006</c> when revoked or expired), then build a principal with
/// ids only. Display name, role and privileges are stashed for <see cref="TenantResolutionMiddleware"/>.
/// </summary>
public static class EncryptedSubjectEvents
{
    public const string PrincipalItem = "platform.session-principal";
    public const string ProblemCodeItem = "platform.auth-problem-code";

    public static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = OnTokenValidatedAsync,
        OnAuthenticationFailed = context =>
        {
            context.HttpContext.Items.TryAdd(ProblemCodeItem, ProblemCodes.AuthenticatedActorRequired);
            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            var code = context.HttpContext.Items[ProblemCodeItem] as string ?? ProblemCodes.AuthenticatedActorRequired;
            await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Authentication is required.", code);
        },
        OnForbidden = context => ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "This action is not permitted for the current actor.", ProblemCodes.Forbidden),
    };

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken token)
        {
            Fail(context, ProblemCodes.TokenSubjectRejected);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var events = services.GetRequiredService<ISecurityEventLog>();
        var cancellationToken = context.HttpContext.RequestAborted;

        var userId = services.GetRequiredService<ISubjectProtector>().Unprotect(token.Subject, token.Kid);
        if (userId is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.TokenSubjectRejected, SecurityEvent.Warning), cancellationToken);
            Fail(context, ProblemCodes.TokenSubjectRejected);
            return;
        }

        if (!token.TryGetClaim("sid", out var sidClaim) || !Guid.TryParse(sidClaim.Value, out var sessionId))
        {
            Fail(context, ProblemCodes.TokenSessionMismatch);
            return;
        }

        var principal = await services.GetRequiredService<ISessionPrincipalQuery>().ResolveAsync(sessionId, userId.Value, cancellationToken);
        var tenantClaim = token.TryGetClaim("tid", out var tidClaim) ? tidClaim.Value : null;
        if (principal is null || (principal.TenantId?.ToString() ?? string.Empty) != (tenantClaim ?? string.Empty))
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.TokenSessionMismatch, SecurityEvent.Warning, ActorUserId: userId), cancellationToken);
            Fail(context, ProblemCodes.TokenSessionMismatch);
            return;
        }

        if (!principal.IsLiveAt(services.GetRequiredService<IClock>().UtcNow))
        {
            Fail(context, ProblemCodes.SessionRevokedOrExpired);
            return;
        }

        context.HttpContext.Items[PrincipalItem] = principal;

        var claims = new List<Claim>
        {
            new("sub", principal.UserId.ToString()),
            new("sid", principal.SessionId.ToString()),
            new("scope", ScopeNames.Of(principal.Scope)),
        };
        if (principal.TenantId is { } tenantId)
        {
            claims.Add(new Claim("tid", tenantId.ToString()));
        }

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme, "sub", roleType: null));
        context.Success();
    }

    private static void Fail(TokenValidatedContext context, string code)
    {
        context.HttpContext.Items[ProblemCodeItem] = code;
        context.Fail(code);
    }
}
