using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Validation 2026-10-11, C-01 (data only, no schema change). v0.4.0 put two registers behind new catalogue keys,
    /// <c>organization.export</c> (branches, roles and users as CSV) and <c>treasury.export</c>, but no existing role received
    /// them, and INV-ORG-011 (ORG-030: nobody grants a permission they do not hold) is right to stop anyone from handing out a
    /// key nobody holds. This migration grants:
    /// <list type="number">
    /// <item>each export key to every role, in every tenant, that holds the module's <c>.view</c> and <c>.manage</c> keys (the
    /// roles that could export before v0.4.0 gated it), including the provisioned "Laboratory Administrator" roles;</item>
    /// <item>the platform tenant's "Platform Administrator" role every key of the catalogue as of this migration (it keeps any
    /// key it already holds), which the seeder <c>PlatformAdministratorSeeder</c> also enforces when it runs.</item>
    /// </list>
    /// Keys stay distinct and ordinally sorted, as <c>EffectivePrivileges.Clean</c> stores them. The rows are changed as the
    /// owner role (BYPASSRLS) outside the application, so no field-change or audit-trail entry is written; the modified stamp
    /// names this migration.
    /// <para>
    /// A key added to <c>PermissionCatalog</c> later needs its own grant migration like this one: the gate
    /// <c>PermissionGrantMigrationTests.The_platform_administrator_holds_every_catalogued_key_after_the_migrations</c> fails
    /// until the Platform Administrator receives it. Down is a no-op: removing the keys would also withdraw grants made since.
    /// </para>
    /// </summary>
    public partial class GrantExportPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE app.roles AS r
                SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || ARRAY['organization.export']) AS k) AS d ORDER BY k COLLATE "C"),
                    modified_at_utc = now(),
                    modified_by_display_name = 'Migration GrantExportPermissions'
                WHERE r.permission_keys @> ARRAY['organization.view', 'organization.manage']
                  AND NOT r.permission_keys @> ARRAY['organization.export'];

                UPDATE app.roles AS r
                SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || ARRAY['treasury.export']) AS k) AS d ORDER BY k COLLATE "C"),
                    modified_at_utc = now(),
                    modified_by_display_name = 'Migration GrantExportPermissions'
                WHERE r.permission_keys @> ARRAY['treasury.view', 'treasury.manage']
                  AND NOT r.permission_keys @> ARRAY['treasury.export'];

                UPDATE app.roles AS r
                SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || c.keys) AS k) AS d ORDER BY k COLLATE "C"),
                    modified_at_utc = now(),
                    modified_by_display_name = 'Migration GrantExportPermissions'
                FROM (SELECT ARRAY[
                    'audit.export', 'audit.view', 'automation.approve', 'automation.create', 'automation.edit',
                    'automation.manage', 'automation.view', 'billing.approve', 'billing.cancel', 'billing.create', 'billing.edit',
                    'billing.export', 'billing.manage', 'billing.view', 'catalogue.create', 'catalogue.edit', 'catalogue.export',
                    'catalogue.manage', 'catalogue.view', 'claims.approve', 'claims.cancel', 'claims.create', 'claims.export',
                    'claims.view', 'dashboard.view', 'files.create', 'files.manage', 'files.view', 'identity.create',
                    'identity.edit', 'identity.manage', 'identity.view', 'integration.create', 'integration.edit',
                    'integration.export', 'integration.manage', 'integration.view', 'notifications.manage', 'notifications.view',
                    'organization.create', 'organization.edit', 'organization.export', 'organization.manage', 'organization.view',
                    'patients.create', 'patients.edit', 'patients.export', 'patients.manage', 'patients.view',
                    'patients.viewConfidential', 'platform.manage', 'platform.view', 'pricing.approve', 'pricing.create',
                    'pricing.edit', 'pricing.export', 'pricing.manage', 'pricing.view', 'quality.create', 'quality.edit',
                    'quality.manage', 'quality.view', 'registration.approve', 'registration.cancel', 'registration.create',
                    'registration.edit', 'registration.export', 'registration.manage', 'registration.view', 'reporting.export',
                    'reporting.view', 'results.approve', 'results.cancel', 'results.create', 'results.edit', 'results.export',
                    'results.manage', 'results.view', 'samples.cancel', 'samples.create', 'samples.edit', 'samples.manage',
                    'samples.view', 'settings.manage', 'settings.view', 'support.create', 'support.manage', 'support.view',
                    'tenancy.create', 'tenancy.edit', 'tenancy.manage', 'tenancy.view', 'treasury.approve', 'treasury.cancel',
                    'treasury.create', 'treasury.export', 'treasury.manage', 'treasury.view', 'wallet.create', 'wallet.manage',
                    'wallet.view'
                ]::text[] AS keys) AS c
                WHERE r.tenant_id = '00000000-0000-0000-0000-000000000001'
                  AND lower(r.name_en) = 'platform administrator'
                  AND NOT r.permission_keys @> c.keys;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data only: the grants stay (see the summary).
        }
    }
}
