using System.Net;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Identity;

/// <summary>
/// One browser or machine session (D-01, 01 §4.7, 02 §4.3). The id is the <c>sid</c> claim of every access token
/// issued for it; the refresh token's secret is stored only as a SHA-256 hash. Rotation creates a successor in
/// the same family; presenting a rotated or revoked token is reuse, and the caller revokes the whole family.
/// Stored in <c>identity.refresh_sessions</c>: no tenant key, no RLS, always looked up by id or token hash.
/// </summary>
public sealed class RefreshSession : AggregateRoot
{
    public const int TokenHashLength = 32;
    public const int MaxUserAgentLength = 512;

    private RefreshSession()
    {
        RefreshTokenHash = [];
    }

    private RefreshSession(
        Guid id,
        Guid familyId,
        Guid platformUserId,
        Guid? tenantId,
        SessionKind kind,
        SessionScope scope,
        byte[] refreshTokenHash,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset absoluteExpiresAtUtc,
        IPAddress? ipAddress,
        string? userAgent)
        : base(id)
    {
        FamilyId = familyId;
        PlatformUserId = platformUserId;
        TenantId = tenantId;
        Kind = kind;
        Scope = scope;
        RefreshTokenHash = refreshTokenHash;
        IssuedAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
        AbsoluteExpiresAtUtc = absoluteExpiresAtUtc;
        IpAddress = ipAddress;
        UserAgent = TrimUserAgent(userAgent);
    }

    /// <summary>The rotation family; reuse of any rotated token revokes every session in it.</summary>
    public Guid FamilyId { get; private set; }

    public Guid PlatformUserId { get; private set; }

    /// <summary>The tenant selected at sign-in; <see langword="null"/> for platform administrators.</summary>
    public Guid? TenantId { get; private set; }

    public SessionKind Kind { get; private set; }

    public SessionScope Scope { get; private set; }

