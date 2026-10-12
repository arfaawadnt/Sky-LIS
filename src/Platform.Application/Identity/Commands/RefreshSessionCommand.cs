using System.Net;
using System.Security.Cryptography;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Tokens;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.Commands;

/// <summary>
/// Refresh rotation (02 §4.3, D-01): the presented session is dead immediately, a successor in the same family
/// takes over, a new access token is issued. Presenting a rotated or revoked token is reuse: the whole family is
/// revoked and <c>REFRESH_REUSE_DETECTED</c> is written. Every refusal is <c>401 AUTH-012</c>.
/// <para>
/// <b>Grace for the immediate predecessor</b> (validation 2026-10-11, C-02). The tabs of one browser share the refresh
/// cookie, so two tabs that refresh at the same moment present the same token twice: the first rotates it, the second
/// arrives with a token that was rotated a few milliseconds earlier. Within <see cref="SessionPolicy.RefreshReuseGrace"/>
/// (default 5 s) such a request is answered with the session that replaced it: a new access token for the successor and
/// <b>no new refresh token</b> (<see cref="RefreshResult.RefreshToken"/> is <see langword="null"/>, the cookie the first
/// response set stays). The grace holds only when all of these are true, otherwise the request is reuse exactly as
/// before: a <b>browser</b> session (machine clients never share a token), the presented secret matches the stored hash
/// (constant time), the session was <b>rotated, not revoked</b>, the rotation is at most the grace old, and its direct
/// successor is still live (not rotated again, not revoked, not expired) — so a replay after the window, or of any older
/// generation, revokes the family. The trade-off: a thief holding the previous token inside those seconds obtains a
/// 15-minute access token of the live session, never a refresh token; the legitimate holder keeps rotating. A rotation
/// that loses a race on the row (<c>CONCURRENCY-409</c>: both requests read the token before either committed) is
/// resolved the same way against the committed winner. Every grace answer writes <c>SESSION_CONTEXT_CHANGED</c>
/// (info, <c>refresh_grace</c>).
/// </para>
/// <para>
/// <b>A session that ended before it was rotated</b> (validation round 2 R2-06). A token whose session was revoked by sign-out,
/// an administrator, a password change, a tenant suspension or expiry, and never rotated, is the cookie of an ended session
/// (a sign-out response lost to a page navigation leaves it in the browser), not a stolen one: it is refused with
/// <c>AUTH-012</c> without the reuse alarm and without touching the family, and written as <c>SESSION_CONTEXT_CHANGED</c>
/// (info, <c>refresh_after_&lt;reason&gt;</c>). A rotated token, or one of a family already revoked for reuse, stays reuse.
/// </para>
/// </summary>
[AllowUnauthenticated]
public sealed record RefreshSessionCommand(string? RefreshToken, string? ClientIp, string? UserAgent) : ICommand<RefreshResult>;

/// <summary>The outcome of a refresh. <see cref="RefreshToken"/> is <see langword="null"/> for a grace answer (C-02): keep the current cookie.</summary>
public sealed record RefreshResult(IssuedAccessToken AccessToken, string? RefreshToken, SessionKind Kind, AuthProfile Profile);

