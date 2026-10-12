namespace Platform.WebApi.Startup;

/// <summary>
/// The middleware sequence of 02 §5.4 as data. <c>Program</c> builds the pipeline by walking this list, so the
/// order cannot drift from the specification; WebApiHardeningTests asserts the list itself.
/// </summary>
public static class PipelineOrder
{
    public const string ForwardedHeaders = "ForwardedHeaders";
    public const string Correlation = "Correlation";
    public const string SecurityHeaders = "SecurityHeaders";
    public const string ExceptionHandler = "ExceptionHandler";
    public const string Authentication = "Authentication";
    public const string CredentialAccountKey = "CredentialAccountKey";
    public const string RateLimiter = "RateLimiter";
    public const string AuthOriginCheck = "AuthOriginCheck";
    public const string TenantResolution = "TenantResolution";
    public const string MfaEnrollmentGate = "MfaEnrollmentGate";
    public const string PasswordChangeGate = "PasswordChangeGate";
    public const string Authorization = "Authorization";
    public const string Endpoints = "Endpoints";

    public static readonly IReadOnlyList<string> Sequence =
    [
        ForwardedHeaders,
        Correlation,
        SecurityHeaders,
        ExceptionHandler,
        Authentication,
        CredentialAccountKey,   // feeds the account-keyed credential limiter (02 §5.3); reads a 4 KB buffered copy of the body
        RateLimiter,
        AuthOriginCheck,
        TenantResolution,
        MfaEnrollmentGate,
        PasswordChangeGate,     // C-04: a user who must change the password reaches only the change (02 §6.1, ORG-001 A13)
        Authorization,
        Endpoints,
    ];
}
