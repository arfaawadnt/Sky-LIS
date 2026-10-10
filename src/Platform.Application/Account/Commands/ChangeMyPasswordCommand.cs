using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Domain.Account;
using Platform.Domain.Identity;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Commands;

/// <summary>
/// A2 (CP-65 / SA-21): the signed-in user replaces their password. Both values arrive opened by the credential envelope
/// filter (D-03). The current password is a credential check like sign-in: a wrong one counts toward the lockout
/// (02 §5.1, 02 §6.1) and is committed before the refusal; a locked account is refused. The new password follows the
/// policy decided on 2026-10-09 (minimum from <c>passwordMinLength</c>, never below the platform floor; maximum 128; no
/// composition rules; not containing the username). The stamp rotates and every other session of the user ends
/// (02 §4.1); the current session family stays signed in.
/// </summary>
[RequireAuthenticatedActor]
public sealed record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

public sealed class ChangeMyPasswordValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public ChangeMyPasswordValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(1024);
        RuleFor(command => command.NewPassword).NotEmpty().MaximumLength(SessionPolicy.PasswordMaxLength);
    }
}

public sealed class ChangeMyPasswordHandler(
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    IPasswordHasher hasher,
    ISessionPrincipalQuery principals,
    ITenantSettings settings,
    SessionPolicy policy,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    ICurrentTenant tenant,
    IClock clock) : ICommandHandler<ChangeMyPasswordCommand>
{
    public async Task Handle(ChangeMyPasswordCommand command, CancellationToken cancellationToken)
    {
        var (userId, sessionId) = AccountActor.RequireSession(user);
        var account = await users.GetForUpdateAsync(userId, cancellationToken)
            ?? throw new DomainException(IdentityCodes.UserNotFound, "The account was not found.", DomainFault.NotFound);

        var now = clock.UtcNow;
        if (account.IsLockedOutAt(now))
        {
            await events.WriteAsync(new SecurityEvent(AccountSecurityEvents.PasswordChangeFailed, SecurityEvent.Warning, tenant.TenantId, userId, "locked"), cancellationToken);
            throw Locked();
        }

        // The shape of the new password reveals nothing about the current one, so it is checked first and costs no attempt.
        var minimum = Math.Max(policy.PasswordMinLength, await settings.GetAsync(SettingKeys.PasswordMinLength, cancellationToken));
        PasswordRule.Check(command.NewPassword, account.Username, minimum);

        var verified = hasher.Verify(account.PasswordHash, command.CurrentPassword);
        if (verified)
        {
            // Only after the check: refusing an unchanged password earlier would confirm a guessed current password.
            if (string.Equals(command.CurrentPassword, command.NewPassword, StringComparison.Ordinal))
            {
                throw new DomainException(AccountCodes.PasswordUnchanged, "The new password must differ from the current one.", DomainFault.Validation);
            }

            account.ChangePassword(hasher.Hash(command.NewPassword), now);
            var currentFamily = (await sessions.GetForUpdateAsync(sessionId, cancellationToken))?.FamilyId;
            foreach (var session in await sessions.ListLiveForUserAsync(userId, cancellationToken))
            {
                if (session.Id != sessionId && session.FamilyId != currentFamily)
                {
                    session.Revoke(RevocationReason.PasswordChanged, now);
                    principals.Evict(session.Id);
                }
            }
        }
        else
        {
            account.RecordFailedPasswordCheck(now, policy.LockoutThreshold, policy.LockoutDuration);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: the new hash and the revocations, or the failed attempt
        principals.Evict(sessionId);

        if (!verified)
        {
            await events.WriteAsync(new SecurityEvent(AccountSecurityEvents.PasswordChangeFailed, SecurityEvent.Warning, tenant.TenantId, userId, "wrong_password"), cancellationToken);
            if (account.IsLockedOutAt(now))
            {
                await events.WriteAsync(new SecurityEvent(SecurityEvents.UserLockedOut, SecurityEvent.Warning, tenant.TenantId, userId), cancellationToken);
                throw Locked();
            }

            throw new DomainException(AccountCodes.CurrentPasswordRejected, "The current password is not correct.", DomainFault.Refused);
        }

        await events.WriteAsync(new SecurityEvent(AccountSecurityEvents.PasswordChanged, SecurityEvent.Info, tenant.TenantId, userId), cancellationToken);
    }

    private static DomainException Locked() =>
        new(AccountCodes.AccountLocked, "Too many wrong passwords: the account is locked for a while.", DomainFault.Refused);
}