public sealed class RefreshSessionCommandHandler(
    IRefreshSessionRepository sessions,
    IPlatformUserRepository users,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock,
    SessionPolicy policy) : ICommandHandler<RefreshSessionCommand, RefreshResult>
{
    public async Task<RefreshResult> Handle(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (!refreshTokens.TryParse(command.RefreshToken, out var sessionId, out var presentedHash))
        {
            throw Rejected();
        }

        var session = await sessions.GetForUpdateAsync(sessionId, cancellationToken) ?? throw Rejected();

        if (!CryptographicOperations.FixedTimeEquals(session.RefreshTokenHash, presentedHash))
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionContextChanged, SecurityEvent.Warning, session.TenantId, session.PlatformUserId, "refresh_hash_mismatch"), cancellationToken);
            throw Rejected();
        }

        if (session.IsRevoked && session.RotatedAtUtc is null && session.RevocationReason is { } ended && ended != RevocationReason.ReuseDetected)
        {
            // R2-06: the session ended before this token was ever rotated; nothing was stolen, nothing more to revoke.
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionContextChanged, SecurityEvent.Info, session.TenantId, session.PlatformUserId, EndedSessionDetail(ended)), cancellationToken);
            throw Rejected();
        }

        if (session.IsRevoked || session.RotatedAtUtc is not null)
        {
            if (await TryGraceAsync(session, now, cancellationToken) is { } graced)
            {
                return graced;
            }

            // Reuse of a dead token: the legitimate holder and the thief both lose; the user signs in again (02 §4.3).
            var family = await sessions.ListFamilyAsync(session.FamilyId, cancellationToken);
            foreach (var member in family)
            {
                member.Revoke(RevocationReason.ReuseDetected, now);
                principals.Evict(member.Id);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.WriteAsync(new SecurityEvent(SecurityEvents.RefreshReuseDetected, SecurityEvent.Error, session.TenantId, session.PlatformUserId, session.FamilyId.ToString()), cancellationToken);
            throw Rejected();
        }

        if (!session.IsLiveAt(now))
        {
            session.Revoke(RevocationReason.Expired, now);
            principals.Evict(session.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw Rejected();
        }

        var user = await users.GetForUpdateAsync(session.PlatformUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            session.Revoke(RevocationReason.Admin, now);
            principals.Evict(session.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw Rejected();
        }

        var secret = refreshTokens.NewSecret();
        var sliding = session.Kind == SessionKind.Machine ? policy.MachineSlidingLifetime : policy.BrowserSlidingLifetime;
        var successor = session.Rotate(secret.Hash, now, sliding, ParseIp(command.ClientIp), command.UserAgent);
        sessions.Add(successor);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException conflict) when (conflict.Code == ProblemCodes.ConcurrencyConflict)
        {
            // Another request rotated the same token between our read and our write (C-02): judge against what won.
            // Nothing else is saved in this scope, so the losing changes left in the tracker are never written.
            var committed = await sessions.FindCommittedAsync(session.Id, cancellationToken);
            if (committed is not null && await TryGraceAsync(committed, now, cancellationToken) is { } graced)
            {
                return graced;
            }

            throw Rejected();
        }

        var principal = await principals.ResolveAsync(successor.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, successor.Id, successor.TenantId, successor.Scope));
        var profile = Profiles.ToProfile(principal, successor.Scope);

        return new RefreshResult(accessToken, refreshTokens.Format(successor.Id, secret.Secret), successor.Kind, profile);
    }

    /// <summary>
    /// The grace answer of C-02 (see the type's remarks): the access token of the live successor of a browser session rotated
    /// at most <see cref="SessionPolicy.RefreshReuseGrace"/> ago, or <see langword="null"/> when the request is reuse.
    /// </summary>
    private async Task<RefreshResult?> TryGraceAsync(RefreshSession presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var grace = policy.EffectiveRefreshReuseGrace;
        if (grace <= TimeSpan.Zero
            || presented.Kind != SessionKind.Browser
            || presented.IsRevoked
            || presented.RotatedAtUtc is not { } rotatedAt
            // A rotation later than this request's clock reading is the concurrent case itself (re-test R-01, 2026-10-12):
            // the winner committed after the loser started, so the elapsed time counts as zero, never as a refusal.
            || now - rotatedAt > grace
            || presented.SuccessorId is not { } successorId)
        {
            return null;
        }

        var successor = await sessions.FindCommittedAsync(successorId, cancellationToken);
        if (successor is null || successor.FamilyId != presented.FamilyId || successor.PlatformUserId != presented.PlatformUserId || !successor.IsLiveAt(now))
        {
            return null;   // the successor moved on (rotated again, revoked, expired): the presented token is an older generation
        }

        var user = await users.GetForUpdateAsync(successor.PlatformUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var principal = await principals.ResolveAsync(successor.Id, user.Id, cancellationToken);
        if (principal is null || !principal.IsLiveAt(now))
        {
            return null;
        }

        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionContextChanged, SecurityEvent.Info, successor.TenantId, successor.PlatformUserId, "refresh_grace"), cancellationToken);
        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, successor.Id, successor.TenantId, successor.Scope));
        return new RefreshResult(accessToken, null, successor.Kind, Profiles.ToProfile(principal, successor.Scope));
    }

    /// <summary><c>refresh_after_logout</c>, <c>refresh_after_expired</c>, …: the detail of a refresh of an ended session (R2-06).</summary>
    internal static string EndedSessionDetail(RevocationReason reason) => reason switch
    {
        RevocationReason.Logout => "refresh_after_logout",
        RevocationReason.Admin => "refresh_after_admin",
        RevocationReason.PasswordChanged => "refresh_after_password_changed",
        RevocationReason.Expired => "refresh_after_expired",
        RevocationReason.TenantSuspended => "refresh_after_tenant_suspended",
        _ => "refresh_after_end",
    };

    private static IPAddress? ParseIp(string? value) => IPAddress.TryParse(value, out var address) ? address : null;

    private static DomainException Rejected() =>
        new(ProblemCodes.RefreshTokenRejected, "The refresh token is missing, unknown, expired or no longer valid.", DomainFault.Unauthenticated);
}

/// <summary>
/// Sign-out revokes the whole family of the presented refresh token (02 §4.1). Only a token whose secret matches a live,
/// unrotated session is honoured: a forged cookie naming someone else's session id, or a stale one, revokes nothing.
/// Silent either way: the cookie is cleared regardless.
/// </summary>
[AllowUnauthenticated]
public sealed record SignOutCommand(string? RefreshToken) : ICommand;

public sealed class SignOutCommandHandler(
    IRefreshSessionRepository sessions,
    IRefreshTokenService refreshTokens,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SignOutCommand>
{
    public async Task Handle(SignOutCommand command, CancellationToken cancellationToken)
    {
        if (!refreshTokens.TryParse(command.RefreshToken, out var sessionId, out var presentedHash))
        {
            return;
        }

        var session = await sessions.GetForUpdateAsync(sessionId, cancellationToken);
        if (session is null || !PresentedRefreshToken.Matches(session, presentedHash))
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var member in await sessions.ListFamilyAsync(session.FamilyId, cancellationToken))
        {
            member.Revoke(RevocationReason.Logout, now);
            principals.Evict(member.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionRevoked, SecurityEvent.Info, session.TenantId, session.PlatformUserId, "logout"), cancellationToken);
    }
}

/// <summary>
/// Whether a presented refresh token is the current secret of its session (R2-04, sign-out): the hash matches in constant
/// time and the session was neither rotated nor revoked. A cookie naming someone else's session id with another secret,
/// or a superseded one, proves nothing and ends nothing.
/// </summary>
internal static class PresentedRefreshToken
{
    public static bool Matches(RefreshSession session, byte[] presentedHash) =>
        !session.IsRevoked && session.RotatedAtUtc is null && CryptographicOperations.FixedTimeEquals(session.RefreshTokenHash, presentedHash);
}
