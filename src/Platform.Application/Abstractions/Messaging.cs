using MediatR;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Abstractions.Messaging;

// Request markers (01 §3.4). Every type implementing one of these is gated by AuthorizationBehavior and must
// carry exactly one policy attribute (D-04). Handlers implement the matching handler interface below so that
// no handler can be written against a raw MediatR request and slip past the gate (RequestPolicyTests).

/// <summary>A state-changing request with no response body.</summary>
public interface ICommand : IRequest;

/// <summary>A state-changing request that returns a value (typically a <c>CreatedResource</c>).</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>A read-only request. Queries are gated exactly like commands (D-04).</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IQuery<TResponse> : IRequest<TResponse>;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

/// <summary>Wraps a domain event for in-process policies (03 §7). Published by the outbox processor after commit, never before (pitfall P-50).</summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : IDomainEvent;

/// <summary>
/// A policy (03 §7, MODULES.md §1): "when <typeparamref name="TEvent"/>, then …". Implemented as a sealed class under
/// <c>Platform.Application/&lt;Module&gt;/Policies/</c>, registered by <c>AddApplication</c> for every closed
/// implementation found in the Application assembly, and invoked by the outbox processor after the commit that
/// raised the event, inside a scope bound to the event's tenant (no elevation). Rules: idempotent on a natural key
/// (the processor retries), never changes the aggregate that raised the event, never sends a command to another
/// module; it loads its own aggregates, calls them and commits once through <c>IUnitOfWork</c>.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
