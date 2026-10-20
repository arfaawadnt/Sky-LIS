using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TreasuryRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cash_closings_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_refunds_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_refunds_tenant_id_treasury_id_refunded_at_utc",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_treasury_id_paid_at_utc",
                schema: "app",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_cash_closings_tenant_id_treasury_id_period_from_utc",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "refunds",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "app",
                table: "refunds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "app",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "cash_closings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_branch_id_refunded_at_utc",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "branch_id", "refunded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_branch_id_paid_at_utc",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_tenant_id_branch_id_period_from_utc",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "branch_id", "period_from_utc" });

            // The treasury tables stay as history (LIS-CR-004): no DropTable here, no CreateTable in Down().
            // The treasury permission group left the catalogue: purge its keys from roles and membership overrides. Idempotent.
            migrationBuilder.Sql("""
                UPDATE app.roles SET permission_keys = ARRAY(SELECT k FROM unnest(permission_keys) k WHERE k NOT LIKE 'treasury.%' ORDER BY k)
                 WHERE EXISTS (SELECT 1 FROM unnest(permission_keys) k WHERE k LIKE 'treasury.%');
                UPDATE app.tenant_memberships SET added_permission_keys = ARRAY(SELECT k FROM unnest(added_permission_keys) k WHERE k NOT LIKE 'treasury.%' ORDER BY k)
                 WHERE EXISTS (SELECT 1 FROM unnest(added_permission_keys) k WHERE k LIKE 'treasury.%');
                UPDATE app.tenant_memberships SET removed_permission_keys = ARRAY(SELECT k FROM unnest(removed_permission_keys) k WHERE k NOT LIKE 'treasury.%' ORDER BY k)
                 WHERE EXISTS (SELECT 1 FROM unnest(removed_permission_keys) k WHERE k LIKE 'treasury.%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refunds_tenant_id_branch_id_refunded_at_utc",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_branch_id_paid_at_utc",
                schema: "app",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_cash_closings_tenant_id_branch_id_period_from_utc",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "refunds",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "app",
                table: "refunds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "app",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "treasury_id",
                schema: "app",
                table: "cash_closings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_tenant_id_treasury_id_refunded_at_utc",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "treasury_id", "refunded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_treasury_id_paid_at_utc",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "treasury_id", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_tenant_id_treasury_id_period_from_utc",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "treasury_id", "period_from_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_cash_closings_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "cash_closings",
                columns: new[] { "tenant_id", "treasury_id" },
                principalSchema: "app",
                principalTable: "treasuries",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "payments",
                columns: new[] { "tenant_id", "treasury_id" },
                principalSchema: "app",
                principalTable: "treasuries",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_treasuries_tenant_id_treasury_id",
                schema: "app",
                table: "refunds",
                columns: new[] { "tenant_id", "treasury_id" },
                principalSchema: "app",
                principalTable: "treasuries",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
