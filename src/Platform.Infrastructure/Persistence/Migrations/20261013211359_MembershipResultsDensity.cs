using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipResultsDensity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_results_density",
                schema: "app",
                table: "tenant_memberships",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // A string preference, not an enum: the domain CHECK is declared here.
            migrationBuilder.Sql("""
                ALTER TABLE app.tenant_memberships ADD CONSTRAINT ck_tenant_memberships_preferred_results_density_domain
                    CHECK (preferred_results_density IS NULL OR preferred_results_density IN ('comfortable', 'compact'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE app.tenant_memberships DROP CONSTRAINT IF EXISTS ck_tenant_memberships_preferred_results_density_domain;");

            migrationBuilder.DropColumn(
                name: "preferred_results_density",
                schema: "app",
                table: "tenant_memberships");
        }
    }
}
