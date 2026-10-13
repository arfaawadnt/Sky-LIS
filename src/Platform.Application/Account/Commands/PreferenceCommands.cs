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
/// (<c>TenantMembership.SetPreferredLanguage</c>; the field change is audited), and since 2026-10-14 the row density of the
/// results grid (<c>SetPreferredResultsDensity</c>). Each value given is saved; at least one is required. The session
/// principal is cached for a few seconds per session, so every live session of the user is evicted: the next refresh
/// profile carries the preferences.
/// </summary>
[RequireAuthenticatedActor]
public sealed record UpdateMyPreferencesCommand(string? Language, string? ResultsDensity = null) : ICommand;

public sealed class UpdateMyPreferencesValidator : AbstractValidator<UpdateMyPreferencesCommand>
{
    public UpdateMyPreferencesValidator()
    {
        RuleFor(command => command).Must(command => command.Language is not null || command.ResultsDensity is not null)
            .OverridePropertyName("language").WithMessage("A language or a results density is required.");
        RuleFor(command => command.Language).NotEmpty().Must(language => language is "en" or "ar").WithMessage("The language must be 'en' or 'ar'.")
            .When(command => command.Language is not null);
        RuleFor(command => command.ResultsDensity).Must(density => density is "comfortable" or "compact").WithMessage("The results density must be 'comfortable' or 'compact'.")
            .When(command => command.ResultsDensity is not null);
    }
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

        if (command.Language is not null)
        {
            membership.SetPreferredLanguage(command.Language);
        }

        if (command.ResultsDensity is not null)
        {
            membership.SetPreferredResultsDensity(command.ResultsDensity);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        principals.Evict(sessionId);
        foreach (var session in await sessions.ListLiveForUserAsync(userId, cancellationToken))
        {
            principals.Evict(session.Id);
        }
    }
}
