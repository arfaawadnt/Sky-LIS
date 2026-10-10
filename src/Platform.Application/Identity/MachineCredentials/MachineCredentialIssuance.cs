using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.MachineCredentials;

/// <summary>What a credential is issued for: the tenant, a name, the permission keys, the branch its sessions bind to and the expiry.</summary>
public sealed record MachineCredentialSpec(Guid TenantId, string Name, IReadOnlyCollection<string> Scopes, Guid? BranchId, DateTimeOffset? ExpiresAtUtc);

/// <summary>The staged credential and the secret to show once; nothing is persisted until the caller commits.</summary>
public sealed record IssuedMachineCredential(MachineCredential Credential, string Secret);

/// <summary>
/// M1 Issue (02 §6.3), shared by the credential management command and by every module that creates a credential with
/// its own record (site agents, CP-61) so both commit the credential and their aggregate together. Stages the service
/// identity — a platform user <c>svc.&lt;client id&gt;</c> with an unusable password, a role "Service: …" holding exactly
/// the scopes and a membership in the tenant bound to the branch — and the credential. A static function rather than a
/// registered service: the callers' handlers own the ports and the single commit (01 §3.3).
/// </summary>
public static class MachineCredentialIssuance
{
    public const string ServiceEmailDomain = "machine.lis.local";

    public static IssuedMachineCredential Issue(
        MachineCredentialSpec spec,
        IMachineCredentialRepository credentials,
        IMachineServiceIdentityStore identities,
        IMachineSecrets secrets,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(secrets);

        var name = MachineCredential.CleanName(spec.Name);
        var scopes = MachineCredential.CleanScopes(spec.Scopes);
        var clientId = secrets.NewClientId();
        var secret = secrets.NewSecret();
        var username = MachineCredential.ServiceUsername(clientId);
        var display = new LocalizedText($"Service: {name}", $"خدمة: {name}");
        var suffix = clientId[^8..];

        var user = PlatformUser.Register(username, $"{username}@{ServiceEmailDomain}", display.En, secrets.UnusablePasswordHash(), nowUtc);
        var role = Role.Create(new LocalizedText($"{display.En} ({suffix})", $"{display.Ar} ({suffix})"), scopes, nowUtc, $"Machine credential {clientId}");
        var membership = TenantMembership.Create(spec.TenantId, user.Id, "SVC-" + clientId[MachineCredential.ClientIdPrefix.Length..32].ToUpperInvariant(), display, "en", nowUtc);
        if (spec.BranchId is { } branchId && branchId != Guid.Empty)
        {
            membership.AssignBranches([branchId]);
        }

        identities.Add(user, role, membership);
        membership.AssignRole(role, nowUtc);

        var credential = MachineCredential.Issue(spec.TenantId, clientId, name, secret.Hash, user.Id, scopes, spec.BranchId, spec.ExpiresAtUtc, nowUtc);
        credentials.Add(credential);
        return new IssuedMachineCredential(credential, secret.Secret);
    }

    /// <summary>SOD-INT-001: a person never grants a machine more than they hold (MCR-004).</summary>
    public static void EnsureGrantable(IUserPrivileges privileges, IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        var missing = MachineCredential.CleanScopes(scopes).Where(scope => !privileges.Has(scope)).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException(MachineCredentialCodes.ScopeBeyondOwnPrivileges, $"You cannot grant a permission you do not hold: {string.Join(", ", missing)}.", DomainFault.Refused);
        }
    }

    /// <summary>The tenant of the management request, or <c>TENANT-000</c>.</summary>
    public static Guid RequireTenant(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.TenantId ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this request.", DomainFault.Refused);
    }

    /// <summary>
    /// M3 side effects in the caller's commit: the service user is deactivated (a revoked principal fails at the next
    /// request, 02 §4.4) and every live session is revoked and evicted from the principal cache.
    /// </summary>
    public static async Task EndServiceIdentityAsync(MachineCredential credential, IPlatformUserRepository users, IRefreshSessionRepository sessions, ISessionPrincipalQuery principals, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var user = await users.GetForUpdateAsync(credential.ServiceUserId, cancellationToken);
        user?.Deactivate(nowUtc);
        await EndSessionsAsync(credential, sessions, principals, nowUtc, cancellationToken);
    }

    /// <summary>M2 side effect: the credential's live sessions end (02 §6.3: no enforced overlap after a rotation).</summary>
    public static async Task EndSessionsAsync(MachineCredential credential, IRefreshSessionRepository sessions, ISessionPrincipalQuery principals, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        foreach (var session in await sessions.ListLiveForUserAsync(credential.ServiceUserId, cancellationToken))
        {
            session.Revoke(RevocationReason.Admin, nowUtc);
            principals.Evict(session.Id);
        }
    }
}
