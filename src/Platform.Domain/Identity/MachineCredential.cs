using Platform.Domain.Authorization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Identity;

/// <summary>
/// A machine client's credential (02 §4.2, §6.3; intake LIS-INT-INT-001 §5): a public client id and a secret shown
/// once, stored only as a keyed hash. The credential belongs to one tenant and owns a <b>service identity</b> — a
/// <see cref="PlatformUser"/> (<c>svc.&lt;client id&gt;</c>, unusable password), a membership in the tenant and a role
/// holding exactly <see cref="Scopes"/> — so the session, privilege and row-level-security machinery treats a machine
/// like any member. Stored in <c>identity.machine_credentials</c> without row-level security (it is read by client id
/// before any tenant is known, like <c>refresh_sessions</c>); every management read filters by the tenant explicitly.
/// </summary>
public sealed class MachineCredential : AggregateRoot
{
    public const string ClientIdPrefix = "lis-";
    public const int ClientIdLength = 36;
    public const int MaxNameLength = 100;
    public const int MaxSecretHashLength = 128;
    public const int MaxScopes = 50;
    public const int MaxReasonLength = 500;
    public const string ServiceUsernamePrefix = "svc.";

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(730);

    private MachineCredential()
    {
        ClientId = string.Empty;
        Name = string.Empty;
        SecretHash = string.Empty;
        Scopes = [];
    }

