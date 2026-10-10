using System.Net;
using FluentValidation;
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

/// <summary>Browser sign-in (02 §4.1). The password arrives in plaintext here because the WebApi edge already opened its envelope (D-03).</summary>
[AllowUnauthenticated]
public sealed record SignInCommand(Guid? Tenant, string Username, string Password, string? ClientIp, string? UserAgent) : ICommand<SignInResult>;

public sealed record SignInResult(
    string Step,
    IssuedAccessToken? AccessToken,
    string? RefreshToken,
    SessionKind Kind,
    AuthProfile? Profile,
    IReadOnlyList<TenantChoice>? Tenants);

public sealed class SignInCommandValidator : AbstractValidator<SignInCommand>
{
    public SignInCommandValidator()
    {
        RuleFor(command => command.Username).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(1024);
        RuleFor(command => command.UserAgent).MaximumLength(RefreshSession.MaxUserAgentLength);
    }
}

public sealed class SignInCommandHandler(
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IMembershipDirectory memberships,
    IPasswordHasher hasher,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    ISecurityEventLog events,
    ISessionPrincipalQuery principals,
    IUnitOfWork unitOfWork,
    IClock clock,
    SessionPolicy policy) : ICommandHandler<SignInCommand, SignInResult>
{
    // A fixed hash verified for unknown usernames so the response time does not reveal whether the account exists.
    private static readonly string DecoyHash = "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public async Task<SignInResult> Handle(SignInCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await users.FindByUsernameAsync(command.Username, cancellationToken);

        if (user is null)
        {
            hasher.Verify(DecoyHash, command.Password);
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, Detail: "unknown_account"), cancellationToken);
            throw InvalidCredentials();
        }

        if (!user.IsActive)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "inactive"), cancellationToken);
            throw new DomainException(ProblemCodes.AccountInactive, "The account is inactive.", DomainFault.Unauthenticated);
        }

        if (user.IsLockedOutAt(now))
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "locked_out"), cancellationToken);
            throw new DomainException(ProblemCodes.AccountLocked, "The account is locked; try again later.", DomainFault.Unauthenticated);
        }

        if (!hasher.Verify(user.PasswordHash, command.Password))
        {
            // A checked, wrong password: the only thing that counts toward the lockout (02 §5.1). Committed before refusing.
            user.RecordFailedPasswordCheck(now, policy.LockoutThreshold, policy.LockoutDuration);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "wrong_password"), cancellationToken);
            if (user.IsLockedOut)
            {
                await events.WriteAsync(new SecurityEvent(SecurityEvents.UserLockedOut, SecurityEvent.Warning, ActorUserId: user.Id), cancellationToken);
            }

            throw InvalidCredentials();
        }

        user.RecordSuccessfulPasswordCheck(now);
        if (hasher.NeedsRehash(user.PasswordHash))
        {
            user.UpgradePasswordHash(hasher.Hash(command.Password));
        }

        // The access end date set by an administrator (CP-38) ends sign-in; checked after the password so it reveals nothing to a guesser.
        if (user.IsPasswordExpiredAt(now))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "access_expired"), cancellationToken);
            throw new DomainException(ProblemCodes.AccountExpired, "The account has expired.", DomainFault.Unauthenticated);
        }

        var active = await memberships.ListActiveForUserAsync(user.Id, cancellationToken);
        var membership = SelectMembership(active, command.Tenant);
        if (membership is null)
        {
            if (active.Count > 1 && command.Tenant is null)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new SignInResult(AuthResponse.StepSelectTenant, null, null, SessionKind.Browser, null,
                    active.Select(m => new TenantChoice(m.TenantId, m.TenantId.ToString())).ToList());
            }

            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "no_membership"), cancellationToken);
            throw new DomainException(ProblemCodes.NoActiveMembership, "No active membership for this tenant.", DomainFault.Unauthenticated);
        }

        // MFA is a capability whose policy is configuration (02 §4.5); until a policy requires it, sessions open with full scope.
        var secret = refreshTokens.NewSecret();
        var session = RefreshSession.Open(
            user.Id,
            membership.TenantId,
            SessionKind.Browser,
            SessionScope.Full,
            secret.Hash,
            now,
            policy.BrowserSlidingLifetime,
            policy.BrowserAbsoluteLifetime,
            ParseIp(command.ClientIp),
            command.UserAgent);
        if (membership.BranchIds.Count == 1)
        {
            session.SelectBranch(membership.BranchIds[0], now);   // CP-04: a single assigned branch needs no selection step
        }

        sessions.Add(session);

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: counters, rehash, session

        await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInSucceeded, SecurityEvent.Info, membership.TenantId, user.Id), cancellationToken);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionCreated, SecurityEvent.Info, membership.TenantId, user.Id, session.Id.ToString()), cancellationToken);

        // The profile the client gates its screens on comes from the same elevated projection that authorizes every request (D-04, P-20).
        var principal = await principals.ResolveAsync(session.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, session.Id, session.TenantId, session.Scope));
        var profile = Profiles.ToProfile(principal, session.Scope);

        return new SignInResult(AuthResponse.StepSignedIn, accessToken, refreshTokens.Format(session.Id, secret.Secret), session.Kind, profile, null);
    }

    private static MembershipSummary? SelectMembership(IReadOnlyList<MembershipSummary> active, Guid? requested)
    {
        if (requested is { } tenant)
        {
            return active.FirstOrDefault(m => m.TenantId == tenant);
        }

        return active.Count == 1 ? active[0] : null;
    }

    private static IPAddress? ParseIp(string? value) => IPAddress.TryParse(value, out var address) ? address : null;

    private static DomainException InvalidCredentials() =>
        new(ProblemCodes.InvalidCredentials, "The username or password is incorrect.", DomainFault.Unauthenticated);
}

/// <summary>Claim values of the <c>scope</c> claim (02 §4.2).</summary>
public static class ScopeNames
{
    public const string Full = "full";
    public const string MfaPending = "mfa_pending";
    public const string MfaEnrollment = "mfa_enrollment";

    public static string Of(SessionScope scope) => scope switch
    {
        SessionScope.Full => Full,
        SessionScope.MfaPending => MfaPending,
        SessionScope.MfaEnrollment => MfaEnrollment,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown session scope."),
    };

    public static SessionScope? Parse(string? value) => value switch
    {
        Full => SessionScope.Full,
        MfaPending => SessionScope.MfaPending,
        MfaEnrollment => SessionScope.MfaEnrollment,
        _ => null,
    };
}
