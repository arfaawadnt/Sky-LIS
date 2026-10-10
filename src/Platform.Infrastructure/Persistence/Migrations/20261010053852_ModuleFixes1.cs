using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModuleFixes1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sample_events_tenant_id_sample_id_at_utc",
                schema: "app",
                table: "sample_events");

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                schema: "app",
                table: "sample_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing timelines get their step numbers (1, 2, 3… per sample, in the order the steps were recorded) before
            // the unique index; otherwise every existing row would hold 0 and the index could not be built.
            migrationBuilder.Sql("""
                UPDATE app.sample_events AS e
                SET sequence = numbered.step
                FROM (
                    SELECT tenant_id, id, row_number() OVER (PARTITION BY tenant_id, sample_id ORDER BY at_utc, id) AS step
                    FROM app.sample_events
                ) AS numbered
                WHERE e.tenant_id = numbered.tenant_id AND e.id = numbered.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sample_events_tenant_id_sample_id_sequence",
                schema: "app",
                table: "sample_events",
                columns: new[] { "tenant_id", "sample_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sample_events_tenant_id_sample_id_sequence",
                schema: "app",
                table: "sample_events");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "app",
                table: "sample_events");

            migrationBuilder.CreateIndex(
                name: "ix_sample_events_tenant_id_sample_id_at_utc",
                schema: "app",
                table: "sample_events",
                columns: new[] { "tenant_id", "sample_id", "at_utc" });
        }
    }
}
