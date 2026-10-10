using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Modules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "saas");

            migrationBuilder.AddColumn<string[]>(
                name: "added_permission_keys",
                schema: "app",
                table: "tenant_memberships",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "designation",
                schema: "app",
                table: "tenant_memberships",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary_administrator",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_cancel",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_modify_verified_result",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_pay",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_preview_without_verify",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_print_invoice",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_refund",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_see_vip",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "override_can_transfer",
                schema: "app",
                table: "tenant_memberships",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "override_discount_cap_percent",
                schema: "app",
                table: "tenant_memberships",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "removed_permission_keys",
                schema: "app",
                table: "tenant_memberships",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "shifts",
                schema: "app",
                table: "tenant_memberships",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "signature_file_id",
                schema: "app",
                table: "tenant_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "app",
                table: "roles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_cancel",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_modify_verified_result",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_pay",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_preview_without_verify",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_print_invoice",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_refund",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_see_vip",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "special_can_transfer",
                schema: "app",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "special_discount_cap_percent",
                schema: "app",
                table: "roles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                schema: "identity",
                table: "platform_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_expires_at_utc",
                schema: "identity",
                table: "platform_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_log_entries",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sample_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    analyser_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_log_entries", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_agent_log_entries_level_domain", "\"level\" IN ('Debug', 'Info', 'Warning', 'Error')");
                });

            migrationBuilder.CreateTable(
                name: "analyser_connection_states",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analyser_connection_states", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_analyser_connection_states_status_domain", "\"status\" IN ('Unknown', 'Connected', 'Disconnected', 'Error')");
                });

            migrationBuilder.CreateTable(
                name: "analyser_models",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    discipline = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protocol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    physical_link = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    modes = table.Column<string[]>(type: "text[]", nullable: false),
                    driver_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("pk_analyser_models", x => x.id);
                    table.CheckConstraint("ck_analyser_models_physical_link_domain", "\"physical_link\" IN ('Serial', 'Tcp', 'File')");
                    table.CheckConstraint("ck_analyser_models_protocol_domain", "\"protocol\" IN ('Astm', 'Hl7', 'Proprietary')");
                });

            migrationBuilder.CreateTable(
                name: "analyser_result_intake",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analyser_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sample_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    analyser_test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raw_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    converted_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    flags = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    measured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analyser_result_intake", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_analyser_result_intake_status_domain", "\"status\" IN ('Accepted', 'Refused', 'Unmapped', 'AutoSendOff', 'UnknownSample')");
                });

            migrationBuilder.CreateTable(
                name: "analysers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_sample_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analyser_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    connection_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    serial_settings = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    serial_port = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tcp_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    tcp_port = table.Column<int>(type: "integer", nullable: true),
                    tcp_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    folder_path = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    auto_print = table.Column<bool>(type: "boolean", nullable: false),
                    download_orders = table.Column<bool>(type: "boolean", nullable: false),
                    append_unregistered_tests = table.Column<bool>(type: "boolean", nullable: false),
                    append_unregistered_samples = table.Column<bool>(type: "boolean", nullable: false),
                    overwrite_reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    overwrite_verified = table.Column<bool>(type: "boolean", nullable: false),
                    print_copies = table.Column<int>(type: "integer", nullable: false),
                    site_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_analysers", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_analysers_connection_kind_domain", "\"connection_kind\" IN ('Serial', 'Tcp', 'File')");
                    table.CheckConstraint("ck_analysers_mode_domain", "\"mode\" IN ('Unidirectional', 'Bidirectional', 'Download', 'SharedFolder')");
                    table.CheckConstraint("ck_analysers_print_copies_range", "print_copies >= 0 AND print_copies <= 10");
                    table.CheckConstraint("ck_analysers_tcp_role_domain", "\"tcp_role\" IS NULL OR \"tcp_role\" IN ('Client', 'Server')");
                });

            migrationBuilder.CreateTable(
                name: "base_price_lists",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    copied_from_list_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_base_price_lists", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: true),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_branches", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "cash_closings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cash_payments = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    card_payments = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    pos_payments = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    transfer_payments = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    payment_count = table.Column<int>(type: "integer", nullable: false),
                    cash_refunds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    card_refunds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    pos_refunds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    transfer_refunds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    refund_count = table.Column<int>(type: "integer", nullable: false),
                    outstanding_balance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    visit_count = table.Column<int>(type: "integer", nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    counted_cash = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_cash_closings", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_cash_closings_period", "period_from_utc < period_to_utc");
                    table.ForeignKey(
                        name: "fk_cash_closings_platform_users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_closings_platform_users_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_closings_treasuries_tenant_id_treasury_id",
                        columns: x => new { x.tenant_id, x.treasury_id },
                        principalSchema: "app",
                        principalTable: "treasuries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_groups",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    report_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    report_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_clinical_groups", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "code_systems",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    use = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    last_imported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_code_systems", x => x.id);
                    table.CheckConstraint("ck_code_systems_name_domain", "\"name\" IN ('Loinc', 'Cpt', 'Icd10', 'Snomed')");
                });

            migrationBuilder.CreateTable(
                name: "comment_templates",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    short_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    text_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_templates", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "countries",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    iso_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    dial_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    currency = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mobile_length = table.Column<int>(type: "integer", nullable: false),
                    flag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diagnoses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    icd_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnoses", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "doctors",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    branch_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    share_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_doctors_share_percent_range", "share_percent >= 0 AND share_percent <= 100");
                });

            migrationBuilder.CreateTable(
                name: "drug_types",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drug_types", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "machine_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    service_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    secret_rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_machine_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_credentials_service_user",
                        column: x => x.service_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "measurement_units",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("pk_measurement_units", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    subject_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    text_en = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    text_ar = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                    table.CheckConstraint("ck_notification_templates_channel_domain", "\"channel\" IN ('Sms', 'Email')");
                });

            migrationBuilder.CreateTable(
                name: "organisms",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisms", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "patient_conditions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
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
                    table.PrimaryKey("pk_patient_conditions", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    gender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    age_years = table.Column<int>(type: "integer", nullable: true),
                    age_months = table.Column<int>(type: "integer", nullable: true),
                    age_days = table.Column<int>(type: "integer", nullable: true),
                    age_recorded_on = table.Column<DateOnly>(type: "date", nullable: true),
                    registered_on = table.Column<DateOnly>(type: "date", nullable: false),
                    patient_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mobile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    national_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    nationality = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    comments = table.Column<string>(type: "text", nullable: true),
                    is_vip = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    merged_into_patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merge_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_patients", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_patients_birth_or_age", "date_of_birth IS NOT NULL OR (age_years IS NOT NULL AND age_recorded_on IS NOT NULL)");
                    table.CheckConstraint("ck_patients_gender_domain", "\"gender\" IN ('Male', 'Female')");
                    table.CheckConstraint("ck_patients_merge_consistency", "(status = 'Merged') = (merged_into_patient_id IS NOT NULL AND merged_at_utc IS NOT NULL AND merged_by_user_id IS NOT NULL AND merge_reason IS NOT NULL)");
                    table.CheckConstraint("ck_patients_not_merged_into_self", "merged_into_patient_id IS NULL OR merged_into_patient_id <> id");
                    table.CheckConstraint("ck_patients_status_domain", "\"status\" IN ('Active', 'Merged')");
                    table.CheckConstraint("ck_patients_title_domain", "\"title\" IN ('Mr', 'Mrs', 'Miss', 'Ms', 'Dr', 'Master', 'Baby', 'Prof')");
                    table.ForeignKey(
                        name: "fk_patients_patients_tenant_id_merged_into_patient_id",
                        columns: x => new { x.tenant_id, x.merged_into_patient_id },
                        principalSchema: "app",
                        principalTable: "patients",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    search_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    web_portal_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    web_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_payers", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payers_type_domain", "\"type\" IN ('Cash', 'Credit', 'Insurance', 'Lab')");
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    monthly_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    max_branches = table.Column<int>(type: "integer", nullable: false),
                    max_users = table.Column<int>(type: "integer", nullable: false),
                    sms_quota = table.Column<int>(type: "integer", nullable: false),
                    max_analysers = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preparations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    question_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    question_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    answer_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    gender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preparations", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_preparations_answer_type_domain", "\"answer_type\" IN ('Text', 'YesNo', 'OptionList')");
                    table.CheckConstraint("ck_preparations_gender_domain", "\"gender\" IN ('Any', 'Male', 'Female')");
                });

            migrationBuilder.CreateTable(
                name: "reasons",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reasons", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_reasons_kind_domain", "\"kind\" IN ('Rejection', 'Cancellation', 'Discount', 'Refund', 'Transfer')");
                });

            migrationBuilder.CreateTable(
                name: "report_templates",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signature_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    show_qr = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    header_text_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    header_text_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    footer_text_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    footer_text_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_templates", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_report_templates_language_domain", "\"language\" IN ('En', 'Ar', 'Both')");
                    table.CheckConstraint("ck_report_templates_type_domain", "\"type\" IN ('Results', 'Culture', 'Text', 'Claim', 'Receipt', 'Label', 'JobOrder')");
                });

            migrationBuilder.CreateTable(
                name: "role_result_rights",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    can_see = table.Column<bool>(type: "boolean", nullable: false),
                    can_review = table.Column<bool>(type: "boolean", nullable: false),
                    can_verify = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit = table.Column<bool>(type: "boolean", nullable: false),
                    can_print = table.Column<bool>(type: "boolean", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_result_rights", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_role_result_rights_roles_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "app",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_templates",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    clinical_group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    formula = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_rule_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample_types",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    short_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("pk_sample_types", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "samples",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tube_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    sample_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accession = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    collected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    collected_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    rejection_reason_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    rejection_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rejected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_sample_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaces_sample_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_samples", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_samples_status_domain", "\"status\" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Cancelled', 'Replaced')");
                });

            migrationBuilder.CreateTable(
                name: "sensitivity_values",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sensitivity_values", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sensitivity_values_display_order_range", "display_order >= 0 AND display_order <= 999");
                });

            migrationBuilder.CreateTable(
                name: "shortcut_sets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shortcut_sets", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_shortcut_sets_services_present", "cardinality(service_ids) >= 1");
                });

            migrationBuilder.CreateTable(
                name: "site_agent_states",
                schema: "app",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reported_error = table.Column<bool>(type: "boolean", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_agent_states", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "subscription_requests",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    licence_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    manager_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manager_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    codes_sent = table.Column<int>(type: "integer", nullable: false),
                    last_code_sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_subscription_requests", x => x.id);
                    table.CheckConstraint("ck_subscription_requests_status_domain", "\"status\" IN ('Pending', 'Verified', 'Completed')");
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    laboratory_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    laboratory_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.CheckConstraint("ck_support_tickets_priority_domain", "\"priority\" IN ('Low', 'Normal', 'High')");
                    table.CheckConstraint("ck_support_tickets_status_domain", "\"status\" IN ('Open', 'InProgress', 'Resolved', 'Closed')");
                    table.ForeignKey(
                        name: "fk_support_tickets_opened_by_user",
                        column: x => x.opened_by_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "test_rules",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    added_service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_rules", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_test_rules_sides", "cardinality(trigger_service_ids) >= 1 AND cardinality(added_service_ids) >= 1 AND NOT (trigger_service_ids && added_service_ids)");
                });

            migrationBuilder.CreateTable(
                name: "visit_accounts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    refunded_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
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
                    table.PrimaryKey("pk_visit_accounts", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_visit_accounts_totals", "paid_total >= 0 AND refunded_total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "pcr_kit_templates",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    analyser_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    layout_json = table.Column<string>(type: "jsonb", nullable: false),
                    thresholds_json = table.Column<string>(type: "jsonb", nullable: false),
                    verdict_rules_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_pcr_kit_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_pcr_kit_templates_analyser_models_analyser_model_id",
                        column: x => x.analyser_model_id,
                        principalSchema: "saas",
                        principalTable: "analyser_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analyser_flags",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyser_flags", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_analyser_flags_action_domain", "\"action\" IN ('Record', 'Stop')");
                    table.ForeignKey(
                        name: "fk_analyser_flags_analysers_tenant_id_analyser_id",
                        columns: x => new { x.tenant_id, x.analyser_id },
                        principalSchema: "app",
                        principalTable: "analysers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "analyser_sample_codes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_analyser_sample_codes", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_analyser_sample_codes_analyser",
                        columns: x => new { x.tenant_id, x.analyser_id },
                        principalSchema: "app",
                        principalTable: "analysers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analyser_test_mappings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyser_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    host_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    result_selection = table.Column<int>(type: "integer", nullable: false),
                    slope = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    intercept = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    query = table.Column<bool>(type: "boolean", nullable: false),
                    query_details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auto_send = table.Column<bool>(type: "boolean", nullable: false),
                    range_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    methodology = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_analyser_test_mappings", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_analyser_test_mappings_slope_non_zero", "slope <> 0");
                    table.ForeignKey(
                        name: "fk_analyser_test_mappings_analyser",
                        columns: x => new { x.tenant_id, x.analyser_id },
                        principalSchema: "app",
                        principalTable: "analysers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pcr_kits",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyser_test_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    serial_lock = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    layout_delimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    layout_sample_column = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    layout_gene_column = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    layout_well_column = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    layout_content_column = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    layout_patient_content_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    layout_serial_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    layout_no_amplification_tokens = table.Column<string[]>(type: "text[]", nullable: false),
                    value_column = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detected_min_positive_targets = table.Column<int>(type: "integer", nullable: false),
                    detected_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    not_detected_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    presumptive_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invalid_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    result_folder = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    backup_folder = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    failed_folder = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
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
                    table.PrimaryKey("pk_pcr_kits", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pcr_kits_detected_min_targets_range", "detected_min_positive_targets >= 1 AND detected_min_positive_targets <= 10");
                    table.ForeignKey(
                        name: "fk_pcr_kits_analyser",
                        columns: x => new { x.tenant_id, x.analyser_id },
                        principalSchema: "app",
                        principalTable: "analysers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "base_price_lines",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_base_price_lines", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_base_price_lines_list",
                        columns: x => new { x.tenant_id, x.price_list_id },
                        principalSchema: "app",
                        principalTable: "base_price_lists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "code_system_items",
                schema: "saas",
                columns: table => new
                {
                    code_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_code_system_items", x => new { x.code_system_id, x.code });
                    table.ForeignKey(
                        name: "fk_code_system_items_code_systems_code_system_id",
                        column: x => x.code_system_id,
                        principalSchema: "saas",
                        principalTable: "code_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "governorates",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_governorates", x => x.id);
                    table.ForeignKey(
                        name: "fk_governorates_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "saas",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sending_labs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    courier_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sending_labs", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sending_labs_doctors_tenant_id_doctor_id",
                        columns: x => new { x.tenant_id, x.doctor_id },
                        principalSchema: "app",
                        principalTable: "doctors",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commercial_names",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drug_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_names", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_commercial_names_drug_types_tenant_id_drug_type_id",
                        columns: x => new { x.tenant_id, x.drug_type_id },
                        principalSchema: "app",
                        principalTable: "drug_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "site_agents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    config_version = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_site_agents", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_site_agents_config_version_positive", "config_version >= 1");
                    table.ForeignKey(
                        name: "fk_site_agents_machine_credential",
                        column: x => x.machine_credential_id,
                        principalSchema: "identity",
                        principalTable: "machine_credentials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_documents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_patient_documents", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_patient_documents_deactivation", "is_active OR (deactivated_at_utc IS NOT NULL AND deactivated_by_user_id IS NOT NULL AND deactivation_reason IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_patient_documents_patients_tenant_id_patient_id",
                        columns: x => new { x.tenant_id, x.patient_id },
                        principalSchema: "app",
                        principalTable: "patients",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_tiers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_tiers", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_tiers_payer",
                        columns: x => new { x.tenant_id, x.payer_id },
                        principalSchema: "app",
                        principalTable: "payers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_conditions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_conditions", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sample_conditions_sample_types_tenant_id_sample_type_id",
                        columns: x => new { x.tenant_id, x.sample_type_id },
                        principalSchema: "app",
                        principalTable: "sample_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_events", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sample_events_action_domain", "\"action\" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Unrejected', 'Replaced', 'ReplacementOrdered', 'Cancelled')");
                    table.CheckConstraint("ck_sample_events_status_domain", "\"status\" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Cancelled', 'Replaced')");
                    table.ForeignKey(
                        name: "fk_sample_events_samples_tenant_id_sample_id",
                        columns: x => new { x.tenant_id, x.sample_id },
                        principalSchema: "app",
                        principalTable: "samples",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_replies",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    from_platform = table.Column<bool>(type: "boolean", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    support_ticket_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_ticket_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_support_ticket_replies_support_tickets_support_ticket_id",
                        column: x => x.support_ticket_id,
                        principalSchema: "saas",
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payments_method_domain", "\"method\" IN ('Cash', 'Card', 'Pos', 'Transfer')");
                    table.CheckConstraint("ck_payments_reference_for_non_cash", "method = 'Cash' OR reference IS NOT NULL");
                    table.CheckConstraint("ck_payments_status_domain", "\"status\" IN ('Posted', 'Voided')");
                    table.CheckConstraint("ck_payments_void_consistency", "(status = 'Voided') = (void_reason IS NOT NULL AND voided_at_utc IS NOT NULL AND voided_by_user_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payments_platform_users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_platform_users_voided_by_user_id",
                        column: x => x.voided_by_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_treasuries_tenant_id_treasury_id",
                        columns: x => new { x.tenant_id, x.treasury_id },
                        principalSchema: "app",
                        principalTable: "treasuries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_visit_accounts_tenant_id_visit_id",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visit_accounts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    treasury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refunded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_refunds_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_refunds_method_domain", "\"method\" IN ('Cash', 'Card', 'Pos', 'Transfer')");
                    table.CheckConstraint("ck_refunds_reference_for_non_cash", "method = 'Cash' OR reference IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_refunds_platform_users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_treasuries_tenant_id_treasury_id",
                        columns: x => new { x.tenant_id, x.treasury_id },
                        principalSchema: "app",
                        principalTable: "treasuries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_visit_accounts_tenant_id_visit_id",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visit_accounts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analyser_value_conversions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    from_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyser_value_conversions", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_analyser_value_conversions_analyser_test_mappings_tenant_id",
                        columns: x => new { x.tenant_id, x.mapping_id },
                        principalSchema: "app",
                        principalTable: "analyser_test_mappings",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pcr_kit_genes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_internal_control = table.Column<bool>(type: "boolean", nullable: false),
                    positive_ct_max = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    borderline_ct_max = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    kit_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pcr_kit_genes", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pcr_kit_genes_thresholds", "positive_ct_max > 0 AND positive_ct_max <= borderline_ct_max AND borderline_ct_max <= 60");
                    table.ForeignKey(
                        name: "fk_pcr_kit_genes_pcr_kits_tenant_id_kit_id",
                        columns: x => new { x.tenant_id, x.kit_id },
                        principalSchema: "app",
                        principalTable: "pcr_kits",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    licence_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    manager_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manager_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portal_slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trial_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    administrator_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_source_domain", "\"source\" IN ('Subscription', 'Manual')");
                    table.CheckConstraint("ck_tenants_status_domain", "\"status\" IN ('Trial', 'Active', 'Inactive')");
                    table.ForeignKey(
                        name: "fk_tenants_countries_country_id",
                        column: x => x.country_id,
                        principalSchema: "saas",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenants_governorates_governorate_id",
                        column: x => x.governorate_id,
                        principalSchema: "saas",
                        principalTable: "governorates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenants_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "saas",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "antibiotics",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    commercial_name_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_antibiotics", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_antibiotics_commercial_names",
                        columns: x => new { x.tenant_id, x.commercial_name_id },
                        principalSchema: "app",
                        principalTable: "commercial_names",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_pricings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_contract_pricings", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_pricings_payer",
                        columns: x => new { x.tenant_id, x.payer_id },
                        principalSchema: "app",
                        principalTable: "payers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contract_pricings_tier",
                        columns: x => new { x.tenant_id, x.contract_tier_id },
                        principalSchema: "app",
                        principalTable: "contract_tiers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    short_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shortcut_no = table.Column<int>(type: "integer", nullable: true),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    report_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    loinc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cpt = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sample_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    header = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    precaution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    gender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    turnaround_hours = table.Column<int>(type: "integer", nullable: false),
                    label_count = table.Column<int>(type: "integer", nullable: false),
                    print_alone = table.Column<bool>(type: "boolean", nullable: false),
                    is_orderable = table.Column<bool>(type: "boolean", nullable: false),
                    show_on_web = table.Column<bool>(type: "boolean", nullable: false),
                    profile_sum = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_services", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_services_gender_domain", "\"gender\" IN ('Any', 'Male', 'Female')");
                    table.CheckConstraint("ck_services_type_domain", "\"type\" IN ('Test', 'Profile', 'Package', 'Culture', 'TextReport', 'Additional')");
                    table.ForeignKey(
                        name: "fk_services_clinical_groups_tenant_id_clinical_group_id",
                        columns: x => new { x.tenant_id, x.clinical_group_id },
                        principalSchema: "app",
                        principalTable: "clinical_groups",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_services_sample_condition",
                        columns: x => new { x.tenant_id, x.sample_condition_id },
                        principalSchema: "app",
                        principalTable: "sample_conditions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_services_sample_types_tenant_id_sample_type_id",
                        columns: x => new { x.tenant_id, x.sample_type_id },
                        principalSchema: "app",
                        principalTable: "sample_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tests",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    report_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    loinc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cpt = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sample_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    methodology = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decimals = table.Column<int>(type: "integer", nullable: false),
                    is_hidden_helper = table.Column<bool>(type: "boolean", nullable: false),
                    print_reference = table.Column<bool>(type: "boolean", nullable: false),
                    label_count = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    duration_hours = table.Column<int>(type: "integer", nullable: false),
                    min_duration_hours = table.Column<int>(type: "integer", nullable: false),
                    frequency_check_days = table.Column<int>(type: "integer", nullable: true),
                    frequency_action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    delta_percent = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    delta_window_days = table.Column<int>(type: "integer", nullable: true),
                    test_days = table.Column<int>(type: "integer", nullable: false),
                    formula_expression = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    formula_references = table.Column<string[]>(type: "text[]", nullable: false),
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
                    table.PrimaryKey("pk_tests", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tests_frequency_action_domain", "\"frequency_action\" IN ('Alert', 'Prevent')");
                    table.CheckConstraint("ck_tests_gender_domain", "\"gender\" IN ('Any', 'Male', 'Female')");
                    table.CheckConstraint("ck_tests_type_domain", "\"type\" IN ('Numeric', 'NonNumeric', 'OptionalList', 'RestrictedList', 'Formula', 'FormattedText', 'Culture')");
                    table.ForeignKey(
                        name: "fk_tests_clinical_groups_tenant_id_clinical_group_id",
                        columns: x => new { x.tenant_id, x.clinical_group_id },
                        principalSchema: "app",
                        principalTable: "clinical_groups",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tests_measurement_units_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "app",
                        principalTable: "measurement_units",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tests_sample_condition",
                        columns: x => new { x.tenant_id, x.sample_condition_id },
                        principalSchema: "app",
                        principalTable: "sample_conditions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tests_sample_types_tenant_id_sample_type_id",
                        columns: x => new { x.tenant_id, x.sample_type_id },
                        principalSchema: "app",
                        principalTable: "sample_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    next_charge_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallets_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "saas",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_prices",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_pricing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    patient_share = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payer_share = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    alert_condition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_list_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_prices", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_prices_pricing",
                        columns: x => new { x.tenant_id, x.contract_pricing_id },
                        principalSchema: "app",
                        principalTable: "contract_pricings",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_pricing_sources",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_pricing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    base_price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_factor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    payer_factor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_pricing_sources", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_contract_pricing_sources_priority", "priority BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_contract_pricing_sources_list",
                        columns: x => new { x.tenant_id, x.base_price_list_id },
                        principalSchema: "app",
                        principalTable: "base_price_lists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contract_pricing_sources_pricing",
                        columns: x => new { x.tenant_id, x.contract_pricing_id },
                        principalSchema: "app",
                        principalTable: "contract_pricings",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_members",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    in_sum = table.Column<bool>(type: "boolean", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_members", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_service_members_kind_domain", "\"kind\" IN ('Test', 'Service')");
                    table.ForeignKey(
                        name: "fk_service_members_services_tenant_id_service_id",
                        columns: x => new { x.tenant_id, x.service_id },
                        principalSchema: "app",
                        principalTable: "services",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_list_values",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lab_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_normal = table.Column<bool>(type: "boolean", nullable: false),
                    is_critical = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_placeholder = table.Column<bool>(type: "boolean", nullable: false),
                    colour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_list_values", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_test_list_values_tests_tenant_id_lab_test_id",
                        columns: x => new { x.tenant_id, x.lab_test_id },
                        principalSchema: "app",
                        principalTable: "tests",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_paragraphs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lab_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_paragraphs", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_test_paragraphs_tests_tenant_id_lab_test_id",
                        columns: x => new { x.tenant_id, x.lab_test_id },
                        principalSchema: "app",
                        principalTable: "tests",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_range_bands",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lab_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sex = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    age_from = table.Column<int>(type: "integer", nullable: false),
                    age_to = table.Column<int>(type: "integer", nullable: false),
                    age_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    patient_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    panic_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    panic_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    reportable_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    reportable_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    text_range = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_range_bands", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_test_range_bands_age_unit_domain", "\"age_unit\" IN ('Days', 'Months', 'Years')");
                    table.CheckConstraint("ck_test_range_bands_sex_domain", "\"sex\" IN ('Any', 'Male', 'Female')");
                    table.ForeignKey(
                        name: "fk_test_range_bands_patient_condition",
                        columns: x => new { x.tenant_id, x.patient_condition_id },
                        principalSchema: "app",
                        principalTable: "patient_conditions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_test_range_bands_tests_tenant_id_lab_test_id",
                        columns: x => new { x.tenant_id, x.lab_test_id },
                        principalSchema: "app",
                        principalTable: "tests",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_top_up_requests",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_top_up_requests", x => x.id);
                    table.CheckConstraint("ck_wallet_top_up_requests_status_domain", "\"status\" IN ('Pending', 'Completed', 'Declined')");
                    table.ForeignKey(
                        name: "fk_wallet_top_up_requests_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "saas",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet_transactions",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_transactions", x => x.id);
                    table.CheckConstraint("ck_wallet_transactions_kind_domain", "\"kind\" IN ('TopUp', 'Subscription', 'Usage')");
                    table.ForeignKey(
                        name: "fk_wallet_transactions_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "saas",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_memberships_primary_administrator",
                schema: "app",
                table: "tenant_memberships",
                column: "tenant_id",
                unique: true,
                filter: "is_primary_administrator");

            migrationBuilder.CreateIndex(
                name: "ix_agent_log_entries_agent_time",
                schema: "app",
                table: "agent_log_entries",
                columns: new[] { "tenant_id", "agent_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_analyser_flags_analyser",
                schema: "app",
                table: "analyser_flags",
                columns: new[] { "tenant_id", "analyser_id" });

            migrationBuilder.CreateIndex(
                name: "ix_analyser_models_vendor_model",
                schema: "saas",
                table: "analyser_models",
                columns: new[] { "vendor", "model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analyser_result_intake_analyser",
                schema: "app",
                table: "analyser_result_intake",
                columns: new[] { "tenant_id", "analyser_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_analyser_result_intake_sample",
                schema: "app",
                table: "analyser_result_intake",
                columns: new[] { "tenant_id", "sample_number" });

            migrationBuilder.CreateIndex(
                name: "ix_analyser_result_intake_status",
                schema: "app",
                table: "analyser_result_intake",
                columns: new[] { "tenant_id", "status", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_analyser_result_intake_time",
                schema: "app",
                table: "analyser_result_intake",
                columns: new[] { "tenant_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_analyser_sample_codes_type",
                schema: "app",
                table: "analyser_sample_codes",
                columns: new[] { "tenant_id", "analyser_id", "sample_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analyser_test_mappings_test",
                schema: "app",
                table: "analyser_test_mappings",
                columns: new[] { "tenant_id", "test_id" });

            migrationBuilder.CreateIndex(
                name: "ux_analyser_test_mappings_code",
                schema: "app",
                table: "analyser_test_mappings",
                columns: new[] { "tenant_id", "analyser_id", "analyser_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analyser_value_conversions_mapping",
                schema: "app",
                table: "analyser_value_conversions",
                columns: new[] { "tenant_id", "mapping_id" });

            migrationBuilder.CreateIndex(
                name: "ix_analysers_clinical_group",
                schema: "app",
                table: "analysers",
                columns: new[] { "tenant_id", "clinical_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_analysers_site_agent",
                schema: "app",
                table: "analysers",
                columns: new[] { "tenant_id", "site_agent_id" });

            migrationBuilder.CreateIndex(
                name: "ux_analysers_code",
                schema: "app",
                table: "analysers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_antibiotics_tenant_id_code",
                schema: "app",
                table: "antibiotics",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_antibiotics_tenant_id_commercial_name_id",
                schema: "app",
                table: "antibiotics",
                columns: new[] { "tenant_id", "commercial_name_id" });

            migrationBuilder.CreateIndex(
                name: "ix_antibiotics_tenant_id_is_active",
                schema: "app",
                table: "antibiotics",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_base_price_lines_tenant_id_service_id",
                schema: "app",
                table: "base_price_lines",
                columns: new[] { "tenant_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ux_base_price_lines_service",
                schema: "app",
                table: "base_price_lines",
                columns: new[] { "tenant_id", "price_list_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_base_price_lists_tenant_id_is_active_year",
                schema: "app",
                table: "base_price_lists",
                columns: new[] { "tenant_id", "is_active", "year" });

            migrationBuilder.CreateIndex(
                name: "ux_base_price_lists_code",
                schema: "app",
                table: "base_price_lists",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_code",
                schema: "app",
                table: "branches",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_is_active",
                schema: "app",
                table: "branches",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_cashier_user_id",
                schema: "app",
                table: "cash_closings",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_closed_by_user_id",
                schema: "app",
                table: "cash_closings",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_tenant_id_closed_at_utc",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "closed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_tenant_id_number",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_tenant_id_treasury_id_period_from_utc",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "treasury_id", "period_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_groups_tenant_id_code",
                schema: "app",
                table: "clinical_groups",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clinical_groups_tenant_id_is_active_display_order",
                schema: "app",
                table: "clinical_groups",
                columns: new[] { "tenant_id", "is_active", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_code_systems_name_version",
                schema: "saas",
                table: "code_systems",
                columns: new[] { "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comment_templates_tenant_id_clinical_group_id",
                schema: "app",
                table: "comment_templates",
                columns: new[] { "tenant_id", "clinical_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_comment_templates_tenant_id_is_active",
                schema: "app",
                table: "comment_templates",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_comment_templates_tenant_id_short_name",
                schema: "app",
                table: "comment_templates",
                columns: new[] { "tenant_id", "short_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_names_tenant_id_drug_type_id",
                schema: "app",
                table: "commercial_names",
                columns: new[] { "tenant_id", "drug_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_names_tenant_id_is_active",
                schema: "app",
                table: "commercial_names",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_prices_service",
                schema: "app",
                table: "contract_prices",
                columns: new[] { "tenant_id", "contract_pricing_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_pricing_sources_list",
                schema: "app",
                table: "contract_pricing_sources",
                columns: new[] { "tenant_id", "base_price_list_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_pricing_sources_pricing",
                schema: "app",
                table: "contract_pricing_sources",
                columns: new[] { "tenant_id", "contract_pricing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_pricings_tenant_id_payer_id",
                schema: "app",
                table: "contract_pricings",
                columns: new[] { "tenant_id", "payer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_pricings_tier",
                schema: "app",
                table: "contract_pricings",
                columns: new[] { "tenant_id", "contract_tier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_tiers_tenant_id_payer_id",
                schema: "app",
                table: "contract_tiers",
                columns: new[] { "tenant_id", "payer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_countries_iso_code",
                schema: "saas",
                table: "countries",
                column: "iso_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnoses_tenant_id_icd_code",
                schema: "app",
                table: "diagnoses",
                columns: new[] { "tenant_id", "icd_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnoses_tenant_id_is_active",
                schema: "app",
                table: "diagnoses",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_doctors_tenant_id_code",
                schema: "app",
                table: "doctors",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctors_tenant_id_is_active",
                schema: "app",
                table: "doctors",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_drug_types_tenant_id_is_active",
                schema: "app",
                table: "drug_types",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_governorates_country_id",
                schema: "saas",
                table: "governorates",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_credentials_tenant",
                schema: "identity",
                table: "machine_credentials",
                columns: new[] { "tenant_id", "revoked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_machine_credentials_client_id",
                schema: "identity",
                table: "machine_credentials",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_machine_credentials_service_user",
                schema: "identity",
                table: "machine_credentials",
                column: "service_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_measurement_units_tenant_id_name_key",
                schema: "app",
                table: "measurement_units",
                columns: new[] { "tenant_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_code",
                schema: "saas",
                table: "notification_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisms_tenant_id_code",
                schema: "app",
                table: "organisms",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisms_tenant_id_is_active",
                schema: "app",
                table: "organisms",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_conditions_tenant_id_name_key",
                schema: "app",
                table: "patient_conditions",
                columns: new[] { "tenant_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_documents_tenant_id_patient_id",
                schema: "app",
                table: "patient_documents",
                columns: new[] { "tenant_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_documents_tenant_id_storage_key",
                schema: "app",
                table: "patient_documents",
                columns: new[] { "tenant_id", "storage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_merged_into_patient_id",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "merged_into_patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_mobile",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "mobile" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_name_key",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "name_key" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_national_id",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "national_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_number",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_status",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payers_tenant_id_is_active",
                schema: "app",
                table: "payers",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_payers_default",
                schema: "app",
                table: "payers",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_payers_search_key",
                schema: "app",
                table: "payers",
                columns: new[] { "tenant_id", "search_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payers_web_username",
                schema: "app",
                table: "payers",
                columns: new[] { "tenant_id", "web_username" },
                unique: true,
                filter: "web_username IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_cashier_user_id",
                schema: "app",
                table: "payments",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_batch_id",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "batch_id" },
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_cashier_user_id_paid_at_utc",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "cashier_user_id", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_paid_at_utc",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_receipt_number",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_treasury_id_paid_at_utc",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "treasury_id", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_visit_id",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_voided_by_user_id",
                schema: "app",
                table: "payments",
                column: "voided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pcr_kit_genes_kit",
                schema: "app",
                table: "pcr_kit_genes",
                columns: new[] { "tenant_id", "kit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pcr_kit_templates_analyser_model_id",
                schema: "saas",
                table: "pcr_kit_templates",
                column: "analyser_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_pcr_kit_templates_name",
                schema: "saas",
                table: "pcr_kit_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pcr_kits_tenant_id_analyser_id",
                schema: "app",
                table: "pcr_kits",
                columns: new[] { "tenant_id", "analyser_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pcr_kits_name",
                schema: "app",
                table: "pcr_kits",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                schema: "saas",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preparations_tenant_id_code",
                schema: "app",
                table: "preparations",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preparations_tenant_id_is_active",
                schema: "app",
                table: "preparations",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_reasons_tenant_id_is_active",
                schema: "app",
                table: "reasons",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_reasons_tenant_id_kind",
                schema: "app",
                table: "reasons",
                columns: new[] { "tenant_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_cashier_user_id",
                schema: "app",
                table: "refunds",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_number",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_refunded_at_utc",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "refunded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_treasury_id_refunded_at_utc",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "treasury_id", "refunded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_visit_id",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_templates_tenant_id_is_active",
                schema: "app",
                table: "report_templates",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_report_templates_tenant_id_type",
                schema: "app",
                table: "report_templates",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_role_result_rights_group",
                schema: "app",
                table: "role_result_rights",
                columns: new[] { "tenant_id", "clinical_group_id" });

            migrationBuilder.CreateIndex(
                name: "ux_role_result_rights_scope",
                schema: "app",
                table: "role_result_rights",
                columns: new[] { "tenant_id", "role_id", "clinical_group_id", "branch_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_rule_templates_name",
                schema: "saas",
                table: "rule_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_conditions_tenant_id_sample_type_id_code",
                schema: "app",
                table: "sample_conditions",
                columns: new[] { "tenant_id", "sample_type_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_events_tenant_id_sample_id_at_utc",
                schema: "app",
                table: "sample_events",
                columns: new[] { "tenant_id", "sample_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_types_tenant_id_code",
                schema: "app",
                table: "sample_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_accession",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "accession" });

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_ordered_at_utc",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "ordered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_sample_number",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "sample_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_status_status_changed_at_utc",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "status", "status_changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_tube_id_generation",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "tube_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_visit_id",
                schema: "app",
                table: "samples",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sending_labs_tenant_id_doctor_id",
                schema: "app",
                table: "sending_labs",
                columns: new[] { "tenant_id", "doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sending_labs_tenant_id_is_active",
                schema: "app",
                table: "sending_labs",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_sending_labs_tenant_id_payer_id",
                schema: "app",
                table: "sending_labs",
                columns: new[] { "tenant_id", "payer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sensitivity_values_tenant_id_code",
                schema: "app",
                table: "sensitivity_values",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sensitivity_values_tenant_id_is_active",
                schema: "app",
                table: "sensitivity_values",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_service_members_tenant_id_member_id",
                schema: "app",
                table: "service_members",
                columns: new[] { "tenant_id", "member_id" });

            migrationBuilder.CreateIndex(
                name: "ux_service_members_member",
                schema: "app",
                table: "service_members",
                columns: new[] { "tenant_id", "service_id", "kind", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_service_members_order",
                schema: "app",
                table: "service_members",
                columns: new[] { "tenant_id", "service_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_clinical_group_id",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "clinical_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_code",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_sample_condition_id",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "sample_condition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_sample_type_id",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "sample_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_type_is_active",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_services_shortcut_no",
                schema: "app",
                table: "services",
                columns: new[] { "tenant_id", "shortcut_no" },
                unique: true,
                filter: "shortcut_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shortcut_sets_tenant_id_code",
                schema: "app",
                table: "shortcut_sets",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shortcut_sets_tenant_id_is_active",
                schema: "app",
                table: "shortcut_sets",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_site_agents_branch",
                schema: "app",
                table: "site_agents",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_site_agents_machine_credential_id",
                schema: "app",
                table: "site_agents",
                column: "machine_credential_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_agents_service_user",
                schema: "app",
                table: "site_agents",
                column: "service_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_site_agents_name",
                schema: "app",
                table: "site_agents",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_requests_expires_at_utc",
                schema: "saas",
                table: "subscription_requests",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_replies_ticket_at",
                schema: "saas",
                table: "support_ticket_replies",
                columns: new[] { "support_ticket_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_opened_by_user_id",
                schema: "saas",
                table: "support_tickets",
                column: "opened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status_activity",
                schema: "saas",
                table: "support_tickets",
                columns: new[] { "status", "last_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_activity",
                schema: "saas",
                table: "support_tickets",
                columns: new[] { "tenant_id", "last_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_id_number",
                schema: "saas",
                table: "support_tickets",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_country_id",
                schema: "saas",
                table: "tenants",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_governorate_id",
                schema: "saas",
                table: "tenants",
                column: "governorate_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_plan_id",
                schema: "saas",
                table: "tenants",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_portal_slug",
                schema: "saas",
                table: "tenants",
                column: "portal_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_status",
                schema: "saas",
                table: "tenants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_test_list_values_tenant_id_lab_test_id",
                schema: "app",
                table: "test_list_values",
                columns: new[] { "tenant_id", "lab_test_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_paragraphs_tenant_id_lab_test_id",
                schema: "app",
                table: "test_paragraphs",
                columns: new[] { "tenant_id", "lab_test_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_range_bands_patient_condition",
                schema: "app",
                table: "test_range_bands",
                columns: new[] { "tenant_id", "patient_condition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_range_bands_tenant_id_lab_test_id",
                schema: "app",
                table: "test_range_bands",
                columns: new[] { "tenant_id", "lab_test_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_rules_tenant_id_clinical_group_id",
                schema: "app",
                table: "test_rules",
                columns: new[] { "tenant_id", "clinical_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_rules_tenant_id_is_active",
                schema: "app",
                table: "test_rules",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tests_tenant_id_clinical_group_id_is_active",
                schema: "app",
                table: "tests",
                columns: new[] { "tenant_id", "clinical_group_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tests_tenant_id_code",
                schema: "app",
                table: "tests",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tests_tenant_id_sample_condition_id",
                schema: "app",
                table: "tests",
                columns: new[] { "tenant_id", "sample_condition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tests_tenant_id_sample_type_id",
                schema: "app",
                table: "tests",
                columns: new[] { "tenant_id", "sample_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tests_tenant_id_unit_id",
                schema: "app",
                table: "tests",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_top_up_requests_wallet_id_status",
                schema: "saas",
                table: "wallet_top_up_requests",
                columns: new[] { "wallet_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_wallet_id_at_utc",
                schema: "saas",
                table: "wallet_transactions",
                columns: new[] { "wallet_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_wallets_tenant_id",
                schema: "saas",
                table: "wallets",
                column: "tenant_id",
                unique: true);

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.agent_log_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.agent_log_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.agent_log_entries
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_connection_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_connection_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_connection_states
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_result_intake ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_result_intake FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_result_intake
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analysers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analysers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analysers
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.base_price_lists ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.base_price_lists FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.base_price_lists
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.branches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.branches FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.branches
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.cash_closings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.cash_closings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.cash_closings
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.clinical_groups ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.clinical_groups FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.clinical_groups
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.comment_templates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.comment_templates FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.comment_templates
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.diagnoses ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.diagnoses FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.diagnoses
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.doctors ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.doctors FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.doctors
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.drug_types ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.drug_types FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.drug_types
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.measurement_units ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.measurement_units FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.measurement_units
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.organisms ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.organisms FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.organisms
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.patient_conditions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.patient_conditions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.patient_conditions
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.patients ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.patients FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.patients
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.payers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.payers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.payers
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.preparations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.preparations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.preparations
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.reasons ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.reasons FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.reasons
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.report_templates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.report_templates FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.report_templates
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.role_result_rights ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.role_result_rights FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.role_result_rights
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sample_types ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sample_types FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sample_types
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.samples ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.samples FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.samples
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sensitivity_values ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sensitivity_values FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sensitivity_values
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.shortcut_sets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.shortcut_sets FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.shortcut_sets
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.site_agent_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.site_agent_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.site_agent_states
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.test_rules ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.test_rules FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.test_rules
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visit_accounts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visit_accounts FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visit_accounts
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_flags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_flags FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_flags
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_sample_codes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_sample_codes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_sample_codes
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_test_mappings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_test_mappings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_test_mappings
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.pcr_kits ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.pcr_kits FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.pcr_kits
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.base_price_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.base_price_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.base_price_lines
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sending_labs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sending_labs FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sending_labs
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.commercial_names ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.commercial_names FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.commercial_names
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.site_agents ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.site_agents FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.site_agents
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.patient_documents ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.patient_documents FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.patient_documents
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.contract_tiers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.contract_tiers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.contract_tiers
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sample_conditions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sample_conditions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sample_conditions
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.sample_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.sample_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.sample_events
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.payments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.payments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.payments
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.refunds ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.refunds FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.refunds
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.analyser_value_conversions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.analyser_value_conversions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.analyser_value_conversions
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.pcr_kit_genes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.pcr_kit_genes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.pcr_kit_genes
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.antibiotics ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.antibiotics FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.antibiotics
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.contract_pricings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.contract_pricings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.contract_pricings
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.services ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.services FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.services
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.tests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.tests FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.tests
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.contract_prices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.contract_prices FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.contract_prices
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.contract_pricing_sources ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.contract_pricing_sources FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.contract_pricing_sources
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.service_members ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.service_members FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.service_members
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.test_list_values ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.test_list_values FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.test_list_values
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.test_paragraphs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.test_paragraphs FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.test_paragraphs
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.test_range_bands ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.test_range_bands FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.test_range_bands
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Account/Account.migration.sql
            migrationBuilder.Sql("""
                -- Account & onboarding (intake LIS-INT-ACC-001): grants and the row-level security of the support desk tables.
                -- Appended by the integrator to the wave migration after saas.support_tickets and saas.support_ticket_replies exist;
                -- plain SQL, safe to run twice (DROP POLICY IF EXISTS before CREATE POLICY, idempotent GRANT and ALTER TABLE).

                -- 1. Grants: lis_app has no default privileges in saas (deploy/postgres/schema.sql). No DELETE: tickets and replies
                --    are kept for the record (BR-15); a ticket is closed, never removed.
                GRANT USAGE ON SCHEMA saas TO lis_app;
                GRANT SELECT, INSERT, UPDATE ON saas.support_tickets, saas.support_ticket_replies TO lis_app;

                -- 2. INV-SUP-004 in the database (defence in depth behind SupportTicketRepository.Scoped): a laboratory reaches only
                --    its own tickets; the platform tenant (the support desk) reaches every laboratory's, without elevation. This is a
                --    platform-level table, not an app tenant table: no tenant table's policy is widened (MODULES.md, acc row).
                ALTER TABLE saas.support_tickets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE saas.support_tickets FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS support_desk_scope ON saas.support_tickets;
                CREATE POLICY support_desk_scope ON saas.support_tickets
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.current_tenant', true) = '00000000-0000-0000-0000-000000000001'
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.current_tenant', true) = '00000000-0000-0000-0000-000000000001'
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );

                -- A reply is visible and writable exactly when its ticket is (the sub-select is itself bounded by the policy above).
                ALTER TABLE saas.support_ticket_replies ENABLE ROW LEVEL SECURITY;
                ALTER TABLE saas.support_ticket_replies FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS support_desk_scope ON saas.support_ticket_replies;
                CREATE POLICY support_desk_scope ON saas.support_ticket_replies
                    USING (EXISTS (SELECT 1 FROM saas.support_tickets t WHERE t.id = support_ticket_replies.support_ticket_id))
                    WITH CHECK (EXISTS (SELECT 1 FROM saas.support_tickets t WHERE t.id = support_ticket_replies.support_ticket_id));
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Billing/Billing.migration.sql
            migrationBuilder.Sql("""
                -- Billing (wave 2, intake LIS-INT-BIL-001): extra SQL the integrator appends to the wave migration after the tables of
                -- app.visit_accounts, app.payments, app.refunds and app.cash_closings (and Registration's app.visits) exist.
                -- Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS, guarded constraint creation.

                -- INV-BIL-004 / pitfall P-54: money records are never edited in place or deleted, for every role. Refunds and closings
                -- never change; a payment changes only by its void (Posted -> Voided with reason, voider and time), once.
                CREATE OR REPLACE FUNCTION app.billing_refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'billing rows are immutable: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
                END
                $fn$;

                CREATE OR REPLACE FUNCTION app.billing_guard_payment() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'payments are never deleted (receipt %)', OLD.receipt_number;
                    END IF;

                    IF OLD.status = 'Voided' THEN
                        RAISE EXCEPTION 'a voided payment is frozen (receipt %)', OLD.receipt_number;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
                       OR NEW.receipt_number IS DISTINCT FROM OLD.receipt_number
                       OR NEW.amount IS DISTINCT FROM OLD.amount
                       OR NEW.method IS DISTINCT FROM OLD.method
                       OR NEW.reference IS DISTINCT FROM OLD.reference
                       OR NEW.treasury_id IS DISTINCT FROM OLD.treasury_id
                       OR NEW.device_id IS DISTINCT FROM OLD.device_id
                       OR NEW.cashier_user_id IS DISTINCT FROM OLD.cashier_user_id
                       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
                       OR NEW.batch_id IS DISTINCT FROM OLD.batch_id
                       OR NEW.paid_at_utc IS DISTINCT FROM OLD.paid_at_utc THEN
                        RAISE EXCEPTION 'a payment changes only by its void (receipt %)', OLD.receipt_number;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                DROP TRIGGER IF EXISTS payments_guard ON app.payments;
                CREATE TRIGGER payments_guard BEFORE UPDATE OR DELETE ON app.payments FOR EACH ROW EXECUTE FUNCTION app.billing_guard_payment();

                DROP TRIGGER IF EXISTS refunds_immutable ON app.refunds;
                CREATE TRIGGER refunds_immutable BEFORE UPDATE OR DELETE ON app.refunds FOR EACH ROW EXECUTE FUNCTION app.billing_refuse_change();

                DROP TRIGGER IF EXISTS cash_closings_immutable ON app.cash_closings;
                CREATE TRIGGER cash_closings_immutable BEFORE UPDATE OR DELETE ON app.cash_closings FOR EACH ROW EXECUTE FUNCTION app.billing_refuse_change();

                -- An account belongs to a visit (id = the visit id). Registration owns app.visits in the same wave, so the foreign key is
                -- declared here rather than in the EF model (MODULE-PATTERN §3 "Same-wave lookups").
                DO $$
                BEGIN
                    IF to_regclass('app.visits') IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_accounts_visits' AND conrelid = 'app.visit_accounts'::regclass) THEN
                        ALTER TABLE app.visit_accounts
                            ADD CONSTRAINT fk_visit_accounts_visits FOREIGN KEY (tenant_id, id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT;
                    END IF;
                END
                $$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Catalogue/CatalogueA.migration.sql
            migrationBuilder.Sql("""
                -- Catalogue half A (intake LIS-INT-CAT-001, INV-CAT-004): the service sequence is unique per tenant, checked at
                -- commit so that the bulk save of CP-42 can swap two sequences in one transaction (EF issues one UPDATE per row).
                -- The handler refuses duplicates first with CAT-105; this constraint is the backstop. Idempotent.
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'ux_services_sequence'
                          AND conrelid = 'app.services'::regclass
                    ) THEN
                        ALTER TABLE app.services
                            ADD CONSTRAINT ux_services_sequence UNIQUE (tenant_id, sequence) DEFERRABLE INITIALLY DEFERRED;
                    END IF;
                END
                $$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Catalogue/CatalogueB.migration.sql
            migrationBuilder.Sql("""
                -- Catalogue B (intake LIS-INT-CTB-001), appended to the wave migration by scripts/add-module-sql-to-migration.mjs.
                -- INV-CTB-003: English names are unique per tenant, case-insensitively (per kind for reasons). The handlers refuse
                -- duplicates first with CAT-301; these indexes are the second line (UNIQUE-409). Idempotent; nothing tenant-specific
                -- is seeded.

                CREATE UNIQUE INDEX IF NOT EXISTS ux_sending_labs_name_en ON app.sending_labs (tenant_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_test_rules_name_en ON app.test_rules (tenant_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_reasons_kind_name_en ON app.reasons (tenant_id, kind, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_report_templates_name_en ON app.report_templates (tenant_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_drug_types_name_en ON app.drug_types (tenant_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_commercial_names_name_en ON app.commercial_names (tenant_id, lower(name_en));
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Integration/Integration.migration.sql
            migrationBuilder.Sql("""
                -- Integration module (intake LIS-INT-INT-001): extra SQL appended by the integrator to the wave-3 migration. Idempotent.
                -- The tables, keys, indexes and CHECK constraints come from the EF model; FORCE RLS + tenant_isolation on every app.*
                -- table of the module (analysers, analyser_flags, analyser_test_mappings, analyser_value_conversions,
                -- analyser_sample_codes, site_agents, pcr_kits, pcr_kit_genes, site_agent_states, analyser_connection_states,
                -- agent_log_entries, analyser_result_intake) come from scripts/add-rls-to-migration.mjs.
                -- identity.machine_credentials has no RLS (like identity.refresh_sessions: read by client id before a tenant is known);
                -- lis_app already holds SELECT, INSERT, UPDATE on the identity schema through its default privileges (schema.sql).

                -- 1. The platform library of analyser models (SA-15) is read by tenants (CP-58 model picker): SELECT only, when present.
                DO $$
                BEGIN
                    IF to_regclass('saas.analyser_models') IS NOT NULL THEN
                        GRANT USAGE ON SCHEMA saas TO lis_app;
                        GRANT SELECT ON saas.analyser_models TO lis_app;
                    END IF;
                END
                $$;

                -- 2. Append-only logs: the agent log and the result intake are evidence (WF-07 "everything pending is traced"); nobody,
                --    the owner included, updates or deletes a row. Retention purges, when built, run as a dedicated maintenance role.
                CREATE OR REPLACE FUNCTION app.integration_refuse_change() RETURNS trigger
                    LANGUAGE plpgsql AS
                $$
                BEGIN
                    RAISE EXCEPTION 'append-only table %.%: % refused', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '42501';
                END
                $$;

                DO $$
                BEGIN
                    IF to_regclass('app.agent_log_entries') IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_agent_log_entries_append_only' AND tgrelid = 'app.agent_log_entries'::regclass) THEN
                        CREATE TRIGGER trg_agent_log_entries_append_only BEFORE UPDATE OR DELETE ON app.agent_log_entries
                            FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
                    END IF;

                    IF to_regclass('app.analyser_result_intake') IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_analyser_result_intake_append_only' AND tgrelid = 'app.analyser_result_intake'::regclass) THEN
                        CREATE TRIGGER trg_analyser_result_intake_append_only BEFORE UPDATE OR DELETE ON app.analyser_result_intake
                            FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
                    END IF;
                END
                $$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Organization/Organization.migration.sql
            migrationBuilder.Sql("""
                -- Organization module (intake LIS-INT-ORG-001), appended by the integrator to the wave-1 migration after the tables
                -- app.branches, app.roles (extended) and app.role_result_rights exist. Idempotent. RLS for the new tables comes from
                -- scripts/add-rls-to-migration.mjs; nothing here touches app.bypass_rls.

                -- INV-ORG-006: role names are unique per tenant, English form, case-insensitive (EF cannot express the expression index).
                CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_tenant_name_en ON app.roles (tenant_id, lower(name_en));

                -- INV-ORG-009: the discount cap of a role or of a user's override is empty (tenant default) or 0..100.
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_roles_special_discount_cap_percent_range') THEN
                        ALTER TABLE app.roles
                            ADD CONSTRAINT ck_roles_special_discount_cap_percent_range
                            CHECK (special_discount_cap_percent IS NULL OR special_discount_cap_percent BETWEEN 0 AND 100);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_memberships_override_discount_cap_percent_range') THEN
                        ALTER TABLE app.tenant_memberships
                            ADD CONSTRAINT ck_tenant_memberships_override_discount_cap_percent_range
                            CHECK (override_discount_cap_percent IS NULL OR override_discount_cap_percent BETWEEN 0 AND 100);
                    END IF;
                    -- INV-ORG-001: branch codes are stored upper-case.
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_branches_code_upper') THEN
                        ALTER TABLE app.branches ADD CONSTRAINT ck_branches_code_upper CHECK (code = upper(code));
                    END IF;
                END
                $$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Patients/Patients.migration.sql
            migrationBuilder.Sql("""
                -- Patients (wave 1, intake LIS-INT-PAT-001): extra SQL the integrator appends to the wave migration after
                -- app.patients and app.patient_documents exist (RLS comes from scripts/add-rls-to-migration.mjs).
                -- Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS.

                -- INV-PAT-001 / INV-PAT-006 / BR-15: a patient file is never deleted, its number never changes, and a Merged file is
                -- frozen (the only way out of Active is the merge, once). Enforced for every role, behind the aggregate's back too.
                CREATE OR REPLACE FUNCTION app.patients_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'patient files are never deleted (patient %)', OLD.number;
                    END IF;

                    IF OLD.status = 'Merged' THEN
                        RAISE EXCEPTION 'a merged patient file is frozen (patient %)', OLD.number;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.number IS DISTINCT FROM OLD.number
                       OR NEW.registered_on IS DISTINCT FROM OLD.registered_on THEN
                        RAISE EXCEPTION 'the identity of a patient file never changes (patient %)', OLD.number;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                DROP TRIGGER IF EXISTS patients_guard ON app.patients;
                CREATE TRIGGER patients_guard BEFORE UPDATE OR DELETE ON app.patients FOR EACH ROW EXECUTE FUNCTION app.patients_guard();

                -- INV-PAT-008 / INV-PAT-009 / BR-15: a document is never deleted, its content record never changes, and the only
                -- change is its deactivation (once, with actor, time and reason).
                CREATE OR REPLACE FUNCTION app.patient_documents_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'patient documents are never deleted (document %)', OLD.id;
                    END IF;

                    IF NOT OLD.is_active THEN
                        RAISE EXCEPTION 'a deactivated patient document is frozen (document %)', OLD.id;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.patient_id IS DISTINCT FROM OLD.patient_id
                       OR NEW.file_name IS DISTINCT FROM OLD.file_name
                       OR NEW.content_type IS DISTINCT FROM OLD.content_type
                       OR NEW.size_bytes IS DISTINCT FROM OLD.size_bytes
                       OR NEW.storage_key IS DISTINCT FROM OLD.storage_key
                       OR NEW.sha256 IS DISTINCT FROM OLD.sha256
                       OR NEW.note IS DISTINCT FROM OLD.note
                       OR NEW.uploaded_by_user_id IS DISTINCT FROM OLD.uploaded_by_user_id
                       OR NEW.uploaded_at_utc IS DISTINCT FROM OLD.uploaded_at_utc THEN
                        RAISE EXCEPTION 'a patient document changes only by its deactivation (document %)', OLD.id;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                DROP TRIGGER IF EXISTS patient_documents_guard ON app.patient_documents;
                CREATE TRIGGER patient_documents_guard BEFORE UPDATE OR DELETE ON app.patient_documents FOR EACH ROW EXECUTE FUNCTION app.patient_documents_guard();
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/PlatformAdmin/PlatformAdmin.migration.sql
            migrationBuilder.Sql("""
                -- Platform administration (intake LIS-INT-PLT-001): grants, the indexes EF cannot express, the cross-tenant count
                -- function and the idempotent seed. Appended by the integrator to the wave migration after the saas tables exist;
                -- plain SQL, safe to run twice (IF NOT EXISTS, CREATE OR REPLACE, ON CONFLICT DO NOTHING on natural keys).

                -- 1. Grants: lis_app has no default privileges in saas (deploy/postgres/schema.sql).
                GRANT USAGE ON SCHEMA saas TO lis_app;
                GRANT SELECT, INSERT, UPDATE ON
                    saas.tenants, saas.plans, saas.wallets, saas.wallet_transactions, saas.wallet_top_up_requests,
                    saas.notification_templates, saas.analyser_models, saas.pcr_kit_templates, saas.code_systems, saas.code_system_items,
                    saas.rule_templates, saas.subscription_requests
                    TO lis_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON saas.countries, saas.governorates TO lis_app;   -- SA-10: an unreferenced country may be deleted

                -- 2. INV-PLT-011: a governorate's English name is unique within its country (case-insensitive).
                CREATE UNIQUE INDEX IF NOT EXISTS ux_governorates_country_name ON saas.governorates (country_id, lower(name_en));

                -- 3. SA-04 branch and user counts across tenants without elevation: SECURITY DEFINER (owner lis_owner, BYPASSRLS),
                --    counts only, and only for a session whose tenant is the platform tenant.
                CREATE OR REPLACE FUNCTION saas.client_usage_counts(p_tenant_ids uuid[])
                RETURNS TABLE (tenant_id uuid, branches bigint, users bigint)
                LANGUAGE plpgsql
                STABLE
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $fn$
                #variable_conflict use_column
                BEGIN
                    IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
                        RAISE EXCEPTION 'saas.client_usage_counts is reserved to the platform tenant' USING ERRCODE = '42501';
                    END IF;

                    IF to_regclass('app.branches') IS NULL THEN
                        RETURN QUERY
                            SELECT t.id,
                                   0::bigint,
                                   (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
                            FROM unnest(p_tenant_ids) AS t(id);
                    ELSE
                        RETURN QUERY EXECUTE
                            'SELECT t.id,
                                    (SELECT count(*) FROM app.branches b WHERE b.tenant_id = t.id),
                                    (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
                             FROM unnest($1) AS t(id)'
                            USING p_tenant_ids;
                    END IF;
                END
                $fn$;
                REVOKE ALL ON FUNCTION saas.client_usage_counts(uuid[]) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION saas.client_usage_counts(uuid[]) TO lis_app;

                -- 4. Seed: countries (SA-10), the governorates of Egypt (SA-11), plans (SA-05), notification templates (SA-20).
                INSERT INTO saas.countries (id, name_en, name_ar, iso_code, dial_code, currency, currency_code, language, time_zone, mobile_length, flag, remarks, is_active, created_at_utc, created_by_display_name)
                VALUES
                    (md5('lis:country:EG')::uuid, 'Egypt', 'مصر', 'EG', '+20', 'Egyptian Pound', 'EGP', 'ar', 'Africa/Cairo', 10, '🇪🇬', NULL, true, now(), 'seed'),
                    (md5('lis:country:SA')::uuid, 'Saudi Arabia', 'المملكة العربية السعودية', 'SA', '+966', 'Saudi Riyal', 'SAR', 'ar', 'Asia/Riyadh', 9, '🇸🇦', NULL, true, now(), 'seed'),
                    (md5('lis:country:AE')::uuid, 'United Arab Emirates', 'الإمارات العربية المتحدة', 'AE', '+971', 'UAE Dirham', 'AED', 'ar', 'Asia/Dubai', 9, '🇦🇪', NULL, true, now(), 'seed'),
                    (md5('lis:country:KW')::uuid, 'Kuwait', 'الكويت', 'KW', '+965', 'Kuwaiti Dinar', 'KWD', 'ar', 'Asia/Kuwait', 8, '🇰🇼', NULL, true, now(), 'seed'),
                    (md5('lis:country:JO')::uuid, 'Jordan', 'الأردن', 'JO', '+962', 'Jordanian Dinar', 'JOD', 'ar', 'Asia/Amman', 9, '🇯🇴', NULL, true, now(), 'seed')
                ON CONFLICT (iso_code) DO NOTHING;

                INSERT INTO saas.governorates (id, country_id, name_en, name_ar, district, remarks, is_active, created_at_utc, created_by_display_name)
                SELECT md5('lis:governorate:EG:' || g.name_en)::uuid, c.id, g.name_en, g.name_ar, NULL, NULL, true, now(), 'seed'
                FROM saas.countries c
                CROSS JOIN (VALUES
                    ('Cairo', 'القاهرة'), ('Giza', 'الجيزة'), ('Alexandria', 'الإسكندرية'), ('Qalyubia', 'القليوبية'), ('Sharqia', 'الشرقية'),
                    ('Dakahlia', 'الدقهلية'), ('Gharbia', 'الغربية'), ('Monufia', 'المنوفية'), ('Beheira', 'البحيرة'), ('Kafr El Sheikh', 'كفر الشيخ'),
                    ('Damietta', 'دمياط'), ('Port Said', 'بورسعيد'), ('Ismailia', 'الإسماعيلية'), ('Suez', 'السويس'), ('North Sinai', 'شمال سيناء'),
                    ('South Sinai', 'جنوب سيناء'), ('Faiyum', 'الفيوم'), ('Beni Suef', 'بني سويف'), ('Minya', 'المنيا'), ('Asyut', 'أسيوط'),
                    ('Sohag', 'سوهاج'), ('Qena', 'قنا'), ('Luxor', 'الأقصر'), ('Aswan', 'أسوان'), ('Red Sea', 'البحر الأحمر'),
                    ('New Valley', 'الوادي الجديد'), ('Matrouh', 'مطروح')
                ) AS g(name_en, name_ar)
                WHERE c.iso_code = 'EG'
                ON CONFLICT (country_id, lower(name_en)) DO NOTHING;

                INSERT INTO saas.plans (id, code, name_en, name_ar, monthly_fee, currency_code, max_branches, max_users, sms_quota, max_analysers, is_active, created_at_utc, created_by_display_name)
                VALUES
                    (md5('lis:plan:STARTER')::uuid, 'STARTER', 'Starter', 'الباقة الأساسية', 1500.00, 'EGP', 1, 5, 500, 1, true, now(), 'seed'),
                    (md5('lis:plan:STANDARD')::uuid, 'STANDARD', 'Standard', 'الباقة القياسية', 3500.00, 'EGP', 3, 20, 2000, 5, true, now(), 'seed'),
                    (md5('lis:plan:ENTERPRISE')::uuid, 'ENTERPRISE', 'Enterprise', 'باقة المؤسسات', 9000.00, 'EGP', 20, 200, 10000, 50, true, now(), 'seed')
                ON CONFLICT (code) DO NOTHING;

                INSERT INTO saas.notification_templates (id, code, channel, subject_en, subject_ar, text_en, text_ar, is_active, created_at_utc, created_by_display_name)
                VALUES
                    (md5('lis:template:otp')::uuid, 'otp', 'Sms', NULL, NULL,
                     'Your Sky LIS verification code is {code}. It is valid for {minutes} minutes. Never share it.',
                     'رمز التحقق الخاص بك في Sky LIS هو {code}. صالح لمدة {minutes} دقائق. لا تشاركه مع أحد.', true, now(), 'seed'),
                    (md5('lis:template:password-recovery')::uuid, 'password-recovery', 'Sms', NULL, NULL,
                     'Use {code} to reset your Sky LIS password. The code expires in {minutes} minutes. Ignore this message if you did not ask for it.',
                     'استخدم الرمز {code} لإعادة تعيين كلمة مرور Sky LIS. ينتهي الرمز خلال {minutes} دقائق. تجاهل هذه الرسالة إن لم تطلبها.', true, now(), 'seed'),
                    (md5('lis:template:portal-url')::uuid, 'portal-url', 'Sms', NULL, NULL,
                     '{lab}: your Sky LIS portal is ready at {url}. Sign in with the username {username}.',
                     '{lab}: بوابة Sky LIS الخاصة بكم جاهزة على {url}. سجّلوا الدخول باسم المستخدم {username}.', true, now(), 'seed'),
                    (md5('lis:template:treasury-request')::uuid, 'treasury-request', 'Sms', NULL, NULL,
                     'Treasury access request {number}: {requester} asks for access to treasury {treasury}.',
                     'طلب صلاحية الخزينة {number}: يطلب {requester} صلاحية على الخزينة {treasury}.', true, now(), 'seed'),
                    (md5('lis:template:panic-value')::uuid, 'panic-value', 'Sms', NULL, NULL,
                     'Panic value for {patient} (accession {acc}): {test} = {value}. Please act now.',
                     'قيمة حرجة للمريض {patient} (رقم الطلب {acc}): {test} = {value}. يرجى التصرف فورًا.', true, now(), 'seed')
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_log_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_connection_states",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_flags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_result_intake",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_sample_codes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_value_conversions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "antibiotics",
                schema: "app");

            migrationBuilder.DropTable(
                name: "base_price_lines",
                schema: "app");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "app");

            migrationBuilder.DropTable(
                name: "cash_closings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "code_system_items",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "comment_templates",
                schema: "app");

            migrationBuilder.DropTable(
                name: "contract_prices",
                schema: "app");

            migrationBuilder.DropTable(
                name: "contract_pricing_sources",
                schema: "app");

            migrationBuilder.DropTable(
                name: "diagnoses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "organisms",
                schema: "app");

            migrationBuilder.DropTable(
                name: "patient_documents",
                schema: "app");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "pcr_kit_genes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "pcr_kit_templates",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "preparations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "reasons",
                schema: "app");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "app");

            migrationBuilder.DropTable(
                name: "report_templates",
                schema: "app");

            migrationBuilder.DropTable(
                name: "role_result_rights",
                schema: "app");

            migrationBuilder.DropTable(
                name: "rule_templates",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "sample_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sending_labs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sensitivity_values",
                schema: "app");

            migrationBuilder.DropTable(
                name: "service_members",
                schema: "app");

            migrationBuilder.DropTable(
                name: "shortcut_sets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "site_agent_states",
                schema: "app");

            migrationBuilder.DropTable(
                name: "site_agents",
                schema: "app");

            migrationBuilder.DropTable(
                name: "subscription_requests",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "support_ticket_replies",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "test_list_values",
                schema: "app");

            migrationBuilder.DropTable(
                name: "test_paragraphs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "test_range_bands",
                schema: "app");

            migrationBuilder.DropTable(
                name: "test_rules",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wallet_top_up_requests",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "wallet_transactions",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "analyser_test_mappings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "commercial_names",
                schema: "app");

            migrationBuilder.DropTable(
                name: "code_systems",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "base_price_lists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "contract_pricings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "patients",
                schema: "app");

            migrationBuilder.DropTable(
                name: "pcr_kits",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analyser_models",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "visit_accounts",
                schema: "app");

            migrationBuilder.DropTable(
                name: "samples",
                schema: "app");

            migrationBuilder.DropTable(
                name: "doctors",
                schema: "app");

            migrationBuilder.DropTable(
                name: "services",
                schema: "app");

            migrationBuilder.DropTable(
                name: "machine_credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "support_tickets",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "patient_conditions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tests",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "drug_types",
                schema: "app");

            migrationBuilder.DropTable(
                name: "contract_tiers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "analysers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "clinical_groups",
                schema: "app");

            migrationBuilder.DropTable(
                name: "measurement_units",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sample_conditions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "payers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sample_types",
                schema: "app");

            migrationBuilder.DropTable(
                name: "governorates",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "countries",
                schema: "saas");

            migrationBuilder.DropIndex(
                name: "ux_tenant_memberships_primary_administrator",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "added_permission_keys",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "designation",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "is_primary_administrator",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_cancel",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_modify_verified_result",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_pay",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_preview_without_verify",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_print_invoice",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_refund",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_see_vip",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_can_transfer",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "override_discount_cap_percent",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "removed_permission_keys",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "shifts",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "signature_file_id",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_cancel",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_modify_verified_result",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_pay",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_preview_without_verify",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_print_invoice",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_refund",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_see_vip",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_can_transfer",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "special_discount_cap_percent",
                schema: "app",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                schema: "identity",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "password_expires_at_utc",
                schema: "identity",
                table: "platform_users");
        }
    }
}
