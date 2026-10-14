using Platform.Application.Abstractions.Security;

namespace Platform.Infrastructure.Security;

/// <summary>
/// Scoped holder of the current tenant and elevation flag. The host sets the tenant from the resolved principal;
/// jobs use <see cref="ForTenant"/> per unit of work; the three allow-listed sites use <see cref="Elevate"/>
/// inside a <c>using</c> (ElevationScopingTests). Change scope only between units of work: an open transaction
/// keeps its connection, and that connection was stamped when it opened.
/// </summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public Guid? TenantId { get; private set; }

    public bool IsElevated { get; private set; }

    public Guid? CurrentTenantId => TenantId;

    /// <summary>Binds the scope to a tenant without elevation (the host's path for every authenticated request).</summary>
    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("The tenant id cannot be empty.", nameof(tenantId));
        }

        TenantId = tenantId;
        IsElevated = false;
    }

    public void Clear()
    {
        TenantId = null;
        IsElevated = false;
    }

    public IDisposable ForTenant(Guid tenantId)
    {
        var restore = new Restore(this, TenantId, IsElevated);
        SetTenant(tenantId);
        return restore;
    }

    public IDisposable Elevate()
    {
        var restore = new Restore(this, TenantId, IsElevated);
        IsElevated = true;
        return restore;
    }

    private sealed class Restore(TenantContextAccessor owner, Guid? tenantId, bool elevated) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.TenantId = tenantId;
            owner.IsElevated = elevated;
        }
    }
}

/// <summary>Scoped holder of the resolved actor (02 §4.4). The host fills it from the session row; jobs leave it empty (system actor).</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    public bool IsAuthenticated => UserId.HasValue;

    public Guid? UserId { get; private set; }

    public Guid? SessionId { get; private set; }

    public string? DisplayName { get; private set; }

    public Guid? BranchId { get; private set; }
    public bool IsPrimaryAdministrator { get; private set; }

    public void Set(Guid userId, Guid? sessionId, string displayName, Guid? branchId = null, bool isPrimaryAdministrator = false)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user id cannot be empty.", nameof(userId));
        }

        UserId = userId;
        SessionId = sessionId;
        DisplayName = displayName;
        BranchId = branchId;
        IsPrimaryAdministrator = isPrimaryAdministrator;
    }

    public void Clear()
    {
        UserId = null;
        SessionId = null;
        DisplayName = null;
        BranchId = null;
        IsPrimaryAdministrator = false;
    }
}
