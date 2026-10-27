using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Jobs;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// W2 monthly subscription charge (intake LIS-INT-PLT-001 §7, validation LIS-VR-002 §7 C-8) on a real PostgreSQL 17:
/// a due Active client is charged its plan's fee once per period and its next charge moves one month; a second pass
/// in the same period charges nothing, even two passes racing; an insufficient balance is charged below zero without
/// suspending the client (metered usage then pauses, WAL-010); trial and inactive clients are skipped and a currency
/// mismatch fails without a debit; one client's charge never touches another's wallet and the charger refuses a
/// client tenant's scope. The pass lists every due wallet of the shared database, so assertions look at this test's
/// clients only.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WalletChargeRunTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset DueAt = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_due_active_client_is_charged_its_monthly_fee_once_per_period_and_the_next_charge_moves_one_month()
    {
        var place = await SeedPlaceAsync(monthlyFee: 1500m);
        var client = await ProvisionAsync(place, "charge-once");
        await TopUpAsync(client, 2000m);
        using var job = NewJob();

        var first = await job.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None);
        var item = Assert.Single(first.Items, row => row.TenantId == client);
        Assert.Equal((WalletCharger.Charged, "2026-10", -1500m, 500m), (item.Outcome, item.Period, item.Amount, item.BalanceAfter));

        var wallet = await ReadWalletAsync(client);
        Assert.Equal((500m, DueAt.AddMonths(1)), (wallet.Balance, wallet.NextChargeAtUtc));
        var charge = Assert.Single(await SubscriptionChargesAsync(wallet.Id));
        Assert.Equal((-1500m, 500m, $"SUB-2026-10-{place.Plan.Code}"), (charge.Amount, charge.BalanceAfter, charge.Reference));

        // Same period again: the wallet is no longer due, so the pass does not even look at it.
        var second = await job.RunOnceAsync(DueAt.AddHours(2), CancellationToken.None);
        Assert.DoesNotContain(second.Items, row => row.TenantId == client);
        Assert.Single(await SubscriptionChargesAsync(wallet.Id));

        // The next period falls due a month later and is charged once more.
        var nextMonth = await job.RunOnceAsync(DueAt.AddMonths(1).AddHours(1), CancellationToken.None);
        var next = Assert.Single(nextMonth.Items, row => row.TenantId == client);
        Assert.Equal((WalletCharger.Charged, "2026-11"), (next.Outcome, next.Period));
        Assert.Equal(2, (await SubscriptionChargesAsync(wallet.Id)).Count);
        Assert.Equal(DueAt.AddMonths(2), (await ReadWalletAsync(client)).NextChargeAtUtc);
    }

    [DatabaseFact]
    public async Task Two_passes_racing_on_the_same_period_charge_it_once()
    {
        var place = await SeedPlaceAsync(monthlyFee: 300m);
        var client = await ProvisionAsync(place, "charge-race");
        using var one = NewJob();   // two processes: separate in-process gates, one database
        using var two = NewJob();

        var results = await Task.WhenAll(one.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None), two.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None));

        var outcomes = results.SelectMany(summary => summary.Items).Where(row => row.TenantId == client).Select(row => row.Outcome).ToList();
        Assert.Single(outcomes, outcome => outcome == WalletCharger.Charged);
        Assert.DoesNotContain(WalletCharger.Failed, outcomes);
        var wallet = await ReadWalletAsync(client);
        Assert.Single(await SubscriptionChargesAsync(wallet.Id));
        Assert.Equal((-300m, DueAt.AddMonths(1)), (wallet.Balance, wallet.NextChargeAtUtc));
    }

    [DatabaseFact]
    public async Task An_insufficient_balance_is_charged_below_zero_without_suspending_the_client_and_metered_usage_then_pauses()
    {
        var place = await SeedPlaceAsync(monthlyFee: 3500m);
        var client = await ProvisionAsync(place, "charge-short");
        await TopUpAsync(client, 1000m);

        using var job = NewJob();
        var summary = await job.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None);
        var item = Assert.Single(summary.Items, row => row.TenantId == client);
        Assert.Equal((WalletCharger.Charged, -2500m), (item.Outcome, item.BalanceAfter));
        Assert.True(summary.AtOrBelowZero >= 1);

        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            var tenant = (await scope.ServiceProvider.GetRequiredService<ITenantRepository>().FindStatusAsync(client, CancellationToken.None))!;
            Assert.Equal(TenantStatus.Active, tenant.Value.Status);   // no suspension: the portal keeps working (CP-66)

            var wallet = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(client, CancellationToken.None))!;
            var refused = Assert.Throws<DomainException>(() => wallet.DebitUsage(0.25m, "sms", DueAt.AddHours(2)));
            Assert.Equal(PlatformAdminCodes.InsufficientBalance, refused.Code);   // WAL-010: metered services pause at zero
        }

        using (var context = fixture.AppScope(TenantIds.Platform))
        {
            var debited = await context.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ops.outbox_events WHERE type = {typeof(WalletDebited).FullName} AND payload ->> 'tenantId' = {client.ToString()}")
                .SingleAsync();
            Assert.Equal(1, debited);   // WalletDebited(Subscription) with the balance after: the hook for "pause at zero" policies
        }
    }

    [DatabaseFact]
    public async Task Trial_and_inactive_clients_are_skipped_and_a_currency_mismatch_fails_without_touching_the_wallet()
    {
        var place = await SeedPlaceAsync(monthlyFee: 100m);
        var trial = await ProvisionAsync(place, "charge-trial", TenantSource.Subscription, trialDays: 14);
        var inactive = await ProvisionAsync(place, "charge-inactive");
        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            var tenant = (await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetForUpdateAsync(inactive, CancellationToken.None))!;
            tenant.Deactivate("unpaid", DueAt);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var dollars = await SeedPlaceAsync(monthlyFee: 100m, walletCurrency: "USD");   // the wallet takes the country's currency, the plan stays EGP (Q5)
        var mismatch = await ProvisionAsync(dollars, "charge-usd");

        using var job = NewJob();
        var summary = await job.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None);

        Assert.Equal((WalletCharger.Skipped, "trial"), Outcome(summary, trial));
        Assert.Equal((WalletCharger.Skipped, "inactive"), Outcome(summary, inactive));
        Assert.Equal((WalletCharger.Failed, "currency-mismatch"), Outcome(summary, mismatch));
        foreach (var untouched in new[] { trial, inactive, mismatch })
        {
            var wallet = await ReadWalletAsync(untouched);
            Assert.Equal((0m, DueAt), (wallet.Balance, wallet.NextChargeAtUtc));
            Assert.Empty(await SubscriptionChargesAsync(wallet.Id));
        }
    }

    [DatabaseFact]
    public async Task One_clients_charge_never_touches_another_and_the_charger_refuses_a_client_tenant_scope()
    {
        var cheap = await SeedPlaceAsync(monthlyFee: 100m);
        var dear = await SeedPlaceAsync(monthlyFee: 900m);
        var first = await ProvisionAsync(cheap, "charge-iso-a");
        var second = await ProvisionAsync(dear, "charge-iso-b");
        await TopUpAsync(first, 1000m);
        await TopUpAsync(second, 1000m);

        using (var job = NewJob())
        {
            await job.RunOnceAsync(DueAt.AddHours(1), CancellationToken.None);
        }

        var (a, b) = (await ReadWalletAsync(first), await ReadWalletAsync(second));
        Assert.Equal((900m, 100m), (a.Balance, b.Balance));
        Assert.Equal(-100m, Assert.Single(await SubscriptionChargesAsync(a.Id)).Amount);
        Assert.Equal(-900m, Assert.Single(await SubscriptionChargesAsync(b.Id)).Amount);

        // The charger runs in the platform tenant's scope only: a client tenant never charges a wallet (PLT-403).
        using var clientScope = fixture.AppScope(first);
        var charger = ActivatorUtilities.CreateInstance<WalletCharger>(clientScope.ServiceProvider);
        var refused = await Assert.ThrowsAsync<DomainException>(() => charger.ChargeAsync(second, DueAt.AddMonths(1).AddHours(1), CancellationToken.None));
        Assert.Equal(PlatformAdminCodes.PlatformTenantRequired, refused.Code);
        Assert.Single(await SubscriptionChargesAsync(b.Id));
    }

    private static (string Outcome, string? Reason) Outcome(WalletChargeRunSummary summary, Guid client)
    {
        var item = Assert.Single(summary.Items, row => row.TenantId == client);
        return (item.Outcome, item.Reason);
    }

    private WalletChargeJob NewJob() =>
        new(fixture.AppProvider.GetRequiredService<IServiceScopeFactory>(), new WalletChargeOptions(), fixture.AppProvider.GetRequiredService<IClock>(), NullLogger<WalletChargeJob>.Instance);

    private async Task<(Country Country, Governorate Governorate, Plan Plan)> SeedPlaceAsync(decimal monthlyFee, string walletCurrency = "EGP")
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Chargeland " + iso, "أرض " + iso, iso, "+20", "Pound", walletCurrency, "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("C" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Charge plan", "باقة"), monthlyFee, "EGP", 1, 5, 100, 1));
        using var scope = fixture.AppScope(TenantIds.Platform);
        scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
        scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
        scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (country, governorate, plan);
    }

    /// <summary>A client whose wallet's first charge falls due at <see cref="DueAt"/>.</summary>
    private async Task<Guid> ProvisionAsync((Country Country, Governorate Governorate, Plan Plan) place, string slugPrefix, TenantSource source = TenantSource.Manual, int trialDays = 0)
    {
        var slug = slugPrefix + "-" + Guid.CreateVersion7().ToString("N")[^6..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, place.Country, place.Governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = Tenant.Provision(profile, place.Country, place.Governorate, place.Plan, slug, source, "en", trialDays, DueAt.AddMonths(-1));
        using var scope = fixture.AppScope(tenant.Id, registerTenant: false);   // the provisioning scope: the new tenant (intake §11)
        scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(Wallet.Open(tenant.Id, place.Country.CurrencyCode, DueAt));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }

    private async Task TopUpAsync(Guid client, decimal amount)
    {
        using var scope = fixture.AppScope(TenantIds.Platform);
        var wallet = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(client, CancellationToken.None))!;
        wallet.TopUp(amount, "INV-" + amount, null, null, DueAt.AddMonths(-1));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Wallet> ReadWalletAsync(Guid client)
    {
        using var scope = fixture.AppScope(TenantIds.Platform);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Wallet>().AsNoTracking().SingleAsync(wallet => wallet.TenantId == client);
    }

    private async Task<List<WalletTransaction>> SubscriptionChargesAsync(Guid walletId)
    {
        using var scope = fixture.AppScope(TenantIds.Platform);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WalletTransaction>().AsNoTracking()
            .Where(transaction => transaction.WalletId == walletId && transaction.Kind == WalletTransactionKind.Subscription)
            .OrderBy(transaction => transaction.AtUtc)
            .ToListAsync();
    }
}
