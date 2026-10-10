using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Tokens;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.MachineCredentials.Commands;

/// <summary>The access token, the refresh token the machine stores, and the scopes the credential carries.</summary>
public sealed record MachineTokenResult(IssuedAccessToken AccessToken, string RefreshToken, IReadOnlyList<string> Scopes);

/// <summary>
/// M4 Token exchange (02 §4.2, §6.3): client id + secret (opened from its credential envelope by the edge) → a Machine
/// session in the credential's tenant, bound to its branch. Every refusal is the same <c>401 AUTH-002</c>, whatever was
/// wrong (unknown client, wrong secret, revoked, expired, inactive service user); the security event records the category.
/// </summary>
[AllowUnauthenticated]
public sealed record ExchangeMachineTokenCommand(string ClientId, string ClientSecret, string? ClientIp, string? UserAgent) : ICommand<MachineTokenResult>;

public sealed class ExchangeMachineTokenValidator : AbstractValidator<ExchangeMachineTokenCommand>
{
    public ExchangeMachineTokenValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty().MaximumLength(MachineCredential.ClientIdLength);
        RuleFor(command => command.ClientSecret).NotEmpty().MaximumLength(256);
        RuleFor(command => command.UserAgent).MaximumLength(RefreshSession.MaxUserAgentLength);
    }
}

public sealed class ExchangeMachineTokenHandler(
    IMachineCredentialRepository credentials,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IMachineSecrets secrets,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock,
    SessionPolicy policy) : ICommandHandler<ExchangeMachineTokenCommand, MachineTokenResult>
{
    // Verified against for unknown client ids so the response time does not tell whether the client exists.
    private const string DecoyHash = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public async Task<MachineTokenResult> Handle(ExchangeMachineTokenCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var clientId = command.ClientId.Trim();
        var credential = MachineCredential.IsValidClientId(clientId) ? await credentials.FindByClientIdAsync(clientId, cancellationToken) : null;

        if (credential is null)
        {
            secrets.Verify(DecoyHash, command.ClientSecret);
            throw await RefusedAsync(null, null, "unknown_client", cancellationToken);
        }

        // The secret is checked first: a caller without it learns nothing about the credential's state.
        if (!secrets.Verify(credential.SecretHash, command.ClientSecret))
        {
            throw await RefusedAsync(credential.TenantId, credential.ServiceUserId, "wrong_secret", cancellationToken);
        }

        if (credential.IsRevoked)
        {
            throw await RefusedAsync(credential.TenantId, credential.ServiceUserId, "revoked", cancellationToken);
        }

        if (credential.ExpiresAtUtc <= now)
        {
            throw await RefusedAsync(credential.TenantId, credential.ServiceUserId, "expired", cancellationToken);
        }

        var user = await users.GetForUpdateAsync(credential.ServiceUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw await RefusedAsync(credential.TenantId, credential.ServiceUserId, "inactive_user", cancellationToken);
        }

        var secret = refreshTokens.NewSecret();
        var session = RefreshSession.Open(
            user.Id,
            credential.TenantId,
            SessionKind.Machine,
            SessionScope.Full,
            secret.Hash,
            now,
            policy.MachineSlidingLifetime,
            policy.MachineAbsoluteLifetime,
            IPAddress.TryParse(command.ClientIp, out var address) ? address : null,
            command.UserAgent);
        if (credential.BranchId is { } branchId)
        {
            session.SelectBranch(branchId, now);   // CP-04: a site agent works for its branch
        }

        sessions.Add(session);
        credential.RecordUse(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.TokenIssued, SecurityEvent.Info, credential.TenantId, user.Id, credential.ClientId), cancellationToken);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionCreated, SecurityEvent.Info, credential.TenantId, user.Id, session.Id.ToString()), cancellationToken);

        _ = await principals.ResolveAsync(session.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, session.Id, session.TenantId, session.Scope));
        return new MachineTokenResult(accessToken, refreshTokens.Format(session.Id, secret.Secret), credential.Scopes);
    }

    private async Task<DomainException> RefusedAsync(Guid? tenantId, Guid? serviceUserId, string reason, CancellationToken cancellationToken)
    {
        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.TokenRefused, SecurityEvent.Warning, tenantId, serviceUserId, reason), cancellationToken);
        return new DomainException(ProblemCodes.InvalidCredentials, "The client id or secret is incorrect.", DomainFault.Unauthenticated);
    }
}

