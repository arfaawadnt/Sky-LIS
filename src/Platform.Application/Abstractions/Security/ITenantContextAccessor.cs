namespace Platform.Application.Abstractions.Security;

/// <summary>
/// The scoped holder of the current tenant and elevation state. The host sets it from the resolved principal
/// (never from a header, pitfall P-30); jobs set it per tenant unit of work; the connection interceptor reads it
/// on every connection open (01 §4.5).
/// </summary>
public interface ITenantContextAccessor : ICurrentTenant, ICurrentTenantSetter
{
    /// <summary>The tenant of the current scope, or <see langword="null"/> when unresolved (the database then sees the nil tenant and matches nothing).</summary>
    Guid? CurrentTenantId { get; }
}
