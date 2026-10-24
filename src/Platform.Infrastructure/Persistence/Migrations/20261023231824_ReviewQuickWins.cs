using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewQuickWins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "for_culture",
                schema: "app",
                table: "sample_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // module-sql: src/Platform.Infrastructure/Persistence/Catalogue/CatalogueA.migration.sql
            migrationBuilder.Sql("""
                -- ---------------------------------------------------------------------------------------------------------------------
                -- 2026-10-24 Culture specimens (change request LIS-CR-007, P-MIC, reviewer point C-11): app.sample_types.for_culture
                -- marks the sample types used by cultures (Microbiology › Culture specimens, the culture service's sample-type picker).
                -- Appended to the ReviewQuickWins migration after EF added the column (NOT NULL DEFAULT false). The existing culture
                -- specimens of every tenant — the platform's global templates included, so new laboratories copy the flag — are the
                -- types whose English name names one: swab, culture, urine, stool, sputum, pus, CSF, wound. Only sets the flag (never
                -- clears one), so a rerun changes nothing. Idempotent.
                -- ---------------------------------------------------------------------------------------------------------------------
                UPDATE app.sample_types
                SET for_culture = true
                WHERE NOT for_culture
                  AND name_en ILIKE ANY (ARRAY['%swab%', '%culture%', '%urine%', '%stool%', '%sputum%', '%pus%', '%csf%', '%wound%']);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "for_culture",
                schema: "app",
                table: "sample_types");
        }
    }
}
