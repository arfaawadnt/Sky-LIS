using Platform.Application.Billing;
using Platform.Application.Billing.Commands;
using Platform.Application.Billing.Queries;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Billing;

/// <summary>The cash ledger use cases (CP-70, change request LIS-CR-011, intake LIS-INT-BIL-001 §18): T6, T7, the ledger query and BIL-061.</summary>
public sealed class CashLedgerUseCaseTests
{
    private static PostCashEntryHandler PostHandler(BillingWorld world, InMemoryCashEntries entries) =>
        new(world.Methods, world.Closings, entries, world.Numbers, world.UnitOfWork, world.User, world.Clock);

    private static CashLedgerHandler LedgerHandler(BillingWorld world, FakeLedger ledger) =>
        new(ledger, world.Drawer, world.Visits, world.Directory, world.Zone, world.Settings, world.User, world.Privileges, world.Clock);

    [Fact]
    public async Task T6_A_cash_out_is_posted_for_the_session_cashier_with_a_number_and_one_commit()
    {
        var world = new BillingWorld();
        var entries = new InMemoryCashEntries();

        var created = await PostHandler(world, entries).Handle(new PostCashEntryCommand("cashOut", "pettyCash", 75.5m, "Cleaning supplies"), CancellationToken.None);

        var entry = Assert.Single(entries.Items);
        Assert.Equal(("CSH-2026-00001", created.Id, created.Number), (entry.Number, entry.Id, created.Number));
        Assert.Equal((CashEntryKind.CashOut, CashEntryCategory.PettyCash, PaymentMethod.Cash, (string?)null), (entry.Kind, entry.Category, entry.Method, entry.MethodCode));
        Assert.Equal((world.Cashier, world.Branch, BillingWorld.Now), (entry.CashierUserId, entry.BranchId, entry.PostedAtUtc));
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task T6_A_method_code_decides_the_kind_and_its_reference_rule()
    {
        var world = new BillingWorld();
        world.Methods.Items.AddRange(PaymentMethodDefinition.Defaults(BillingWorld.Now));
        var entries = new InMemoryCashEntries();

        var refusal = await Assert.ThrowsAsync<DomainException>(() => PostHandler(world, entries).Handle(new PostCashEntryCommand("CashOut", "BankDeposit", 1000m, "Daily deposit", MethodCode: "TRANSFER"), CancellationToken.None));
        Assert.Equal(BillingCodes.ReferenceRequired, refusal.Code);
        Assert.Equal(0, world.Numbers.Drawn);   // refused before a number is drawn

        await PostHandler(world, entries).Handle(new PostCashEntryCommand("CashOut", "BankDeposit", 1000m, "Daily deposit", MethodCode: "TRANSFER", Reference: "DEP-7"), CancellationToken.None);
        Assert.Equal((PaymentMethod.Transfer, "TRANSFER", "DEP-7"), (entries.Items[0].Method, entries.Items[0].MethodCode, entries.Items[0].Reference));
    }

    [Fact]
    public async Task T6_A_closed_period_refuses_entries_payments_and_refunds_BIL_061()
    {
        var world = new BillingWorld();
        world.Closings.CoversAnything = true;
        var visit = world.AddVisit(100m, "ACC-2026-00070");

        var entry = await Assert.ThrowsAsync<DomainException>(() => PostHandler(world, new InMemoryCashEntries()).Handle(new PostCashEntryCommand("CashIn", "OpeningFloat", 200m, "Float"), CancellationToken.None));
        var payment = await Assert.ThrowsAsync<DomainException>(() => new RecordPaymentHandler(world.Visits, world.Methods, world.Accounts, world.Closings, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock)
            .Handle(new RecordPaymentCommand(visit.VisitId, 10m, "Cash", null), CancellationToken.None));
        var refund = await Assert.ThrowsAsync<DomainException>(() => new RecordRefundHandler(world.Visits, world.Reasons, world.Methods, world.Accounts, world.Closings, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock)
            .Handle(new RecordRefundCommand(visit.VisitId, 10m, "Cash", null, world.RefundReasonId, null), CancellationToken.None));

        Assert.All([entry, payment, refund], refusal => Assert.Equal((BillingCodes.PeriodClosed, DomainFault.Conflict), (refusal.Code, refusal.Fault)));
        Assert.Equal((0, 0), (world.Numbers.Drawn, world.UnitOfWork.Commits));
    }

    [Fact]
    public async Task T7_A_void_is_refused_inside_a_closed_period_and_unknown_entries_are_not_found()
    {
        var world = new BillingWorld();
        var entries = new InMemoryCashEntries();
        var created = await PostHandler(world, entries).Handle(new PostCashEntryCommand("CashOut", "Supplier", 50m, "Courier"), CancellationToken.None);
        var handler = new VoidCashEntryHandler(entries, world.Closings, world.UnitOfWork, world.User, world.Clock);

        world.Closings.CoversAnything = true;
        Assert.Equal(BillingCodes.PeriodClosed, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new VoidCashEntryCommand(created.Id, "late"), CancellationToken.None))).Code);

        world.Closings.CoversAnything = false;
        await handler.Handle(new VoidCashEntryCommand(created.Id, "typed twice"), CancellationToken.None);
        Assert.Equal((CashEntryStatus.Voided, "typed twice"), (entries.Items[0].Status, entries.Items[0].VoidReason));
        Assert.Equal(BillingCodes.CashEntryNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new VoidCashEntryCommand(Guid.CreateVersion7(), "x"), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task The_ledger_of_today_reads_the_own_drawer_of_the_session_branch_and_offers_the_close()
    {
        var world = new BillingWorld();
        var visit = world.AddVisit(300m, "ACC-2026-00080");
        var ledger = new FakeLedger { Opening = new CashLedgerOpening(100m, Guid.CreateVersion7(), "CLS-2026-00003", BillingWorld.Now.AddHours(-20)) };
        ledger.Lines.Add(new CashLedgerLine(CashLedgerLineKind.Receipt, Guid.CreateVersion7(), "RCT-2026-00001", BillingWorld.Now.AddHours(-2), world.Cashier, visit.VisitId, PaymentMethod.Cash, "CASH", null, null, 300m, false));
        ledger.Lines.Add(new CashLedgerLine(CashLedgerLineKind.CashOut, Guid.CreateVersion7(), "CSH-2026-00001", BillingWorld.Now.AddHours(-1), world.Cashier, null, PaymentMethod.Cash, null, "Supplier", "Courier", 40m, false));

        var view = await LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, null, null), CancellationToken.None);

        var day = new DateOnly(2026, 10, 9);   // 17:00 at +03
        var dayStart = new DateTimeOffset(2026, 10, 8, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(new CashLedgerScope(world.Branch, world.Cashier, dayStart, dayStart.AddDays(1)), ledger.LastScope);
        Assert.Equal(("User", (Guid?)world.Cashier, "Cashier One", day, day, "Test+03"), (view.Scope, view.CashierUserId, view.CashierName, view.FromDay, view.ToDay, view.TimeZone));
        Assert.Equal((100m, "CLS-2026-00003", 300m, 40m, 360m), (view.Summary.OpeningBalance, view.Summary.OpeningClosingNumber, view.Summary.Receipts.Cash, view.Summary.CashOut, view.Summary.ClosingBalance));
        Assert.Equal(["Receipt", "CashOut"], view.Rows.Select(row => row.Kind).ToArray());
        Assert.Equal(("ACC-2026-00080", 400m, "Cashier One"), (view.Rows[0].Accession, view.Rows[0].Balance, view.Rows[0].CashierName));
        Assert.Equal(("Supplier", "Courier", 40m, 360m), (view.Rows[1].Category, view.Rows[1].Text, view.Rows[1].MoneyOut, view.Rows[1].Balance));
        Assert.Equal((dayStart, BillingWorld.Now), (view.CloseWindow!.FromUtc, view.CloseWindow.ToUtc));
        Assert.Empty(view.Cashiers);   // own scope: no cashier choice
        Assert.Equal(("Sky Lab", "Cashier One"), (view.Letterhead.LabNameEn, view.PrintedBy));
    }

    [Fact]
    public async Task The_ledger_of_the_branch_needs_the_right_and_reads_one_cashier_or_all_BIL_044()
    {
        var world = new BillingWorld();
        var ledger = new FakeLedger();
        var other = Guid.CreateVersion7();

        var everyone = await LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, "branch", null), CancellationToken.None);
        Assert.Equal(("Branch", (Guid?)null), (everyone.Scope, ledger.LastScope!.CashierUserId));
        var one = await LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, "Branch", other), CancellationToken.None);
        Assert.Equal(((Guid?)other, (Guid?)other), (one.CashierUserId, ledger.LastScope!.CashierUserId));

        var own = await LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, "User", other), CancellationToken.None);
        Assert.Equal((Guid?)world.Cashier, own.CashierUserId);   // the own drawer ignores another cashier

        world.Privileges.SpecialRights = SpecialRights.None;
        var refusal = await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, "Branch", null), CancellationToken.None));
        Assert.Equal((BillingCodes.BranchCollectionsRightRequired, DomainFault.Refused), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public async Task The_ledger_refuses_a_long_period_a_period_before_the_limit_too_many_lines_and_no_branch()
    {
        var world = new BillingWorld();
        var ledger = new FakeLedger();
        var today = new DateOnly(2026, 10, 9);

        Assert.Equal(BillingCodes.PeriodTooLong, (await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(today.AddDays(-31), today, null, null), CancellationToken.None))).Code);
        Assert.Equal(BillingCodes.PeriodInvalid, (await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, today.AddDays(-1), null, null), CancellationToken.None))).Code);

        var past = await LedgerHandler(world, ledger).Handle(new CashLedgerQuery(today.AddDays(-3), null, null, null), CancellationToken.None);
        Assert.Equal((today.AddDays(-3), today.AddDays(-3)), (past.FromDay, past.ToDay));   // one day when only the start is given
        Assert.Null(past.CloseWindow);   // a past day is read-only

        world.Privileges.SpecialRights = SpecialRights.None;   // report days limit 0: today only
        Assert.Equal(BillingCodes.ReportPeriodBeyondLimit, (await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(today.AddDays(-1), today, null, null), CancellationToken.None))).Code);

        world.Privileges.SpecialRights = SpecialRights.All;
        ledger.Overflow = true;
        Assert.Equal(BillingCodes.LedgerTooLarge, (await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, null, null), CancellationToken.None))).Code);

        world.User.BranchId = null;
        Assert.Equal(ProblemCodes.BranchRequired, (await Assert.ThrowsAsync<DomainException>(() => LedgerHandler(world, ledger).Handle(new CashLedgerQuery(null, null, null, null), CancellationToken.None))).Code);
    }
}

