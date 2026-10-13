using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpecialRightsExtended : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "override_can_view_branch_collections",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_demographics_edit_up_to",
                schema: "app",
                table: "tenant_memberships",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "override_report_days_limit",
                schema: "app",
                table: "tenant_memberships",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_view_branch_collections",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "special_demographics_edit_up_to",
                schema: "app",
                table: "roles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Complete");

            migrationBuilder.AddColumn<int>(
                name: "special_report_days_limit",
                schema: "app",
                table: "roles",
                type: "integer",
                nullable: true);

            // The enum convention skips owned types (01 §4.2 item 6): the domain CHECKs are declared here.
            migrationBuilder.Sql("""
                ALTER TABLE app.roles ADD CONSTRAINT ck_roles_special_demographics_edit_up_to_domain
                    CHECK (special_demographics_edit_up_to IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'));
                ALTER TABLE app.tenant_memberships ADD CONSTRAINT ck_tenant_memberships_override_demographics_edit_up_to_domain
                    CHECK (override_demographics_edit_up_to IS NULL OR override_demographics_edit_up_to IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'));
                ALTER TABLE app.roles ADD CONSTRAINT ck_roles_special_report_days_limit_range
                    CHECK (special_report_days_limit IS NULL OR special_report_days_limit BETWEEN 0 AND 3650);
                ALTER TABLE app.tenant_memberships ADD CONSTRAINT ck_tenant_memberships_override_report_days_limit_range
                    CHECK (override_report_days_limit IS NULL OR override_report_days_limit BETWEEN 0 AND 3650);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE app.tenant_memberships DROP CONSTRAINT IF EXISTS ck_tenant_memberships_override_report_days_limit_range;
                ALTER TABLE app.roles DROP CONSTRAINT IF EXISTS ck_roles_special_report_days_limit_range;
                ALTER TABLE app.tenant_memberships DROP CONSTRAINT IF EXISTS ck_tenant_memberships_override_demographics_edit_up_to_domain;
                ALTER TABLE app.roles DROP CONSTRAINT IF EXISTS ck_roles_special_demographics_edit_up_to_domain;
                """);

            migrationBuilder.DropColumn(
                name: "override_can_view_branch_collections",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_demographics_edit_up_to",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_report_days_limit",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "special_can_view_branch_collections",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_demographics_edit_up_to",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_report_days_limit",
                schema: "app",
                table: "roles");
        }
    }
}
