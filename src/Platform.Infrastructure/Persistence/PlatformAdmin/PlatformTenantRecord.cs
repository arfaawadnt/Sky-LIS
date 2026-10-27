using Microsoft.EntityFrameworkCore;
using Platform.Application.Tenancy;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

/// <summary>
/// <see cref="IPlatformTenantRecord"/>: one idempotent INSERT of the platform row of <c>saas.tenants</c> (no RLS on the table,
/// so no elevation). The row is the registry entry of National Technology's own tenant, never a client: kind Platform, no
/// country, governorate or plan (allowed by <c>ck_tenants_laboratory_place</c>), the reserved address <c>platform</c> (no
/// laboratory may take it), empty contact fields. The EF model filters it out of every read (<c>TenantConfiguration</c>).
/// The same statement is written into migration <c>DbReviewTenantKeys</c>.
/// </summary>
internal sealed class PlatformTenantRecord(AppDbContext context) : IPlatformTenantRecord
{
    public const string PlatformName = "Sky LIS platform";
    public const string PlatformNameAr = "منصة Sky LIS";
    public const string PlatformSlug = "platform";

    /// <summary>The idempotent statement (no parameters): an existing row with the platform id is left untouched.</summary>
    public const string EnsureSql = """
        INSERT INTO saas.tenants (id, kind, name_en, name_ar, country_id, governorate_id, plan_id, address, mobile, manager_name, manager_mobile,
                                  portal_slug, source, status, activated_at_utc, preferred_language, created_at_utc, created_by_display_name)
        VALUES ('00000000-0000-0000-0000-000000000001', 'Platform', 'Sky LIS platform', 'منصة Sky LIS', NULL, NULL, NULL, '', '', '', '',
                'platform', 'Manual', 'Active', now(), 'en', now(), 'Platform tenant record')
        ON CONFLICT (id) DO NOTHING
        """;

    public Task EnsureAsync(CancellationToken cancellationToken) => context.Database.ExecuteSqlRawAsync(EnsureSql, cancellationToken);
}
