using System.Net;
using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Tokens;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.Commands;

/// <summary>
/// Browser sign-in (02 §4.1). The password arrives in plaintext here because the WebApi edge already opened its envelope (D-03).
/// <paramref name="PresentedRefreshToken"/> is the refresh cookie the browser sent with the sign-in, if any (R2-04): the new
/// cookie overwrites it, so the session it identifies — whoever it belonged to — is revoked in the same commit instead of
/// staying active and unreachable on the server.
/// LIS-CR-005: the sign-in address names the target — <paramref name="Laboratory"/> (a laboratory's address) or
/// <paramref name="Admin"/> = true (the Super Admin address), exactly one. Only the membership of that tenant is considered;
/// an unknown address, a user who is not a member and a laboratory user at the Super Admin address are all answered like a
/// wrong password (<c>AUTH-002</c>), after the password was verified, with a distinct reason in the security event.
/// </summary>
[AllowUnauthenticated]
public sealed record SignInCommand(string? Laboratory, bool? Admin, string Username, string Password, string? ClientIp, string? UserAgent, string? PresentedRefreshToken = null) : ICommand<SignInResult>
{
    /// <summary>True when the request signs in at the Super Admin address.</summary>
    public bool AtAdministration => Admin == true && Laboratory is null;
}

public sealed record SignInResult(
    string Step,
    IssuedAccessToken? AccessToken,
    string? RefreshToken,
    SessionKind Kind,
    AuthProfile? Profile);

public sealed class SignInCommandValidator : AbstractValidator<SignInCommand>
{
    public SignInCommandValidator()
    {
        // LIS-CR-005: exactly one target; the shape of an address is checked here (a malformed one is 400, never AUTH-002).
        RuleFor(command => command.Laboratory)
            .Must((command, laboratory) => laboratory is not null ^ command.Admin == true)
            .WithMessage("Send exactly one of 'laboratory' (the laboratory address) or 'admin': true (the Super Admin sign-in).");
        RuleFor(command => command.Laboratory)
            .Must(Tenant.IsAddressShape)
            .When(command => command.Laboratory is not null)
            .WithMessage($"The laboratory address is {Tenant.MinSlugLength} to {Tenant.MaxSlugLength} characters of a-z, 0-9 and '-'.");
        RuleFor(command => command.Username).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(1024);
        RuleFor(command => command.UserAgent).MaximumLength(RefreshSession.MaxUserAgentLength);
    }
}

