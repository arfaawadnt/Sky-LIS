using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// LIS-CR-012 (C-16) on the engine: the reminder log's unique key (UNIQUE-409, INV-PLT-050) and its grants for the runtime role,
/// the reader's figures (plan fee, wallet, zero mark, last reminder) and the sign-in lookup with the platform's grace days.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SubscriptionReminderModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_reminder_key_is_recorded_once_per_client_and_the_reader_shows_the_last_one()
    {
        var clientId = await ProvisionAsync(trialStartedDaysAgo: 12);
        var due = new DueReminder(SubscriptionReminderKind.TrialEnding, 3, "trial-3:202610261200", 2);

        await SaveAsync(scope => scope.ServiceProvider.GetRequiredService<ISubscriptionReminderRepository>().Add(SubscriptionReminder.Record(clientId, due, "subscription-expiry", ["Sms"], Now)));
        var duplicate = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(scope =>
            scope.ServiceProvider.GetRequiredService<ISubscriptionReminderRepository>().Add(SubscriptionReminder.Record(clientId, due, "subscription-expiry", ["Email"], Now.AddHours(1)))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicate.Code);

        using var scope = fixture.AppScope(TenantIds.Platform);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ISubscriptionReminderRepository>().ExistsAsync(clientId, due.Key, CancellationToken.None));
        var row = (await scope.ServiceProvider.GetRequiredService<ISubscriptionStateReader>().FindAsync(clientId, CancellationToken.None))!;
        Assert.Equal((TenantStatus.Trial, 3000m, 0m, "EGP", due.Key), (row.Status, row.MonthlyFee, row.WalletBalance!.Value, row.WalletCurrencyCode, row.LastReminderKey));
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<ISubscriptionStateReader>().ListClientsAsync(CancellationToken.None), client => client.TenantId == clientId);
    }

    [DatabaseFact]
    public async Task The_wallet_zero_mark_is_stored_and_the_lookup_reads_the_state_with_the_default_grace()
    {
        var active = await ProvisionAsync(trialStartedDaysAgo: null);
        await SaveAsync(async scope =>
        {
            var wallet = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(active, CancellationToken.None))!;
            wallet.ChargeSubscription(3000m, "SUB-TEST", Now.AddMonths(-2));
        });
        var expiredTrial = await ProvisionAsync(trialStartedDaysAgo: 400);

        using var scope = fixture.AppScope(null);   // sign-in is anonymous
        var reader = scope.ServiceProvider.GetRequiredService<ISubscriptionStateReader>();
        Assert.Equal(Now.AddMonths(-2), (await reader.FindAsync(active, CancellationToken.None))!.WalletAtOrBelowZeroSinceUtc);
        Assert.Null(await reader.FindAsync(TenantIds.Platform, CancellationToken.None));
        Assert.Null(await reader.FindAsync(Guid.CreateVersion7(), CancellationToken.None));

        var lookup = scope.ServiceProvider.GetRequiredService<ISubscriptionStateLookup>();
        Assert.True((await lookup.FindAsync(expiredTrial, CancellationToken.None)).IsExpired);
        Assert.Equal(SubscriptionStatus.None, await lookup.FindAsync(TenantIds.Platform, CancellationToken.None));
        Assert.NotEqual(SubscriptionLevel.Ok, (await lookup.FindAsync(active, CancellationToken.None)).Level);   // at or below zero since a past month
    }

    private async Task<Guid> ProvisionAsync(int? trialStartedDaysAgo)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Remindia " + iso, "دولة", iso, "+20", "Pound", "EGP", "en", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("S" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 3000m, "EGP", 1, 5, 100, 1));
        var profile = ClientProfile.Create("Lab " + suffix[..8], "مختبر", country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = trialStartedDaysAgo is { } days
            ? Tenant.Provision(profile, country, governorate, plan, "rem-" + suffix[^10..], TenantSource.Subscription, "en", 14, Now.AddDays(-days))
            : Tenant.Provision(profile, country, governorate, plan, "rem-" + suffix[^10..], TenantSource.Manual, "en", 0, Now);
        await SaveAsync(scope =>
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(Wallet.Open(tenant.Id, "EGP", Now.AddMonths(1)));
        });
        return tenant.Id;
    }

    private Task SaveAsync(Action<IServiceScope> stage) => SaveAsync(scope =>
    {
        stage(scope);
        return Task.CompletedTask;
    });

    private async Task SaveAsync(Func<IServiceScope, Task> stage)
    {
        using var scope = fixture.AppScope(TenantIds.Platform);
        await stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
