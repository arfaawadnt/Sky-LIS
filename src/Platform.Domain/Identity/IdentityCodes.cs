namespace Platform.Domain.Identity;

/// <summary>Refusal codes of the Identity module (<c>IDN-nnn</c> for users, <c>SES-nnn</c> for sessions).</summary>
public static class IdentityCodes
{
    public const string UsernameInvalid = "IDN-001";
    public const string EmailInvalid = "IDN-002";
    public const string DisplayNameInvalid = "IDN-003";
    public const string PasswordHashMissing = "IDN-004";
    public const string UserInactive = "IDN-010";
    public const string UserLockedOut = "IDN-011";
    public const string MobileInvalid = "IDN-005";
    public const string UserNotFound = "IDN-404";

    public const string SessionTokenHashInvalid = "SES-001";
    public const string SessionLifetimeInvalid = "SES-002";
    public const string SessionNotLive = "SES-010";
    public const string SessionScopeTransitionInvalid = "SES-011";
    public const string SessionBranchInvalid = "SES-012";
    public const string SessionNotFound = "SES-404";
}