public sealed class SignInCommandHandler(
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IMembershipDirectory memberships,
    ILaboratoryAddresses laboratories,
    IPasswordHasher hasher,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    ISecurityEventLog events,
    ISessionPrincipalQuery principals,
    ISubscriptionStateLookup subscriptions,
    IUnitOfWork unitOfWork,
    IClock clock,
    SessionPolicy policy) : ICommandHandler<SignInCommand, SignInResult>
{
    // A fixed hash verified for unknown usernames so the response time does not reveal whether the account exists.
    private static readonly string DecoyHash = "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public async Task<SignInResult> Handle(SignInCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // LIS-CR-005: the target is resolved before the account, so every path below costs the same reads.
        LaboratoryAddress? laboratory = null;
        if (!command.AtAdministration)
        {
            laboratory = await laboratories.FindAsync(Tenant.NormaliseAddress(command.Laboratory), cancellationToken);
        }

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
            // B-11: an administrator's lock has no end date and only an administrator lifts it; the automatic lockout ends by itself.
            var byAdministrator = user.IsLockedByAdministrator;
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: byAdministrator ? "locked_by_administrator" : "locked_out"), cancellationToken);
            throw byAdministrator
                ? new DomainException(ProblemCodes.AccountLockedByAdministrator, "The account was locked by an administrator; contact your administrator.", DomainFault.Unauthenticated)
                : new DomainException(ProblemCodes.AccountLocked, "The account is locked; try again later.", DomainFault.Unauthenticated);
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

        var membership = await SelectMembershipAsync(command, laboratory, user.Id, cancellationToken);

        // LIS-CR-012 (D-3): once a laboratory's grace is over only its administrators sign in (and reach only the wallet,
        // SUB-021); every other member is refused by name. Checked after the password, like the access end date.
        if (!command.AtAdministration && !membership.IsSubscriptionAdministrator
            && (await subscriptions.FindAsync(membership.TenantId, cancellationToken)).IsExpired)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, membership.TenantId, user.Id, "subscription_expired"), cancellationToken);
            throw new DomainException(SubscriptionCodes.Expired, "This laboratory's subscription has expired. Ask your laboratory administrator to top up the wallet, or contact National Technology.", DomainFault.Refused);
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
        var superseded = await SupersedePresentedSessionAsync(command.PresentedRefreshToken, now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: counters, rehash, superseded session, new session

        if (superseded is not null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionRevoked, SecurityEvent.Info, superseded.TenantId, superseded.PlatformUserId, "superseded_by_sign_in"), cancellationToken);
        }

        await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInSucceeded, SecurityEvent.Info, membership.TenantId, user.Id), cancellationToken);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionCreated, SecurityEvent.Info, membership.TenantId, user.Id, session.Id.ToString()), cancellationToken);

        // The profile the client gates its screens on comes from the same elevated projection that authorizes every request (D-04, P-20).
        var principal = await principals.ResolveAsync(session.Id, user.Id, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, session.Id, session.TenantId, session.Scope));
        var profile = Profiles.ToProfile(principal, session.Scope);

        return new SignInResult(AuthResponse.StepSignedIn, accessToken, refreshTokens.Format(session.Id, secret.Secret), session.Kind, profile);
    }

    /// <summary>
    /// LIS-CR-005: the one membership the sign-in address allows — the platform tenant's at the Super Admin address, the named
    /// laboratory's otherwise. Called after the password was verified. A laboratory that is not usable keeps its own refusal
    /// (<c>CLI-020</c>, SA-04 / INV-PLT-003) for its members only; every other miss is <c>AUTH-002</c> with a distinct reason.
    /// </summary>
    private async Task<MembershipSummary> SelectMembershipAsync(SignInCommand command, LaboratoryAddress? laboratory, Guid userId, CancellationToken cancellationToken)
    {
        Guid tenantId;
        if (command.AtAdministration)
        {
            tenantId = TenantIds.Platform;
        }
        else if (laboratory is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: userId, Detail: "unknown_laboratory"), cancellationToken);
            throw InvalidCredentials();
        }
        else if (!laboratory.IsUsable)
        {
            // SA-04 / INV-PLT-003: a member of a deactivated laboratory (or one whose trial ended) is told so (CLI-020).
            if (await memberships.HasInactiveClientMembershipAsync(userId, laboratory.TenantId, cancellationToken))
            {
                await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: userId, Detail: "client_inactive"), cancellationToken);
                throw new DomainException(PlatformAdminCodes.ClientInactive, "This laboratory's subscription is inactive. Contact National Technology.", DomainFault.Unauthenticated);
            }

            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: userId, Detail: "not_a_member"), cancellationToken);
            throw InvalidCredentials();
        }
        else
        {
            tenantId = laboratory.TenantId;
        }

        var active = await memberships.ListActiveForUserAsync(userId, cancellationToken);
        var membership = active.FirstOrDefault(m => m.TenantId == tenantId);
        if (membership is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.SignInFailed, SecurityEvent.Warning, ActorUserId: userId, Detail: command.AtAdministration ? "not_platform_staff" : "not_a_member"), cancellationToken);
            throw InvalidCredentials();
        }

        return membership;
    }

    /// <summary>
    /// R2-04: the browser's previous session ends when a new one replaces its cookie. Only a token whose secret matches is
    /// honoured (`PresentedRefreshToken.Matches`: a forged or stale cookie naming someone else's session id revokes nothing); its whole family ends, like a sign-out.
    /// </summary>
    private async Task<RefreshSession?> SupersedePresentedSessionAsync(string? presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!refreshTokens.TryParse(presented, out var sessionId, out var presentedHash))
        {
            return null;
        }

        var previous = await sessions.GetForUpdateAsync(sessionId, cancellationToken);
        if (previous is null || previous.Kind != SessionKind.Browser || !PresentedRefreshToken.Matches(previous, presentedHash))
        {
            return null;
        }

        var revoked = false;
        foreach (var member in await sessions.ListFamilyAsync(previous.FamilyId, cancellationToken))
        {
            revoked |= !member.IsRevoked;
            member.Revoke(RevocationReason.Logout, now);
            principals.Evict(member.Id);
        }

        return revoked ? previous : null;
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
