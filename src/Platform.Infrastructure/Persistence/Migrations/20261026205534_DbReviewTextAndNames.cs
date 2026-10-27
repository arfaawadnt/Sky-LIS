using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DbReviewTextAndNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_analyser_value_conversions_analyser_test_mappings_tenant_id",
                schema: "app",
                table: "analyser_value_conversions");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_compatibility_members_sample_compatibility_groups_te",
                schema: "app",
                table: "sample_compatibility_members");

            migrationBuilder.AlterColumn<string>(
                name: "discount_reason",
                schema: "app",
                table: "visits",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "app",
                table: "visit_transfers",
                type: "text",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "visit_lines",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "formula_expression",
                schema: "app",
                table: "tests",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                schema: "app",
                table: "test_paragraphs",
                type: "text",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "list_preferences",
                schema: "app",
                table: "tenant_memberships",
                type: "text",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "saas",
                table: "support_tickets",
                type: "text",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                schema: "saas",
                table: "support_ticket_replies",
                type: "text",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "precaution",
                schema: "app",
                table: "services",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                schema: "audit",
                table: "security_events",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "samples",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "app",
                table: "sample_events",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "formula",
                schema: "saas",
                table: "rule_templates",
                type: "text",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "saas",
                table: "rule_templates",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "report_comment",
                schema: "app",
                table: "results",
                type: "text",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "internal_note",
                schema: "app",
                table: "results",
                type: "text",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "delta_comment",
                schema: "app",
                table: "results",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "results",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "value_before",
                schema: "app",
                table: "result_log_entries",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "value_after",
                schema: "app",
                table: "result_log_entries",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                schema: "app",
                table: "result_log_entries",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "merge_reason",
                schema: "app",
                table: "patients",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "deactivation_reason",
                schema: "app",
                table: "patient_documents",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text_en",
                schema: "saas",
                table: "notification_templates",
                type: "text",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "text_ar",
                schema: "saas",
                table: "notification_templates",
                type: "text",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "audit",
                table: "field_changes",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "display",
                schema: "saas",
                table: "code_system_items",
                type: "text",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "app",
                table: "cash_closings",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                schema: "app",
                table: "bookings",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "bookings",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                schema: "app",
                table: "batches",
                type: "text",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                schema: "app",
                table: "agent_log_entries",
                type: "text",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_value_conversions_mapping",
                schema: "app",
                table: "analyser_value_conversions",
                columns: new[] { "tenant_id", "mapping_id" },
                principalSchema: "app",
                principalTable: "analyser_test_mappings",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_compatibility_members_group",
                schema: "app",
                table: "sample_compatibility_members",
                columns: new[] { "tenant_id", "group_id" },
                principalSchema: "app",
                principalTable: "sample_compatibility_groups",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            // the retired treasury history table is not in the model (LIS-CR-004); its long text columns follow the same rule (DB review comment 2)
            migrationBuilder.Sql("ALTER TABLE app.treasury_access_requests ALTER COLUMN approval_comment TYPE text, ALTER COLUMN decision_reason TYPE text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_analyser_value_conversions_mapping",
                schema: "app",
                table: "analyser_value_conversions");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_compatibility_members_group",
                schema: "app",
                table: "sample_compatibility_members");

            migrationBuilder.AlterColumn<string>(
                name: "discount_reason",
                schema: "app",
                table: "visits",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "app",
                table: "visit_transfers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "visit_lines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "formula_expression",
                schema: "app",
                table: "tests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                schema: "app",
                table: "test_paragraphs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "list_preferences",
                schema: "app",
                table: "tenant_memberships",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "saas",
                table: "support_tickets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                schema: "saas",
                table: "support_ticket_replies",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "precaution",
                schema: "app",
                table: "services",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                schema: "audit",
                table: "security_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "samples",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "app",
                table: "sample_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "formula",
                schema: "saas",
                table: "rule_templates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "saas",
                table: "rule_templates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "report_comment",
                schema: "app",
                table: "results",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "internal_note",
                schema: "app",
                table: "results",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "delta_comment",
                schema: "app",
                table: "results",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "results",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "value_before",
                schema: "app",
                table: "result_log_entries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "value_after",
                schema: "app",
                table: "result_log_entries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                schema: "app",
                table: "result_log_entries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "merge_reason",
                schema: "app",
                table: "patients",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "deactivation_reason",
                schema: "app",
                table: "patient_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text_en",
                schema: "saas",
                table: "notification_templates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "text_ar",
                schema: "saas",
                table: "notification_templates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "audit",
                table: "field_changes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "display",
                schema: "saas",
                table: "code_system_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "app",
                table: "cash_closings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                schema: "app",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "app",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                schema: "app",
                table: "batches",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                schema: "app",
                table: "agent_log_entries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_value_conversions_analyser_test_mappings_tenant_id",
                schema: "app",
                table: "analyser_value_conversions",
                columns: new[] { "tenant_id", "mapping_id" },
                principalSchema: "app",
                principalTable: "analyser_test_mappings",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_compatibility_members_sample_compatibility_groups_te",
                schema: "app",
                table: "sample_compatibility_members",
                columns: new[] { "tenant_id", "group_id" },
                principalSchema: "app",
                principalTable: "sample_compatibility_groups",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("ALTER TABLE app.treasury_access_requests ALTER COLUMN approval_comment TYPE character varying(1000), ALTER COLUMN decision_reason TYPE character varying(1000);");
        }
    }
}
