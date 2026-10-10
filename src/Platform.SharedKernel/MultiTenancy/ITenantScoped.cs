namespace Platform.SharedKernel.MultiTenancy;

/// <summary>
/// Marks an entity that belongs to exactly one tenant. The persistence layer keys it <c>(tenant_id, id)</c>,
/// applies the tenant query filter and stamps <c>TenantId</c> on insert by convention (01 §4.2–4.3); the
/// database enforces the same boundary with FORCE ROW LEVEL SECURITY.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}

/// <summary>
/// Marks a tenant record that is additionally allocated to a branch and/or department, so a membership's working
/// scope (01 §4.7) can narrow what a user sees and edits.
/// </summary>
public interface IAllocatable
{
    Guid? BranchId { get; }

    Guid? DepartmentId { get; }
}
