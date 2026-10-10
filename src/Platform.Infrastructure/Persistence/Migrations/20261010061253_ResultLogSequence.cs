using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultLogSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_result_log_entries_tenant_id_result_id_at_utc",
                schema: "app",
                table: "result_log_entries");

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                schema: "app",
                table: "result_log_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing logs get their positions (1, 2, 3… per result, in recorded order) before the unique index. The log is
            // append-only by trigger (Results.migration.sql), so the trigger is suspended for this one backfill only.
            migrationBuilder.Sql("""
                ALTER TABLE app.result_log_entries DISABLE TRIGGER trg_result_log_entries_append_only;
                UPDATE app.result_log_entries AS e
                SET sequence = numbered.position
                FROM (
                    SELECT tenant_id, id, row_number() OVER (PARTITION BY tenant_id, result_id ORDER BY at_utc, id) AS position
                    FROM app.result_log_entries
                ) AS numbered
                WHERE e.tenant_id = numbered.tenant_id AND e.id = numbered.id;
                ALTER TABLE app.result_log_entries ENABLE TRIGGER trg_result_log_entries_append_only;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_result_log_entries_tenant_id_result_id_sequence",
                schema: "app",
                table: "result_log_entries",
                columns: new[] { "tenant_id", "result_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_result_log_entries_tenant_id_result_id_sequence",
                schema: "app",
                table: "result_log_entries");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "app",
                table: "result_log_entries");

            migrationBuilder.CreateIndex(
                name: "ix_result_log_entries_tenant_id_result_id_at_utc",
                schema: "app",
                table: "result_log_entries",
                columns: new[] { "tenant_id", "result_id", "at_utc" });
        }
    }
}
