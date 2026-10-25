using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "at_or_below_zero_since_utc",
                schema: "saas",
                table: "wallets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "collected_outside",
                schema: "app",
                table: "visit_tubes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "collected_outside_at_utc",
                schema: "app",
                table: "visit_tubes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                schema: "saas",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                schema: "saas",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                schema: "saas",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                schema: "saas",
                table: "subscription_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                schema: "saas",
                table: "subscription_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                schema: "saas",
                table: "subscription_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "collected_outside",
                schema: "app",
                table: "samples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "design",
                schema: "app",
                table: "report_templates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "report_design",
                schema: "app",
                table: "contract_pricings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_in",
                schema: "app",
                table: "cash_closings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_out",
                schema: "app",
                table: "cash_closings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "entry_count",
                schema: "app",
                table: "cash_closings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_balance",
                schema: "app",
                table: "cash_closings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                schema: "app",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                schema: "app",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                schema: "app",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body_en = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    body_ar = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    link_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    placement = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    audience_country_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    client_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    image_content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    image_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    image_storage_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    image_version = table.Column<int>(type: "integer", nullable: false),
                    impressions = table.Column<long>(type: "bigint", nullable: false),
                    clicks = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.CheckConstraint("ck_announcements_audience_status_domain", "\"audience_status\" IN ('Any', 'Trial', 'Active')");
                    table.CheckConstraint("ck_announcements_placement_domain", "\"placement\" IN ('SignIn', 'Welcome')");
                });

            migrationBuilder.CreateTable(
                name: "cash_entries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    method_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_cash_entries", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_cash_entries_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_cash_entries_category_domain", "\"category\" IN ('Supplier', 'PettyCash', 'BankDeposit', 'OpeningFloat', 'OtherIncome', 'Other')");
                    table.CheckConstraint("ck_cash_entries_category_of_kind", "(kind = 'CashOut' AND category IN ('Supplier', 'PettyCash', 'BankDeposit', 'Other')) OR (kind = 'CashIn' AND category IN ('OpeningFloat', 'OtherIncome', 'Other'))");
                    table.CheckConstraint("ck_cash_entries_kind_domain", "\"kind\" IN ('CashOut', 'CashIn')");
                    table.CheckConstraint("ck_cash_entries_method_domain", "\"method\" IN ('Cash', 'Card', 'Pos', 'Transfer')");
                    table.CheckConstraint("ck_cash_entries_reference_for_non_cash", "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");
                    table.CheckConstraint("ck_cash_entries_status_domain", "\"status\" IN ('Posted', 'Voided')");
                    table.CheckConstraint("ck_cash_entries_void_consistency", "(status = 'Voided') = (void_reason IS NOT NULL AND voided_at_utc IS NOT NULL AND voided_by_user_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cash_entries_platform_users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_entries_platform_users_voided_by_user_id",
                        column: x => x.voided_by_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_reminders",
                schema: "saas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    due_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    template_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channels = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_subscription_reminders", x => x.id);
                    table.CheckConstraint("ck_subscription_reminders_kind_domain", "\"kind\" IN ('TrialEnding', 'TrialEnded', 'WalletLow', 'WalletAtZero', 'GraceEnding')");
                    table.ForeignKey(
                        name: "fk_subscription_reminders_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "saas",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_area_id",
                schema: "saas",
                table: "tenants",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_city_id",
                schema: "saas",
                table: "tenants",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_district_id",
                schema: "saas",
                table: "tenants",
                column: "district_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_report_templates_design_domain",
                schema: "app",
                table: "report_templates",
                sql: "\"design\" IN ('Classic', 'Modern', 'Compact', 'Letterhead', 'Cumulative')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contract_pricings_report_design_domain",
                schema: "app",
                table: "contract_pricings",
                sql: "\"report_design\" IN ('Classic', 'Modern', 'Compact', 'Letterhead', 'Cumulative')");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_placement_is_active_from_utc",
                schema: "saas",
                table: "announcements",
                columns: new[] { "placement", "is_active", "from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_entries_cashier_user_id",
                schema: "app",
                table: "cash_entries",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_entries_tenant_id_branch_id_posted_at_utc",
                schema: "app",
                table: "cash_entries",
                columns: new[] { "tenant_id", "branch_id", "posted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_entries_tenant_id_cashier_user_id_posted_at_utc",
                schema: "app",
                table: "cash_entries",
                columns: new[] { "tenant_id", "cashier_user_id", "posted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_entries_tenant_id_number",
                schema: "app",
                table: "cash_entries",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_entries_voided_by_user_id",
                schema: "app",
                table: "cash_entries",
                column: "voided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_reminders_tenant_id_due_key",
                schema: "saas",
                table: "subscription_reminders",
                columns: new[] { "tenant_id", "due_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_reminders_tenant_id_sent_at_utc",
                schema: "saas",
                table: "subscription_reminders",
                columns: new[] { "tenant_id", "sent_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_areas_area_id",
                schema: "saas",
                table: "tenants",
                column: "area_id",
                principalSchema: "saas",
                principalTable: "areas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_cities_city_id",
                schema: "saas",
                table: "tenants",
                column: "city_id",
                principalSchema: "saas",
                principalTable: "cities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_districts_district_id",
                schema: "saas",
                table: "tenants",
                column: "district_id",
                principalSchema: "saas",
                principalTable: "districts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.cash_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.cash_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.cash_entries
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);
            // module-sql: src/Platform.Infrastructure/Persistence/Registration/Registration.migration.sql
            migrationBuilder.Sql("""
                -- ---------------------------------------------------------------------------------------------------------------------
                -- 2026-10-24 Collected outside per sample (change request LIS-CR-008, P-SMP, reviewer point C-02). Appended to the
                -- ReviewFeatures migration after EF added app.visit_tubes.collected_outside (boolean, default false),
                -- app.visit_tubes.collected_outside_at_utc (timestamptz, null) and app.samples.collected_outside (boolean, default false).
                -- Idempotent: each UPDATE touches only rows still false.
                -- ---------------------------------------------------------------------------------------------------------------------

                -- (1) The visit-level flag of the old contract meant "every tube collected outside": copy it to every tube of the visits
                --     that carry it. app.visits.collected_outside stays as history (since this change it is true only for a visit
                --     registered with the old visit-level field, which marks every tube, so a rerun changes nothing). The tube guard
                --     (app.visit_tubes_guard) protects the number and key only, so this UPDATE passes it.
                UPDATE app.visit_tubes t
                SET collected_outside = true
                FROM app.visits v
                WHERE v.tenant_id = t.tenant_id
                  AND v.id = t.visit_id
                  AND v.collected_outside
                  AND NOT t.collected_outside;

                -- (2) The first sample of such a tube (generation 0) shows the "Outside" tag and prints "EXT"; a replacement sample was
                --     collected again in the laboratory and stays false. Its status and times are left as they were recorded.
                UPDATE app.samples s
                SET collected_outside = true
                FROM app.visit_tubes t
                WHERE t.tenant_id = s.tenant_id
                  AND t.id = s.tube_id
                  AND t.collected_outside
                  AND s.generation = 0
                  AND NOT s.collected_outside;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Billing/Billing.migration.sql
            migrationBuilder.Sql("""
                -- ---------------------------------------------------------------------------------------------------------------------
                -- 2026-10-24 Cash ledger (change request LIS-CR-011, package P-CSH, point C-15). Appended to the ReviewFeatures migration
                -- after EF created app.cash_entries (with RLS declared in the migration) and added opening_balance, cash_in, cash_out and
                -- entry_count (NOT NULL DEFAULT 0) to app.cash_closings. Idempotent.
                -- ---------------------------------------------------------------------------------------------------------------------

                -- INV-BIL-004 for cash entries: never deleted, never edited; the only change is the void (Posted -> Voided with reason,
                -- voider and time), once.
                CREATE OR REPLACE FUNCTION app.billing_guard_cash_entry() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'cash entries are never deleted (entry %)', OLD.number;
                    END IF;

                    IF OLD.status = 'Voided' THEN
                        RAISE EXCEPTION 'a voided cash entry is frozen (entry %)', OLD.number;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.number IS DISTINCT FROM OLD.number
                       OR NEW.kind IS DISTINCT FROM OLD.kind
                       OR NEW.category IS DISTINCT FROM OLD.category
                       OR NEW.amount IS DISTINCT FROM OLD.amount
                       OR NEW.method IS DISTINCT FROM OLD.method
                       OR NEW.method_code IS DISTINCT FROM OLD.method_code
                       OR NEW.reference IS DISTINCT FROM OLD.reference
                       OR NEW.reason IS DISTINCT FROM OLD.reason
                       OR NEW.note IS DISTINCT FROM OLD.note
                       OR NEW.cashier_user_id IS DISTINCT FROM OLD.cashier_user_id
                       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
                       OR NEW.posted_at_utc IS DISTINCT FROM OLD.posted_at_utc THEN
                        RAISE EXCEPTION 'a cash entry changes only by its void (entry %)', OLD.number;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                DROP TRIGGER IF EXISTS cash_entries_guard ON app.cash_entries;
                CREATE TRIGGER cash_entries_guard BEFORE UPDATE OR DELETE ON app.cash_entries FOR EACH ROW EXECUTE FUNCTION app.billing_guard_cash_entry();
                -- A row trigger never fires on TRUNCATE (validation 2026-10-11 D-01): refuse it with a statement trigger, as app.payments.
                DROP TRIGGER IF EXISTS cash_entries_no_truncate ON app.cash_entries;
                CREATE TRIGGER cash_entries_no_truncate BEFORE TRUNCATE ON app.cash_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();

                -- Reviewed: app.cash_closings keeps its trigger cash_closings_immutable; ALTER TABLE ... ADD COLUMN ... DEFAULT 0 fires no row
                -- trigger, so existing closings read opening_balance = cash_in = cash_out = entry_count = 0 and their expected_cash (stored) is
                -- unchanged. Nothing to backfill.
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/PlatformAdmin/PlatformAdmin.migration.sql
            migrationBuilder.Sql("""
                -- Change request 2026-10-24 (LIS-CR-013 C-17, P-ANN, migration ReviewFeatures): announcements of National Technology and its
                --    partners (SA-22). Grant for the runtime role on the new saas table (no deletes: an announcement is deactivated; the public
                --    counters are UPDATEs). Idempotent.
                GRANT SELECT, INSERT, UPDATE ON saas.announcements TO lis_app;

                -- Change request 2026-10-24 (LIS-CR-012 C-16, P-REM, migration ReviewFeatures): subscription reminders and grace.
                --    (a) Grants on the reminder log: SELECT, INSERT only (append-only: a reminder row is never changed or deleted).
                --    (b) Backfill of saas.wallets.at_or_below_zero_since_utc for wallets already at or below zero: the time of the first debit
                --        that left the balance at or below zero after the last positive balance (null when never debited). Idempotent: only
                --        rows still null are touched.
                --    (c) The three editable reminder templates of SA-20 (channel Email so both subjects exist; the SMS uses the same text).
                GRANT SELECT, INSERT ON saas.subscription_reminders TO lis_app;

                UPDATE saas.wallets AS w
                SET at_or_below_zero_since_utc = (
                    SELECT min(t.at_utc)
                    FROM saas.wallet_transactions AS t
                    WHERE t.wallet_id = w.id
                      AND t.amount < 0
                      AND t.balance_after <= 0
                      AND t.at_utc > COALESCE(
                          (SELECT max(p.at_utc) FROM saas.wallet_transactions AS p WHERE p.wallet_id = w.id AND p.balance_after > 0),
                          '-infinity'::timestamptz))
                WHERE w.balance <= 0 AND w.at_or_below_zero_since_utc IS NULL;

                INSERT INTO saas.notification_templates (id, code, channel, subject_en, subject_ar, text_en, text_ar, is_active, created_at_utc, created_by_display_name)
                VALUES
                    (md5('lis:template:subscription-expiry')::uuid, 'subscription-expiry', 'Email', 'Your Sky LIS trial is ending', 'الفترة التجريبية لـ Sky LIS على وشك الانتهاء',
                     '{laboratory}: your Sky LIS trial ends in {days} day(s). Top up {amount} to keep working without interruption: {link}',
                     '{laboratory}: تنتهي الفترة التجريبية في Sky LIS خلال {days} يوم. اشحن المحفظة بمبلغ {amount} لمواصلة العمل دون انقطاع: {link}', true, now(), 'seed'),
                    (md5('lis:template:wallet-low')::uuid, 'wallet-low', 'Email', 'Your Sky LIS wallet is running low', 'رصيد محفظة Sky LIS منخفض',
                     '{laboratory}: your Sky LIS wallet balance is low. Top up {amount} within {days} day(s) to avoid an interruption: {link}',
                     '{laboratory}: رصيد محفظة Sky LIS منخفض. اشحن {amount} خلال {days} يوم لتجنب انقطاع الخدمة: {link}', true, now(), 'seed'),
                    (md5('lis:template:subscription-grace')::uuid, 'subscription-grace', 'Email', 'Your Sky LIS subscription is about to stop', 'اشتراك Sky LIS على وشك التوقف',
                     '{laboratory}: the grace period of your Sky LIS subscription ends in {days} day(s). After that only your administrators can sign in, to the wallet. Top up {amount}: {link}',
                     '{laboratory}: تنتهي فترة السماح لاشتراك Sky LIS خلال {days} يوم. بعدها يستطيع المسؤولون فقط الدخول إلى المحفظة. اشحن {amount}: {link}', true, now(), 'seed')
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenants_areas_area_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropForeignKey(
                name: "fk_tenants_cities_city_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropForeignKey(
                name: "fk_tenants_districts_district_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropTable(
                name: "announcements",
                schema: "saas");

            migrationBuilder.DropTable(
                name: "cash_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "subscription_reminders",
                schema: "saas");

            migrationBuilder.DropIndex(
                name: "ix_tenants_area_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tenants_city_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tenants_district_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_report_templates_design_domain",
                schema: "app",
                table: "report_templates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contract_pricings_report_design_domain",
                schema: "app",
                table: "contract_pricings");

            migrationBuilder.DropColumn(
                name: "at_or_below_zero_since_utc",
                schema: "saas",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "collected_outside",
                schema: "app",
                table: "visit_tubes");

            migrationBuilder.DropColumn(
                name: "collected_outside_at_utc",
                schema: "app",
                table: "visit_tubes");

            migrationBuilder.DropColumn(
                name: "area_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "city_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "district_id",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "area_id",
                schema: "saas",
                table: "subscription_requests");

            migrationBuilder.DropColumn(
                name: "city_id",
                schema: "saas",
                table: "subscription_requests");

            migrationBuilder.DropColumn(
                name: "district_id",
                schema: "saas",
                table: "subscription_requests");

            migrationBuilder.DropColumn(
                name: "collected_outside",
                schema: "app",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "design",
                schema: "app",
                table: "report_templates");

            migrationBuilder.DropColumn(
                name: "report_design",
                schema: "app",
                table: "contract_pricings");

            migrationBuilder.DropColumn(
                name: "cash_in",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropColumn(
                name: "cash_out",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropColumn(
                name: "entry_count",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropColumn(
                name: "opening_balance",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropColumn(
                name: "area_id",
                schema: "app",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "city_id",
                schema: "app",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "district_id",
                schema: "app",
                table: "branches");
        }
    }
}
