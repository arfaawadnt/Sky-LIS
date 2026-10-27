namespace Platform.Application.Tenancy;

/// <summary>
/// The row of the platform tenant (<c>TenantIds.Platform</c>, kind Platform) in the registry of tenants <c>saas.tenants</c>
/// (database team review 2026-10-26, comments 3–4: every <c>tenant_id</c> references the registry). Migration
/// <c>DbReviewTenantKeys</c> writes it for an existing database; whatever writes platform-tenant data on a database that may
/// lack it (the first administrator's bootstrap, development tooling, test fixtures after a reset) ensures it first.
/// Idempotent: an existing row is left as it is.
/// </summary>
public interface IPlatformTenantRecord
{
    Task EnsureAsync(CancellationToken cancellationToken);
}
