using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "identity",
                table: "refresh_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile",
                schema: "identity",
                table: "platform_users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_trail",
                schema: "audit",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aggregate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    prev_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    entry_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_trail", x => new { x.tenant_id, x.sequence });
                });

            migrationBuilder.CreateTable(
                name: "field_changes",
                schema: "audit",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_changes", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_field_changes_action_domain", "\"action\" IN ('Created', 'Modified', 'Deleted')");
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_settings",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_settings", x => new { x.tenant_id, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ux_platform_users_mobile",
                schema: "identity",
                table: "platform_users",
                column: "mobile",
                unique: true,
                filter: "mobile IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_trail_tenant_aggregate",
                schema: "audit",
                table: "audit_trail",
                columns: new[] { "tenant_id", "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_trail_tenant_time",
                schema: "audit",
                table: "audit_trail",
                columns: new[] { "tenant_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_trail_tenant_event",
                schema: "audit",
                table: "audit_trail",
                columns: new[] { "tenant_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_field_changes_tenant_actor",
                schema: "audit",
                table: "field_changes",
                columns: new[] { "tenant_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_field_changes_tenant_entity",
                schema: "audit",
                table: "field_changes",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_field_changes_tenant_time",
                schema: "audit",
                table: "field_changes",
                columns: new[] { "tenant_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_live",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "purpose", "target_hash" },
                filter: "consumed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_actor_time",
                schema: "audit",
                table: "security_events",
                columns: new[] { "actor_user_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_security_events_tenant_time",
                schema: "audit",
                table: "security_events",
                columns: new[] { "tenant_id", "at_utc" });

            // Append-only ledger (01 §4.8): an UPDATE or DELETE on any audit row is refused by a trigger, for every role
            // (lis_app additionally holds no UPDATE/DELETE privilege on the schema, deploy/postgres/schema.sql).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'audit rows are append-only: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
                END
                $fn$;
                CREATE TRIGGER field_changes_append_only BEFORE UPDATE OR DELETE ON audit.field_changes FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
                CREATE TRIGGER audit_trail_append_only BEFORE UPDATE OR DELETE ON audit.audit_trail FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
                CREATE TRIGGER security_events_append_only BEFORE UPDATE OR DELETE ON audit.security_events FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
                """);

            // audit.security_events carries a nullable tenant (02 §7: events before authentication). Reads are bounded to the
            // current tenant like every other table; a pre-authentication scope (the nil tenant) may append for any tenant
            // because the sign-in outcome is known only then, an authenticated scope only for its own tenant or none.
            migrationBuilder.Sql("""
                ALTER TABLE audit.security_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit.security_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON audit.security_events
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id IS NULL
                        OR tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR NULLIF(current_setting('app.current_tenant', true), '')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE audit.audit_trail ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit.audit_trail FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON audit.audit_trail
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE audit.field_changes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit.field_changes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON audit.field_changes
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.tenant_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.tenant_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.tenant_settings
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
            migrationBuilder.DropTable(
                name: "audit_trail",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "field_changes",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "security_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "tenant_settings",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ux_platform_users_mobile",
                schema: "identity",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "mobile",
                schema: "identity",
                table: "platform_users");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.refuse_change();");
        }
    }
}