internal sealed class InMemoryCashEntries : ICashEntryRepository
{
    public List<CashEntry> Items { get; } = [];

    public Task<CashEntry?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(entry => entry.Id == id));

    public void Add(CashEntry aggregate) => Items.Add(aggregate);
}

internal sealed class FakeLedger : ICashLedgerReader
{
    public List<CashLedgerLine> Lines { get; } = [];

    public CashLedgerOpening Opening { get; set; } = CashLedgerOpening.None;

    /// <summary>Answers one line more than the limit (the handler must refuse).</summary>
    public bool Overflow { get; set; }

    public CashLedgerScope? LastScope { get; private set; }

    public Task<IReadOnlyList<CashLedgerLine>> LinesAsync(CashLedgerScope scope, int maxLines, CancellationToken cancellationToken)
    {
        LastScope = scope;
        if (Overflow)
        {
            var filler = new CashLedgerLine(CashLedgerLineKind.Receipt, Guid.CreateVersion7(), "RCT", scope.FromUtc, Guid.CreateVersion7(), null, PaymentMethod.Cash, null, null, null, 1m, false);
            return Task.FromResult<IReadOnlyList<CashLedgerLine>>(Enumerable.Repeat(filler, maxLines).ToList());
        }

        return Task.FromResult<IReadOnlyList<CashLedgerLine>>([.. Lines]);
    }

    public Task<CashLedgerOpening> OpeningAsync(Guid branchId, Guid? cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken) => Task.FromResult(Opening);
}
