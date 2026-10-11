using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Validation 2026-10-11, D-01: the append-only and never-deleted tables refuse UPDATE/DELETE row by row, but TRUNCATE fires
    /// no row trigger, so the owner role could still empty them (and an emptied hash chain has nothing left to break). Every
    /// table that refuses row deletion now also refuses TRUNCATE with a statement-level trigger, for every role. Gates:
    /// MigrationScriptTests.Every_table_that_refuses_row_deletion_also_refuses_truncate (sources) and AppendOnlyTruncateTests
    /// (real PostgreSQL). The test fixtures disable these triggers explicitly around their reset.
    /// </summary>
    public partial class AppendOnlyTruncateGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.refuse_truncate() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'rows of %.% are append-only or never deleted: TRUNCATE is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME
                        USING ERRCODE = '42501';
                END
                $fn$;
                CREATE TRIGGER audit_trail_no_truncate BEFORE TRUNCATE ON audit.audit_trail FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER field_changes_no_truncate BEFORE TRUNCATE ON audit.field_changes FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER security_events_no_truncate BEFORE TRUNCATE ON audit.security_events FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER payments_no_truncate BEFORE TRUNCATE ON app.payments FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER refunds_no_truncate BEFORE TRUNCATE ON app.refunds FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER cash_closings_no_truncate BEFORE TRUNCATE ON app.cash_closings FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER agent_log_entries_no_truncate BEFORE TRUNCATE ON app.agent_log_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER analyser_result_intake_no_truncate BEFORE TRUNCATE ON app.analyser_result_intake FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER patients_no_truncate BEFORE TRUNCATE ON app.patients FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER patient_documents_no_truncate BEFORE TRUNCATE ON app.patient_documents FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER visits_no_truncate BEFORE TRUNCATE ON app.visits FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER visit_lines_no_truncate BEFORE TRUNCATE ON app.visit_lines FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER visit_tubes_no_truncate BEFORE TRUNCATE ON app.visit_tubes FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER visit_preparation_answers_no_truncate BEFORE TRUNCATE ON app.visit_preparation_answers FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER visit_transfers_no_truncate BEFORE TRUNCATE ON app.visit_transfers FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER bookings_no_truncate BEFORE TRUNCATE ON app.bookings FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER batches_no_truncate BEFORE TRUNCATE ON app.batches FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER results_no_truncate BEFORE TRUNCATE ON app.results FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                CREATE TRIGGER result_log_entries_no_truncate BEFORE TRUNCATE ON app.result_log_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_trail_no_truncate ON audit.audit_trail;
                DROP TRIGGER IF EXISTS field_changes_no_truncate ON audit.field_changes;
                DROP TRIGGER IF EXISTS security_events_no_truncate ON audit.security_events;
                DROP TRIGGER IF EXISTS payments_no_truncate ON app.payments;
                DROP TRIGGER IF EXISTS refunds_no_truncate ON app.refunds;
                DROP TRIGGER IF EXISTS cash_closings_no_truncate ON app.cash_closings;
                DROP TRIGGER IF EXISTS agent_log_entries_no_truncate ON app.agent_log_entries;
                DROP TRIGGER IF EXISTS analyser_result_intake_no_truncate ON app.analyser_result_intake;
                DROP TRIGGER IF EXISTS patients_no_truncate ON app.patients;
                DROP TRIGGER IF EXISTS patient_documents_no_truncate ON app.patient_documents;
                DROP TRIGGER IF EXISTS visits_no_truncate ON app.visits;
                DROP TRIGGER IF EXISTS visit_lines_no_truncate ON app.visit_lines;
                DROP TRIGGER IF EXISTS visit_tubes_no_truncate ON app.visit_tubes;
                DROP TRIGGER IF EXISTS visit_preparation_answers_no_truncate ON app.visit_preparation_answers;
                DROP TRIGGER IF EXISTS visit_transfers_no_truncate ON app.visit_transfers;
                DROP TRIGGER IF EXISTS bookings_no_truncate ON app.bookings;
                DROP TRIGGER IF EXISTS batches_no_truncate ON app.batches;
                DROP TRIGGER IF EXISTS results_no_truncate ON app.results;
                DROP TRIGGER IF EXISTS result_log_entries_no_truncate ON app.result_log_entries;
                DROP FUNCTION IF EXISTS audit.refuse_truncate();
                """);
        }
    }
}
