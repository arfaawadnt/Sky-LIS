using System.Text;
using Platform.Application.Billing;
using Platform.Application.Billing.Commands;
using Platform.Application.Billing.Queries;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Billing;

/// <summary>The use cases of intake LIS-INT-BIL-001 over in-memory ports: T1–T5 handlers, refusals before numbers are drawn, the account view and the CSV export.</summary>
public sealed class BillingUseCaseTests
{
    private const string Device = "device-reception-1";

    private static RecordPaymentHandler PaymentHandler(BillingWorld world) =>
        new(world.Visits, world.Stations, world.Methods, world.Accounts, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

    private static RecordRefundHandler RefundHandler(BillingWorld world) =>
        new(world.Visits, world.Stations, world.Reasons, world.Methods, world.Accounts, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

    [Fact]
    public async Task T1_A_first_payment_opens_the_account_draws_a_receipt_and_commits_once()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00001");

        var created = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 120m, "cash", null, world.TreasuryId, $" {Device} "), CancellationToken.None);

        Assert.Equal("RCT-2026-00001", created.ReceiptNumber);
        var account = Assert.Single(world.Accounts.Items);
        Assert.Equal((visit.VisitId, 120m), (account.VisitId, account.PaidTotal));
        var payment = Assert.Single(account.Payments);
        Assert.Equal((created.Id, Device, world.Branch, world.Cashier), (payment.Id, payment.DeviceId, payment.BranchId, payment.CashierUserId));
        Assert.Equal(Device, world.Stations.LastDeviceId);
        Assert.Equal(1, world.UnitOfWork.Commits);

        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 180m, "Pos", "POS-77", world.TreasuryId, Device), CancellationToken.None);
        Assert.Single(world.Accounts.Items);
        Assert.Equal(300m, world.Accounts.Items[0].PaidTotal);
    }

    [Fact]
    public async Task T1_A_refused_payment_draws_no_receipt_number_and_commits_nothing()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(50m, "ACC-2026-00002");

        var refusal = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 60m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));

        Assert.Equal(BillingCodes.AmountExceedsRemaining, refusal.Code);
        Assert.Equal(0, world.Numbers.Drawn);
        Assert.Equal(0, world.UnitOfWork.Commits);
        Assert.Empty(world.Accounts.Items);
    }

    [Fact]
    public async Task T1_Refusals_before_the_aggregate_TENANT_004_BIL_404_BIL_409_BIL_020()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(100m, "ACC-2026-00003");

        world.User.BranchId = null;
        Assert.Equal(ProblemCodes.BranchRequired, (await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None))).Code);
        world.User.BranchId = world.Branch;

        Assert.Equal(BillingCodes.VisitNotFound, (await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(Guid.CreateVersion7(), 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None))).Code);
        Assert.Equal(BillingCodes.TreasuryNotFound, (await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 10m, "Cash", null, Guid.CreateVersion7(), Device), CancellationToken.None))).Code);

        world.Stations.Replace(new TreasuryStationView(world.TreasuryId, "MAIN", "Main drawer", "الخزينة الرئيسية", world.Branch, IsActive: true, ApprovedForDevice: false));
        var revoked = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal((BillingCodes.TreasuryNotApproved, DomainFault.Refused), (revoked.Code, revoked.Fault));
        Assert.Equal(0, world.Numbers.Drawn);
    }

    [Fact]
    public async Task T1_A_member_of_a_lab_to_lab_batch_is_billed_through_claims_BIL_015()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(100m, "ACC-2026-00004", Guid.CreateVersion7(), "LabToLab", "BTH-2026-00001");

        var refusal = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));

        Assert.Equal(BillingCodes.BilledThroughClaims, refusal.Code);
    }

    [Fact]
    public async Task T2_A_payment_inside_a_closed_period_cannot_be_voided_BIL_013()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(100m, "ACC-2026-00005");
        var created = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        var handler = new VoidPaymentHandler(world.Accounts, world.Closings, world.UnitOfWork, world.User, world.Privileges, world.Clock);

        world.Closings.CoversAnything = true;
        Assert.Equal(BillingCodes.PaymentFrozen, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new VoidPaymentCommand(created.Id, "late"), CancellationToken.None))).Code);

        world.Closings.CoversAnything = false;
        await handler.Handle(new VoidPaymentCommand(created.Id, "wrong visit"), CancellationToken.None);
        Assert.Equal(PaymentStatus.Voided, world.Accounts.Items[0].Payments.Single().Status);
        Assert.Equal(BillingCodes.PaymentNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new VoidPaymentCommand(Guid.CreateVersion7(), "x"), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task T3_A_refund_needs_an_active_reason_of_kind_Refund_BIL_016_and_draws_no_number_when_refused()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00006");
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 300m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        world.Visits.Items[0] = visit with { PatientShareTotal = 200m };   // VisitTransferred re-priced the visit
        var drawn = world.Numbers.Drawn;

        var unknown = await Assert.ThrowsAsync<DomainException>(() => RefundHandler(world).Handle(new RecordRefundCommand(visit.VisitId, 100m, "Cash", null, Guid.CreateVersion7(), null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal((BillingCodes.RefundReasonInvalid, DomainFault.Validation), (unknown.Code, unknown.Fault));
        Assert.Equal(drawn, world.Numbers.Drawn);

        var created = await RefundHandler(world).Handle(new RecordRefundCommand(visit.VisitId, 100m, "Cash", null, world.RefundReasonId, "contract transfer", world.TreasuryId, Device), CancellationToken.None);
        Assert.Equal("RFD-2026-00001", created.Number);
        Assert.Equal(100m, world.Accounts.Items[0].RefundedTotal);
    }

    [Fact]
    public async Task T4_Pay_all_distributes_in_accession_order_with_one_receipt_per_member_and_one_commit()
    {
        var world = new BillingWorld();
        var batchId = Guid.CreateVersion7();
        world.Visits.Batches.Add(new BillableBatch(batchId, "BTH-2026-00001", "Group", null, null, null));
        var second = world.AddVisit(150m, "ACC-2026-00012", batchId, "Group", "BTH-2026-00001");
        var settled = world.AddVisit(0m, "ACC-2026-00011", batchId, "Group", "BTH-2026-00001");
        var first = world.AddVisit(100m, "ACC-2026-00010", batchId, "Group", "BTH-2026-00001");
        var handler = new PayBatchHandler(world.Visits, world.Stations, world.Methods, world.Accounts, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

        var result = await handler.Handle(new PayBatchCommand(batchId, 200m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);

        Assert.Equal(
            [(first.VisitId, "ACC-2026-00010", 100m, "RCT-2026-00001"), (second.VisitId, "ACC-2026-00012", 100m, "RCT-2026-00002")],
            result.Payments.Select(payment => (payment.VisitId, payment.Accession, payment.Amount, payment.ReceiptNumber)).ToList());
        Assert.DoesNotContain(world.Accounts.Items, account => account.Id == settled.VisitId);
        Assert.All(world.Accounts.Items.SelectMany(account => account.Payments), payment => Assert.Equal(batchId, payment.BatchId));
        Assert.Equal(1, world.UnitOfWork.Commits);

        var over = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PayBatchCommand(batchId, 50.01m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal(BillingCodes.AmountExceedsBatchRemaining, over.Code);
    }

    [Fact]
    public async Task T4_A_lab_to_lab_batch_is_refused_BIL_015()
    {
        var world = new BillingWorld();
        var batchId = Guid.CreateVersion7();
        world.Visits.Batches.Add(new BillableBatch(batchId, "BTH-2026-00002", "LabToLab", null, null, Guid.CreateVersion7()));
        world.AddVisit(100m, "ACC-2026-00020", batchId, "LabToLab", "BTH-2026-00002");
        var handler = new PayBatchHandler(world.Visits, world.Stations, world.Methods, world.Accounts, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

        Assert.Equal(BillingCodes.BilledThroughClaims, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PayBatchCommand(batchId, 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None))).Code);
        Assert.Equal(BillingCodes.BatchNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PayBatchCommand(Guid.CreateVersion7(), 10m, "Cash", null, world.TreasuryId, Device), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task T5_Close_stores_the_totals_and_refuses_an_overlapping_period_BIL_120()
    {
        var world = new BillingWorld();
        world.Drawer.Totals = new ClosingTotals(500m, 200m, 0m, 0m, 3, 50m, 0m, 0m, 0m, 1, 0m, 0m, 3);
        var handler = new CloseCashDrawerHandler(world.Stations, world.Closings, world.Drawer, world.Numbers, world.UnitOfWork, world.User, world.Clock);
        var from = BillingWorld.Now.AddHours(-8);

        var created = await handler.Handle(new CloseCashDrawerCommand(world.TreasuryId, world.Cashier, from, BillingWorld.Now, 450m, null), CancellationToken.None);

        Assert.Equal("CLS-2026-00001", created.Number);
        var closing = Assert.Single(world.Closings.Items);
        Assert.Equal((450m, 0m, world.Branch), (closing.ExpectedCash, closing.Difference, closing.BranchId));

        var overlap = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CloseCashDrawerCommand(world.TreasuryId, null, from.AddHours(1), BillingWorld.Now, null, null), CancellationToken.None));
        Assert.Equal((BillingCodes.ClosingOverlaps, DomainFault.Conflict), (overlap.Code, overlap.Fault));
        Assert.Equal(1, world.Numbers.Drawn);
    }

    [Fact]
    public async Task The_special_rights_of_CP_37_narrow_the_permissions_BIL_040_041_042_043()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00060");
        var paid = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 300m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        world.Visits.Items[0] = visit with { PatientShareTotal = 200m };
        world.Privileges.SpecialRights = new SpecialRights(null, canPay: false, canRefund: false, canTransfer: true, canCancel: false, canModifyVerifiedResult: true, canPreviewWithoutVerify: true, canPrintInvoice: false, canSeeVip: true);
        var drawn = world.Numbers.Drawn;

        async Task<DomainException> Refused(Func<Task> call) => await Assert.ThrowsAsync<DomainException>(call);

        var pay = await Refused(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 1m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal((BillingCodes.PayRightRequired, DomainFault.Refused), (pay.Code, pay.Fault));
        var batch = await Refused(() => new PayBatchHandler(world.Visits, world.Stations, world.Methods, world.Accounts, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock)
            .Handle(new PayBatchCommand(Guid.CreateVersion7(), 1m, "Cash", null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal(BillingCodes.PayRightRequired, batch.Code);
        var refund = await Refused(() => RefundHandler(world).Handle(new RecordRefundCommand(visit.VisitId, 100m, "Cash", null, world.RefundReasonId, null, world.TreasuryId, Device), CancellationToken.None));
        Assert.Equal(BillingCodes.RefundRightRequired, refund.Code);
        var cancel = await Refused(() => new VoidPaymentHandler(world.Accounts, world.Closings, world.UnitOfWork, world.User, world.Privileges, world.Clock).Handle(new VoidPaymentCommand(paid.Id, "x"), CancellationToken.None));
        Assert.Equal(BillingCodes.CancelRightRequired, cancel.Code);
        var invoice = await Refused(() => new GetInvoiceHandler(world.Visits, world.Accounts, world.Directory, world.Settings, world.User, world.Privileges, world.Clock).Handle(new GetInvoiceQuery(visit.VisitId), CancellationToken.None));
        Assert.Equal(BillingCodes.PrintInvoiceRightRequired, invoice.Code);

        Assert.Equal(drawn, world.Numbers.Drawn);
        Assert.Equal(PaymentStatus.Posted, world.Accounts.Items[0].Payments.Single().Status);
        Assert.Equal(0m, world.Accounts.Items[0].RefundedTotal);
    }

    [Fact]
    public async Task The_visit_account_shows_the_balance_by_the_single_definition()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00030");
        var first = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 50m, "Card", "SLIP", world.TreasuryId, Device), CancellationToken.None);
        await new VoidPaymentHandler(world.Accounts, world.Closings, world.UnitOfWork, world.User, world.Privileges, world.Clock).Handle(new VoidPaymentCommand(first.Id, "duplicate"), CancellationToken.None);
        var handler = new GetVisitAccountHandler(world.Visits, world.Accounts, world.Reasons, world.Directory, world.Settings);

        var view = await handler.Handle(new GetVisitAccountQuery(null, "acc-2026-00030"), CancellationToken.None);

        Assert.Equal((50m, 0m, 250m, 0m, "Partial", "EGP"), (view.Paid, view.Refunded, view.Remaining, view.Refundable, view.BalanceStatus, view.Currency));
        Assert.Equal(2, view.Payments.Count);
        Assert.All(view.Payments, payment => Assert.Equal("ACC-2026-00030", payment.Accession));
        Assert.Equal(("PAT-2026-00001", "Patient Name"), (view.PatientNumber, view.PatientName));
        Assert.Equal(BillingCodes.VisitNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetVisitAccountQuery(null, "ACC-0000"), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task The_payments_export_neutralises_formulas_and_quotes_separators()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00040");
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Transfer", "=HYPERLINK(\"x\")", world.TreasuryId, Device), CancellationToken.None);
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 20.5m, "Card", "slip, 7", world.TreasuryId, Device), CancellationToken.None);

        var file = await new ExportPaymentsHandler(world.Accounts, world.Visits, world.Clock).Handle(new ExportPaymentsQuery(null, null, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(("text/csv; charset=utf-8", "payments-20261009-1400.csv"), (file.ContentType, file.FileName));
        var text = Encoding.UTF8.GetString(file.Content);
        Assert.StartsWith("﻿Receipt,Paid at (UTC),Accession,Amount", text, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text, StringComparison.Ordinal);
        Assert.Contains(",\"slip, 7\",", text, StringComparison.Ordinal);
        Assert.Contains(",ACC-2026-00040,20.50,Card,", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_payments_export_writes_local_times_in_the_browser_time_zone()
    {
        // UAT R1-06: the register shows local times; its CSV was still in UTC.
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00041");
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        var paidAt = world.Clock.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        var file = await new ExportPaymentsHandler(world.Accounts, world.Visits, world.Clock).Handle(
            new ExportPaymentsQuery(null, null, null, null, null, null, null, null, "Africa/Cairo"), CancellationToken.None);

        var text = Encoding.UTF8.GetString(file.Content);
        Assert.StartsWith("﻿Receipt,Paid at (Africa/Cairo),Accession", text, StringComparison.Ordinal);
        Assert.Contains("Voided at (Africa/Cairo)", text, StringComparison.Ordinal);
        Assert.Contains(TimeZoneInfo.ConvertTime(paidAt, zone).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.DoesNotContain(paidAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);

        Assert.True(new ExportPaymentsValidator().Validate(new ExportPaymentsQuery(null, null, null, null, null, null, null, null, "Africa/Cairo")).IsValid);
        Assert.False(new ExportPaymentsValidator().Validate(new ExportPaymentsQuery(null, null, null, null, null, null, null, null, "Mars/Olympus")).IsValid);
    }

    [Fact]
    public async Task The_receipt_carries_the_letterhead_and_the_figures_as_of_printing()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00050");
        var created = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 120m, "Cash", null, world.TreasuryId, Device), CancellationToken.None);
        var handler = new GetPaymentReceiptHandler(world.Accounts, world.Visits, new FakeBalances(world.Accounts, world.Visits), world.Directory, world.Settings, world.User, world.Clock);

        var receipt = await handler.Handle(new GetPaymentReceiptQuery(created.Id), CancellationToken.None);

        Assert.Equal(("RCT-2026-00001", "Posted", "ACC-2026-00050", 120m, 120m, 180m), (receipt.ReceiptNumber, receipt.Status, receipt.Accession, receipt.Amount, receipt.Paid, receipt.Remaining));
        Assert.Equal(("Sky Lab", "مختبر سكاي", "Cashier One", BillingWorld.Now), (receipt.Letterhead.LabNameEn, receipt.Letterhead.LabNameAr, receipt.PrintedBy, receipt.PrintedAtUtc));
    }

    // ---- collections report (change request 2026-10-13, item 2) ----------------------------------------------------

    private static CollectionsReportHandler CollectionsHandler(BillingWorld world) =>
        new(world.Collections, world.Drawer, world.Directory, world.Zone, world.Settings, world.User, world.Privileges, world.Clock);

    private static void SeedCollections(BillingWorld world, Guid? otherCashier = null)
    {
        world.Collections.Cells.Add(new CollectionsCell(world.Branch, world.Cashier, PaymentMethod.Cash, 300m, 2, 50m, 1));
        world.Collections.Cells.Add(new CollectionsCell(world.Branch, world.Cashier, PaymentMethod.Card, 100m, 1, 0m, 0));
        if (otherCashier is { } other)
        {
            world.Collections.Cells.Add(new CollectionsCell(world.Branch, other, PaymentMethod.Cash, 400m, 1, 0m, 0));
        }
    }

    [Fact]
    public async Task S_CR_The_default_report_is_today_in_the_laboratory_zone_for_the_users_own_movements_by_method()
    {
        var world = new BillingWorld();
        world.Clock.UtcNow = new DateTimeOffset(2026, 10, 9, 22, 30, 0, TimeSpan.Zero);   // 01:30 on 10 Oct at +03: the laboratory's day is the 10th
        SeedCollections(world);

        var report = await CollectionsHandler(world).Handle(new CollectionsReportQuery(null, null, null, null), CancellationToken.None);

        var scope = world.Collections.LastScope!;
        Assert.Equal((world.Cashier, (Guid?)null), (scope.CashierUserId, scope.BranchId));
        Assert.Equal((new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 10, 21, 0, 0, TimeSpan.Zero)), (scope.FromUtc, scope.ToUtc));
        Assert.Equal(("User", "Method", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10), "Test+03", "Cashier One"), (report.Scope, report.GroupBy, report.FromDay, report.ToDay, report.TimeZone, report.CashierName));
        Assert.Equal(["Cash", "Card"], report.Rows.Select(row => row.Key).ToArray());   // method order, labels left to the SPA
        Assert.All(report.Rows, row => Assert.Null(row.LabelEn));
        var cash = report.Rows[0];
        Assert.Equal((300m, 50m, 250m, 2, 1), (cash.Cash.Payments, cash.Cash.Refunds, cash.Cash.Net, cash.PaymentCount, cash.RefundCount));
        Assert.Equal((400m, 50m, 350m, 3, 1, 100m), (report.Totals.TotalPayments, report.Totals.TotalRefunds, report.Totals.Net, report.Totals.PaymentCount, report.Totals.RefundCount, report.Totals.Card.Net));
        Assert.Equal(("Sky Lab", "Main", "EGP", "Cashier One", world.Clock.UtcNow), (report.Letterhead.LabNameEn, report.Letterhead.BranchNameEn, report.Currency, report.PrintedBy, report.PrintedAtUtc));
    }

    [Fact]
    public async Task S_CR_The_branch_scope_needs_a_session_branch_and_the_CP_37_right_BIL_044()
    {
        var world = new BillingWorld();
        var other = Guid.CreateVersion7();
        SeedCollections(world, other);
        world.Privileges.SpecialRights = new SpecialRights(null, true, true, true, true, true, true, true, true, DemographicsEditLimit.Printed, canViewBranchCollections: false, reportDaysLimit: null);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => CollectionsHandler(world).Handle(new CollectionsReportQuery(null, null, "branch", "user"), CancellationToken.None));
        Assert.Equal((BillingCodes.BranchCollectionsRightRequired, DomainFault.Refused), (refusal.Code, refusal.Fault));
        Assert.Null(world.Collections.LastScope);

        world.User.BranchId = null;
        world.Privileges.SpecialRights = SpecialRights.All;
        var noBranch = await Assert.ThrowsAsync<DomainException>(() => CollectionsHandler(world).Handle(new CollectionsReportQuery(null, null, "Branch", "User"), CancellationToken.None));
        Assert.Equal(ProblemCodes.BranchRequired, noBranch.Code);

        world.User.BranchId = world.Branch;
        var report = await CollectionsHandler(world).Handle(new CollectionsReportQuery(null, null, "Branch", "User"), CancellationToken.None);
        Assert.Equal(((Guid?)null, world.Branch), (world.Collections.LastScope!.CashierUserId, world.Collections.LastScope.BranchId));
        Assert.Equal(("Branch", "User", null), (report.Scope, report.GroupBy, report.CashierName));
        Assert.Equal(2, report.Rows.Count);   // one row per cashier, named through the drawer's user names
        Assert.All(report.Rows, row => Assert.Equal("Cashier One", row.LabelEn));
        Assert.Equal(400m, Assert.Single(report.Rows, row => row.Key == other.ToString("D")).Net);
        Assert.Equal(750m, report.Totals.Net);
    }

    [Fact]
    public async Task S_CR_The_report_days_limit_refuses_an_earlier_period_BIL_045_and_grouping_by_branch_names_the_branch()
    {
        var world = new BillingWorld();
        SeedCollections(world);
        var today = new DateOnly(2026, 10, 9);   // 14:00Z at +03 is still the 9th
        world.Privileges.SpecialRights = new SpecialRights(null, true, true, true, true, true, true, true, true, DemographicsEditLimit.Printed, canViewBranchCollections: true, reportDaysLimit: 0);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => CollectionsHandler(world).Handle(new CollectionsReportQuery(today.AddDays(-1), today, "User", "Branch"), CancellationToken.None));
        Assert.Equal((BillingCodes.ReportPeriodBeyondLimit, DomainFault.Refused), (refusal.Code, refusal.Fault));
        Assert.Null(world.Collections.LastScope);

        world.Privileges.SpecialRights = new SpecialRights(null, true, true, true, true, true, true, true, true, DemographicsEditLimit.Printed, canViewBranchCollections: true, reportDaysLimit: 1);
        var report = await CollectionsHandler(world).Handle(new CollectionsReportQuery(today.AddDays(-1), today.AddDays(3), "User", "Branch"), CancellationToken.None);
        Assert.Equal((today.AddDays(-1), today.AddDays(3)), (report.FromDay, report.ToDay));
        Assert.Equal((new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero)), (report.FromUtc, report.ToUtc));
        var row = Assert.Single(report.Rows);
        Assert.Equal((world.Branch.ToString("D"), "Main branch", "الفرع الرئيسي", 350m), (row.Key, row.LabelEn, row.LabelAr, row.Net));

        var backwards = await Assert.ThrowsAsync<DomainException>(() => CollectionsHandler(world).Handle(new CollectionsReportQuery(today, today.AddDays(-1), null, null), CancellationToken.None));
        Assert.Equal(BillingCodes.PeriodInvalid, backwards.Code);
    }

    [Fact]
    public void S_CR_The_validator_accepts_the_enum_names_in_any_case_and_refuses_the_rest()
    {
        var validator = new CollectionsReportValidator();

        Assert.True(validator.Validate(new CollectionsReportQuery(null, null, "branch", "METHOD")).IsValid);
        Assert.True(validator.Validate(new CollectionsReportQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), null, null)).IsValid);
        Assert.False(validator.Validate(new CollectionsReportQuery(null, null, "Everyone", null)).IsValid);
        Assert.False(validator.Validate(new CollectionsReportQuery(null, null, null, "Treasury")).IsValid);
        Assert.False(validator.Validate(new CollectionsReportQuery(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1), null, null)).IsValid);
    }
}
