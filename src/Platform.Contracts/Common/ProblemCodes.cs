namespace Platform.Contracts.Common;

/// <summary>
/// Cross-cutting problem codes carried in <c>application/problem+json</c> (01 §5.1). Module refusal codes
/// (<c>REG-010</c> …) live beside their module; ProblemCodeTests asserts uniqueness across all of them.
/// </summary>
public static class ProblemCodes
{
    // Authorization (D-04)
    public const string NoPolicy = "AUTHZ-000";
    public const string Forbidden = "AUTHZ-001";
    public const string UnknownPermission = "AUTHZ-002";

    // Authentication and sessions (D-01, D-02)
    public const string AuthenticatedActorRequired = "AUTH-001";
    public const string InvalidCredentials = "AUTH-002";
    public const string AccountLocked = "AUTH-003";
    public const string AccountInactive = "AUTH-004";

    /// <summary>The account's access end date has passed (CP-38 expiry); refused only after a correct password.</summary>
    public const string AccountExpired = "AUTH-014";
    public const string NoActiveMembership = "AUTH-005";
    public const string SessionRevokedOrExpired = "AUTH-006";
    public const string TokenSubjectRejected = "AUTH-010";
    public const string TokenSessionMismatch = "AUTH-011";
    public const string RefreshTokenRejected = "AUTH-012";
    public const string OtpRejected = "AUTH-013";
    public const string StepUpRequired = "AUTH-STEPUP-REQUIRED";
    public const string MfaEnrollmentRequired = "MFA-ENROLL-REQUIRED";

    // Credential envelope (D-03, 02 §3.6)
    public const string CredentialEnvelopeMalformed = "CRED-001";
    public const string CredentialChallengeExpired = "CRED-002";
    public const string CredentialReplayed = "CRED-003";
    public const string CredentialRetargeted = "CRED-004";
    public const string CredentialPlaintext = "CRED-005";

    // Tenancy and branch of the session (CP-04)
    public const string TenantUnresolved = "TENANT-000";
    public const string BranchRequired = "TENANT-004";
    public const string BranchNotAssigned = "TENANT-005";
    public const string TenantForbidden = "TENANT-403";

    // Request pipeline
    public const string ValidationFailed = "VALIDATION-400";
    public const string ConcurrencyConflict = "CONCURRENCY-409";
    public const string UniqueViolation = "UNIQUE-409";
    public const string IdempotencyMismatch = "IDEMP-409";
    public const string RateLimited = "RATE-429";
    public const string CsrfRejected = "CSRF-403";
}
