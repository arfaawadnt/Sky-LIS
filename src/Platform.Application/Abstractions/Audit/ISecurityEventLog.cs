namespace Platform.Application.Abstractions.Audit;

/// <summary>One row of the security event catalogue (02 §7). <see cref="Detail"/> never contains credential material.</summary>
public sealed record SecurityEvent(string Name, string Severity, Guid? TenantId = null, Guid? ActorUserId = null, string? Detail = null)
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

/// <summary>Appends security events to the structured log and to <c>audit.security_events</c> (append-only). Never throws into a request: a ledger that is unreachable is logged, not fatal.</summary>
public interface ISecurityEventLog
{
    Task WriteAsync(SecurityEvent securityEvent, CancellationToken cancellationToken);
}

/// <summary>Event names of 02 §7.</summary>
public static class SecurityEvents
{
    public const string SignInSucceeded = "SIGN_IN_SUCCEEDED";
    public const string SignInFailed = "SIGN_IN_FAILED";
    public const string UserLockedOut = "USER_LOCKED_OUT";
    public const string SessionCreated = "SESSION_CREATED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string SessionContextChanged = "SESSION_CONTEXT_CHANGED";
    public const string RefreshReuseDetected = "REFRESH_REUSE_DETECTED";
    public const string CsrfRejected = "CSRF_REJECTED";
    public const string TokenSubjectRejected = "TOKEN_SUBJECT_REJECTED";
    public const string TokenSessionMismatch = "TOKEN_SESSION_MISMATCH";
    public const string CredentialEnvelopeRejected = "CREDENTIAL_ENVELOPE_REJECTED";
    public const string RateLimitSustained = "RATE_LIMIT_SUSTAINED";
    public const string StepUpSucceeded = "STEP_UP_SUCCEEDED";
    public const string StepUpFailed = "STEP_UP_FAILED";
    public const string SecurityRelaxationActive = "SECURITY_RELAXATION_ACTIVE";
    public const string RoleGuardViolation = "ROLE_GUARD_VIOLATION";

    // Password recovery with a one-time code (SRS CP-03); the detail is a reason category, never the code or the target.
    public const string PasswordRecoveryStarted = "PASSWORD_RECOVERY_STARTED";
    public const string PasswordRecoveryCompleted = "PASSWORD_RECOVERY_COMPLETED";
    public const string PasswordRecoveryFailed = "PASSWORD_RECOVERY_FAILED";
}
