using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Billing;
using Platform.Application.Identity;
using Platform.Application.Registration;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Identity;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-BIL-001: tenant invisibility of accounts, payments, refunds and closings; unique
/// numbers; the xmin serialisation of two cashiers; the immutability triggers; the read ports over Registration's tables
/// (raw SQL on the MODULES.md §5 columns); since LIS-CR-004 (2026-10-20) the drawer is the branch and the cashier, and the
/// movements posted under the withdrawn treasury stay readable.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BillingModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Accounts_payments_refunds_and_closings_of_another_tenant_are_invisible()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-ISO-1", 300m);
        await PayAsync(world, visit, 300m);
        await LowerShareAsync(world.Tenant, visit, 200m);
        await RefundAsync(world, visit, 100m);
        await CloseAsync(world, Now.AddHours(-1), Now.AddHours(1));

        var other = Guid.CreateVersion7();
        using var scope = fixture.AppScope(other, world.Cashier);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<VisitAccount>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await context.Set<Payment>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await context.Set<Refund>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await context.Set<CashClosing>().IgnoreQueryFilters().CountAsync());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IVisitBalanceLookup>().GetAsync(visit, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Receipt_numbers_are_unique_per_tenant()
    {
        var world = await WorldAsync();
        var first = await InsertVisitAsync(world.Tenant, "ACC-UNQ-1", 100m);
        var second = await InsertVisitAsync(world.Tenant, "ACC-UNQ-2", 100m);
        await PayAsync(world, first, 10m, number: "RCT-2026-77777");

        var refusal = await Assert.ThrowsAsync<DomainException>(() => PayAsync(world, second, 10m, number: "RCT-2026-77777"));

        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);
    }

    [DatabaseFact]
    public async Task Two_cashiers_paying_the_same_visit_at_once_are_serialised_by_the_account_row()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-RACE-1", 100m);
        await PayAsync(world, visit, 1m);

        using var first = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        using var second = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var accountA = (await first.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        var accountB = (await second.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        accountA.RecordPayment("RCT-RACE-A", 99m, PaymentMethod.Cash, null, world.Desk, new VisitCharge(100m, false), Now);
        accountB.RecordPayment("RCT-RACE-B", 99m, PaymentMethod.Cash, null, world.Desk, new VisitCharge(100m, false), Now);

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        var refusal = await Assert.ThrowsAsync<DomainException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        Assert.Equal(ProblemCodes.ConcurrencyConflict, refusal.Code);
        var balance = await Balance(world, visit);
        Assert.Equal((100m, 0m), (balance.Paid, balance.Remaining));
    }

    [DatabaseFact]
    public async Task Two_first_payments_of_one_visit_collide_on_the_account_key()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-RACE-2", 100m);

        using var first = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        using var second = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        foreach (var (scope, number) in new[] { (first, "RCT-OPEN-A"), (second, "RCT-OPEN-B") })
        {
            var account = VisitAccount.Open(visit);
            account.RecordPayment(number, 60m, PaymentMethod.Cash, null, world.Desk, new VisitCharge(100m, false), Now);
            scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().Add(account);
        }

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        var refusal = await Assert.ThrowsAsync<DomainException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);
    }

    [DatabaseFact]
    public async Task The_balance_lookup_counts_posted_payments_and_refunds_only()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-BAL-1", 300m);
        var voided = await PayAsync(world, visit, 100m);
        await PayAsync(world, visit, 200m);
        await VoidAsync(world, voided);
        await PayAsync(world, visit, 100m);
        await LowerShareAsync(world.Tenant, visit, 250m);
        await RefundAsync(world, visit, 30m);

        var balance = await Balance(world, visit);

        // share 250 − posted (200 + 100) + refunds 30 = −20 → 20 refundable
        Assert.Equal((250m, 300m, 30m, -20m, 20m), (balance.PatientShareTotal, balance.Paid, balance.Refunded, balance.Remaining, balance.Refundable));
        using var scope = fixture.AppScope(world.Tenant, world.Cashier);
        var many = await scope.ServiceProvider.GetRequiredService<IVisitBalanceLookup>().GetManyAsync([visit, Guid.CreateVersion7()], CancellationToken.None);
        Assert.Equal([visit], many.Keys.ToArray());
    }

    [DatabaseFact]
    public async Task The_visit_lookup_reads_the_agreed_columns_lines_and_batches()
    {
        var world = await WorldAsync();
        var batch = await InsertBatchAsync(world.Tenant, "BTH-2026-00042");
        var second = await InsertVisitAsync(world.Tenant, "ACC-LKP-2", 150m, batch);
        var first = await InsertVisitAsync(world.Tenant, "ACC-LKP-1", 100m, batch);
        var service = Guid.CreateVersion7();
        await InsertLineAsync(world.Tenant, first, service, "Ordered", 120m, 100m, 1);
        await InsertLineAsync(world.Tenant, first, Guid.CreateVersion7(), "Cancelled", 50m, 50m, 2);

        using var scope = fixture.AppScope(world.Tenant, world.Cashier);
        var lookup = scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>();
        var byAccession = (await lookup.GetByAccessionAsync(" acc-lkp-1 ", CancellationToken.None))!;
        Assert.Equal((first, 100m, "BTH-2026-00042", "Group", false), (byAccession.VisitId, byAccession.PatientShareTotal, byAccession.BatchNumber, byAccession.BatchKind, byAccession.BilledThroughClaims));
        var line = Assert.Single(await lookup.LinesAsync(first, CancellationToken.None));
        Assert.Equal((service, "Ordered", 100m), (line.ServiceId, line.Status, line.PatientShare));
        Assert.Equal(["ACC-LKP-1", "ACC-LKP-2"], (await lookup.BatchMembersAsync(batch, CancellationToken.None)).Select(member => member.Accession).ToArray());
        Assert.Equal(batch, (await lookup.GetBatchByNumberAsync("bth-2026-00042", CancellationToken.None))!.BatchId);
        Assert.Equal(2, (await lookup.GetManyAsync([first, second], CancellationToken.None)).Count);
    }

    [DatabaseFact]
    public async Task New_movements_carry_no_treasury_and_a_movement_posted_under_the_treasury_stays_in_the_registers()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-LEG-1", 500m);
        var current = await PayAsync(world, visit, 100m);
        var legacyTreasury = Guid.CreateVersion7();   // app.treasuries is an orphan table: no foreign key any more
        var legacy = Guid.CreateVersion7();

        using (var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch))
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await context.Set<Payment>().AsNoTracking().SingleAsync(payment => payment.Id == current);
            Assert.Equal(((Guid?)null, (string?)null, world.Branch, world.Cashier), (stored.TreasuryId, stored.DeviceId, stored.BranchId, stored.CashierUserId));

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.payments (tenant_id, id, visit_id, receipt_number, amount, method, treasury_id, device_id, cashier_user_id, branch_id, paid_at_utc, status)
                VALUES ({world.Tenant}, {legacy}, {visit}, {"RCT-LEGACY-1"}, {50m}, {"Cash"}, {legacyTreasury}, {"legacy-device"}, {world.Cashier}, {world.Branch}, {Now}, {"Posted"})
                """);
        }

        using var reader = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var page = await reader.ServiceProvider.GetRequiredService<IVisitAccountRepository>().ListPaymentsAsync(new PaymentFilter(visit, null, null, null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(new[] { current, legacy }.Order().ToArray(), page.Items.Select(item => item.Id).Order().ToArray());
        var account = (await reader.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        var old = account.Payments.Single(payment => payment.Id == legacy);
        Assert.Equal(((Guid?)legacyTreasury, "legacy-device"), (old.TreasuryId, old.DeviceId));

        // the branch drawer counts both; the sheet of a closing made under the treasury narrows to that drawer
        var drawer = reader.ServiceProvider.GetRequiredService<ICashDrawerQueries>();
        Assert.Equal(150m, (await drawer.TotalsAsync(new DrawerScope(world.Branch, null, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None)).CashPayments);
        var narrowed = await drawer.MovementsAsync(new DrawerScope(world.Branch, null, Now.AddHours(-1), Now.AddHours(1), legacyTreasury), 100, CancellationToken.None);
        Assert.Equal(legacy, Assert.Single(narrowed).Id);
        Assert.Empty(await drawer.MovementsAsync(new DrawerScope(Guid.CreateVersion7(), null, Now.AddHours(-1), Now.AddHours(1)), 100, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_drawer_totals_follow_BR_16_and_a_closing_freezes_its_payments()
    {
        var world = await WorldAsync();
        var visitA = await InsertVisitAsync(world.Tenant, "ACC-DRW-1", 500m, discount: 25m);
        var visitB = await InsertVisitAsync(world.Tenant, "ACC-DRW-2", 400m);
        await PayAsync(world, visitA, 300m);
        await PayAsync(world, visitA, 100m, PaymentMethod.Card, "SLIP-1");
        var voided = await PayAsync(world, visitB, 50m);
        await VoidAsync(world, voided);
        await PayAsync(world, visitB, 400m);
        await LowerShareAsync(world.Tenant, visitB, 380m);
        await RefundAsync(world, visitB, 20m);

        using (var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch))
        {
            var totals = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Branch, null, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None);
            Assert.Equal((700m, 100m, 3, 20m, 1, 680m), (totals.CashPayments, totals.CardPayments, totals.PaymentCount, totals.CashRefunds, totals.RefundCount, totals.ExpectedCash));
            Assert.Equal((100m, 25m, 2), (totals.OutstandingBalance, totals.DiscountTotal, totals.VisitCount));   // A owes 100, B is settled
            var movements = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().MovementsAsync(new DrawerScope(world.Branch, world.Cashier, Now.AddHours(-1), Now.AddHours(1)), 100, CancellationToken.None);
            Assert.Equal(5, movements.Count);   // four payments (one voided) and one refund
            Assert.Contains(movements, movement => movement.Status == "Voided");
        }

        await CloseAsync(world, Now.AddHours(-1), Now.AddHours(1));

        using var reader = fixture.AppScope(world.Tenant, world.Cashier);
        var closings = reader.ServiceProvider.GetRequiredService<ICashClosingRepository>();
        Assert.True(await closings.CoversAsync(world.Branch, world.Cashier, Now, CancellationToken.None));
        Assert.False(await closings.CoversAsync(Guid.CreateVersion7(), world.Cashier, Now, CancellationToken.None));   // another branch is another drawer
        Assert.True(await closings.OverlapsAsync(world.Branch, null, Now, Now.AddHours(2), CancellationToken.None));
        Assert.False(await closings.OverlapsAsync(world.Branch, null, Now.AddHours(1), Now.AddHours(2), CancellationToken.None));
        var page = await reader.ServiceProvider.GetRequiredService<IVisitAccountRepository>().ListPaymentsAsync(new PaymentFilter(visitA, null, null, null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.All(page.Items, item => Assert.True(item.IsFrozen));
        Assert.All(page.Items, item => Assert.Equal((world.Branch, "Billing Cashier"), (item.BranchId, item.CashierName)));
        var record = (await closings.GetRecordAsync((await closings.ListAsync(new CashClosingFilter(world.Branch, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).Items.Single().Id, CancellationToken.None))!;
        Assert.Equal((680m, 680m, 0m), (record.Totals.ExpectedCash, record.Item.ExpectedCash, record.Item.Difference));
        Assert.Equal((world.Branch, world.Branch, (Guid?)null), (record.Item.BranchId, record.BranchId, record.LegacyTreasuryId));
        Assert.Empty((await closings.ListAsync(new CashClosingFilter(Guid.CreateVersion7(), null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).Items);
    }

    [DatabaseFact]
    public async Task The_collections_cells_sum_posted_payments_and_refunds_per_branch_cashier_and_method()
    {
        var world = await WorldAsync();
        var second = await RegisterUserAsync("Billing Cashier Two");
        var secondStation = world.Desk with { UserId = second };
        var visitA = await InsertVisitAsync(world.Tenant, "ACC-COL-1", 500m);
        var visitB = await InsertVisitAsync(world.Tenant, "ACC-COL-2", 400m);
        await PayAsync(world, visitA, 300m);
        await PayAsync(world, visitA, 100m, PaymentMethod.Card, "SLIP-1");
        var voided = await PayAsync(world, visitB, 50m);
        await VoidAsync(world, voided);
        await PayAsync(world, visitB, 400m, station: secondStation);
        await LowerShareAsync(world.Tenant, visitB, 380m);
        await RefundAsync(world, visitB, 20m, station: secondStation);

        using (var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch))
        {
            var queries = scope.ServiceProvider.GetRequiredService<ICollectionsReportQueries>();
            var branchCells = await queries.CellsAsync(new CollectionsScope(null, world.Branch, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None);
            Assert.Equal(3, branchCells.Count);   // (cashier 1, cash), (cashier 1, card), (cashier 2, cash); the voided payment is not counted

            var (byMethod, totals) = CollectionsReport.Group(branchCells, CollectionsReportGrouping.Method);
            var cash = Assert.Single(byMethod, group => group.Key == "Cash").Figures;
            Assert.Equal((700m, 20m, 680m, 2, 1), (cash.Cash.Payments, cash.Cash.Refunds, cash.Cash.Net, cash.PaymentCount, cash.RefundCount));
            Assert.Equal(100m, Assert.Single(byMethod, group => group.Key == "Card").Figures.Card.Payments);
            Assert.Equal((800m, 20m, 780m, 3, 1), (totals.TotalPayments, totals.TotalRefunds, totals.Net, totals.PaymentCount, totals.RefundCount));

            var (byUser, _) = CollectionsReport.Group(branchCells, CollectionsReportGrouping.User);
            Assert.Equal(400m, Assert.Single(byUser, group => group.Key == world.Cashier.ToString("D")).Figures.Net);
            Assert.Equal(380m, Assert.Single(byUser, group => group.Key == second.ToString("D")).Figures.Net);

            var own = await queries.CellsAsync(new CollectionsScope(world.Cashier, null, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None);
            Assert.All(own, cell => Assert.Equal(world.Cashier, cell.CashierUserId));
            Assert.Equal(400m, CollectionsReport.Group(own, CollectionsReportGrouping.Method).Totals.Net);

            Assert.Empty(await queries.CellsAsync(new CollectionsScope(null, Guid.CreateVersion7(), Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None));
            Assert.Empty(await queries.CellsAsync(new CollectionsScope(null, world.Branch, Now.AddHours(1), Now.AddHours(2)), CancellationToken.None));
        }

        using var other = fixture.AppScope(Guid.CreateVersion7(), world.Cashier, branchId: world.Branch);
        Assert.Empty(await other.ServiceProvider.GetRequiredService<ICollectionsReportQueries>().CellsAsync(new CollectionsScope(null, world.Branch, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Money_rows_are_immutable_in_the_database()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-IMM-1", 100m);
        var payment = await PayAsync(world, visit, 100m);
        await LowerShareAsync(world.Tenant, visit, 90m);
        await RefundAsync(world, visit, 10m);
        await VoidAsync(world, payment);

        using var scope = fixture.AppScope(world.Tenant, world.Cashier);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE app.payments SET void_reason = 'edited' WHERE id = {0}", payment));
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE app.refunds SET amount = 1 WHERE visit_id = {0}", visit));
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM app.payments WHERE id = {0}", payment));
    }

    // ---- world --------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid Cashier, Guid Approver, Cashier Desk);

    private async Task<World> WorldAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var cashier = await RegisterUserAsync("Billing Cashier");
        var approver = await RegisterUserAsync("Billing Approver");

        return new World(tenant, branch, cashier, approver, new Cashier(cashier, branch));
    }

    private async Task<Guid> RegisterUserAsync(string displayName)
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"bil.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task<Guid> PayAsync(World world, Guid visit, decimal amount, PaymentMethod method = PaymentMethod.Cash, string? reference = null, string? number = null, Cashier? station = null)
    {
        var at = station ?? world.Desk;
        using var scope = fixture.AppScope(world.Tenant, at.UserId, branchId: world.Branch);
        var accounts = scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>();
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var existing = await accounts.GetForUpdateAsync(visit, CancellationToken.None);
        var account = existing ?? VisitAccount.Open(visit);
        number ??= await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Payment.NumberKind, Payment.NumberPrefix, Now, CancellationToken.None);
        var payment = account.RecordPayment(number, amount, method, reference, at, share, Now);
        if (existing is null)
        {
            accounts.Add(account);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return payment.Id;
    }

    private async Task VoidAsync(World world, Guid paymentId)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver);
        var account = (await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetByPaymentIdForUpdateAsync(paymentId, CancellationToken.None))!;
        account.VoidPayment(paymentId, world.Approver, "integration void", frozenByClosing: false, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefundAsync(World world, Guid visit, decimal amount, Cashier? station = null)
    {
        var at = station ?? world.Desk;
        using var scope = fixture.AppScope(world.Tenant, at.UserId, branchId: world.Branch);
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var account = (await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Refund.NumberKind, Refund.NumberPrefix, Now, CancellationToken.None);
        account.RecordRefund(number, amount, PaymentMethod.Cash, null, Guid.CreateVersion7(), "integration", at, share, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task CloseAsync(World world, DateTimeOffset from, DateTimeOffset to)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver, branchId: world.Branch);
        var totals = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Branch, null, from, to), CancellationToken.None);
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(CashClosing.NumberKind, CashClosing.NumberPrefix, Now, CancellationToken.None);
        var closing = CashClosing.Close(number, world.Branch, null, from, to, totals, totals.ExpectedCash, null, world.Approver, to);
        scope.ServiceProvider.GetRequiredService<ICashClosingRepository>().Add(closing);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<VisitBalance> Balance(World world, Guid visit)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Cashier);
        return (await scope.ServiceProvider.GetRequiredService<IVisitBalanceLookup>().GetAsync(visit, CancellationToken.None))!;
    }

    // ---- Registration rows: raw SQL with the MODULES.md §5 columns and the NOT NULL columns of Registration's tables (no DB
    // defaults). Nulls are written as literals: an untyped null parameter has no store type. DELETE is refused on these tables.

    private const string InsertVisitSql = """
        INSERT INTO app.visits (id, tenant_id, accession, patient_id, branch_id, location_type, payer_id, contract_tier_id, batch_id, urgent, biohazard,
                                collected_outside, utc_offset_minutes, status, registered_at_utc, registered_by_user_id, total_list_price, discount_total,
                                patient_share_total, payer_share_total, discount_value, revision, created_at_utc)
        VALUES ({0}, {1}, {2}, {3}, {4}, 'Walkin', {10}, {11}, @batch, false, false, false, 0, 'Open', {5}, {6}, {7}, {8}, {9}, 0, 0, 1, {5})
        """;

    private const string InsertBatchSql = """
        INSERT INTO app.batches (id, tenant_id, number, kind, branch_id, payer_id, contract_tier_id, sending_lab_id, registered_at_utc, registered_by_user_id,
                                 member_count, created_at_utc)
        VALUES ({0}, {1}, {2}, 'Group', {3}, {4}, {5}, NULL, {6}, {7}, 0, {6})
        """;

    private const string InsertLineSql = """
        INSERT INTO app.visit_lines (id, tenant_id, visit_id, service_id, service_code, service_name_en, service_name_ar, service_type, kind, status,
                                     list_price, patient_share, payer_share, discount, test_ids, origin, sequence, ordered_at_utc, ordered_by_user_id,
                                     cancelled_at_utc, cancellation_reason, cancelled_by_user_id)
        VALUES ({0}, {1}, {2}, {3}, 'CBC', 'Complete blood count', 'صورة دم كاملة', 'Test', 'Service', {4},
                {5}, {6}, {7}, 0, ARRAY[]::uuid[], 'Manual', {8}, {9}, {10}, @cancelled)
        """;

    private async Task<Guid> InsertVisitAsync(Guid tenant, string accession, decimal patientShare, Guid? batchId = null, decimal discount = 0m)
    {
        var id = Guid.CreateVersion7();
        using var scope = fixture.AppScope(tenant);
        var sql = InsertVisitSql.Replace("@batch", batchId is null ? "NULL" : "{12}", StringComparison.Ordinal);
        var values = new List<object> { id, tenant, accession, Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Guid.CreateVersion7(), patientShare + discount, discount, patientShare, Guid.CreateVersion7(), Guid.CreateVersion7() };
        if (batchId is { } batch)
        {
            values.Add(batch);
        }

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql, values);
        return id;
    }

    private async Task LowerShareAsync(Guid tenant, Guid visit, decimal patientShare)
    {
        using var scope = fixture.AppScope(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE app.visits SET patient_share_total = {0} WHERE tenant_id = {1} AND id = {2}", patientShare, tenant, visit);
    }

    private async Task<Guid> InsertBatchAsync(Guid tenant, string number)
    {
        var id = Guid.CreateVersion7();
        using var scope = fixture.AppScope(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            InsertBatchSql, id, tenant, number, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Guid.CreateVersion7());
        return id;
    }

    private async Task InsertLineAsync(Guid tenant, Guid visit, Guid service, string status, decimal listPrice, decimal patientShare, int sequence)
    {
        using var scope = fixture.AppScope(tenant);
        // A cancelled line carries its cancellation (ck_visit_lines_cancellation): the time {9}, a reason and the actor {10}.
        var sql = InsertLineSql.Replace("@cancelled", status == "Cancelled" ? "{9}, 'integration test', {10}" : "NULL, NULL, NULL", StringComparison.Ordinal);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            sql, Guid.CreateVersion7(), tenant, visit, service, status, listPrice, patientShare, listPrice - patientShare, sequence, Now, Guid.CreateVersion7());
    }
}
