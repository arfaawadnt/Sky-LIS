using Platform.SharedKernel.Primitives;

namespace Platform.Application.Abstractions.Persistence;

/// <summary>
/// The write-side port of one aggregate. Repositories load tracked aggregates and stage new ones; they never
/// save, decide or throw coded refusals (pitfalls P-02, P-13). Module repositories extend this interface with
/// the projections their queries need (01 §3.2).
/// </summary>
/// <typeparam name="TAggregate">The aggregate root.</typeparam>
public interface IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    /// <summary>Loads the aggregate for modification (tracked), or <see langword="null"/> when it does not exist in the current tenant.</summary>
    Task<TAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stages a new aggregate; nothing is persisted until the handler commits through <see cref="IUnitOfWork"/>.</summary>
    void Add(TAggregate aggregate);
}

/// <summary>
/// The single commit of a command handler (01 §2.3). State change, recorded metadata, outbox rows and audit rows
/// land in one transaction; a handler calls this exactly once (TransitionAtomicityTests).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
