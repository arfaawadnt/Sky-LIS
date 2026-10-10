using Platform.Application.Abstractions.Messaging;

namespace Platform.ArchitectureTests.Support.Fixtures.Policies;

/// <summary>The shape the policy rule accepts: sealed, under a Policies namespace, one closed handler interface.</summary>
internal sealed class CorrectFixturePolicy : IDomainEventHandler<FixtureHappened>
{
    public Task HandleAsync(FixtureHappened domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