    private MachineCredential(Guid id, Guid tenantId, string clientId, string name, string secretHash, Guid serviceUserId, string[] scopes, Guid? branchId, DateTimeOffset expiresAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        ClientId = clientId;
        Name = name;
        SecretHash = secretHash;
        ServiceUserId = serviceUserId;
        Scopes = scopes;
        BranchId = branchId;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>The tenant whose data the machine works on; explicit (not <c>ITenantScoped</c>) because the row is found by client id before a tenant is resolved.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Public identifier, <c>lis-&lt;32 hex&gt;</c>, unique platform-wide.</summary>
    public string ClientId { get; private set; }

    public string Name { get; private set; }

    /// <summary><c>v1.</c> + base64url HMAC-SHA256 of the secret under a master-key subkey. Never the secret.</summary>
    public string SecretHash { get; private set; }

    /// <summary>The service user the sessions of this credential run as.</summary>
    public Guid ServiceUserId { get; private set; }

    /// <summary>Catalogued permission keys, sorted and distinct; the service role holds exactly these.</summary>
    public string[] Scopes { get; private set; }

    /// <summary>The branch every session of the credential is bound to (CP-04), when the machine works for one branch (site agents).</summary>
    public Guid? BranchId { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? SecretRotatedAtUtc { get; private set; }

    /// <summary>Last token exchange or refresh.</summary>
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? RevocationReason { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Usable for a token exchange or refresh at <paramref name="nowUtc"/>.</summary>
    public bool IsLiveAt(DateTimeOffset nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    /// <summary>M1 Issue. The handler checks name uniqueness and the issuer's privileges (SOD-INT-001) before calling.</summary>
    public static MachineCredential Issue(
        Guid tenantId,
        string clientId,
        string name,
        string secretHash,
        Guid serviceUserId,
        IEnumerable<string> scopes,
        Guid? branchId,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        Require(tenantId != Guid.Empty, MachineCredentialCodes.TenantRequired, "A machine credential belongs to a tenant.", DomainFault.Validation);
        Require(IsValidClientId(clientId), MachineCredentialCodes.ClientIdInvalid, "The client id is malformed.", DomainFault.Validation);
        Require(serviceUserId != Guid.Empty, MachineCredentialCodes.ServiceUserRequired, "A machine credential needs its service user.", DomainFault.Validation);

        var expires = ResolveExpiry(expiresAtUtc, nowUtc);
        var credential = new MachineCredential(
            Guid.CreateVersion7(),
            tenantId,
            clientId,
            CleanName(name),
            RequireHash(secretHash),
            serviceUserId,
            CleanScopes(scopes),
            branchId == Guid.Empty ? null : branchId,
            expires);

        credential.Raise(new MachineCredentialIssued(credential.Id, tenantId, clientId, serviceUserId, credential.Scopes, expires, nowUtc));
        return credential;
    }

    public void Rename(string name) => Name = CleanName(name);

    /// <summary>M2 Rotate: a live credential gets a new secret; the caller ends the credential's live sessions in the same commit.</summary>
    public void RotateSecret(string secretHash, DateTimeOffset nowUtc)
    {
        Require(IsLiveAt(nowUtc), MachineCredentialCodes.CredentialNotLive, "Only a live credential can be rotated; issue a new one.", DomainFault.Conflict);
        SecretHash = RequireHash(secretHash);
        SecretRotatedAtUtc = nowUtc;
        Raise(new MachineCredentialSecretRotated(Id, TenantId, ClientId, nowUtc));
    }

    /// <summary>M3 Revoke. Idempotent: a second revocation keeps the first record. The caller deactivates the service user and ends its sessions.</summary>
    public void Revoke(Guid actorUserId, string? reason, DateTimeOffset nowUtc)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        var cleaned = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Require(cleaned is null || cleaned.Length <= MaxReasonLength, MachineCredentialCodes.ReasonTooLong, $"The reason is at most {MaxReasonLength} characters.", DomainFault.Validation);

        RevokedAtUtc = nowUtc;
        RevokedByUserId = actorUserId;
        RevocationReason = cleaned;
        Raise(new MachineCredentialRevoked(Id, TenantId, ClientId, actorUserId, cleaned, nowUtc));
    }

    /// <summary>Stamps the last successful token exchange or refresh (no event: operational).</summary>
    public void RecordUse(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;

    /// <summary>The service user's sign-in name: <c>svc.&lt;client id&gt;</c>.</summary>
    public static string ServiceUsername(string clientId) => ServiceUsernamePrefix + clientId;

    public static bool IsValidClientId(string? clientId) =>
        clientId is { Length: ClientIdLength }
        && clientId.StartsWith(ClientIdPrefix, StringComparison.Ordinal)
        && clientId.AsSpan(ClientIdPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    public static string CleanName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        Require(value.Length is >= 1 and <= MaxNameLength && !value.Any(char.IsControl), MachineCredentialCodes.NameInvalid, $"The name must be 1 to {MaxNameLength} characters.", DomainFault.Validation);
        return value;
    }

    /// <summary>1 to <see cref="MaxScopes"/> catalogued keys (MCR-002), sorted and distinct.</summary>
    public static string[] CleanScopes(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var keys = scopes.Select(scope => (scope ?? string.Empty).Trim()).Where(scope => scope.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unknown = keys.Where(key => !PermissionCatalog.IsKnown(key)).ToList();
        Require(unknown.Count == 0, MachineCredentialCodes.ScopesInvalid, $"Unknown permission keys: {string.Join(", ", unknown)}.", DomainFault.Validation);
        Require(keys.Length is >= 1 and <= MaxScopes, MachineCredentialCodes.ScopesInvalid, $"A machine credential carries 1 to {MaxScopes} permission keys.", DomainFault.Validation);
        return keys;
    }

    private static DateTimeOffset ResolveExpiry(DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
    {
        var expires = expiresAtUtc ?? nowUtc + DefaultLifetime;
        Require(expires > nowUtc && expires <= nowUtc + MaxLifetime, MachineCredentialCodes.ExpiryInvalid, "The expiry must be in the future and at most two years away.", DomainFault.Validation);
        return expires;
    }

    private static string RequireHash(string secretHash)
    {
        Require(!string.IsNullOrWhiteSpace(secretHash) && secretHash.Length <= MaxSecretHashLength, MachineCredentialCodes.SecretHashInvalid, "A secret hash is required.", DomainFault.Validation);
        return secretHash;
    }
}
