using Platform.Contracts.Common;

namespace Platform.Contracts.Identity;

/// <summary>
/// Browser sign-in (02 §4.1). The password travels as a credential envelope (D-03); the edge filter opens it before binding
/// completes. LIS-CR-005: the address the user signs in at names the target — exactly one of <see cref="Laboratory"/> (the
/// laboratory's address, <c>/lab/{slug}</c>) or <see cref="Admin"/> = true (the Super Admin sign-in, <c>/admin/login</c>);
/// anything else is <c>VALIDATION-400</c>. A user with memberships in several laboratories signs in at each one's address.
/// </summary>
public sealed record SignInRequest
{
    /// <summary>The laboratory address (slug, 3–40 of <c>a-z 0-9 -</c>; trimmed and lower-cased).</summary>
    public string? Laboratory { get; init; }

    /// <summary>True at the Super Admin sign-in: only platform staff (members of the platform tenant) are accepted.</summary>
    public bool? Admin { get; init; }

    public string Username { get; init; } = string.Empty;

    [CredentialField]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Machine clients present the refresh token in the body (02 §4.2); browsers send the <c>__Secure-rt</c> cookie and leave this empty.</summary>
public sealed record RefreshRequest(string? RefreshToken);

public sealed record SignOutRequest(string? RefreshToken);

/// <summary>CP-04: binds the current session to one of the membership's branches.</summary>
public sealed record SelectBranchRequest(Guid BranchId);

/// <summary>CP-03 step 1: always answered 202; a one-time code is sent only when the account exists and has a mobile.</summary>
public sealed record StartPasswordRecoveryRequest(string UsernameOrMobile, string? Language);

/// <summary>CP-03 step 2: the one-time code and the new password, which travels as a credential envelope (D-03).</summary>
public sealed record CompletePasswordRecoveryRequest
{
    public string UsernameOrMobile { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    [CredentialField]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>The challenge a client seals credentials against (02 §3.3). One challenge serves one request.</summary>
public sealed record CredentialChallengeResponse(
    string Kid,
    RsaPublicJwk PublicKey,
    string PublicKeyPem,
    string Algorithm,
    string Encryption,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

/// <summary>RSA public key as a JSON Web Key, ready for <c>crypto.subtle.importKey('jwk', …)</c>.</summary>
public sealed record RsaPublicJwk(string Kty, string N, string E, string Alg, string Use, string Kid);

/// <summary>
/// What sign-in and refresh return. The SPA reads identity from <see cref="Profile"/> and never decodes the token
/// (D-02). <see cref="RefreshToken"/> is set for machine clients only; browsers receive it as a cookie.
/// </summary>
public sealed record AuthResponse(
    string Step,
    string? AccessToken,
    DateTimeOffset? ExpiresAtUtc,
    string? RefreshToken,
    AuthProfile? Profile)
{
    public const string StepSignedIn = "signed-in";
    public const string StepMfa = "mfa";
}

/// <summary>
/// The identity the SPA gates its screens on. <see cref="BranchId"/> is the branch the session is bound to (CP-04),
/// <see cref="BranchIds"/> the branches the membership may choose from (empty = the membership is unrestricted;
/// the branch list then comes from the organization module). <see cref="RoleName"/> is the role's English name and
/// <see cref="RoleNameAr"/> its Arabic name, so the shell shows the one of the current language (validation v0.5.0 B-09).
/// <see cref="DisplayNameEn"/> and <see cref="DisplayNameAr"/> are the member's full name in each language (null without a
/// membership); <see cref="DisplayName"/> stays the name in the preferred language (validation v0.5.0 R-12).
/// <see cref="SessionTimeoutMinutes"/> is the idle timeout of the session's tenant (validation F-01): the SPA's idle watcher
/// warns a minute before it and signs out at it; the server refuses a refresh after it (<c>AUTH-017</c>).
/// <see cref="PortalSlug"/> (LIS-CR-005) is the address of the session's laboratory (<c>/lab/{slug}</c>), null for the platform
/// tenant (the Super Admin signs in at <c>/admin/login</c>): the SPA sends a signed-out or timed-out user back to it.
/// </summary>
public sealed record AuthProfile(
    Guid UserId,
    string DisplayName,
    Guid? TenantId,
    string? RoleName,
    IReadOnlyList<string> Permissions,
    string Language,
    string Scope,
    Guid? BranchId,
    IReadOnlyList<Guid> BranchIds,
    bool MustChangePassword = false,
    string? RoleNameAr = null,
    string? DisplayNameEn = null,
    string? DisplayNameAr = null,
    int? SessionTimeoutMinutes = null,
    string? ResultsDensity = null,
    bool IsPrimaryAdministrator = false,
    string? PortalSlug = null,
    SubscriptionNotice? SubscriptionNotice = null);

/// <summary>
/// LIS-CR-012 (C-16): the subscription state of the session's laboratory, read in the session principal's one query; null when
/// there is nothing to say (an active client whose wallet covers at least a week, a trial with more than a week left, the
/// platform tenant). <see cref="Level"/>: <c>Warning</c> | <c>Grace</c> | <c>Expired</c>; <see cref="Reason"/>: <c>Trial</c> |
/// <c>Wallet</c> | <c>WalletAtZero</c>; <see cref="DaysLeft"/> whole days to <see cref="EndsAtUtc"/> (trial end, end of the cover, start or end of the
/// grace); <see cref="AmountToTopUp"/> in <see cref="CurrencyCode"/> brings the wallet back to one month of the plan fee.
/// <see cref="CanManage"/>: the user is an administrator of the laboratory (primary administrator or <c>organization.manage</c>),
/// who sees the banner and, once expired, may still sign in to the wallet (<c>SUB-021</c> elsewhere). <see cref="ShowModal"/>:
/// fewer than three days left, in grace or expired — the SPA shows the sign-in modal.
/// </summary>
public sealed record SubscriptionNotice(
    string Level,
    string Reason,
    int? DaysLeft,
    DateTimeOffset? EndsAtUtc,
    decimal? AmountToTopUp,
    string? CurrencyCode,
    DateTimeOffset? TrialEndsAtUtc,
    bool CanManage,
    bool ShowModal);

/// <summary>The current session as the server sees it (GET /api/v1/auth/me).</summary>
public sealed record SessionResponse(
    Guid SessionId,
    Guid UserId,
    string DisplayName,
    Guid? TenantId,
    string Scope,
    string Kind,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? ReauthenticatedAtUtc,
    IReadOnlyList<string> Permissions,
    Guid? BranchId,
    IReadOnlyList<Guid> BranchIds);

/// <summary>Problem codes of the authentication surface that are not in <see cref="ProblemCodes"/> (unique across the solution).</summary>
public static class AuthProblemCodes
{
    /// <summary>
    /// Validation 2026-10-11 C-04 (ORG-001 A13, 02 §6.1 forced change): the account must change its password first. While
    /// <c>mustChangePassword</c> is set every API request but the password change, the own profile and privileges, the
    /// branch selection of the session and the <c>/api/v1/auth</c> surface (refresh, sign-out, me, challenge) is refused
    /// <c>403 AUTH-016</c>. The SPA routes such a user to the profile page.
    /// </summary>
    public const string PasswordChangeRequired = "AUTH-016";

    /// <summary>
    /// Validation F-01: the refresh came after the session had been idle longer than its tenant's <c>sessionTimeoutMinutes</c>
    /// (the platform's value when the laboratory set none, else 30). The session is revoked (reason <c>Expired</c>, security
    /// event <c>SESSION_REVOKED</c> info <c>idle_timeout</c>, no reuse alarm) and the refresh refused <c>401 AUTH-017</c>;
    /// the SPA shows "Your session ended after inactivity" on the sign-in page. A later refresh with the same cookie is the
    /// ordinary <c>AUTH-012</c> of an ended session.
    /// </summary>
    public const string SessionIdleTimeout = "AUTH-017";
}
