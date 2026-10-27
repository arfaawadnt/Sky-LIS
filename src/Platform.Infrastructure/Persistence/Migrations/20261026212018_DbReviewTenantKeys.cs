using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database team review of 2026-10-26, comments 3 and 4 (scope approved by the product owner):
    /// <list type="number">
    /// <item><c>saas.tenants</c> becomes the registry of every tenant: column <c>kind</c> (Laboratory | Platform), the platform row
    /// (<c>TenantIds.Platform</c>, kind Platform; country, governorate and plan are nullable in the database for it alone, guarded by
    /// <c>ck_tenants_laboratory_place</c>), and every tenant id that owns data without a client record registered as an inactive
    /// laboratory, before any foreign key is added.</item>
    /// <item><c>tenant_id</c> on <c>saas.wallet_transactions</c>, <c>saas.wallet_top_up_requests</c> (from the wallet) and
    /// <c>saas.support_ticket_replies</c> (from the ticket): added, backfilled, then NOT NULL.</item>
    /// <item>Primary keys <c>(tenant_id, id)</c> on wallets, wallet transactions, top-up requests, subscription reminders, support
    /// tickets and replies, and machine credentials; the references to them become <c>(tenant_id, x_id)</c>.</item>
    /// <item><c>fk_&lt;table&gt;_tenant</c> (tenant_id) → <c>saas.tenants (id)</c> ON DELETE RESTRICT on every table with a tenant
    /// column (the model's by convention, the two retired treasury tables by SQL).</item>
    /// </list>
    /// Order: registry and data fixes → new columns and backfills → keys → foreign keys. Row-level security is untouched.
    /// </summary>
    public partial class DbReviewTenantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_site_agents_machine_credential",
                schema: "app",
                table: "site_agents");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_reminders_tenant_tenant_id",
                schema: "saas",
                table: "subscription_reminders");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_replies_support_tickets_support_ticket_id",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_top_up_requests_wallets_wallet_id",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_transactions_wallets_wallet_id",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_wallets_tenants_tenant_id",
                schema: "saas",
                table: "wallets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallets",
                schema: "saas",
                table: "wallets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallet_transactions",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropIndex(
                name: "ix_wallet_transactions_wallet_id_at_utc",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallet_top_up_requests",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropIndex(
                name: "ix_wallet_top_up_requests_wallet_id_status",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropPrimaryKey(
                name: "pk_support_tickets",
                schema: "saas",
                table: "support_tickets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_support_ticket_replies",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropIndex(
                name: "ix_support_ticket_replies_ticket_at",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropPrimaryKey(
                name: "pk_subscription_reminders",
                schema: "saas",
                table: "subscription_reminders");

            migrationBuilder.DropIndex(
                name: "ix_site_agents_machine_credential_id",
                schema: "app",
                table: "site_agents");

            migrationBuilder.DropPrimaryKey(
                name: "pk_machine_credentials",
                schema: "identity",
                table: "machine_credentials");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "saas",
                table: "wallet_transactions",
                type: "uuid",
                nullable: true);   // backfilled below, then NOT NULL

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "saas",
                table: "wallet_top_up_requests",
                type: "uuid",
                nullable: true);   // backfilled below, then NOT NULL

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "saas",
                table: "tenants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Laboratory");   // every existing row is a laboratory; the column keeps the default

            // (1) The registry. The platform row has no country, governorate or plan: the three columns admit NULL for it alone.
            migrationBuilder.Sql("""
                ALTER TABLE saas.tenants ALTER COLUMN country_id DROP NOT NULL;
                ALTER TABLE saas.tenants ALTER COLUMN governorate_id DROP NOT NULL;
                ALTER TABLE saas.tenants ALTER COLUMN plan_id DROP NOT NULL;
                ALTER TABLE saas.tenants ADD CONSTRAINT ck_tenants_laboratory_place
                    CHECK (kind = 'Platform' OR (country_id IS NOT NULL AND governorate_id IS NOT NULL AND plan_id IS NOT NULL));
                """);

            // (2) The platform tenant's row (the statement of PlatformTenantRecord.EnsureSql; idempotent).
            migrationBuilder.Sql("""
                INSERT INTO saas.tenants (id, kind, name_en, name_ar, country_id, governorate_id, plan_id, address, mobile, manager_name, manager_mobile,
                                          portal_slug, source, status, activated_at_utc, preferred_language, created_at_utc, created_by_display_name)
                VALUES ('00000000-0000-0000-0000-000000000001', 'Platform', 'Sky LIS platform', 'منصة Sky LIS', NULL, NULL, NULL, '', '', '', '',
                        'platform', 'Manual', 'Active', now(), 'en', now(), 'Platform tenant record')
                ON CONFLICT (id) DO NOTHING;
                """);

            // (3) Tenant ids that own data without a client record (development and test tenants seeded before client records) become
            //     inactive laboratories, so that the foreign keys below can be added; their users cannot sign in (CLI-020) until the Super
            //     Admin completes and activates the record on SA-04. Country = the first country (by creation) that has a governorate,
            //     governorate = its first, plan = the first plan; a clear error when they are missing. A no-op without such data.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    ids_sql text;
                    orphans uuid[];
                    v_country uuid;
                    v_governorate uuid;
                    v_plan uuid;
                    v_mobile text;
                BEGIN
                    SELECT string_agg(format('SELECT s.tenant_id FROM %I.%I s WHERE s.tenant_id IS NOT NULL', c.table_schema, c.table_name), ' UNION ')
                    INTO ids_sql
                    FROM information_schema.columns c
                    JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                    WHERE c.column_name = 'tenant_id'
                      AND c.table_schema IN ('identity', 'app', 'ops', 'audit', 'read', 'saas')
                      AND NOT (c.table_schema = 'saas' AND c.table_name = 'tenants');
                    IF ids_sql IS NULL THEN
                        RETURN;
                    END IF;

                    EXECUTE 'SELECT coalesce(array_agg(o.tenant_id ORDER BY o.tenant_id), ''{}''::uuid[]) FROM (' || ids_sql || ') o '
                         || 'WHERE o.tenant_id <> ''00000000-0000-0000-0000-000000000001''::uuid '
                         || 'AND NOT EXISTS (SELECT 1 FROM saas.tenants t WHERE t.id = o.tenant_id)'
                    INTO orphans;
                    IF cardinality(orphans) = 0 THEN
                        RETURN;
                    END IF;

                    SELECT c.id, c.dial_code || '1' || repeat('0', greatest(c.mobile_length - 1, 0))
                    INTO v_country, v_mobile
                    FROM saas.countries c
                    WHERE EXISTS (SELECT 1 FROM saas.governorates g WHERE g.country_id = c.id)
                    ORDER BY c.created_at_utc, c.id
                    LIMIT 1;
                    SELECT g.id INTO v_governorate FROM saas.governorates g WHERE g.country_id = v_country ORDER BY g.created_at_utc, g.id LIMIT 1;
                    SELECT p.id INTO v_plan FROM saas.plans p ORDER BY p.created_at_utc, p.id LIMIT 1;
                    IF v_country IS NULL OR v_governorate IS NULL OR v_plan IS NULL THEN
                        RAISE EXCEPTION 'DbReviewTenantKeys: % tenant id(s) own data without a client record in saas.tenants, and no country with a governorate or no plan exists to register them as laboratories. Create a country, a governorate and a plan, then apply the migration again.', cardinality(orphans)
                            USING ERRCODE = 'P0001';
                    END IF;

                    INSERT INTO saas.tenants (id, kind, name_en, name_ar, country_id, governorate_id, plan_id, address, mobile, manager_name, manager_mobile,
                                              portal_slug, source, status, deactivated_at_utc, deactivation_reason, preferred_language, created_at_utc, created_by_display_name)
                    SELECT o.tenant_id, 'Laboratory',
                           'Unregistered laboratory ' || left(replace(o.tenant_id::text, '-', ''), 8),
                           'مختبر غير مسجل ' || left(replace(o.tenant_id::text, '-', ''), 8),
                           v_country, v_governorate, v_plan,
                           'Not recorded (registered by migration)', v_mobile, 'Not recorded (registered by migration)', v_mobile,
                           't' || replace(o.tenant_id::text, '-', ''), 'Manual', 'Inactive', now(),
                           'Registered by migration DbReviewTenantKeys: tenant data without a client record', 'en', now(), 'Migration DbReviewTenantKeys'
                    FROM unnest(orphans) AS o(tenant_id);
                    RAISE NOTICE 'DbReviewTenantKeys: % tenant(s) without a client record registered as inactive laboratories', cardinality(orphans);
                END $$;
                """);


            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "saas",
                table: "support_ticket_replies",
                type: "uuid",
                nullable: true);   // backfilled below, then NOT NULL

            // (4) The new tenant columns take their parent's tenant, then become NOT NULL.
            migrationBuilder.Sql("""
                UPDATE saas.wallet_transactions x SET tenant_id = w.tenant_id FROM saas.wallets w WHERE w.id = x.wallet_id AND x.tenant_id IS NULL;
                UPDATE saas.wallet_top_up_requests x SET tenant_id = w.tenant_id FROM saas.wallets w WHERE w.id = x.wallet_id AND x.tenant_id IS NULL;
                UPDATE saas.support_ticket_replies x SET tenant_id = t.tenant_id FROM saas.support_tickets t WHERE t.id = x.support_ticket_id AND x.tenant_id IS NULL;
                ALTER TABLE saas.wallet_transactions ALTER COLUMN tenant_id SET NOT NULL;
                ALTER TABLE saas.wallet_top_up_requests ALTER COLUMN tenant_id SET NOT NULL;
                ALTER TABLE saas.support_ticket_replies ALTER COLUMN tenant_id SET NOT NULL;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallets",
                schema: "saas",
                table: "wallets",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallet_transactions",
                schema: "saas",
                table: "wallet_transactions",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallet_top_up_requests",
                schema: "saas",
                table: "wallet_top_up_requests",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_support_tickets",
                schema: "saas",
                table: "support_tickets",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_support_ticket_replies",
                schema: "saas",
                table: "support_ticket_replies",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_subscription_reminders",
                schema: "saas",
                table: "subscription_reminders",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_machine_credentials",
                schema: "identity",
                table: "machine_credentials",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_wallet_at",
                schema: "saas",
                table: "wallet_transactions",
                columns: new[] { "tenant_id", "wallet_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_top_up_requests_wallet_status",
                schema: "saas",
                table: "wallet_top_up_requests",
                columns: new[] { "tenant_id", "wallet_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_kind_domain",
                schema: "saas",
                table: "tenants",
                sql: "\"kind\" IN ('Laboratory', 'Platform')");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_replies_ticket_at",
                schema: "saas",
                table: "support_ticket_replies",
                columns: new[] { "tenant_id", "support_ticket_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_requests_tenant_id",
                schema: "saas",
                table: "subscription_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_agents_tenant_id_machine_credential_id",
                schema: "app",
                table: "site_agents",
                columns: new[] { "tenant_id", "machine_credential_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_tenant_id",
                schema: "identity",
                table: "refresh_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_tenant_id",
                schema: "ops",
                table: "outbox_events",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_log_entries_tenant",
                schema: "app",
                table: "agent_log_entries",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_connection_states_tenant",
                schema: "app",
                table: "analyser_connection_states",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_flags_tenant",
                schema: "app",
                table: "analyser_flags",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_result_intake_tenant",
                schema: "app",
                table: "analyser_result_intake",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_sample_codes_tenant",
                schema: "app",
                table: "analyser_sample_codes",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_test_mappings_tenant",
                schema: "app",
                table: "analyser_test_mappings",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analyser_value_conversions_tenant",
                schema: "app",
                table: "analyser_value_conversions",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_analysers_tenant",
                schema: "app",
                table: "analysers",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_antibiotics_tenant",
                schema: "app",
                table: "antibiotics",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_trail_tenant",
                schema: "audit",
                table: "audit_trail",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_base_price_lines_tenant",
                schema: "app",
                table: "base_price_lines",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_base_price_lists_tenant",
                schema: "app",
                table: "base_price_lists",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_batches_tenant",
                schema: "app",
                table: "batches",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_tenant",
                schema: "app",
                table: "bookings",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_branches_tenant",
                schema: "app",
                table: "branches",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_closings_tenant",
                schema: "app",
                table: "cash_closings",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_entries_tenant",
                schema: "app",
                table: "cash_entries",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_clinical_groups_tenant",
                schema: "app",
                table: "clinical_groups",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_templates_tenant",
                schema: "app",
                table: "comment_templates",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_commercial_names_tenant",
                schema: "app",
                table: "commercial_names",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contract_prices_tenant",
                schema: "app",
                table: "contract_prices",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contract_pricing_sources_tenant",
                schema: "app",
                table: "contract_pricing_sources",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contract_pricings_tenant",
                schema: "app",
                table: "contract_pricings",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contract_tiers_tenant",
                schema: "app",
                table: "contract_tiers",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_diagnoses_tenant",
                schema: "app",
                table: "diagnoses",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_doctors_tenant",
                schema: "app",
                table: "doctors",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_drug_types_tenant",
                schema: "app",
                table: "drug_types",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_field_changes_tenant",
                schema: "audit",
                table: "field_changes",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_laboratory_logos_tenant",
                schema: "app",
                table: "laboratory_logos",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_machine_credentials_tenant",
                schema: "identity",
                table: "machine_credentials",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_measurement_units_tenant",
                schema: "app",
                table: "measurement_units",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_organisms_tenant",
                schema: "app",
                table: "organisms",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_outbox_events_tenant",
                schema: "ops",
                table: "outbox_events",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_patient_conditions_tenant",
                schema: "app",
                table: "patient_conditions",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_patient_documents_tenant",
                schema: "app",
                table: "patient_documents",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_patients_tenant",
                schema: "app",
                table: "patients",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payers_tenant",
                schema: "app",
                table: "payers",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_methods_tenant",
                schema: "app",
                table: "payment_methods",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_tenant",
                schema: "app",
                table: "payments",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pcr_kit_genes_tenant",
                schema: "app",
                table: "pcr_kit_genes",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pcr_kits_tenant",
                schema: "app",
                table: "pcr_kits",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_preparations_tenant",
                schema: "app",
                table: "preparations",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_reasons_tenant",
                schema: "app",
                table: "reasons",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_reference_counters_tenant",
                schema: "app",
                table: "reference_counters",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_sessions_tenant",
                schema: "identity",
                table: "refresh_sessions",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_tenant",
                schema: "app",
                table: "refunds",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_report_templates_tenant",
                schema: "app",
                table: "report_templates",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_result_log_entries_tenant",
                schema: "app",
                table: "result_log_entries",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_results_tenant",
                schema: "app",
                table: "results",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_role_result_rights_tenant",
                schema: "app",
                table: "role_result_rights",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_tenant",
                schema: "app",
                table: "roles",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_compatibility_groups_tenant",
                schema: "app",
                table: "sample_compatibility_groups",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_compatibility_members_tenant",
                schema: "app",
                table: "sample_compatibility_members",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_conditions_tenant",
                schema: "app",
                table: "sample_conditions",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_events_tenant",
                schema: "app",
                table: "sample_events",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sample_types_tenant",
                schema: "app",
                table: "sample_types",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_samples_tenant",
                schema: "app",
                table: "samples",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_security_events_tenant",
                schema: "audit",
                table: "security_events",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sending_labs_tenant",
                schema: "app",
                table: "sending_labs",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sensitivity_values_tenant",
                schema: "app",
                table: "sensitivity_values",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_service_members_tenant",
                schema: "app",
                table: "service_members",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_services_tenant",
                schema: "app",
                table: "services",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_shortcut_sets_tenant",
                schema: "app",
                table: "shortcut_sets",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_site_agent_states_tenant",
                schema: "app",
                table: "site_agent_states",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_site_agents_machine_credential",
                schema: "app",
                table: "site_agents",
                columns: new[] { "tenant_id", "machine_credential_id" },
                principalSchema: "identity",
                principalTable: "machine_credentials",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_site_agents_tenant",
                schema: "app",
                table: "site_agents",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_reminders_tenant",
                schema: "saas",
                table: "subscription_reminders",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_requests_tenant",
                schema: "saas",
                table: "subscription_requests",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_replies_tenant",
                schema: "saas",
                table: "support_ticket_replies",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_replies_ticket",
                schema: "saas",
                table: "support_ticket_replies",
                columns: new[] { "tenant_id", "support_ticket_id" },
                principalSchema: "saas",
                principalTable: "support_tickets",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_support_tickets_tenant",
                schema: "saas",
                table: "support_tickets",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_memberships_tenant",
                schema: "app",
                table: "tenant_memberships",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_settings_tenant",
                schema: "app",
                table: "tenant_settings",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_list_values_tenant",
                schema: "app",
                table: "test_list_values",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_paragraphs_tenant",
                schema: "app",
                table: "test_paragraphs",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_range_bands_tenant",
                schema: "app",
                table: "test_range_bands",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_rules_tenant",
                schema: "app",
                table: "test_rules",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tests_tenant",
                schema: "app",
                table: "tests",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visit_accounts_tenant",
                schema: "app",
                table: "visit_accounts",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visit_lines_tenant",
                schema: "app",
                table: "visit_lines",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visit_preparation_answers_tenant",
                schema: "app",
                table: "visit_preparation_answers",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visit_transfers_tenant",
                schema: "app",
                table: "visit_transfers",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visit_tubes_tenant",
                schema: "app",
                table: "visit_tubes",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visits_tenant",
                schema: "app",
                table: "visits",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_top_up_requests_tenant",
                schema: "saas",
                table: "wallet_top_up_requests",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_top_up_requests_wallet",
                schema: "saas",
                table: "wallet_top_up_requests",
                columns: new[] { "tenant_id", "wallet_id" },
                principalSchema: "saas",
                principalTable: "wallets",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_transactions_tenant",
                schema: "saas",
                table: "wallet_transactions",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_transactions_wallet",
                schema: "saas",
                table: "wallet_transactions",
                columns: new[] { "tenant_id", "wallet_id" },
                principalSchema: "saas",
                principalTable: "wallets",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallets_tenant",
                schema: "saas",
                table: "wallets",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // (5) The tables of the retired treasury module (kept as history, outside the model) get the same foreign key.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('app.treasuries') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_treasuries_tenant') THEN
                        ALTER TABLE app.treasuries ADD CONSTRAINT fk_treasuries_tenant FOREIGN KEY (tenant_id) REFERENCES saas.tenants (id) ON DELETE RESTRICT;
                    END IF;
                    IF to_regclass('app.treasury_access_requests') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_treasury_access_requests_tenant') THEN
                        ALTER TABLE app.treasury_access_requests ADD CONSTRAINT fk_treasury_access_requests_tenant FOREIGN KEY (tenant_id) REFERENCES saas.tenants (id) ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS app.treasuries DROP CONSTRAINT IF EXISTS fk_treasuries_tenant;
                ALTER TABLE IF EXISTS app.treasury_access_requests DROP CONSTRAINT IF EXISTS fk_treasury_access_requests_tenant;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_agent_log_entries_tenant",
                schema: "app",
                table: "agent_log_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_connection_states_tenant",
                schema: "app",
                table: "analyser_connection_states");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_flags_tenant",
                schema: "app",
                table: "analyser_flags");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_result_intake_tenant",
                schema: "app",
                table: "analyser_result_intake");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_sample_codes_tenant",
                schema: "app",
                table: "analyser_sample_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_test_mappings_tenant",
                schema: "app",
                table: "analyser_test_mappings");

            migrationBuilder.DropForeignKey(
                name: "fk_analyser_value_conversions_tenant",
                schema: "app",
                table: "analyser_value_conversions");

            migrationBuilder.DropForeignKey(
                name: "fk_analysers_tenant",
                schema: "app",
                table: "analysers");

            migrationBuilder.DropForeignKey(
                name: "fk_antibiotics_tenant",
                schema: "app",
                table: "antibiotics");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_trail_tenant",
                schema: "audit",
                table: "audit_trail");

            migrationBuilder.DropForeignKey(
                name: "fk_base_price_lines_tenant",
                schema: "app",
                table: "base_price_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_base_price_lists_tenant",
                schema: "app",
                table: "base_price_lists");

            migrationBuilder.DropForeignKey(
                name: "fk_batches_tenant",
                schema: "app",
                table: "batches");

            migrationBuilder.DropForeignKey(
                name: "fk_bookings_tenant",
                schema: "app",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "fk_branches_tenant",
                schema: "app",
                table: "branches");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_closings_tenant",
                schema: "app",
                table: "cash_closings");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_entries_tenant",
                schema: "app",
                table: "cash_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_clinical_groups_tenant",
                schema: "app",
                table: "clinical_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_comment_templates_tenant",
                schema: "app",
                table: "comment_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_commercial_names_tenant",
                schema: "app",
                table: "commercial_names");

            migrationBuilder.DropForeignKey(
                name: "fk_contract_prices_tenant",
                schema: "app",
                table: "contract_prices");

            migrationBuilder.DropForeignKey(
                name: "fk_contract_pricing_sources_tenant",
                schema: "app",
                table: "contract_pricing_sources");

            migrationBuilder.DropForeignKey(
                name: "fk_contract_pricings_tenant",
                schema: "app",
                table: "contract_pricings");

            migrationBuilder.DropForeignKey(
                name: "fk_contract_tiers_tenant",
                schema: "app",
                table: "contract_tiers");

            migrationBuilder.DropForeignKey(
                name: "fk_diagnoses_tenant",
                schema: "app",
                table: "diagnoses");

            migrationBuilder.DropForeignKey(
                name: "fk_doctors_tenant",
                schema: "app",
                table: "doctors");

            migrationBuilder.DropForeignKey(
                name: "fk_drug_types_tenant",
                schema: "app",
                table: "drug_types");

            migrationBuilder.DropForeignKey(
                name: "fk_field_changes_tenant",
                schema: "audit",
                table: "field_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_laboratory_logos_tenant",
                schema: "app",
                table: "laboratory_logos");

            migrationBuilder.DropForeignKey(
                name: "fk_machine_credentials_tenant",
                schema: "identity",
                table: "machine_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_measurement_units_tenant",
                schema: "app",
                table: "measurement_units");

            migrationBuilder.DropForeignKey(
                name: "fk_organisms_tenant",
                schema: "app",
                table: "organisms");

            migrationBuilder.DropForeignKey(
                name: "fk_outbox_events_tenant",
                schema: "ops",
                table: "outbox_events");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_conditions_tenant",
                schema: "app",
                table: "patient_conditions");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_documents_tenant",
                schema: "app",
                table: "patient_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_patients_tenant",
                schema: "app",
                table: "patients");

            migrationBuilder.DropForeignKey(
                name: "fk_payers_tenant",
                schema: "app",
                table: "payers");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_methods_tenant",
                schema: "app",
                table: "payment_methods");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_tenant",
                schema: "app",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_pcr_kit_genes_tenant",
                schema: "app",
                table: "pcr_kit_genes");

            migrationBuilder.DropForeignKey(
                name: "fk_pcr_kits_tenant",
                schema: "app",
                table: "pcr_kits");

            migrationBuilder.DropForeignKey(
                name: "fk_preparations_tenant",
                schema: "app",
                table: "preparations");

            migrationBuilder.DropForeignKey(
                name: "fk_reasons_tenant",
                schema: "app",
                table: "reasons");

            migrationBuilder.DropForeignKey(
                name: "fk_reference_counters_tenant",
                schema: "app",
                table: "reference_counters");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_sessions_tenant",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_refunds_tenant",
                schema: "app",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_report_templates_tenant",
                schema: "app",
                table: "report_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_result_log_entries_tenant",
                schema: "app",
                table: "result_log_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_results_tenant",
                schema: "app",
                table: "results");

            migrationBuilder.DropForeignKey(
                name: "fk_role_result_rights_tenant",
                schema: "app",
                table: "role_result_rights");

            migrationBuilder.DropForeignKey(
                name: "fk_roles_tenant",
                schema: "app",
                table: "roles");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_compatibility_groups_tenant",
                schema: "app",
                table: "sample_compatibility_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_compatibility_members_tenant",
                schema: "app",
                table: "sample_compatibility_members");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_conditions_tenant",
                schema: "app",
                table: "sample_conditions");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_events_tenant",
                schema: "app",
                table: "sample_events");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_types_tenant",
                schema: "app",
                table: "sample_types");

            migrationBuilder.DropForeignKey(
                name: "fk_samples_tenant",
                schema: "app",
                table: "samples");

            migrationBuilder.DropForeignKey(
                name: "fk_security_events_tenant",
                schema: "audit",
                table: "security_events");

            migrationBuilder.DropForeignKey(
                name: "fk_sending_labs_tenant",
                schema: "app",
                table: "sending_labs");

            migrationBuilder.DropForeignKey(
                name: "fk_sensitivity_values_tenant",
                schema: "app",
                table: "sensitivity_values");

            migrationBuilder.DropForeignKey(
                name: "fk_service_members_tenant",
                schema: "app",
                table: "service_members");

            migrationBuilder.DropForeignKey(
                name: "fk_services_tenant",
                schema: "app",
                table: "services");

            migrationBuilder.DropForeignKey(
                name: "fk_shortcut_sets_tenant",
                schema: "app",
                table: "shortcut_sets");

            migrationBuilder.DropForeignKey(
                name: "fk_site_agent_states_tenant",
                schema: "app",
                table: "site_agent_states");

            migrationBuilder.DropForeignKey(
                name: "fk_site_agents_machine_credential",
                schema: "app",
                table: "site_agents");

            migrationBuilder.DropForeignKey(
                name: "fk_site_agents_tenant",
                schema: "app",
                table: "site_agents");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_reminders_tenant",
                schema: "saas",
                table: "subscription_reminders");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_requests_tenant",
                schema: "saas",
                table: "subscription_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_replies_tenant",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_replies_ticket",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropForeignKey(
                name: "fk_support_tickets_tenant",
                schema: "saas",
                table: "support_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_memberships_tenant",
                schema: "app",
                table: "tenant_memberships");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_settings_tenant",
                schema: "app",
                table: "tenant_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_test_list_values_tenant",
                schema: "app",
                table: "test_list_values");

            migrationBuilder.DropForeignKey(
                name: "fk_test_paragraphs_tenant",
                schema: "app",
                table: "test_paragraphs");

            migrationBuilder.DropForeignKey(
                name: "fk_test_range_bands_tenant",
                schema: "app",
                table: "test_range_bands");

            migrationBuilder.DropForeignKey(
                name: "fk_test_rules_tenant",
                schema: "app",
                table: "test_rules");

            migrationBuilder.DropForeignKey(
                name: "fk_tests_tenant",
                schema: "app",
                table: "tests");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_accounts_tenant",
                schema: "app",
                table: "visit_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_lines_tenant",
                schema: "app",
                table: "visit_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_preparation_answers_tenant",
                schema: "app",
                table: "visit_preparation_answers");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_transfers_tenant",
                schema: "app",
                table: "visit_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_tubes_tenant",
                schema: "app",
                table: "visit_tubes");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_tenant",
                schema: "app",
                table: "visits");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_top_up_requests_tenant",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_top_up_requests_wallet",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_transactions_tenant",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_transactions_wallet",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_wallets_tenant",
                schema: "saas",
                table: "wallets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallets",
                schema: "saas",
                table: "wallets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallet_transactions",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropIndex(
                name: "ix_wallet_transactions_wallet_at",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_wallet_top_up_requests",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropIndex(
                name: "ix_wallet_top_up_requests_wallet_status",
                schema: "saas",
                table: "wallet_top_up_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_kind_domain",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropPrimaryKey(
                name: "pk_support_tickets",
                schema: "saas",
                table: "support_tickets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_support_ticket_replies",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropIndex(
                name: "ix_support_ticket_replies_ticket_at",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.DropIndex(
                name: "ix_subscription_requests_tenant_id",
                schema: "saas",
                table: "subscription_requests");

            migrationBuilder.DropPrimaryKey(
                name: "pk_subscription_reminders",
                schema: "saas",
                table: "subscription_reminders");

            migrationBuilder.DropIndex(
                name: "ix_site_agents_tenant_id_machine_credential_id",
                schema: "app",
                table: "site_agents");

            migrationBuilder.DropIndex(
                name: "ix_refresh_sessions_tenant_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropIndex(
                name: "ix_outbox_events_tenant_id",
                schema: "ops",
                table: "outbox_events");

            migrationBuilder.DropPrimaryKey(
                name: "pk_machine_credentials",
                schema: "identity",
                table: "machine_credentials");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "saas",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "saas",
                table: "wallet_top_up_requests");

            // The rows this migration wrote (the platform row; the laboratories it registered, still inactive with its reason), then NOT NULL again.
            migrationBuilder.Sql("""
                DELETE FROM saas.tenants WHERE kind = 'Platform';
                DELETE FROM saas.tenants
                WHERE created_by_display_name = 'Migration DbReviewTenantKeys' AND status = 'Inactive'
                  AND deactivation_reason = 'Registered by migration DbReviewTenantKeys: tenant data without a client record';
                ALTER TABLE saas.tenants DROP CONSTRAINT IF EXISTS ck_tenants_laboratory_place;
                ALTER TABLE saas.tenants ALTER COLUMN country_id SET NOT NULL;
                ALTER TABLE saas.tenants ALTER COLUMN governorate_id SET NOT NULL;
                ALTER TABLE saas.tenants ALTER COLUMN plan_id SET NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "saas",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "saas",
                table: "support_ticket_replies");

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallets",
                schema: "saas",
                table: "wallets",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallet_transactions",
                schema: "saas",
                table: "wallet_transactions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_wallet_top_up_requests",
                schema: "saas",
                table: "wallet_top_up_requests",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_support_tickets",
                schema: "saas",
                table: "support_tickets",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_support_ticket_replies",
                schema: "saas",
                table: "support_ticket_replies",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_subscription_reminders",
                schema: "saas",
                table: "subscription_reminders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_machine_credentials",
                schema: "identity",
                table: "machine_credentials",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_wallet_id_at_utc",
                schema: "saas",
                table: "wallet_transactions",
                columns: new[] { "wallet_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_top_up_requests_wallet_id_status",
                schema: "saas",
                table: "wallet_top_up_requests",
                columns: new[] { "wallet_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_replies_ticket_at",
                schema: "saas",
                table: "support_ticket_replies",
                columns: new[] { "support_ticket_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_site_agents_machine_credential_id",
                schema: "app",
                table: "site_agents",
                column: "machine_credential_id");

            migrationBuilder.AddForeignKey(
                name: "fk_site_agents_machine_credential",
                schema: "app",
                table: "site_agents",
                column: "machine_credential_id",
                principalSchema: "identity",
                principalTable: "machine_credentials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_reminders_tenant_tenant_id",
                schema: "saas",
                table: "subscription_reminders",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_replies_support_tickets_support_ticket_id",
                schema: "saas",
                table: "support_ticket_replies",
                column: "support_ticket_id",
                principalSchema: "saas",
                principalTable: "support_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_top_up_requests_wallets_wallet_id",
                schema: "saas",
                table: "wallet_top_up_requests",
                column: "wallet_id",
                principalSchema: "saas",
                principalTable: "wallets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_transactions_wallets_wallet_id",
                schema: "saas",
                table: "wallet_transactions",
                column: "wallet_id",
                principalSchema: "saas",
                principalTable: "wallets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_wallets_tenants_tenant_id",
                schema: "saas",
                table: "wallets",
                column: "tenant_id",
                principalSchema: "saas",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
