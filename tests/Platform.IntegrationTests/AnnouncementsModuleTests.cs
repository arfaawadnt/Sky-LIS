using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Application.Settings;
using Platform.Contracts.Common;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of announcements (LIS-CR-013, C-17; intake LIS-INT-PLT-001 §19): saas.announcements round-trips its texts,
/// schedule and uuid[] client list; the register searches both titles and serves every sort key; the live read honours
/// activity and schedule; the counters are atomic increments on live rows only; the audience directory reads the client
/// records; the opt-out setting is the client's own row, read in its scope. Platform rows carry no tenant.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AnnouncementsModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task An_announcement_round_trips_and_the_register_searches_both_titles_and_serves_every_sort_key()
    {
        var client = Guid.CreateVersion7();
        var tag = Guid.CreateVersion7().ToString("N")[..8];
        var announcement = Announcement.Create(
            new AnnouncementContent($"Offer {tag}", $"عرض {tag}", "Body", "نص", "https://nationaltechnology.net"),
            AnnouncementPlacement.Welcome,
            new AnnouncementSchedule(Now.AddDays(-1), Now.AddDays(5)),
            new AnnouncementAudience(null, null, AnnouncementAudienceStatus.Trial, [client]));
        announcement.Activate(Now);
        await SaveAsync(scope => scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>().Add(announcement));

        using var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>();
        var view = await repository.GetViewAsync(announcement.Id, Now, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal(($"Offer {tag}", "نص", "Welcome", "Trial", true, true), (view.TitleEn, view.BodyAr, view.Placement, view.AudienceStatus, view.IsActive, view.IsLive));
        Assert.Equal([client], view.AudienceClientIds);
        Assert.Equal(Now.AddDays(5), view.ToUtc);

        var byArabic = await repository.ListAsync(new AnnouncementFilter($"عرض {tag}", null, null), PageRequest.Normalized(1, 20), Now, CancellationToken.None);
        Assert.Equal([announcement.Id], byArabic.Items.Select(item => item.Id));
        Assert.Empty((await repository.ListAsync(new AnnouncementFilter(tag, AnnouncementPlacement.SignIn, null), PageRequest.Normalized(1, 20), Now, CancellationToken.None)).Items);
        Assert.Empty((await repository.ListAsync(new AnnouncementFilter("%_" + tag, null, null), PageRequest.Normalized(1, 20), Now, CancellationToken.None)).Items);   // wildcards are escaped

        foreach (var key in AnnouncementSortKeys.Announcements.Names)
        {
            foreach (var descending in new[] { false, true })
            {
                var page = await repository.ListAsync(new AnnouncementFilter(null, null, null, new ListSort(key, descending)), PageRequest.Normalized(1, 50), Now, CancellationToken.None);
                Assert.NotEmpty(page.Items);
            }
        }
    }

    [DatabaseFact]
    public async Task The_live_read_honours_activity_and_schedule_and_the_counters_count_live_rows_atomically()
    {
        var live = Create("live", Now.AddHours(-1), null, active: true);
        var draft = Create("draft", Now.AddHours(-1), null, active: false);
        var future = Create("future", Now.AddHours(1), null, active: true);
        var ended = Create("ended", Now.AddDays(-3), Now.AddDays(-1), active: false);
        await SaveAsync(scope =>
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>();
            foreach (var announcement in new[] { live, draft, future, ended })
            {
                repository.Add(announcement);
            }
        });

        using (var reader = fixture.AppScope(null))
        {
            var rows = await reader.ServiceProvider.GetRequiredService<IAnnouncementRepository>().ListLiveAsync(AnnouncementPlacement.SignIn, Now, CancellationToken.None);
            Assert.Contains(rows, row => row.Id == live.Id);
            Assert.DoesNotContain(rows, row => row.Id == draft.Id || row.Id == future.Id || row.Id == ended.Id);
        }

        // ten concurrent impressions, each on its own scope (connection): none is lost
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var anonymous = fixture.AppScope(null);
            Assert.True(await anonymous.ServiceProvider.GetRequiredService<IAnnouncementCounters>().IncrementAsync(live.Id, AnnouncementCounter.Impression, Now, CancellationToken.None));
        }));

        using var scope = fixture.AppScope(null);
        var counters = scope.ServiceProvider.GetRequiredService<IAnnouncementCounters>();
        Assert.True(await counters.IncrementAsync(live.Id, AnnouncementCounter.Click, Now, CancellationToken.None));
        Assert.False(await counters.IncrementAsync(draft.Id, AnnouncementCounter.Impression, Now, CancellationToken.None));
        Assert.False(await counters.IncrementAsync(future.Id, AnnouncementCounter.Click, Now, CancellationToken.None));
        Assert.False(await counters.IncrementAsync(Guid.CreateVersion7(), AnnouncementCounter.Click, Now, CancellationToken.None));

        var counted = await scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>().FindAsync(live.Id, CancellationToken.None);
        Assert.Equal((10L, 1L), (counted!.Impressions, counted.Clicks));
        Assert.Equal(0L, (await scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>().FindAsync(draft.Id, CancellationToken.None))!.Impressions);
    }

    [DatabaseFact]
    public async Task The_audience_directory_reads_client_records_and_the_opt_out_is_the_clients_own_row()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var slug = "ann-" + Guid.CreateVersion7().ToString("N")[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Subscription, "en", 14, Now);
        using (var provisioning = fixture.AppScope(tenant.Id, registerTenant: false))   // as provisioning does: the event is recorded in the new tenant's scope
        {
            provisioning.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            await provisioning.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            var directory = scope.ServiceProvider.GetRequiredService<IAnnouncementAudienceDirectory>();
            Assert.Equal(new AnnouncementViewer(tenant.Id, country.Id, plan.Id, TenantStatus.Trial), await directory.FindViewerAsync(tenant.Id, CancellationToken.None));
            Assert.Null(await directory.FindViewerAsync(Guid.CreateVersion7(), CancellationToken.None));
            Assert.True(await directory.CountryExistsAsync(country.Id, CancellationToken.None));
            Assert.False(await directory.CountryExistsAsync(Guid.CreateVersion7(), CancellationToken.None));
            Assert.True(await directory.PlanExistsAsync(plan.Id, CancellationToken.None));
            var stranger = Guid.CreateVersion7();
            Assert.Equal([stranger], await directory.MissingClientsAsync([tenant.Id, stranger], CancellationToken.None));
        }

        // the Super Admin writes the client's own row from the platform scope, as SetClientAnnouncementsHiddenHandler does
        using (var platform = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            using (platform.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant.Id))
            {
                await platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.HideAnnouncements.Key, "true", null, Now, CancellationToken.None);
                await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            }
        }

        // a row of the platform tenant itself (never written by the API: SET-004) does not hide them for every laboratory
        using (var platform = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            await platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.HideAnnouncements.Key, "true", null, Now, CancellationToken.None);
            await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var platform = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            var store = platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>();
            var scopeSetter = platform.ServiceProvider.GetRequiredService<ICurrentTenantSetter>();
            Assert.True(await AnnouncementFeed.HiddenForAsync(store, scopeSetter, tenant.Id, CancellationToken.None));
            Assert.False(await AnnouncementFeed.HiddenForAsync(store, scopeSetter, Guid.CreateVersion7(), CancellationToken.None));   // another laboratory: no own row, the platform row is not inherited
        }

        using var cleanup = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7());   // leave no platform row behind for the other tests
        await cleanup.ServiceProvider.GetRequiredService<ITenantSettingsStore>().RemoveAsync(SettingKeys.HideAnnouncements.Key, CancellationToken.None);
        await cleanup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private static Announcement Create(string title, DateTimeOffset from, DateTimeOffset? to, bool active)
    {
        var announcement = Announcement.Create(new AnnouncementContent(title, title, null, null, null), AnnouncementPlacement.SignIn, new AnnouncementSchedule(from, to), AnnouncementAudience.Everyone);
        if (active)
        {
            announcement.Activate(from);
        }

        return announcement;
    }

    private async Task<(Country Country, Governorate Governorate, Plan Plan)> SeedPlaceAsync()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Annland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("A" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[^4..], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        await SaveAsync(scope =>
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
        });
        return (country, governorate, plan);
    }

    private async Task SaveAsync(Action<IServiceScope> stage)
    {
        using var scope = fixture.AppScope(null);
        stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
