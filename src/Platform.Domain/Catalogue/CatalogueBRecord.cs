using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// The common shape of the Catalogue B reference lists (intake LIS-INT-CTB-001 §6): a tenant-scoped root that is
/// active or inactive and is never deleted (BR-15). Deactivate (T3) and reactivate (T4) are idempotent and raise the
/// list's own event once. The tenant is stamped by the persistence layer from the current scope.
/// </summary>
public abstract class CatalogueBRecord : AggregateRoot, ITenantScoped
{
    /// <summary>Reserved for object materialisers (EF Core).</summary>
    protected CatalogueBRecord()
    {
    }

    protected CatalogueBRecord(Guid id)
        : base(id)
    {
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Offered in pickers and applied by the read ports only while true.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Optimistic concurrency token, mapped onto PostgreSQL's <c>xmin</c> system column.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>T3 Deactivate (<c>catalogue.manage</c>). Idempotent: an inactive record raises nothing.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(Deactivated(nowUtc));
    }

    /// <summary>T4 Reactivate (<c>catalogue.manage</c>). Idempotent: an active record raises nothing.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(Reactivated(nowUtc));
    }

    protected abstract IDomainEvent Deactivated(DateTimeOffset nowUtc);

    protected abstract IDomainEvent Reactivated(DateTimeOffset nowUtc);

    /// <summary>A linked record must be active when it is newly linked (CAT-310); an unchanged link to a record deactivated later stays valid.</summary>
    protected static void RequireActiveLink(bool isActive, string what) =>
        Require(isActive, CatalogueBCodes.ReferenceInactive, $"The {what} is inactive and cannot be linked.", DomainFault.Conflict);
}
