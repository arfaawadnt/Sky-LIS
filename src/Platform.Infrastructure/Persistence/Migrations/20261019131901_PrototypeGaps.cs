using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrototypeGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_reference_for_non_cash",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_reference_for_non_cash",
                schema: "app",
                table: "payments");

            migrationBuilder.AddColumn<string>(
                name: "method_code",
                schema: "app",
                table: "refunds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "method_code",
                schema: "app",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "app",
                table: "payers",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone1",
                schema: "app",
                table: "payers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone2",
                schema: "app",
                table: "payers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp",
                schema: "app",
                table: "payers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                schema: "app",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                schema: "app",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                schema: "app",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "second_phone",
                schema: "app",
                table: "patients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cities",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_cities", x => x.id);
                    table.ForeignKey(
                        name: "fk_cities_governorate_governorate_id",
                        column: x => x.governorate_id,
                        principalSchema: "saas",
                        principalTable: "governorates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requires_reference = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_payment_methods", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payment_methods_kind_domain", "\"kind\" IN ('Cash', 'Card', 'Pos', 'Transfer')");
                    table.CheckConstraint("ck_payment_methods_sequence", "sequence >= 0 AND sequence <= 9999");
                });

            migrationBuilder.CreateTable(
                name: "districts",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_districts", x => x.id);
                    table.ForeignKey(
                        name: "fk_districts_cities_city_id",
                        column: x => x.city_id,
                        principalSchema: "saas",
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "areas",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_areas_district_district_id",
                        column: x => x.district_id,
                        principalSchema: "saas",
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_reference_for_non_cash",
                schema: "app",
                table: "refunds",
                sql: "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_reference_for_non_cash",
                schema: "app",
                table: "payments",
                sql: "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_second_phone",
                schema: "app",
                table: "patients",
                columns: new[] { "tenant_id", "second_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_areas_district_id",
                schema: "saas",
                table: "areas",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_cities_governorate_id",
                schema: "saas",
                table: "cities",
                column: "governorate_id");

            migrationBuilder.CreateIndex(
                name: "ix_districts_city_id",
                schema: "saas",
                table: "districts",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_tenant_id_code",
                schema: "app",
                table: "payment_methods",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_tenant_id_is_active_sequence",
                schema: "app",
                table: "payment_methods",
                columns: new[] { "tenant_id", "is_active", "sequence" });

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.payment_methods ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.payment_methods FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.payment_methods
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Billing/Billing.migration.sql
            migrationBuilder.Sql("""
                -- ---------------------------------------------------------------------------------------------------------------------
                -- 2026-10-19 Payment methods (change request LIS-CR-003, P-C10): the set-up list app.payment_methods, method_code on
                -- payments and refunds. Appended to the PrototypeGaps migration after EF created the table and the columns. Idempotent.
                -- ---------------------------------------------------------------------------------------------------------------------

                -- (1) Every existing client starts with the four default methods (CASH, CARD, POS, TRANSFER); the platform tenant takes
                --     none and a tenant that already defined a code keeps its own row (ON CONFLICT DO NOTHING on (tenant_id, code)).
                INSERT INTO app.payment_methods (tenant_id, id, code, name_en, name_ar, kind, requires_reference, is_active, sequence, created_at_utc)
                SELECT t.id, gen_random_uuid(), d.code, d.name_en, d.name_ar, d.kind, d.requires_reference, true, d.sequence, now()
                FROM saas.tenants t
                CROSS JOIN (VALUES
                    ('CASH', 'Cash', 'نقدي', 'Cash', false, 1),
                    ('CARD', 'Card', 'بطاقة', 'Card', true, 2),
                    ('POS', 'POS', 'نقطة بيع', 'Pos', true, 3),
                    ('TRANSFER', 'Bank transfer', 'تحويل بنكي', 'Transfer', true, 4)) AS d (code, name_en, name_ar, kind, requires_reference, sequence)
                WHERE t.id <> '00000000-0000-0000-0000-000000000001'
                ON CONFLICT (tenant_id, code) DO NOTHING;

                -- (2) The payment guard keeps method_code immutable like the other posting facts (a payment changes only by its void).
                CREATE OR REPLACE FUNCTION app.billing_guard_payment() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'payments are never deleted (receipt %)', OLD.receipt_number;
                    END IF;

                    IF OLD.status = 'Voided' THEN
                        RAISE EXCEPTION 'a voided payment is frozen (receipt %)', OLD.receipt_number;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
                       OR NEW.receipt_number IS DISTINCT FROM OLD.receipt_number
                       OR NEW.amount IS DISTINCT FROM OLD.amount
                       OR NEW.method IS DISTINCT FROM OLD.method
                       OR NEW.method_code IS DISTINCT FROM OLD.method_code
                       OR NEW.reference IS DISTINCT FROM OLD.reference
                       OR NEW.treasury_id IS DISTINCT FROM OLD.treasury_id
                       OR NEW.device_id IS DISTINCT FROM OLD.device_id
                       OR NEW.cashier_user_id IS DISTINCT FROM OLD.cashier_user_id
                       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
                       OR NEW.batch_id IS DISTINCT FROM OLD.batch_id
                       OR NEW.paid_at_utc IS DISTINCT FROM OLD.paid_at_utc THEN
                        RAISE EXCEPTION 'a payment changes only by its void (receipt %)', OLD.receipt_number;
                    END IF;

                    RETURN NEW;
                END
                $fn$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/PlatformAdmin/PlatformAdmin.migration.sql
            migrationBuilder.Sql("""
                -- 5. Change request 2026-10-19 (LIS-CR-003 A2, migration PrototypeGaps): the location hierarchy under a governorate.
                --    Grants for the runtime role on the three new saas tables (no deletes: rows are deactivated) and the per-parent
                --    unique English names (case-insensitive, like ux_governorates_country_name). No seed rows: the business owner
                --    enters the cities of each governorate (intake LIS-INT-PLT-001 §17). Idempotent.
                GRANT SELECT, INSERT, UPDATE ON saas.cities, saas.districts, saas.areas TO lis_app;
                CREATE UNIQUE INDEX IF NOT EXISTS ux_cities_governorate_name ON saas.cities (governorate_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_districts_city_name ON saas.districts (city_id, lower(name_en));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_areas_district_name ON saas.areas (district_id, lower(name_en));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "areas",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "app");

            migrationBuilder.DropTable(
                name: "districts",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "cities",
                schema: "saas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_reference_for_non_cash",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_reference_for_non_cash",
                schema: "app",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_patients_tenant_id_second_phone",
                schema: "app",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "method_code",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropColumn(
                name: "method_code",
                schema: "app",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "email",
                schema: "app",
                table: "payers");

            migrationBuilder.DropColumn(
                name: "phone1",
                schema: "app",
                table: "payers");

            migrationBuilder.DropColumn(
                name: "phone2",
                schema: "app",
                table: "payers");

            migrationBuilder.DropColumn(
                name: "whatsapp",
                schema: "app",
                table: "payers");

            migrationBuilder.DropColumn(
                name: "area_id",
                schema: "app",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "city_id",
                schema: "app",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "district_id",
                schema: "app",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "second_phone",
                schema: "app",
                table: "patients");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_reference_for_non_cash",
                schema: "app",
                table: "refunds",
                sql: "method = 'Cash' OR reference IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_reference_for_non_cash",
                schema: "app",
                table: "payments",
                sql: "method = 'Cash' OR reference IS NOT NULL");
        }
    }
}
