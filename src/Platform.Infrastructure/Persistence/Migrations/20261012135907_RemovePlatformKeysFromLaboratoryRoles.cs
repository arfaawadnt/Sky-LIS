using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Validation 2026-10-12, F-07 (data only, no schema change). The development seed <c>seed-demo-lab</c> gave a laboratory's
    /// "Laboratory Administrator" role the whole catalogue, including the platform-only modules (<c>platform.*</c>,
    /// <c>tenancy.*</c>; <c>EffectivePrivileges.PlatformOnlyModules</c>). Those keys never took effect outside the platform
    /// tenant (<c>EffectivePrivileges.PermissionKeys</c> drops them), but every later save of the role was refused with
    /// ORG-033 ("Platform permissions cannot be granted in a laboratory"). This migration removes the platform-only keys from
    /// every role and from the added and removed keys of every membership override, in every tenant except the platform
    /// tenant. Keys stay distinct and ordinally sorted, as <c>EffectivePrivileges.Clean</c> stores them; only rows that held
    /// such a key are touched (a role that held nothing else keeps an empty list; ORG-005 asks for a permission on
    /// its next save). The rows are changed as the owner role (BYPASSRLS) outside the application, so no field-change or
    /// audit-trail entry is written; the modified stamp names this migration. Down is a no-op: the keys had no effect.
    /// </summary>
    public partial class RemovePlatformKeysFromLaboratoryRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE app.roles AS r
                SET permission_keys = ARRAY(
                        SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys) AS k) AS d
                        WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
                        ORDER BY k COLLATE "C"),
                    modified_at_utc = now(),
                    modified_by_display_name = 'Migration RemovePlatformKeysFromLaboratoryRoles'
                WHERE r.tenant_id <> '00000000-0000-0000-0000-000000000001'
                  AND EXISTS (SELECT 1 FROM unnest(r.permission_keys) AS k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'));

                UPDATE app.tenant_memberships AS m
                SET added_permission_keys = ARRAY(
                        SELECT k FROM (SELECT DISTINCT unnest(m.added_permission_keys) AS k) AS d
                        WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
                        ORDER BY k COLLATE "C"),
                    removed_permission_keys = ARRAY(
                        SELECT k FROM (SELECT DISTINCT unnest(m.removed_permission_keys) AS k) AS d
                        WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
                        ORDER BY k COLLATE "C"),
                    modified_at_utc = now(),
                    modified_by_display_name = 'Migration RemovePlatformKeysFromLaboratoryRoles'
                WHERE m.tenant_id <> '00000000-0000-0000-0000-000000000001'
                  AND EXISTS (SELECT 1 FROM unnest(m.added_permission_keys || m.removed_permission_keys) AS k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data only: the platform-only keys had no effect in a laboratory and are not restored (see the summary).
        }
    }
}
