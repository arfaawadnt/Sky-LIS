using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// The single commit of a command (01 §2.3). One <c>SaveChangesAsync</c> runs the interceptor chain (tenant stamp,
/// audit stamp, outbox) and writes the state change, its metadata and its events in one transaction. Engine
/// refusals are translated into coded faults here (01 §5.1): a concurrency clash is <c>CONCURRENCY-409</c>, a
/// row-level-security <c>WITH CHECK</c> refusal is <c>TENANT-403</c>, a unique index is <c>UNIQUE-409</c>.
/// </summary>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DomainException(ProblemCodes.ConcurrencyConflict, "The record was changed by someone else; reload and try again.", DomainFault.Conflict, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege })
        {
            throw new DomainException(ProblemCodes.TenantForbidden, "The write was refused by the tenant isolation policy.", DomainFault.Refused, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            throw new DomainException(ProblemCodes.UniqueViolation, $"A record with the same unique value already exists ({violation.ConstraintName}).", DomainFault.Conflict, exception);
        }
    }
}
