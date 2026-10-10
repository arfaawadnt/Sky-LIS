using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Application.Abstractions.Security;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes who created or modified an aggregate and when (01 §2.3): the actor resolved per request and the display
/// name as of the action, from <see cref="ICurrentUser"/> and <see cref="IClock"/>, never from a token claim.
/// </summary>
public sealed class AuditStampInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var stamp = new AuditStamp(clock.UtcNow, currentUser.UserId, currentUser.DisplayName);

        foreach (var entry in context.ChangeTracker.Entries<IAuditStamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.StampCreated(stamp);
                    break;
                case EntityState.Modified:
                    entry.Entity.StampModified(stamp);
                    break;
            }
        }
    }
}
