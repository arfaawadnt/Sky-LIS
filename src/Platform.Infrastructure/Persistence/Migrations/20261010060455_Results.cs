using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Results : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "results",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accession = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_hidden_helper = table.Column<bool>(type: "boolean", nullable: false),
                    tube_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_unit_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    urgent = table.Column<bool>(type: "boolean", nullable: false),
                    service_sequence = table.Column<int>(type: "integer", nullable: false),
                    member_order = table.Column<int>(type: "integer", nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    text_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    formatted_report = table.Column<string>(type: "jsonb", nullable: true),
                    culture_report = table.Column<string>(type: "jsonb", nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decimals = table.Column<int>(type: "integer", nullable: false),
                    range_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_panic_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_panic_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_reportable_low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_reportable_high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    range_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    flag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_panic = table.Column<bool>(type: "boolean", nullable: false),
                    delta_flag = table.Column<bool>(type: "boolean", nullable: false),
                    delta_percent = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    previous_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    previous_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delta_acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delta_acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delta_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analyser_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    analyser_flags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    measured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    printed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_printed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_printed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false),
                    last_print_list_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amendment_count = table.Column<int>(type: "integer", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    report_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_results", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_results_flag_domain", "\"flag\" IN ('Normal', 'Low', 'High', 'CriticalLow', 'CriticalHigh', 'Abnormal')");
                    table.CheckConstraint("ck_results_source_domain", "\"source\" IN ('Manual', 'Analyser')");
                    table.CheckConstraint("ck_results_status_domain", "\"status\" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')");
                    table.CheckConstraint("ck_results_test_type_domain", "\"test_type\" IN ('Numeric', 'NonNumeric', 'OptionalList', 'RestrictedList', 'Formula', 'FormattedText', 'Culture')");
                });

            migrationBuilder.CreateTable(
                name: "result_log_entries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    from_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    value_before = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    value_after = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    analyser_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_result_log_entries", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_result_log_entries_from_status_domain", "\"from_status\" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')");
                    table.CheckConstraint("ck_result_log_entries_kind_domain", "\"kind\" IN ('Ordered', 'Value', 'Status', 'DeltaAcknowledged', 'Comment', 'Note', 'PatientMoved')");
                    table.CheckConstraint("ck_result_log_entries_source_domain", "\"source\" IN ('Manual', 'Analyser')");
                    table.CheckConstraint("ck_result_log_entries_to_status_domain", "\"to_status\" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_result_log_entries_results_tenant_id_result_id",
                        columns: x => new { x.tenant_id, x.result_id },
                        principalSchema: "app",
                        principalTable: "results",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_result_log_entries_tenant_id_result_id_at_utc",
                schema: "app",
                table: "result_log_entries",
                columns: new[] { "tenant_id", "result_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_accession",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "accession" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_clinical_group_id_status",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "clinical_group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_line_id_test_id",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "line_id", "test_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_patient_id_test_id",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "patient_id", "test_id" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_registered_at_utc",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "registered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_report_unit_key",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "report_unit_key" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_status_expected_at_utc",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "status", "expected_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_tube_id",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "tube_id" });

            migrationBuilder.CreateIndex(
                name: "ix_results_tenant_id_visit_id",
                schema: "app",
                table: "results",
                columns: new[] { "tenant_id", "visit_id" });

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.results ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.results FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.results
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.result_log_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.result_log_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.result_log_entries
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Results/Results.migration.sql
            migrationBuilder.Sql("""
                -- Results (wave 3, intake LIS-INT-RES-001): extra SQL the integrator appends to the wave migration after the tables
                -- app.results and app.result_log_entries exist. Idempotent: CREATE OR REPLACE, guarded trigger creation.

                -- INV-RES-010 / BR-15: the result log is evidence (LDM §5.3 RL / SL): nobody, the owner included, updates or deletes a
                -- row; and a result is never deleted (a cancelled line cancels its results). Retention purges, when built, run as a
                -- dedicated maintenance role.
                CREATE OR REPLACE FUNCTION app.results_refuse_change() RETURNS trigger
                    LANGUAGE plpgsql AS
                $$
                BEGIN
                    RAISE EXCEPTION 'results are never erased: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = '42501';
                END
                $$;

                DO $$
                BEGIN
                    IF to_regclass('app.result_log_entries') IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_result_log_entries_append_only' AND tgrelid = 'app.result_log_entries'::regclass) THEN
                        CREATE TRIGGER trg_result_log_entries_append_only BEFORE UPDATE OR DELETE ON app.result_log_entries
                            FOR EACH ROW EXECUTE FUNCTION app.results_refuse_change();
                    END IF;

                    IF to_regclass('app.results') IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_results_never_deleted' AND tgrelid = 'app.results'::regclass) THEN
                        CREATE TRIGGER trg_results_never_deleted BEFORE DELETE ON app.results
                            FOR EACH ROW EXECUTE FUNCTION app.results_refuse_change();
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "result_log_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "results",
                schema: "app");
        }
    }
}
