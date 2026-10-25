using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.PlatformAdmin.Counters;

// LIS-CR-013 (C-17): the impression and click counters of an announcement, reported anonymously by the sign-in page and the
// dashboard card. Not a business transition: the whole write is one atomic increment through IAnnouncementCounters (no aggregate,
// no row version, no audit row per view), which is why this handler lives outside Commands/ and commits no unit of work
// (TransitionAtomicityTests covers the business command handlers; the identity exceptions follow the same reasoning).

/// <summary>Counts one impression or click of a live announcement; anything else (unknown, draft, out of schedule) counts nothing. Always answers 204.</summary>
[AllowUnauthenticated]
public sealed record RecordAnnouncementCounterCommand(Guid AnnouncementId, AnnouncementCounter Counter) : ICommand;

public sealed class RecordAnnouncementCounterValidator : AbstractValidator<RecordAnnouncementCounterCommand>
{
    public RecordAnnouncementCounterValidator()
    {
        RuleFor(command => command.AnnouncementId).NotEmpty();
        RuleFor(command => command.Counter).IsInEnum();
    }
}

public sealed class RecordAnnouncementCounterHandler(IAnnouncementCounters counters, IClock clock) : ICommandHandler<RecordAnnouncementCounterCommand>
{
    public async Task Handle(RecordAnnouncementCounterCommand command, CancellationToken cancellationToken) =>
        await counters.IncrementAsync(command.AnnouncementId, command.Counter, clock.UtcNow, cancellationToken);
}
