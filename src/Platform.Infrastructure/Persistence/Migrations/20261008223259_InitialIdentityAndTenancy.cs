using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAndTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enqueued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    security_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    password_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_password_attempts = table.Column<int>(type: "integer", nullable: false),
                    is_locked_out = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_platform_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    refresh_token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    successor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reauthenticated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("pk_refresh_sessions", x => x.id);
                    table.CheckConstraint("ck_refresh_sessions_kind_domain", "\"kind\" IN ('Browser', 'Machine')");
                    table.CheckConstraint("ck_refresh_sessions_revocation_reason_domain", "\"revocation_reason\" IN ('Logout', 'Admin', 'PasswordChanged', 'ReuseDetected', 'Expired', 'TenantSuspended')");
                    table.CheckConstraint("ck_refresh_sessions_scope_domain", "\"scope\" IN ('Full', 'MfaPending', 'MfaEnrollment')");
                    table.ForeignKey(
                        name: "fk_refresh_sessions_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    full_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    full_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    branch_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    department_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
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
                    table.PrimaryKey("pk_tenant_memberships", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_tenant_memberships_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Row-level security is declared in the migration that creates the table (01 §4.2 item 5, pitfall P-04).
            // The bypass clause is what an allow-listed elevated scope turns on (outbox, provisioning, principal
            // query); the runtime role itself is NOBYPASSRLS. An unresolved tenant stamps the nil UUID and matches nothing.
            migrationBuilder.Sql("""
                ALTER TABLE app.tenant_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.tenant_memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.tenant_memberships
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pending",
                schema: "ops",
                table: "outbox_events",
                column: "next_attempt_at_utc",
                filter: "processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                schema: "identity",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_username",
                schema: "identity",
                table: "platform_users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_family_id",
                schema: "identity",
                table: "refresh_sessions",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_refresh_token_hash",
                schema: "identity",
                table: "refresh_sessions",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_user_live",
                schema: "identity",
                table: "refresh_sessions",
                column: "platform_user_id",
                filter: "is_revoked = false");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_platform_user_id",
                schema: "app",
                table: "tenant_memberships",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant_id_employee_code",
                schema: "app",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "employee_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant_id_platform_user_id",
                schema: "app",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "platform_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "refresh_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "tenant_memberships",
                schema: "app");

            migrationBuilder.DropTable(
                name: "platform_users",
                schema: "identity");
        }
    }
}
