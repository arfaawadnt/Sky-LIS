using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Billing;
using Platform.Application.Identity;
using Platform.Application.Registration;
using Platform.Application.Treasury;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Identity;
using Platform.Domain.Treasury;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-BIL-001: tenant invisibility of accounts, payments, refunds and closings; unique
/// numbers; the xmin serialisation of two cashiers; the immutability triggers; the read ports over Registration's tables
/// (raw SQL on the MODULES.md §5 columns) and the BR-27 lookup over the Treasury tables.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BillingModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private const string Device = "device-billing-1";

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
        accountA.RecordPayment("RCT-RACE-A", 99m, PaymentMethod.Cash, null, world.Station, new VisitCharge(100m, false), Now);
        accountB.RecordPayment("RCT-RACE-B", 99m, PaymentMethod.Cash, null, world.Station, new VisitCharge(100m, false), Now);

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
            account.RecordPayment(number, 60m, PaymentMethod.Cash, null, world.Station, new VisitCharge(100m, false), Now);
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
    public async Task The_station_lookup_follows_the_live_approval_for_user_treasury_and_device()
    {
        var world = await WorldAsync();
        using (var scope = fixture.AppScope(world.Tenant, world.Cashier))
        {
            var stations = scope.ServiceProvider.GetRequiredService<ITreasuryStationLookup>();
            Assert.True((await stations.GetAsync(world.Treasury, world.Cashier, Device, CancellationToken.None))!.ApprovedForDevice);
            Assert.False((await stations.GetAsync(world.Treasury, world.Cashier, "another-device", CancellationToken.None))!.ApprovedForDevice);
            Assert.Equal([world.Treasury], (await stations.ApprovedAsync(world.Cashier, Device, world.Branch, CancellationToken.None)).Select(item => item.Id).ToArray());
            Assert.Empty(await stations.ApprovedAsync(world.Cashier, Device, Guid.CreateVersion7(), CancellationToken.None));
        }

        using (var scope = fixture.AppScope(world.Tenant, world.Approver))
        {
            var request = (await scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>().GetForUpdateAsync(world.Request, CancellationToken.None))!;
            request.Revoke(world.Approver, Now, "device reassigned");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var after = fixture.AppScope(world.Tenant, world.Cashier);
        Assert.False((await after.ServiceProvider.GetRequiredService<ITreasuryStationLookup>().GetAsync(world.Treasury, world.Cashier, Device, CancellationToken.None))!.ApprovedForDevice);
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
            var totals = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Treasury, null, Now.AddHours(-1), Now.AddHours(1)), CancellationToken.None);
            Assert.Equal((700m, 100m, 3, 20m, 1, 680m), (totals.CashPayments, totals.CardPayments, totals.PaymentCount, totals.CashRefunds, totals.RefundCount, totals.ExpectedCash));
            Assert.Equal((100m, 25m, 2), (totals.OutstandingBalance, totals.DiscountTotal, totals.VisitCount));   // A owes 100, B is settled
            var movements = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().MovementsAsync(new DrawerScope(world.Treasury, world.Cashier, Now.AddHours(-1), Now.AddHours(1)), 100, CancellationToken.None);
            Assert.Equal(5, movements.Count);   // four payments (one voided) and one refund
            Assert.Contains(movements, movement => movement.Status == "Voided");
        }

        await CloseAsync(world, Now.AddHours(-1), Now.AddHours(1));

        using var reader = fixture.AppScope(world.Tenant, world.Cashier);
        var closings = reader.ServiceProvider.GetRequiredService<ICashClosingRepository>();
        Assert.True(await closings.CoversAsync(world.Treasury, world.Cashier, Now, CancellationToken.None));
        Assert.True(await closings.OverlapsAsync(world.Treasury, null, Now, Now.AddHours(2), CancellationToken.None));
        Assert.False(await closings.OverlapsAsync(world.Treasury, null, Now.AddHours(1), Now.AddHours(2), CancellationToken.None));
        var page = await reader.ServiceProvider.GetRequiredService<IVisitAccountRepository>().ListPaymentsAsync(new PaymentFilter(visitA, null, null, null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.All(page.Items, item => Assert.True(item.IsFrozen));
        Assert.All(page.Items, item => Assert.Equal(("MAIN", "Billing Cashier"), (item.TreasuryCode[..4], item.CashierName)));
        var record = (await closings.GetRecordAsync((await closings.ListAsync(new CashClosingFilter(world.Treasury, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).Items.Single().Id, CancellationToken.None))!;
        Assert.Equal((680m, 680m, 0m), (record.Totals.ExpectedCash, record.Item.ExpectedCash, record.Item.Difference));
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

    private sealed record World(Guid Tenant, Guid Branch, Guid Cashier, Guid Approver, Guid Treasury, Guid Request, CashierStation Station);

    private async Task<World> WorldAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var cashier = await RegisterUserAsync("Billing Cashier");
        var approver = await RegisterUserAsync("Billing Approver");

        Guid treasuryId;
        using (var scope = fixture.AppScope(tenant, approver))
        {
            var treasury = Domain.Treasury.Treasury.Create("MAIN" + Guid.CreateVersion7().ToString("N")[..6], new LocalizedText("Main drawer", "الخزينة الرئيسية"), branch, Now);
            scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().Add(treasury);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            treasuryId = treasury.Id;
        }

        Guid requestId;
        using (var scope = fixture.AppScope(tenant, cashier))
        {
            var treasury = (await scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().GetForUpdateAsync(treasuryId, CancellationToken.None))!;
            var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(TreasuryAccessRequest.NumberKind, TreasuryAccessRequest.NumberPrefix, Now, CancellationToken.None);
            var request = TreasuryAccessRequest.Request(treasury, number, cashier, Device, "Billing PC", Now);
            scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>().Add(request);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            requestId = request.Id;
        }

        using (var scope = fixture.AppScope(tenant, approver))
        {
            var request = (await scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>().GetForUpdateAsync(requestId, CancellationToken.None))!;
            request.Approve(approver, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        return new World(tenant, branch, cashier, approver, treasuryId, requestId, new CashierStation(treasuryId, branch, true, true, Device, cashier, branch));
    }

    private async Task<Guid> RegisterUserAsync(string displayName)
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"bil.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task<Guid> PayAsync(World world, Guid visit, decimal amount, PaymentMethod method = PaymentMethod.Cash, string? reference = null, string? number = null)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var accounts = scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>();
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var existing = await accounts.GetForUpdateAsync(visit, CancellationToken.None);
        var account = existing ?? VisitAccount.Open(visit);
        number ??= await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Payment.NumberKind, Payment.NumberPrefix, Now, CancellationToken.None);
        var payment = account.RecordPayment(number, amount, method, reference, world.Station, share, Now);
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

    private async Task RefundAsync(World world, Guid visit, decimal amount)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var account = (await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Refund.NumberKind, Refund.NumberPrefix, Now, CancellationToken.None);
        account.RecordRefund(number, amount, PaymentMethod.Cash, null, Guid.CreateVersion7(), "integration", world.Station, share, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task CloseAsync(World world, DateTimeOffset from, DateTimeOffset to)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver, branchId: world.Branch);
        var totals = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Treasury, null, from, to), CancellationToken.None);
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(CashClosing.NumberKind, CashClosing.NumberPrefix, Now, CancellationToken.None);
        var closing = CashClosing.Close(number, world.Treasury, world.Branch, world.Branch, null, from, to, totals, totals.ExpectedCash, null, world.Approver, to);
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
