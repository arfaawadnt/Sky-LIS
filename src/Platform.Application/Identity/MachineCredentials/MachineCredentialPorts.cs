using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;

namespace Platform.Application.Identity.MachineCredentials;

/// <summary>
/// Write side of <see cref="MachineCredential"/> (02 §6.3). The table has no row-level security (the token exchange
/// finds a row by client id before any tenant is known), so every management read names the tenant explicitly.
/// </summary>
public interface IMachineCredentialRepository : IRepository<MachineCredential>
{
    /// <summary>The credential with this client id in any tenant (token exchange only), tracked.</summary>
    Task<MachineCredential?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>The credential whose sessions run as <paramref name="serviceUserId"/> (refresh, agent resolution), tracked.</summary>
    Task<MachineCredential?> FindByServiceUserAsync(Guid serviceUserId, CancellationToken cancellationToken);

    /// <summary>The credential <paramref name="id"/> of <paramref name="tenantId"/>, tracked; another tenant's id reads as absent.</summary>
    Task<MachineCredential?> GetInTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>A live (not revoked) credential of the tenant already carries this name, compared case-insensitively.</summary>
    Task<bool> LiveNameExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken);

    Task<Paged<MachineCredentialListItem>> ListAsync(Guid tenantId, bool includeRevoked, DateTimeOffset nowUtc, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Client id, expiry and status (Live, Expired, Revoked) of the given credentials of the tenant, for registers that show them.</summary>
    Task<IReadOnlyDictionary<Guid, MachineCredentialSummary>> SummariesAsync(Guid tenantId, IReadOnlyCollection<Guid> credentialIds, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed record MachineCredentialSummary(Guid Id, string ClientId, DateTimeOffset ExpiresAtUtc, string Status);

/// <summary>
/// Stages the service identity of a credential: the platform user, its role and its membership. The role is created in
/// the same commit as the membership that references it, so the store stamps the role with the scope's tenant as it adds
/// it (what the tenant-stamp interceptor would do at commit) and <see cref="TenantMembership.AssignRole"/> can check it.
/// </summary>
public interface IMachineServiceIdentityStore
{
    void Add(PlatformUser serviceUser, Role role, TenantMembership membership);
}

/// <summary>A freshly generated secret: the value shown once and the keyed hash stored.</summary>
public sealed record MachineSecret(string Secret, string Hash);

/// <summary>Random client ids and secrets, and their keyed hash (02 §6.3: the secret is random, so a keyed hash suffices).</summary>
public interface IMachineSecrets
{
    /// <summary><c>lis-&lt;32 hex&gt;</c> from 16 random bytes.</summary>
    string NewClientId();

    /// <summary>32 random bytes, base64url, with its hash.</summary>
    MachineSecret NewSecret();

    /// <summary>Constant-time comparison of the presented secret with the stored hash; a malformed hash never matches.</summary>
    bool Verify(string storedHash, string presentedSecret);

    /// <summary>A password hash no password can ever match (not a PHC string), for the service user.</summary>
    string UnusablePasswordHash();
}

/// <summary>Security event names of machine credentials (02 §7). Details carry a reason category, never secret material.</summary>
public static class MachineSecurityEvents
{
    public const string CredentialCreated = "MACHINE_CREDENTIAL_CREATED";
    public const string CredentialRotated = "MACHINE_CREDENTIAL_ROTATED";
    public const string CredentialRevoked = "MACHINE_CREDENTIAL_REVOKED";
    public const string TokenIssued = "MACHINE_TOKEN_ISSUED";
    public const string TokenRefused = "MACHINE_TOKEN_REFUSED";
}
