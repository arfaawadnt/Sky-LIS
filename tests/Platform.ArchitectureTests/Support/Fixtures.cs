using MediatR;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Primitives;

namespace Platform.ArchitectureTests.Support.Fixtures;

// Deliberately wrong types that live only in the test assembly. The production gates scan the production
// assemblies, so these never pollute a real result; GateSelfTests runs the gates against this assembly to
// prove each rule detects what it claims to detect.

internal sealed record UngatedFixtureCommand : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
internal sealed record GatedFixtureCommand : ICommand;

[RequirePermissionPolicy("nonexistent", PermissionAction.View)]
internal sealed record UnknownPermissionFixtureQuery : IQuery<int>;

[AllowUnauthenticated]
[RequireAuthenticatedActor]
internal sealed record DoublePolicyFixtureCommand : ICommand;

internal sealed record RawFixtureRequest : IRequest<int>;

internal sealed class RawFixtureRequestHandler : IRequestHandler<RawFixtureRequest, int>
{
    public Task<int> Handle(RawFixtureRequest request, CancellationToken cancellationToken) => Task.FromResult(0);
}

internal sealed class GatedFixtureCommandHandler : ICommandHandler<GatedFixtureCommand>
{
    public Task Handle(GatedFixtureCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal class LeakyFixtureAggregate : AggregateRoot
{
    public LeakyFixtureAggregate()
    {
    }

    public string Name { get; set; } = string.Empty;

    public int Count { get; init; }
}

internal sealed class HardenedFixtureAggregate : AggregateRoot
{
    private HardenedFixtureAggregate()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public static HardenedFixtureAggregate Create(string name) => new() { Name = name };
}

internal sealed record FixtureHappened(Guid AggregateId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Wrong on both counts: not sealed and not under a Policies namespace.</summary>
internal class LeakyFixturePolicy : IDomainEventHandler<FixtureHappened>
{
    public Task HandleAsync(FixtureHappened domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