    /// <summary>SHA-256 of the refresh secret. The secret itself never reaches the database or the logs.</summary>
    public byte[] RefreshTokenHash { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    /// <summary>Sliding (idle) expiry, extended on every rotation.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Hard limit, fixed when the family starts; successors inherit it.</summary>
    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    public DateTimeOffset? RotatedAtUtc { get; private set; }

    public Guid? SuccessorId { get; private set; }

    public bool IsRevoked { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public RevocationReason? RevocationReason { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    /// <summary>Last fresh password proof, for step-up re-authentication (02 §4.5).</summary>
    public DateTimeOffset? ReauthenticatedAtUtc { get; private set; }

    /// <summary>
    /// The branch the session works in (CP-04): chosen once after sign-in, inherited by every successor of the
    /// rotation family, read by <c>ICurrentUser.BranchId</c> and stamped on every branch-scoped transaction.
    /// <see langword="null"/> until selected; sign-in selects it when the membership has exactly one branch.
    /// </summary>
    public Guid? BranchId { get; private set; }

    public IPAddress? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Optimistic concurrency token, mapped onto PostgreSQL's <c>xmin</c> system column.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Starts a new family. Lifetimes come from configuration (02 §4.1): browser 8 h sliding / 12 h absolute, machine 30 d / 90 d.</summary>
    public static RefreshSession Open(
        Guid platformUserId,
        Guid? tenantId,
        SessionKind kind,
        SessionScope scope,
        byte[] refreshTokenHash,
        DateTimeOffset nowUtc,
        TimeSpan slidingLifetime,
        TimeSpan absoluteLifetime,
        IPAddress? ipAddress = null,
        string? userAgent = null)
    {
        Require(platformUserId != Guid.Empty, IdentityCodes.UserNotFound, "A session needs a user.", DomainFault.Validation);
        Require(slidingLifetime > TimeSpan.Zero && absoluteLifetime >= slidingLifetime, IdentityCodes.SessionLifetimeInvalid, "The absolute lifetime must be at least the sliding lifetime.", DomainFault.Validation);

        var session = new RefreshSession(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            platformUserId,
            tenantId,
            kind,
            scope,
            RequireHash(refreshTokenHash),
            nowUtc,
            nowUtc + slidingLifetime,
            nowUtc + absoluteLifetime,
            ipAddress,
            userAgent);

        session.Raise(new RefreshSessionOpened(session.Id, session.FamilyId, platformUserId, tenantId, kind, nowUtc));
        return session;
    }

    public bool IsLiveAt(DateTimeOffset nowUtc) => !IsRevoked && RotatedAtUtc is null && ExpiresAtUtc > nowUtc && AbsoluteExpiresAtUtc > nowUtc;

    /// <summary>
    /// Validation F-01 (tenant setting <c>sessionTimeoutMinutes</c>): true when nobody renewed the session for longer than
    /// <paramref name="idleTimeout"/>. The last activity the server knows is <see cref="LastSeenAtUtc"/>: the sign-in that
    /// opened the session or the rotation that created it (every refresh is a rotation, so a live session's own value is the
    /// time of the last refresh), or a later <see cref="Touch"/>. The SPA renews the session while its user is active (idle
    /// watcher heartbeat), so an idle session is one whose browser stopped asking.
    /// </summary>
    public bool IsIdleAt(DateTimeOffset nowUtc, TimeSpan idleTimeout) => idleTimeout > TimeSpan.Zero && nowUtc - LastSeenAtUtc > idleTimeout;

    /// <summary>
    /// Rotates: this session is dead immediately and a successor in the same family takes over, with the sliding
    /// expiry extended and the absolute expiry kept. Presenting this session again afterwards is reuse.
    /// </summary>
    public RefreshSession Rotate(byte[] successorTokenHash, DateTimeOffset nowUtc, TimeSpan slidingLifetime, IPAddress? ipAddress = null, string? userAgent = null)
    {
        Require(IsLiveAt(nowUtc), IdentityCodes.SessionNotLive, "The session is not live.", DomainFault.Conflict);
        Require(slidingLifetime > TimeSpan.Zero, IdentityCodes.SessionLifetimeInvalid, "The sliding lifetime must be positive.", DomainFault.Validation);

        var expires = nowUtc + slidingLifetime;
        var successor = new RefreshSession(
            Guid.CreateVersion7(),
            FamilyId,
            PlatformUserId,
            TenantId,
            Kind,
            Scope,
            RequireHash(successorTokenHash),
            nowUtc,
            expires < AbsoluteExpiresAtUtc ? expires : AbsoluteExpiresAtUtc,
            AbsoluteExpiresAtUtc,
            ipAddress ?? IpAddress,
            userAgent ?? UserAgent)
        {
            ReauthenticatedAtUtc = ReauthenticatedAtUtc,
            BranchId = BranchId,
        };

        RotatedAtUtc = nowUtc;
        SuccessorId = successor.Id;
        LastSeenAtUtc = nowUtc;
        Raise(new RefreshSessionRotated(Id, successor.Id, FamilyId, PlatformUserId, nowUtc));
        return successor;
    }

    /// <summary>Ends the session. Idempotent: a second revocation keeps the first reason.</summary>
    public void Revoke(RevocationReason reason, DateTimeOffset nowUtc)
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAtUtc = nowUtc;
        RevocationReason = reason;
        Raise(new RefreshSessionRevoked(Id, FamilyId, PlatformUserId, reason, nowUtc));
    }

    /// <summary>MFA verified or enrolment completed: the session becomes fully usable (02 §4.1).</summary>
    public void GrantFullScope(DateTimeOffset nowUtc)
    {
        Require(IsLiveAt(nowUtc), IdentityCodes.SessionNotLive, "The session is not live.", DomainFault.Conflict);
        Require(Scope != SessionScope.Full, IdentityCodes.SessionScopeTransitionInvalid, "The session already has full scope.", DomainFault.Conflict);
        Scope = SessionScope.Full;
    }

    public void Touch(DateTimeOffset nowUtc, IPAddress? ipAddress = null, string? userAgent = null)
    {
        LastSeenAtUtc = nowUtc;
        if (ipAddress is not null)
        {
            IpAddress = ipAddress;
        }

        if (userAgent is not null)
        {
            UserAgent = TrimUserAgent(userAgent);
        }
    }

    public void MarkReauthenticated(DateTimeOffset nowUtc)
    {
        Require(IsLiveAt(nowUtc), IdentityCodes.SessionNotLive, "The session is not live.", DomainFault.Conflict);
        ReauthenticatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Binds the session to a branch (CP-04). Whether the branch is one the membership may work in is the handler's
    /// check against the membership scope (TENANT-005); the session only records the choice. Re-selecting the same
    /// branch is a no-op; choosing another one is allowed and announced.
    /// </summary>
    public void SelectBranch(Guid branchId, DateTimeOffset nowUtc)
    {
        Require(IsLiveAt(nowUtc), IdentityCodes.SessionNotLive, "The session is not live.", DomainFault.Conflict);
        Require(branchId != Guid.Empty, IdentityCodes.SessionBranchInvalid, "A branch is required.", DomainFault.Validation);

        if (BranchId == branchId)
        {
            return;
        }

        BranchId = branchId;
        Raise(new RefreshSessionBranchSelected(Id, PlatformUserId, TenantId, branchId, nowUtc));
    }

    private static byte[] RequireHash(byte[] hash)
    {
        Require(hash is { Length: TokenHashLength }, IdentityCodes.SessionTokenHashInvalid, $"The token hash must be {TokenHashLength} bytes.", DomainFault.Validation);
        return hash;
    }

    private static string? TrimUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var value = userAgent.Trim();
        return value.Length <= MaxUserAgentLength ? value : value[..MaxUserAgentLength];
    }
}
