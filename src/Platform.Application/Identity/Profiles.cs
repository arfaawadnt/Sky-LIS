using Platform.Application.Abstractions.Security;
using Platform.Application.Identity.Commands;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;

namespace Platform.Application.Identity;

/// <summary>The one place a <see cref="SessionPrincipal"/> becomes the profile or the session view the SPA reads (D-02: never from token claims).</summary>
public static class Profiles
{
    public static AuthProfile ToProfile(SessionPrincipal principal, SessionScope scope)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new AuthProfile(
            principal.UserId,
            principal.DisplayName,
            principal.TenantId,
            principal.RoleName,
            principal.PermissionKeys.ToList(),
            principal.Language,
            ScopeNames.Of(scope),
            principal.BranchId,
            principal.BranchIds,
            principal.MustChangePassword,
            principal.RoleNameAr,
            principal.DisplayNameEn,
            principal.DisplayNameAr,
            principal.SessionTimeoutMinutes,
            principal.ResultsDensity);
    }

    public static SessionResponse ToSession(SessionPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new SessionResponse(
            principal.SessionId,
            principal.UserId,
            principal.DisplayName,
            principal.TenantId,
            ScopeNames.Of(principal.Scope),
            principal.Kind == SessionKind.Browser ? "browser" : "machine",
            principal.ExpiresAtUtc,
            principal.AbsoluteExpiresAtUtc,
            principal.ReauthenticatedAtUtc,
            principal.PermissionKeys.ToList(),
            principal.BranchId,
            principal.BranchIds);
    }
}
