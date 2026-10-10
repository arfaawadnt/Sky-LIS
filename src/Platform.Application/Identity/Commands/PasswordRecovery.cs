using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.Commands;

// CP-03 "Forgot password → OTP → new credentials". Two anonymous commands; both are rate-limited like sign-in
// (02 §5.2 credential-account on the identifier, credential-client on the address). Neither reveals whether an
// account exists: the first always succeeds, the second refuses every failure with the same code.

/// <summary>The purpose that keys the one-time codes of this flow; a code issued for another purpose never verifies here.</summary>
public static class PasswordRecovery
{
    public const string OtpPurpose = "password-recovery";
    public const int MaxIdentifierLength = 64;

    /// <summary>Finds the account by username first, then by mobile (normalised); <see langword="null"/> when neither matches.</summary>
    public static async Task<PlatformUser?> FindAsync(IPlatformUserRepository users, string usernameOrMobile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);
        var identifier = (usernameOrMobile ?? string.Empty).Trim();
        if (identifier.Length == 0)
        {
            return null;
        }

        var user = await users.FindByUsernameAsync(identifier, cancellationToken);
        if (user is not null)
        {
            return user;
        }

        string? mobile;
        try
        {
            mobile = PlatformUser.NormaliseMobile(identifier);
        }
        catch (DomainException)
        {
            return null;
        }

        return mobile is null ? null : await users.FindByMobileAsync(mobile, cancellationToken);
    }
}

/// <summary>Step 1: sends a one-time code to the account's registered mobile. Always 202; the outcome is a security event, never a response.</summary>
[AllowUnauthenticated]
public sealed record StartPasswordRecoveryCommand(string UsernameOrMobile, string? Language, string? ClientIp) : ICommand;

public sealed class StartPasswordRecoveryCommandValidator : AbstractValidator<StartPasswordRecoveryCommand>
{
    public StartPasswordRecoveryCommandValidator()
    {
        RuleFor(command => command.UsernameOrMobile).NotEmpty().MaximumLength(PasswordRecovery.MaxIdentifierLength);
        RuleFor(command => command.Language).MaximumLength(5);
    }
}

public sealed class StartPasswordRecoveryCommandHandler(IPlatformUserRepository users, IOtpService otp, ISecurityEventLog events) : ICommandHandler<StartPasswordRecoveryCommand>
{
    public async Task Handle(StartPasswordRecoveryCommand command, CancellationToken cancellationToken)
    {
        var user = await PasswordRecovery.FindAsync(users, command.UsernameOrMobile, cancellationToken);
        if (user is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryFailed, SecurityEvent.Warning, Detail: "unknown_account"), cancellationToken);
            return;
        }

        if (!user.IsActive)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "inactive"), cancellationToken);
            return;
        }

        if (user.Mobile is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "no_mobile"), cancellationToken);
            return;
        }

        var language = command.Language is { Length: > 0 } && command.Language.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        await otp.IssueAsync(PasswordRecovery.OtpPurpose, user.Mobile, language, cancellationToken, NotificationTemplateCodes.PasswordRecovery);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryStarted, SecurityEvent.Info, ActorUserId: user.Id), cancellationToken);
    }
}

/// <summary>Step 2: the code proves possession of the mobile; the password is replaced and every live session of the user is revoked (02 §4.3).</summary>
[AllowUnauthenticated]
public sealed record CompletePasswordRecoveryCommand(string UsernameOrMobile, string Code, string NewPassword) : ICommand;

public sealed class CompletePasswordRecoveryCommandValidator : AbstractValidator<CompletePasswordRecoveryCommand>
{
    public CompletePasswordRecoveryCommandValidator(SessionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        RuleFor(command => command.UsernameOrMobile).NotEmpty().MaximumLength(PasswordRecovery.MaxIdentifierLength);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(12);
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(policy.PasswordMinLength).MaximumLength(SessionPolicy.PasswordMaxLength);
    }
}

public sealed class CompletePasswordRecoveryCommandHandler(
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IOtpService otp,
    IPasswordHasher hasher,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CompletePasswordRecoveryCommand>
{
    public async Task Handle(CompletePasswordRecoveryCommand command, CancellationToken cancellationToken)
    {
        var user = await PasswordRecovery.FindAsync(users, command.UsernameOrMobile, cancellationToken);
        if (user is null || !user.IsActive || user.Mobile is null)
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryFailed, SecurityEvent.Warning, ActorUserId: user?.Id, Detail: user is null ? "unknown_account" : user.IsActive ? "no_mobile" : "inactive"), cancellationToken);
            throw Rejected();
        }

        if (!await otp.VerifyAsync(PasswordRecovery.OtpPurpose, user.Mobile, command.Code, cancellationToken))
        {
            await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryFailed, SecurityEvent.Warning, ActorUserId: user.Id, Detail: "otp_rejected"), cancellationToken);
            throw Rejected();
        }

        if (command.NewPassword.Contains(user.Username, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ProblemCodes.ValidationFailed, "The password must not contain the username.", DomainFault.Validation);
        }

        var now = clock.UtcNow;
        user.RecoverPassword(hasher.Hash(command.NewPassword), now);
        foreach (var session in await sessions.ListLiveForUserAsync(user.Id, cancellationToken))
        {
            session.Revoke(RevocationReason.PasswordChanged, now);
            principals.Evict(session.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: the new hash, the stamp rotation and the revocations
        await events.WriteAsync(new SecurityEvent(SecurityEvents.PasswordRecoveryCompleted, SecurityEvent.Info, ActorUserId: user.Id), cancellationToken);
    }

    private static DomainException Rejected() =>
        new(ProblemCodes.OtpRejected, "The code is not valid for this account, or it has expired.", DomainFault.Unauthenticated);
}
