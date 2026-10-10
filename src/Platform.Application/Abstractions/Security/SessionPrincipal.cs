using Platform.Domain.Identity;
using Platform.Domain.Organization;

namespace Platform.Application.Abstractions.Security;

/// <summary>
/// The result of the one query every authenticated request makes (02 §4.4): the session row joined to the user,
/// the membership of the session's tenant and its role. Identity, role and privileges are resolved here per request,
/// never read from token claims (D-02). <see cref="PermissionKeys"/>, <see cref="SpecialRights"/> and
/// <see cref="ResultRights"/> are the member's <b>effective</b> privileges (BR-17: role ∪ added − removed, the
/// user's special-rights override or the role's, the role's result rights; intake LIS-INT-ORG-001 §5).
/// <see cref="BranchIds"/> is the membership's assignment as stored: empty means every active branch of the tenant.
/// </summary>
public sealed record SessionPrincipal(
    Guid SessionId,
    Guid UserId,
    string DisplayName,
    bool UserActive,
    Guid? TenantId,
    SessionKind Kind,
    SessionScope Scope,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    bool IsRevoked,
    DateTimeOffset? ReauthenticatedAtUtc,
    bool MembershipActive,
    string Language,
    string? RoleName,
    IReadOnlySet<string> PermissionKeys,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<Guid> DepartmentIds,
    Guid? BranchId)
{
    /// <summary>Special rights of the member (the user's override, else the role's, else none).</summary>
    public SpecialRights SpecialRights { get; init; } = SpecialRights.None;

    /// <summary>Result rights per clinical group and branch, from the member's role.</summary>
    public IReadOnlyList<ResultRight> ResultRights { get; init; } = [];

    /// <summary>The membership in the session's tenant, when there is one.</summary>
    public Guid? MembershipId { get; init; }

    public Guid? RoleId { get; init; }

    public bool IsPrimaryAdministrator { get; init; }

    /// <summary>The next sign-in must change the password (CP-38); the sign-in flow and the change-password page enforce it.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>The access end date (LDM manual §12.6); from it the session is no longer live.</summary>
    public DateTimeOffset? PasswordExpiresAtUtc { get; init; }

    /// <summary>A rotated session still authenticates its own access token until it expires; a revoked or expired one never does, nor one whose user's access ended.</summary>
    public bool IsLiveAt(DateTimeOffset nowUtc) =>
        !IsRevoked && UserActive && ExpiresAtUtc > nowUtc && AbsoluteExpiresAtUtc > nowUtc && (TenantId is null || MembershipActive)
        && (PasswordExpiresAtUtc is null || PasswordExpiresAtUtc > nowUtc);
}

/// <summary>Resolves the principal for a session id and the user id decrypted from <c>sub</c>; a mismatch yields <see langword="null"/>. Cached ≤ 10 s per session.</summary>
public interface ISessionPrincipalQuery
{
    Task<SessionPrincipal?> ResolveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);

    void Evict(Guid sessionId);
}

/// <summary>Argon2id password hashing (02 §6.1). The domain stores the PHC string and never sees a plaintext password.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);

    /// <summary>True when the stored hash uses older parameters and should be replaced on the next successful check.</summary>
    bool NeedsRehash(string passwordHash);
}