/// <summary>
/// M5 Machine refresh (02 §4.3): the presented machine session is rotated in its family; presenting a rotated or revoked
/// token revokes the family (<c>REFRESH_REUSE_DETECTED</c>); a session whose credential is revoked or expired, or whose
/// service user is inactive, is revoked. One commit records whichever outcome, then a refusal is <c>401 AUTH-012</c>.
/// A browser session is never refreshed here.
/// </summary>
[AllowUnauthenticated]
public sealed record RefreshMachineTokenCommand(string? RefreshToken, string? ClientIp, string? UserAgent) : ICommand<MachineTokenResult>;

public sealed class RefreshMachineTokenValidator : AbstractValidator<RefreshMachineTokenCommand>
{
    public RefreshMachineTokenValidator()
    {
        RuleFor(command => command.RefreshToken).MaximumLength(256);
        RuleFor(command => command.UserAgent).MaximumLength(RefreshSession.MaxUserAgentLength);
    }
}

public sealed class RefreshMachineTokenHandler(
    IMachineCredentialRepository credentials,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock,
    SessionPolicy policy) : ICommandHandler<RefreshMachineTokenCommand, MachineTokenResult>
{
    private enum Outcome
    {
        Rotated,
        Reused,
        Expired,
        CredentialEnded,
        UserInactive,
    }

    public async Task<MachineTokenResult> Handle(RefreshMachineTokenCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!refreshTokens.TryParse(command.RefreshToken, out var sessionId, out var presentedHash))
        {
            throw Rejected();
        }

        var session = await sessions.GetForUpdateAsync(sessionId, cancellationToken);
        if (session is null || session.Kind != SessionKind.Machine)
        {
            throw Rejected();
        }

        if (!CryptographicOperations.FixedTimeEquals(session.RefreshTokenHash, presentedHash))
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionContextChanged, SecurityEvent.Warning, session.TenantId, session.PlatformUserId, "machine_refresh_hash_mismatch"), cancellationToken);
            throw Rejected();
        }

        var credential = await credentials.FindByServiceUserAsync(session.PlatformUserId, cancellationToken);
        var user = await users.GetForUpdateAsync(session.PlatformUserId, cancellationToken);

        RefreshSession? successor = null;
        RefreshSecret? secret = null;
        Outcome outcome;
        if (session.IsRevoked || session.RotatedAtUtc is not null)
        {
            outcome = Outcome.Reused;
            await RevokeFamilyAsync(session, RevocationReason.ReuseDetected, now, cancellationToken);
        }
        else if (!session.IsLiveAt(now))
        {
            outcome = Outcome.Expired;
            Revoke(session, RevocationReason.Expired, now);
        }
        else if (credential is null || !credential.IsLiveAt(now))
        {
            outcome = Outcome.CredentialEnded;
            await RevokeFamilyAsync(session, RevocationReason.Admin, now, cancellationToken);
        }
        else if (user is null || !user.IsActive)
        {
            outcome = Outcome.UserInactive;
            await RevokeFamilyAsync(session, RevocationReason.Admin, now, cancellationToken);
        }
        else
        {
            outcome = Outcome.Rotated;
            secret = refreshTokens.NewSecret();
            successor = session.Rotate(secret.Hash, now, policy.MachineSlidingLifetime, IPAddress.TryParse(command.ClientIp, out var address) ? address : null, command.UserAgent);
            sessions.Add(successor);
            credential.RecordUse(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit for every outcome: a refusal keeps its revocations

        if (outcome != Outcome.Rotated || successor is null || secret is null || credential is null || user is null)
        {
            var name = outcome == Outcome.Reused ? SecurityEvents.RefreshReuseDetected : MachineSecurityEvents.TokenRefused;
            var severity = outcome == Outcome.Reused ? SecurityEvent.Error : SecurityEvent.Warning;
            await events.WriteAsync(new SecurityEvent(name, severity, session.TenantId, session.PlatformUserId, "refresh_" + outcome.ToString().ToLowerInvariant()), cancellationToken);
            throw Rejected();
        }

        _ = await principals.ResolveAsync(successor.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, successor.Id, successor.TenantId, successor.Scope));
        return new MachineTokenResult(accessToken, refreshTokens.Format(successor.Id, secret.Secret), credential.Scopes);
    }

    private async Task RevokeFamilyAsync(RefreshSession session, RevocationReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var member in await sessions.ListFamilyAsync(session.FamilyId, cancellationToken))
        {
            Revoke(member, reason, now);
        }

        Revoke(session, reason, now);
    }

    private void Revoke(RefreshSession session, RevocationReason reason, DateTimeOffset now)
    {
        session.Revoke(reason, now);
        principals.Evict(session.Id);
    }

    private static DomainException Rejected() =>
        new(ProblemCodes.RefreshTokenRejected, "The refresh token is missing, unknown, expired or no longer valid.", DomainFault.Unauthenticated);
}
