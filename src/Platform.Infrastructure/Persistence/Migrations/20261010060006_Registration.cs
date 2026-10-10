using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Registration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batches",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sending_lab_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referring_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    courier_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    courier_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_batches", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_batches_kind", "(kind = 'LabToLab') = (sending_lab_id IS NOT NULL)");
                    table.CheckConstraint("ck_batches_kind_domain", "\"kind\" IN ('Group', 'LabToLab')");
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    patient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    age_years = table.Column<int>(type: "integer", nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    visit_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    phlebotomist_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accession = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    converted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    converted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_bookings", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_bookings_cancellation", "(status = 'Cancelled') = (cancellation_reason IS NOT NULL AND cancelled_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_bookings_conversion", "(status = 'Converted') = (visit_id IS NOT NULL AND accession IS NOT NULL AND converted_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_bookings_status_domain", "\"status\" IN ('Booked', 'Converted', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accession = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    treatment_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    referring_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    insurance_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    diagnosis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    urgent = table.Column<bool>(type: "boolean", nullable: false),
                    biohazard = table.Column<bool>(type: "boolean", nullable: false),
                    collected_outside = table.Column<bool>(type: "boolean", nullable: false),
                    utc_offset_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_list_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    patient_share_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payer_share_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    discount_value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    discount_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_visits", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_visits_completion", "(status = 'Completed') = (completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_visits_discount_kind_domain", "\"discount_kind\" IN ('Amount', 'Percent')");
                    table.CheckConstraint("ck_visits_location_type_domain", "\"location_type\" IN ('Walkin', 'Home', 'Batch', 'LabToLab')");
                    table.CheckConstraint("ck_visits_status_domain", "\"status\" IN ('Open', 'Completed')");
                    table.CheckConstraint("ck_visits_totals", "total_list_price >= 0 AND discount_total >= 0 AND patient_share_total >= 0 AND payer_share_total >= 0");
                    table.ForeignKey(
                        name: "fk_visits_batch",
                        columns: x => new { x.tenant_id, x.batch_id },
                        principalSchema: "app",
                        principalTable: "batches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visits_booking",
                        columns: x => new { x.tenant_id, x.booking_id },
                        principalSchema: "app",
                        principalTable: "bookings",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_preparation_answers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    question_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    answer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    answered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_preparation_answers", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_visit_preparation_answers_visit",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visits",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_transfers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_payer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_contract_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_payer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_contract_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    patient_share_before = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    patient_share_after = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payer_share_before = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payer_share_after = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_transfers", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_visit_transfers_kind_domain", "\"kind\" IN ('Contract', 'LineShares')");
                    table.ForeignKey(
                        name: "fk_visit_transfers_visit",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visits",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_tubes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clinical_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    clinical_group_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    clinical_group_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    sample_type_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    sample_type_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    sample_condition_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sample_condition_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_tubes", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_visit_tubes_visit",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visits",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_lines",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    service_name_en = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    service_name_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    service_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    parent_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tube_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    list_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    patient_share = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payer_share = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    expected_result_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    test_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    collector_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    added_by_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    ordered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ordered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_lines", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_visit_lines_amounts", "list_price >= 0 AND patient_share >= 0 AND payer_share >= 0 AND discount >= 0");
                    table.CheckConstraint("ck_visit_lines_cancellation", "(status = 'Cancelled') = (cancelled_at_utc IS NOT NULL AND cancellation_reason IS NOT NULL AND cancelled_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_visit_lines_kind_domain", "\"kind\" IN ('Service', 'Package', 'PackageMember')");
                    table.CheckConstraint("ck_visit_lines_member", "(kind = 'PackageMember') = (parent_line_id IS NOT NULL)");
                    table.CheckConstraint("ck_visit_lines_origin_domain", "\"origin\" IN ('Manual', 'Rule', 'Diagnosis')");
                    table.CheckConstraint("ck_visit_lines_status_domain", "\"status\" IN ('Ordered', 'Reserved', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_visit_lines_parent",
                        columns: x => new { x.tenant_id, x.parent_line_id },
                        principalSchema: "app",
                        principalTable: "visit_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_lines_tube",
                        columns: x => new { x.tenant_id, x.tube_id },
                        principalSchema: "app",
                        principalTable: "visit_tubes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_lines_visit",
                        columns: x => new { x.tenant_id, x.visit_id },
                        principalSchema: "app",
                        principalTable: "visits",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batches_registered_at",
                schema: "app",
                table: "batches",
                columns: new[] { "tenant_id", "registered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_sending_lab",
                schema: "app",
                table: "batches",
                columns: new[] { "tenant_id", "sending_lab_id" },
                filter: "sending_lab_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id_number",
                schema: "app",
                table: "batches",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_patient",
                schema: "app",
                table: "bookings",
                columns: new[] { "tenant_id", "patient_id" },
                filter: "patient_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_phlebotomist",
                schema: "app",
                table: "bookings",
                columns: new[] { "tenant_id", "phlebotomist_user_id" },
                filter: "phlebotomist_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_visit_at",
                schema: "app",
                table: "bookings",
                columns: new[] { "tenant_id", "status", "visit_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_number",
                schema: "app",
                table: "bookings",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_lines_cancelled",
                schema: "app",
                table: "visit_lines",
                columns: new[] { "tenant_id", "cancelled_at_utc" },
                filter: "status = 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_visit_lines_service",
                schema: "app",
                table: "visit_lines",
                columns: new[] { "tenant_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_lines_tenant_id_parent_line_id",
                schema: "app",
                table: "visit_lines",
                columns: new[] { "tenant_id", "parent_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_lines_tube",
                schema: "app",
                table: "visit_lines",
                columns: new[] { "tenant_id", "tube_id" },
                filter: "tube_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visit_lines_visit",
                schema: "app",
                table: "visit_lines",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_preparation_answers_question",
                schema: "app",
                table: "visit_preparation_answers",
                columns: new[] { "tenant_id", "visit_id", "preparation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_transfers_at",
                schema: "app",
                table: "visit_transfers",
                columns: new[] { "tenant_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_transfers_visit",
                schema: "app",
                table: "visit_transfers",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_tubes_visit",
                schema: "app",
                table: "visit_tubes",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_tubes_sample_number",
                schema: "app",
                table: "visit_tubes",
                columns: new[] { "tenant_id", "sample_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_accession",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "accession" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_batch_id",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "batch_id" },
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_booking_id",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "booking_id" },
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_branch_id_registered_at_utc",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "branch_id", "registered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_patient_id_registered_at_utc",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "patient_id", "registered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_payer_id",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "payer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_registered_at_utc",
                schema: "app",
                table: "visits",
                columns: new[] { "tenant_id", "registered_at_utc" });

            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
                ALTER TABLE app.batches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.batches FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.batches
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.bookings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.bookings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.bookings
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visits ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visits FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visits
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visit_preparation_answers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visit_preparation_answers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visit_preparation_answers
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visit_transfers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visit_transfers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visit_transfers
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visit_tubes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visit_tubes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visit_tubes
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                ALTER TABLE app.visit_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.visit_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON app.visit_lines
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Billing/BillingVisitsForeignKey.migration.sql
            migrationBuilder.Sql("""
                -- The account of a visit references the visit (id = the visit id). Billing.migration.sql declares the same guarded block,
                -- but it ran before Registration created app.visits (wave order), so it is repeated here once the table exists. Idempotent.
                DO $$
                BEGIN
                    IF to_regclass('app.visits') IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_accounts_visits' AND conrelid = 'app.visit_accounts'::regclass) THEN
                        ALTER TABLE app.visit_accounts
                            ADD CONSTRAINT fk_visit_accounts_visits FOREIGN KEY (tenant_id, id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT;
                    END IF;
                END
                $$;
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Registration/Registration.migration.sql
            migrationBuilder.Sql("""
                -- Registration (wave 2, intake LIS-INT-REG-001): extra SQL the integrator appends to the wave migration after the tables
                -- app.visits, app.visit_lines, app.visit_tubes, app.visit_preparation_answers, app.visit_transfers, app.bookings and
                -- app.batches exist (RLS comes from scripts/add-rls-to-migration.mjs). Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS.

                -- BR-15 / INV-REG-006: nothing of the module is ever deleted, for every role (cancellation, transfer and conversion keep
                -- the record with reason, actor and time).
                CREATE OR REPLACE FUNCTION app.registration_refuse_delete() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'registration records are never deleted: DELETE on %.% is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME;
                END
                $fn$;

                -- BR-01 / INV-REG-001: the accession, the patient file it was registered on and the registration time never change;
                -- the patient may change only by a merge re-point, which keeps the first file in original_patient_id.
                CREATE OR REPLACE FUNCTION app.visits_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.accession IS DISTINCT FROM OLD.accession
                       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
                       OR NEW.registered_at_utc IS DISTINCT FROM OLD.registered_at_utc
                       OR NEW.registered_by_user_id IS DISTINCT FROM OLD.registered_by_user_id THEN
                        RAISE EXCEPTION 'the identity of a visit never changes (visit %)', OLD.accession;
                    END IF;

                    IF NEW.patient_id IS DISTINCT FROM OLD.patient_id AND NEW.original_patient_id IS NULL THEN
                        RAISE EXCEPTION 'a visit changes patient only by a merge that keeps the original file (visit %)', OLD.accession;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                -- BR-01: a tube's sample number and key never change.
                CREATE OR REPLACE FUNCTION app.visit_tubes_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
                       OR NEW.sample_number IS DISTINCT FROM OLD.sample_number
                       OR NEW.clinical_group_id IS DISTINCT FROM OLD.clinical_group_id
                       OR NEW.sample_type_id IS DISTINCT FROM OLD.sample_type_id
                       OR NEW.sample_condition_id IS DISTINCT FROM OLD.sample_condition_id THEN
                        RAISE EXCEPTION 'a tube never changes (sample %)', OLD.sample_number;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                -- BR-15: a cancelled line is frozen; a line never moves to another visit or service.
                CREATE OR REPLACE FUNCTION app.visit_lines_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.status = 'Cancelled' THEN
                        RAISE EXCEPTION 'a cancelled service line is frozen (line %)', OLD.id;
                    END IF;

                    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                       OR NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
                       OR NEW.service_id IS DISTINCT FROM OLD.service_id
                       OR NEW.kind IS DISTINCT FROM OLD.kind
                       OR NEW.parent_line_id IS DISTINCT FROM OLD.parent_line_id
                       OR NEW.ordered_at_utc IS DISTINCT FROM OLD.ordered_at_utc
                       OR NEW.ordered_by_user_id IS DISTINCT FROM OLD.ordered_by_user_id THEN
                        RAISE EXCEPTION 'the identity of a service line never changes (line %)', OLD.id;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                -- The transfer log is append-only.
                CREATE OR REPLACE FUNCTION app.visit_transfers_refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'the transfer log is append-only: % on app.visit_transfers is not allowed', TG_OP;
                END
                $fn$;

                DROP TRIGGER IF EXISTS visits_no_delete ON app.visits;
                CREATE TRIGGER visits_no_delete BEFORE DELETE ON app.visits FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
                DROP TRIGGER IF EXISTS visits_guard ON app.visits;
                CREATE TRIGGER visits_guard BEFORE UPDATE ON app.visits FOR EACH ROW EXECUTE FUNCTION app.visits_guard();

                DROP TRIGGER IF EXISTS visit_lines_no_delete ON app.visit_lines;
                CREATE TRIGGER visit_lines_no_delete BEFORE DELETE ON app.visit_lines FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
                DROP TRIGGER IF EXISTS visit_lines_guard ON app.visit_lines;
                CREATE TRIGGER visit_lines_guard BEFORE UPDATE ON app.visit_lines FOR EACH ROW EXECUTE FUNCTION app.visit_lines_guard();

                DROP TRIGGER IF EXISTS visit_tubes_no_delete ON app.visit_tubes;
                CREATE TRIGGER visit_tubes_no_delete BEFORE DELETE ON app.visit_tubes FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
                DROP TRIGGER IF EXISTS visit_tubes_guard ON app.visit_tubes;
                CREATE TRIGGER visit_tubes_guard BEFORE UPDATE ON app.visit_tubes FOR EACH ROW EXECUTE FUNCTION app.visit_tubes_guard();

                DROP TRIGGER IF EXISTS visit_preparation_answers_no_delete ON app.visit_preparation_answers;
                CREATE TRIGGER visit_preparation_answers_no_delete BEFORE DELETE ON app.visit_preparation_answers FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();

                DROP TRIGGER IF EXISTS visit_transfers_append_only ON app.visit_transfers;
                CREATE TRIGGER visit_transfers_append_only BEFORE UPDATE OR DELETE ON app.visit_transfers FOR EACH ROW EXECUTE FUNCTION app.visit_transfers_refuse_change();

                DROP TRIGGER IF EXISTS bookings_no_delete ON app.bookings;
                CREATE TRIGGER bookings_no_delete BEFORE DELETE ON app.bookings FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();

                DROP TRIGGER IF EXISTS batches_no_delete ON app.batches;
                CREATE TRIGGER batches_no_delete BEFORE DELETE ON app.batches FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
                """);

            // module-sql: src/Platform.Infrastructure/Persistence/Reporting/Reporting.migration.sql
            migrationBuilder.Sql("""
                -- Reporting (intake LIS-INT-REP-001, Release 1 scope: SA-03 platform activity). Appended by the integrator to the wave
                -- migration; plain SQL, safe to run twice (CREATE OR REPLACE, idempotent grants). Pattern of saas.client_usage_counts
                -- (PlatformAdmin.migration.sql): the function is created by the migration role (lis_owner, BYPASSRLS), runs as its owner
                -- (SECURITY DEFINER), answers counts only, and only to a session whose tenant is the platform tenant. The application
                -- never elevates for these figures.

                GRANT USAGE ON SCHEMA saas TO lis_app;

                -- SA-03 "served patients, tests and registrations per period" across the clients, one row per tenant with activity:
                --   registrations   = visits registered in [p_from, p_to)                       (app.visits, MODULES.md §5)
                --   served_patients = distinct patients of those visits
                --   tests           = results of those visits that are not cancelled             (app.results, MODULES.md §5; 0 before Results)
                -- Tables that do not exist yet answer nothing (no row), never an error; the statements are dynamic for that reason.
                CREATE OR REPLACE FUNCTION saas.client_activity_counts(p_from timestamptz, p_to timestamptz)
                RETURNS TABLE (tenant_id uuid, served_patients bigint, tests bigint, registrations bigint)
                LANGUAGE plpgsql
                STABLE
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $fn$
                #variable_conflict use_column
                BEGIN
                    IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
                        RAISE EXCEPTION 'saas.client_activity_counts is reserved to the platform tenant' USING ERRCODE = '42501';
                    END IF;

                    IF p_from IS NULL OR p_to IS NULL OR p_from >= p_to OR to_regclass('app.visits') IS NULL THEN
                        RETURN;
                    END IF;

                    IF to_regclass('app.results') IS NULL THEN
                        RETURN QUERY EXECUTE
                            'SELECT v.tenant_id, count(DISTINCT v.patient_id), 0::bigint, count(*)
                             FROM app.visits v
                             WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
                             GROUP BY v.tenant_id'
                            USING p_from, p_to;
                    ELSE
                        RETURN QUERY EXECUTE
                            'SELECT v.tenant_id, count(DISTINCT v.patient_id), coalesce(sum(t.tests), 0)::bigint, count(*)
                             FROM app.visits v
                             CROSS JOIN LATERAL (
                                 SELECT count(*) AS tests FROM app.results r
                                 WHERE r.tenant_id = v.tenant_id AND r.visit_id = v.id AND r.status <> ''Cancelled''
                             ) t
                             WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
                             GROUP BY v.tenant_id'
                            USING p_from, p_to;
                    END IF;
                END
                $fn$;
                REVOKE ALL ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) TO lis_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The Up step adds fk_visit_accounts_visits (billing -> registration) once app.visits exists; the table cannot be
            // dropped while that key remains, so the key goes first. Idempotent, like the Up block.
            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS app.visit_accounts DROP CONSTRAINT IF EXISTS fk_visit_accounts_visits;
                """);

            migrationBuilder.DropTable(
                name: "visit_lines",
                schema: "app");

            migrationBuilder.DropTable(
                name: "visit_preparation_answers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "visit_transfers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "visit_tubes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "app");

            migrationBuilder.DropTable(
                name: "batches",
                schema: "app");

            migrationBuilder.DropTable(
                name: "bookings",
                schema: "app");
        }
    }
}
