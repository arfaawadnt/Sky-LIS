namespace Platform.Domain.Identity;

/// <summary>Who holds the session (02 §4.2): a browser (refresh token in the <c>__Secure-rt</c> cookie) or a machine client (refresh token in the body).</summary>
public enum SessionKind
{
    Browser = 0,
    Machine = 1,
}

/// <summary>What the session may do (02 §4.1). Only <see cref="Full"/> reaches data endpoints.</summary>
public enum SessionScope
{
    Full = 0,
    MfaPending = 1,
    MfaEnrollment = 2,
}

/// <summary>Why a session ended (01 §4.7).</summary>
public enum RevocationReason
{
    Logout = 0,
    Admin = 1,
    PasswordChanged = 2,
    ReuseDetected = 3,
    Expired = 4,
    TenantSuspended = 5,
}
