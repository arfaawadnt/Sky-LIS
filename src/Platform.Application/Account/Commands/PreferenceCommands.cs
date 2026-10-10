using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Domain.Account;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Commands;

/// <summary>
/// A1 (CP-65 / SA-21): the language the signed-in user works in, stored on their membership of the session's tenant
/// (<c>TenantMembership.SetPreferredLanguage</c>; the field change is audited). The session principal is cached for a
/// few seconds per session, so every live session of the user is evicted: the next refresh profile carries the language.
/// </summary>
[RequireAuthenticatedActor]
public sealed record UpdateMyPreferencesCommand(string Language) : ICommand;

public sealed class UpdateMyPreferencesValidator : AbstractValidator<UpdateMyPreferencesCommand>
{
    public UpdateMyPreferencesValidator() =>
        RuleFor(command => command.Language).NotEmpty().Must(language => language is "en" or "ar").WithMessage("The language must be 'en' or 'ar'.");
}

public sealed class UpdateMyPreferencesHandler(
    ITenantMembershipRepository memberships,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<UpdateMyPreferencesCommand>
{
    public async Task Handle(UpdateMyPreferencesCommand command, CancellationToken cancellationToken)
    {
        var (userId, sessionId) = AccountActor.RequireSession(user);
        var membership = await memberships.FindByPlatformUserAsync(userId, cancellationToken)
            ?? throw new DomainException(AccountCodes.MembershipNotFound, "The account has no membership in this tenant.", DomainFault.NotFound);

        membership.SetPreferredLanguage(command.Language);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        principals.Evict(sessionId);
        foreach (var session in await sessions.ListLiveForUserAsync(userId, cancellationToken))
        {
            principals.Evict(session.Id);
        }
    }
}
