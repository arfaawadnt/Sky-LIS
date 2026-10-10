namespace Platform.Domain.Identity;

/// <summary>Refusal codes of machine credentials (02 §6.3; intake LIS-INT-INT-001 §7 rows M1–M5).</summary>
public static class MachineCredentialCodes
{
    public const string NameInvalid = "MCR-001";
    public const string ScopesInvalid = "MCR-002";
    public const string ExpiryInvalid = "MCR-003";
    public const string ScopeBeyondOwnPrivileges = "MCR-004";
    public const string ClientIdInvalid = "MCR-005";
    public const string SecretHashInvalid = "MCR-006";
    public const string ServiceUserRequired = "MCR-007";
    public const string TenantRequired = "MCR-008";
    public const string ReasonTooLong = "MCR-009";
    public const string CredentialNotLive = "MCR-010";

    public const string NameExists = "MCR-100";
    public const string CredentialNotFound = "MCR-404";
}
