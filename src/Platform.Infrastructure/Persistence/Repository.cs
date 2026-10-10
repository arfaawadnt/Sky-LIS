using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Base of every aggregate repository (ADR-0010 carried forward). Reads of tenant-scoped aggregates are bounded
/// by the tenant query filter and, independently, by row-level security. Repositories never save (the handler
/// commits once through <see cref="IUnitOfWork"/>), never decide and never throw coded refusals (pitfalls P-02, P-13).
/// </summary>
/// <typeparam name="TAggregate">The aggregate root.</typeparam>
public abstract class Repository<TAggregate>(AppDbContext context) : IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    protected AppDbContext Context { get; } = context;

    protected DbSet<TAggregate> Set => Context.Set<TAggregate>();

    public virtual Task<TAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(aggregate => aggregate.Id == id, cancellationToken);

    public void Add(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        Set.Add(aggregate);
    }
}
