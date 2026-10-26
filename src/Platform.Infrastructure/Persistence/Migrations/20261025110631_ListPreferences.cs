using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ListPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "list_preferences",
                schema: "app",
                table: "tenant_memberships",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "list_preferences",
                schema: "app",
                table: "tenant_memberships");
        }
    }
}
