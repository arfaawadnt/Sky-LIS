using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Infrastructure.Security;
using Platform.WebApi.Security;

namespace Platform.WebApi.Middleware;

/// <summary>
/// Fills the scoped accessors from the resolved principal only (01 §3.2, pitfall P-30): never from a header, a
/// query string or a token claim. The tenant stamped here is what the connection interceptor sends to PostgreSQL.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Items[EncryptedSubjectEvents.PrincipalItem] is SessionPrincipal principal)
        {
            context.RequestServices.GetRequiredService<CurrentUserAccessor>().Set(principal.UserId, principal.SessionId, principal.DisplayName, principal.BranchId, principal.IsPrimaryAdministrator);
            context.RequestServices.GetRequiredService<UserPrivilegesAccessor>().Set(principal);   // keys, special rights and result rights (IUserPrivileges)
            if (principal.TenantId is { } tenantId)
            {
                context.RequestServices.GetRequiredService<TenantContextAccessor>().SetTenant(tenantId);
            }
        }

        return next(context);
    }
}

/// <summary>
/// While MFA enrolment is being forced (02 §4.5), a session with the <c>mfa_enrollment</c> scope may reach only the
/// enrolment endpoints and sign-out; everything else is <c>403 MFA-ENROLL-REQUIRED</c>. A pending-MFA session holds
/// no access token at all; one that reaches here is refused.
/// </summary>
public sealed class MfaEnrollmentGateMiddleware(RequestDelegate next)
{
    private static readonly PathString[] EnrollmentPaths =
    [
        RefreshCookie.CookiePath + "/mfa/enroll",
        RefreshCookie.CookiePath + "/mfa/confirm",
        RefreshCookie.CookiePath + "/sign-out",
        RefreshCookie.CookiePath + "/me",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items[EncryptedSubjectEvents.PrincipalItem] is SessionPrincipal { Scope: not SessionScope.Full } principal)
        {
            if (principal.Scope == SessionScope.MfaPending)
            {
                await ProblemResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Multi-factor verification is pending.", ProblemCodes.AuthenticatedActorRequired);
                return;
            }

            if (!EnrollmentPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
            {
                await ProblemResponse.WriteAsync(context, StatusCodes.Status403Forbidden, "Multi-factor enrolment must be completed first.", ProblemCodes.MfaEnrollmentRequired);
                return;
            }
        }

        await next(context);
    }
}
