using Platform.Application.Abstractions.Security;
using Platform.Application.Identity.Commands;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;

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
            principal.ResultsDensity,
            principal.IsPrimaryAdministrator,
            principal.PortalSlug,
            NoticeOf(principal),
            ListPreferencesOf(principal));
    }

    /// <summary>LIS-CR-014 T2: the saved table views of the profile; null when the member has none.</summary>
    public static IReadOnlyDictionary<string, ListPreferenceView>? ListPreferencesOf(SessionPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.ListPreferences.Count == 0
            ? null
            : principal.ListPreferences.ToDictionary(pair => pair.Key, pair => new ListPreferenceView(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>LIS-CR-012: the subscription notice of the profile; null while the subscription is Ok.</summary>
    public static SubscriptionNotice? NoticeOf(SessionPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var state = principal.Subscription;
        if (state.Level == SubscriptionLevel.Ok)
        {
            return null;
        }

        return new SubscriptionNotice(
            state.Level.ToString(),
            state.Reason.ToString(),
            state.DaysLeft,
            state.EndsAtUtc,
            state.AmountToTopUp,
            state.CurrencyCode,
            state.TrialEndsAtUtc,
            principal.IsSubscriptionAdministrator,
            state.CallsForModal);
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
