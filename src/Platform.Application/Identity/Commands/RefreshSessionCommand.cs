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
/// </summary>
[AllowUnauthenticated]
public sealed record RefreshSessionCommand(string? RefreshToken, string? ClientIp, string? UserAgent) : ICommand<RefreshResult>;

public sealed record RefreshResult(IssuedAccessToken AccessToken, string RefreshToken, SessionKind Kind, AuthProfile Profile);

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

        if (session.IsRevoked || session.RotatedAtUtc is not null)
        {
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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var principal = await principals.ResolveAsync(successor.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, successor.Id, successor.TenantId, successor.Scope));
        var profile = Profiles.ToProfile(principal, successor.Scope);

        return new RefreshResult(accessToken, refreshTokens.Format(successor.Id, secret.Secret), successor.Kind, profile);
    }

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
