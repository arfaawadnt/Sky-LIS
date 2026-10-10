using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TreasuryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "app",
                table: "tenant_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reference_counters",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_counters", x => new { x.tenant_id, x.kind, x.year });
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    permission_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "treasuries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasuries", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "treasury_access_requests",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treasury_access_requests", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_treasury_access_requests_status_domain", "\"status\" IN ('Pending', 'Approved', 'Rejected', 'Revoked', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_treasury_access_requests_requested_by_user",
                        column: x => x.requested_by_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_treasury_access_requests_treasuries_tenant_id_treasury_id",
                        columns: x => new { x.tenant_id, x.treasury_id },
                        principalSchema: "app",
                        principalTable: "treasuries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant_id_role_id",
                schema: "app",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_is_active",
                schema: "app",
                table: "roles",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_treasuries_tenant_id_branch_id",
                schema: "app",
                table: "treasuries",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_treasuries_tenant_id_code",
                schema: "app",
                table: "treasuries",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_access_requests_requested_by_user_id",
                schema: "app",
                table: "treasury_access_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_treasury_access_requests_tenant_id_number",
                schema: "app",
                table: "treasury_access_requests",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treasury_access_requests_tenant_id_requested_by_user_id",
                schema: "app",
                table: "treasury_access_requests",
                columns: new[] { "tenant_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_treasury_access_requests_tenant_id_status",
                schema: "app",
                table: "treasury_access_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_treasury_access_requests_tenant_id_treasury_id",
                schema: "app",
                table: "treasury_access_requests",
                columns: new[] { "tenant_id", "treasury_id" });

            migrationBuilder.CreateIndex(
                name: "ux_treasury_access_requests_live",
                schema: "app",
                table: "treasury_access_requests",
                columns: new[] { "tenant_id", "requested_by_user_id", "treasury_id", "device_id" },
                unique: true,
                filter: "status IN ('Pending', 'Approved')");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_memberships_roles_tenant_id_role_id",
                schema: "app",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "role_id" },
                principalSchema: "app",
                principalTable: "roles",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.roles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.roles FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.roles
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.treasuries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.treasuries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.treasuries
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.treasury_access_requests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.treasury_access_requests FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.treasury_access_requests
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.reference_counters ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.reference_counters FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.reference_counters
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_memberships_roles_tenant_id_role_id",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropTable(
                name: "reference_counters",
                schema: "app");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "treasury_access_requests",
                schema: "app");

            migrationBuilder.DropTable(
                name: "treasuries",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_tenant_memberships_tenant_id_role_id",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "app",
                table: "tenant_memberships");
        }
    }
}
