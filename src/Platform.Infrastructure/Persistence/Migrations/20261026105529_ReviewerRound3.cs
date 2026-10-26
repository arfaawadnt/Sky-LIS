using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewerRound3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_kind_domain",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_member",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_origin_domain",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.AddColumn<bool>(
                name: "visible_on_receipt",
                schema: "app",
                table: "visit_lines",
                type: "boolean",
                nullable: false,
                defaultValue: true);   // every existing line stays on the receipt (LIS-CR-015); the EF model declares no default on purpose (false would be its unset sentinel)

            migrationBuilder.AddColumn<string>(
                name: "formula_operands",
                schema: "app",
                table: "tests",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "merges_into_condition_id",
                schema: "app",
                table: "sample_conditions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_formula_operand",
                schema: "app",
                table: "visit_lines",
                sql: "kind <> 'FormulaOperand' OR (list_price = 0 AND patient_share = 0 AND payer_share = 0 AND discount = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_kind_domain",
                schema: "app",
                table: "visit_lines",
                sql: "\"kind\" IN ('Service', 'Package', 'PackageMember', 'FormulaOperand')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_member",
                schema: "app",
                table: "visit_lines",
                sql: "(kind IN ('PackageMember', 'FormulaOperand')) = (parent_line_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_origin_domain",
                schema: "app",
                table: "visit_lines",
                sql: "\"origin\" IN ('Manual', 'Rule', 'Diagnosis', 'Formula')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_receipt",
                schema: "app",
                table: "visit_lines",
                sql: "visible_on_receipt OR kind = 'FormulaOperand'");

            migrationBuilder.CreateIndex(
                name: "ix_sample_conditions_tenant_id_merges_into_condition_id",
                schema: "app",
                table: "sample_conditions",
                columns: new[] { "tenant_id", "merges_into_condition_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_sample_conditions_merges_into",
                schema: "app",
                table: "sample_conditions",
                columns: new[] { "tenant_id", "merges_into_condition_id" },
                principalSchema: "app",
                principalTable: "sample_conditions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sample_conditions_merges_into",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_formula_operand",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_kind_domain",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_member",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_origin_domain",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_lines_receipt",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropIndex(
                name: "ix_sample_conditions_tenant_id_merges_into_condition_id",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.DropColumn(
                name: "visible_on_receipt",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropColumn(
                name: "formula_operands",
                schema: "app",
                table: "tests");

            migrationBuilder.DropColumn(
                name: "merges_into_condition_id",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_kind_domain",
                schema: "app",
                table: "visit_lines",
                sql: "\"kind\" IN ('Service', 'Package', 'PackageMember')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_member",
                schema: "app",
                table: "visit_lines",
                sql: "(kind = 'PackageMember') = (parent_line_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_lines_origin_domain",
                schema: "app",
                table: "visit_lines",
                sql: "\"origin\" IN ('Manual', 'Rule', 'Diagnosis')");
        }
    }
}
