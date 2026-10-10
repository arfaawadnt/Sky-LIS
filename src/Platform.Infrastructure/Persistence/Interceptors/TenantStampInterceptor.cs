using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Attributes every new tenant row to the current tenant (01 §2.3) and refuses cross-tenant writes in the
/// application layer before PostgreSQL's <c>WITH CHECK</c> does. An unresolved tenant is <c>TENANT-000</c>, never
/// a guess (pitfall P-30); a factory that set another tenant explicitly is allowed only inside an elevated scope.
/// </summary>
public sealed class TenantStampInterceptor(ITenantContextAccessor tenant) : SaveChangesInterceptor
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

        var current = tenant.CurrentTenantId;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.TenantId == Guid.Empty:
                    entry.Property(nameof(ITenantScoped.TenantId)).CurrentValue = current
                        ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this scope; the row cannot be attributed.", DomainFault.Refused);
                    break;

                case EntityState.Added:
                case EntityState.Modified:
                case EntityState.Deleted:
                    if (!tenant.IsElevated && entry.Entity.TenantId != current)
                    {
                        throw new DomainException(ProblemCodes.TenantForbidden, "The row belongs to another tenant.", DomainFault.Refused);
                    }

                    break;
            }
        }
    }
}
