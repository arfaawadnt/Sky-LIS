using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompatibilityGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sample_conditions_merges_into",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.DropIndex(
                name: "ix_sample_conditions_tenant_id_merges_into_condition_id",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.DropColumn(
                name: "merges_into_condition_id",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.CreateTable(
                name: "sample_compatibility_groups",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_compatibility_groups", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "sample_compatibility_members",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_compatibility_members", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sample_compatibility_members_sample_compatibility_groups_te",
                        columns: x => new { x.tenant_id, x.group_id },
                        principalSchema: "app",
                        principalTable: "sample_compatibility_groups",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sample_compatibility_members_sample_condition",
                        columns: x => new { x.tenant_id, x.sample_condition_id },
                        principalSchema: "app",
                        principalTable: "sample_conditions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sample_compatibility_members_sample_type",
                        columns: x => new { x.tenant_id, x.sample_type_id },
                        principalSchema: "app",
                        principalTable: "sample_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sample_compatibility_groups_tenant_id_code",
                schema: "app",
                table: "sample_compatibility_groups",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_compatibility_members_sample",
                schema: "app",
                table: "sample_compatibility_members",
                columns: new[] { "tenant_id", "sample_type_id", "sample_condition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_compatibility_members_tenant_id_sample_condition_id",
                schema: "app",
                table: "sample_compatibility_members",
                columns: new[] { "tenant_id", "sample_condition_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sample_compatibility_members_pair",
                schema: "app",
                table: "sample_compatibility_members",
                columns: new[] { "tenant_id", "group_id", "sample_type_id", "sample_condition_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.sample_compatibility_groups ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sample_compatibility_groups FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sample_compatibility_groups
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sample_compatibility_members ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sample_compatibility_members FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sample_compatibility_members
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // LIS-CR-015 M4 (groups): the order of a member in its group starts at 1
            migrationBuilder.Sql("ALTER TABLE app.sample_compatibility_members ADD CONSTRAINT ck_sample_compatibility_members_priority CHECK (priority > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sample_compatibility_members",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sample_compatibility_groups",
                schema: "app");

            migrationBuilder.AddColumn<Guid>(
                name: "merges_into_condition_id",
                schema: "app",
                table: "sample_conditions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_conditions_tenant_id_merges_into_condition_id",
                schema: "app",
                table: "sample_conditions",
                columns: new[] { "tenant_id", "merges_into_condition_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_sample_conditions_merges_into",
                schema: "app",
                table: "sample_conditions",
                columns: new[] { "tenant_id", "merges_into_condition_id" },
                principalSchema: "app",
                principalTable: "sample_conditions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
