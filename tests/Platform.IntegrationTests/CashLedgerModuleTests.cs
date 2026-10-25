using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Billing;
using Platform.Application.Identity;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Identity;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of the cash ledger (change request LIS-CR-011, intake LIS-INT-BIL-001 §18): tenant invisibility and unique
/// numbers of cash entries, the immutability trigger, the UNION ALL reader of the ledger (lines, scopes, opening balance) and
/// the closing totals that now carry the opening balance and the cash entries.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashLedgerModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly PaymentMethodUse Cash = PaymentMethodUse.Legacy(PaymentMethod.Cash);

    [DatabaseFact]
    public async Task Cash_entries_of_another_tenant_are_invisible_and_numbers_are_unique_per_tenant()
    {
        var world = await WorldAsync();
        var entry = await PostAsync(world, world.Desk, CashEntryKind.CashOut, CashEntryCategory.Supplier, 50m, Now, number: "CSH-2026-77777");

        var duplicate = await Assert.ThrowsAsync<DomainException>(() => PostAsync(world, world.Desk, CashEntryKind.CashIn, CashEntryCategory.OpeningFloat, 10m, Now, number: "CSH-2026-77777"));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicate.Code);

        using var scope = fixture.AppScope(Guid.CreateVersion7(), world.Cashier);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CashEntry>().IgnoreQueryFilters().CountAsync());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ICashEntryRepository>().GetForUpdateAsync(entry, CancellationToken.None));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ICashLedgerReader>().LinesAsync(new CashLedgerScope(world.Branch, null, Now.AddHours(-1), Now.AddHours(1)), 100, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task A_cash_entry_changes_only_by_its_void_once()
    {
        var world = await WorldAsync();
        var entry = await PostAsync(world, world.Desk, CashEntryKind.CashOut, CashEntryCategory.PettyCash, 30m, Now);

        using (var scope = fixture.AppScope(world.Tenant, world.Approver))
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE app.cash_entries SET amount = 1 WHERE id = {0}", entry));
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM app.cash_entries WHERE id = {0}", entry));
        }

        await VoidAsync(world, entry, "typed twice");
        using (var scope = fixture.AppScope(world.Tenant, world.Approver))
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CashEntry>().AsNoTracking().SingleAsync(row => row.Id == entry);
            Assert.Equal((CashEntryStatus.Voided, "typed twice", (Guid?)world.Approver), (stored.Status, stored.VoidReason, stored.VoidedByUserId));
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE app.cash_entries SET void_reason = 'edited' WHERE id = {0}", entry));
        }
    }

    [DatabaseFact]
    public async Task The_ledger_lines_union_receipts_refunds_entries_and_the_closings_of_the_scope()
    {
        var world = await WorldAsync();
        var second = await RegisterUserAsync("Ledger Cashier Two");
        var otherDesk = new Cashier(second, world.Branch);
        var visit = await InsertVisitAsync(world.Tenant, "ACC-LDG-1", 500m);

        await PayAsync(world, world.Desk, visit, 300m, Now.AddMinutes(10));
        var voided = await PayAsync(world, world.Desk, visit, 50m, Now.AddMinutes(20));
        await VoidPaymentAsync(world, voided);
        await PayAsync(world, world.Desk, visit, 100m, Now.AddMinutes(30), PaymentMethod.Card, "SLIP-1");
        await LowerShareAsync(world.Tenant, visit, 380m);
        await RefundAsync(world, world.Desk, visit, 20m, Now.AddMinutes(40));
        await PostAsync(world, world.Desk, CashEntryKind.CashOut, CashEntryCategory.Supplier, 70m, Now.AddMinutes(50));
        await PostAsync(world, world.Desk, CashEntryKind.CashIn, CashEntryCategory.OpeningFloat, 25m, Now.AddMinutes(5));
        await PostAsync(world, otherDesk, CashEntryKind.CashOut, CashEntryCategory.Other, 999m, Now.AddMinutes(15));   // another cashier's drawer
        await CloseAsync(world, world.Cashier, Now, Now.AddMinutes(60), counted: 230m, note: "5 short");

        using var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var reader = scope.ServiceProvider.GetRequiredService<ICashLedgerReader>();
        var lines = await reader.LinesAsync(new CashLedgerScope(world.Branch, world.Cashier, Now, Now.AddHours(2)), 100, CancellationToken.None);

        Assert.Equal(
            [CashLedgerLineKind.CashIn, CashLedgerLineKind.Receipt, CashLedgerLineKind.Receipt, CashLedgerLineKind.Receipt, CashLedgerLineKind.Refund, CashLedgerLineKind.CashOut, CashLedgerLineKind.Closing],
            lines.Select(line => line.Kind).ToArray());
        Assert.True(lines[2].IsVoided);
        Assert.Equal((PaymentMethod.Card, (Guid?)visit), (lines[3].Method, lines[3].VisitId));
        Assert.Equal(("Supplier", "integration"), (lines[5].Category, lines[5].Text));
        var closing = lines[6];
        Assert.Equal((235m, 230m, -5m, "5 short", world.Approver), (closing.Expected, closing.Counted, closing.Difference, closing.Text, closing.CashierUserId));

        var (_, summary) = CashLedger.Build(CashLedgerOpening.None, lines);
        Assert.Equal((300m, 100m, 20m, 25m, 70m, 230m), (summary.Receipts.Cash, summary.Receipts.Card, summary.Refunds.Cash, summary.CashIn, summary.CashOut, summary.ClosingBalance));

        var everyone = await reader.LinesAsync(new CashLedgerScope(world.Branch, null, Now, Now.AddHours(2)), 100, CancellationToken.None);
        Assert.Contains(everyone, line => line.Amount == 999m);
        Assert.DoesNotContain(everyone, line => line.Kind == CashLedgerLineKind.Closing);   // the cashier's closing is not the branch book

        Assert.Equal(3, (await reader.LinesAsync(new CashLedgerScope(world.Branch, world.Cashier, Now, Now.AddHours(2)), 3, CancellationToken.None)).Count);
    }

    [DatabaseFact]
    public async Task The_opening_is_the_previous_count_plus_the_cash_moved_since_and_the_closing_totals_carry_it()
    {
        var world = await WorldAsync();
        var visit = await InsertVisitAsync(world.Tenant, "ACC-LDG-2", 1000m);

        using (var scope = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch))
        {
            Assert.Equal(CashLedgerOpening.None, await scope.ServiceProvider.GetRequiredService<ICashLedgerReader>().OpeningAsync(world.Branch, world.Cashier, Now, CancellationToken.None));
        }

        await PayAsync(world, world.Desk, visit, 200m, Now.AddMinutes(10));
        await CloseAsync(world, world.Cashier, Now, Now.AddHours(1), counted: 190m, note: "10 short");
        await PayAsync(world, world.Desk, visit, 100m, Now.AddHours(2));                                          // after the close, before the next ledger
        await PostAsync(world, world.Desk, CashEntryKind.CashOut, CashEntryCategory.BankDeposit, 150m, Now.AddHours(3));
        await PayAsync(world, world.Desk, visit, 40m, Now.AddHours(5));                                           // inside the next period
        await PostAsync(world, world.Desk, CashEntryKind.CashIn, CashEntryCategory.OtherIncome, 15m, Now.AddHours(5).AddMinutes(1));

        using var reader = fixture.AppScope(world.Tenant, world.Cashier, branchId: world.Branch);
        var opening = await reader.ServiceProvider.GetRequiredService<ICashLedgerReader>().OpeningAsync(world.Branch, world.Cashier, Now.AddHours(4), CancellationToken.None);
        Assert.Equal((190m + 100m - 150m, "CLS-LDG-1", (DateTimeOffset?)Now.AddHours(1)), (opening.Balance, opening.ClosingNumber, opening.ClosedThroughUtc));

        var branchWide = await reader.ServiceProvider.GetRequiredService<ICashLedgerReader>().OpeningAsync(world.Branch, null, Now.AddHours(4), CancellationToken.None);
        Assert.Equal(CashLedgerOpening.None, branchWide);   // the branch book was never closed

        var totals = await reader.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Branch, world.Cashier, Now.AddHours(4), Now.AddHours(6)), CancellationToken.None);
        Assert.Equal((140m, 40m, 15m, 0m, 1, 195m), (totals.OpeningBalance, totals.CashPayments, totals.CashIn, totals.CashOut, totals.EntryCount, totals.ExpectedCash));

        var movements = await reader.ServiceProvider.GetRequiredService<ICashDrawerQueries>().MovementsAsync(new DrawerScope(world.Branch, world.Cashier, Now.AddHours(4), Now.AddHours(6)), 100, CancellationToken.None);
        Assert.Equal(["Payment", "CashIn"], movements.Select(movement => movement.Kind).ToArray());
        Assert.Equal((Guid.Empty, "integration"), (movements[1].VisitId, movements[1].Text));

        var cashiers = await reader.ServiceProvider.GetRequiredService<ICashDrawerQueries>().CashiersAsync(world.Branch, Now.AddHours(3), Now.AddHours(4), CancellationToken.None);
        Assert.Equal([world.Cashier], cashiers.Select(cashier => cashier.UserId).ToArray());   // a cash entry alone makes a cashier

        var closings = reader.ServiceProvider.GetRequiredService<ICashClosingRepository>();
        var record = (await closings.GetRecordAsync((await closings.ListAsync(new CashClosingFilter(world.Branch, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).Items.Single().Id, CancellationToken.None))!;
        Assert.Equal((0m, 0m, 0m, 0, 200m), (record.Totals.OpeningBalance, record.Totals.CashIn, record.Totals.CashOut, record.Totals.EntryCount, record.Totals.ExpectedCash));
    }

    // ---- world --------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid Cashier, Guid Approver, Cashier Desk);

    private async Task<World> WorldAsync()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var cashier = await RegisterUserAsync("Ledger Cashier");
        var approver = await RegisterUserAsync("Ledger Approver");
        return new World(tenant, branch, cashier, approver, new Cashier(cashier, branch));
    }

    private async Task<Guid> RegisterUserAsync(string displayName)
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"ldg.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task<Guid> PostAsync(World world, Cashier desk, CashEntryKind kind, CashEntryCategory category, decimal amount, DateTimeOffset at, string? number = null)
    {
        using var scope = fixture.AppScope(world.Tenant, desk.UserId, branchId: world.Branch);
        number ??= await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(CashEntry.NumberKind, CashEntry.NumberPrefix, at, CancellationToken.None);
        var entry = CashEntry.Post(number, kind, category, amount, Cash, null, "integration", null, desk, insideClosedPeriod: false, at);
        scope.ServiceProvider.GetRequiredService<ICashEntryRepository>().Add(entry);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return entry.Id;
    }

    private async Task VoidAsync(World world, Guid entryId, string reason)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver, branchId: world.Branch);
        var entry = (await scope.ServiceProvider.GetRequiredService<ICashEntryRepository>().GetForUpdateAsync(entryId, CancellationToken.None))!;
        entry.Void(world.Approver, reason, frozenByClosing: false, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> PayAsync(World world, Cashier desk, Guid visit, decimal amount, DateTimeOffset at, PaymentMethod method = PaymentMethod.Cash, string? reference = null)
    {
        using var scope = fixture.AppScope(world.Tenant, desk.UserId, branchId: world.Branch);
        var accounts = scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>();
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var existing = await accounts.GetForUpdateAsync(visit, CancellationToken.None);
        var account = existing ?? VisitAccount.Open(visit);
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Payment.NumberKind, Payment.NumberPrefix, at, CancellationToken.None);
        var payment = account.RecordPayment(number, amount, method, reference, desk, share, at);
        if (existing is null)
        {
            accounts.Add(account);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return payment.Id;
    }

    private async Task VoidPaymentAsync(World world, Guid paymentId)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver);
        var account = (await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetByPaymentIdForUpdateAsync(paymentId, CancellationToken.None))!;
        account.VoidPayment(paymentId, world.Approver, "integration void", frozenByClosing: false, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefundAsync(World world, Cashier desk, Guid visit, decimal amount, DateTimeOffset at)
    {
        using var scope = fixture.AppScope(world.Tenant, desk.UserId, branchId: world.Branch);
        var share = (await scope.ServiceProvider.GetRequiredService<IBillableVisitLookup>().GetAsync(visit, CancellationToken.None))!.Charge;
        var account = (await scope.ServiceProvider.GetRequiredService<IVisitAccountRepository>().GetForUpdateAsync(visit, CancellationToken.None))!;
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Refund.NumberKind, Refund.NumberPrefix, at, CancellationToken.None);
        account.RecordRefund(number, amount, PaymentMethod.Cash, null, Guid.CreateVersion7(), "integration", desk, share, at);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task CloseAsync(World world, Guid? cashier, DateTimeOffset from, DateTimeOffset to, decimal? counted, string? note)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Approver, branchId: world.Branch);
        var totals = await scope.ServiceProvider.GetRequiredService<ICashDrawerQueries>().TotalsAsync(new DrawerScope(world.Branch, cashier, from, to), CancellationToken.None);
        var closing = CashClosing.Close("CLS-LDG-1", world.Branch, cashier, from, to, totals, counted, note, world.Approver, to);
        scope.ServiceProvider.GetRequiredService<ICashClosingRepository>().Add(closing);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private const string InsertVisitSql = """
        INSERT INTO app.visits (id, tenant_id, accession, patient_id, branch_id, location_type, payer_id, contract_tier_id, batch_id, urgent, biohazard,
                                collected_outside, utc_offset_minutes, status, registered_at_utc, registered_by_user_id, total_list_price, discount_total,
                                patient_share_total, payer_share_total, discount_value, revision, created_at_utc)
        VALUES ({0}, {1}, {2}, {3}, {4}, 'Walkin', {8}, {9}, NULL, false, false, false, 0, 'Open', {5}, {6}, {7}, 0, {7}, 0, 0, 1, {5})
        """;

    private async Task<Guid> InsertVisitAsync(Guid tenant, string accession, decimal patientShare)
    {
        var id = Guid.CreateVersion7();
        using var scope = fixture.AppScope(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            InsertVisitSql, id, tenant, accession, Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Guid.CreateVersion7(), patientShare, Guid.CreateVersion7(), Guid.CreateVersion7());
        return id;
    }

    private async Task LowerShareAsync(Guid tenant, Guid visit, decimal patientShare)
    {
        using var scope = fixture.AppScope(tenant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE app.visits SET patient_share_total = {0} WHERE tenant_id = {1} AND id = {2}", patientShare, tenant, visit);
    }
}
