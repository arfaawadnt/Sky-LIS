using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Account;
using Platform.Application.Audit;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// SA-09 "client" column and filter (UAT C-18): in the platform tenant's audit log every entry about a laboratory —
/// its client record, its wallet, a wallet movement, an event naming it — carries the client's tenant id and names, the
/// platform's own records carry none, the filter narrows both audit sources to one client, and a laboratory's own log
/// has no clients at all.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditClientTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_platform_log_names_the_client_of_each_entry_and_filters_by_client()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = "X" + (char)('A' + (Math.Abs(suffix.GetHashCode(StringComparison.Ordinal)) % 26));
        var country = Country.Create(new CountryData("Auditland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("A" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Audit plan", "باقة"), 100m, "EGP", 1, 5, 100, 1));
        var tenant = Tenant.Provision(
            ClientProfile.Create("Audit Lab", "مختبر التدقيق", country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null),
            country, governorate, plan, "audit" + suffix[..6], TenantSource.Manual, "en", 14, Now);
        var wallet = Wallet.Open(tenant.Id, country.CurrencyCode, Now.AddMonths(1));
        var administrator = PlatformUser.Register("audit.admin." + suffix[..8], $"audit.admin.{suffix[..8]}@lab.example", "Audit Lab admin", "$argon2id$v=19$m=65536,t=3,p=1$c2FsdA$aGFzaA", Now);
        tenant.AttachAdministrator(administrator.Id, administrator.Username);

        using (var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(wallet);
            scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(administrator);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            var stored = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(tenant.Id, CancellationToken.None))!;
            stored.TopUp(500m, "TOPUP-1", null, null, Now.AddMinutes(5));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        // A field change of the client record itself, written in the platform's log (re-test C-18: it showed "—").
        using (var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            var stored = (await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetForUpdateAsync(tenant.Id, CancellationToken.None))!;
            stored.Deactivate("Audit client test", Now.AddMinutes(10));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        // C-11: SA-04 "reset administrator password" changes the client administrator's user record in the platform's log.
        using (var scope = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7()))
        {
            var stored = (await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().GetForUpdateAsync(administrator.Id, CancellationToken.None))!;
            stored.ResetPasswordByAdministrator("$argon2id$v=19$m=65536,t=3,p=1$c2FsdDI$aGFzaDI", mustChange: true, Now.AddMinutes(15));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var platform = fixture.AppScope(TenantIds.Platform, Guid.CreateVersion7());
        var reader = platform.ServiceProvider.GetRequiredService<IAuditReader>();
        var all = (await reader.ListAsync(new AuditEntryFilter(null, null, null, null, null), PageRequest.Normalized(1, 200), CancellationToken.None)).Items;

        var aboutClient = all.Where(item => item.ClientTenantId == tenant.Id).ToList();
        Assert.Contains(aboutClient, item => item.EntityType == nameof(Tenant));                         // the client record
        Assert.Contains(aboutClient, item => item.Kind == AuditEntryItem.KindFieldChange && item.EntityType == nameof(Tenant) && item.Action == "Modified"
            && item.Changes!.Any(change => change.Field == nameof(Tenant.DeactivationReason)));            // a field change of the client record
        Assert.Contains(aboutClient, item => item.Action == nameof(TenantProvisioned));                   // an event naming it
        Assert.Contains(aboutClient, item => item.EntityType == nameof(Wallet));                          // its wallet
        Assert.Contains(aboutClient, item => item.Action == nameof(WalletToppedUp));
        Assert.Contains(aboutClient, item => item.Kind == AuditEntryItem.KindFieldChange && item.EntityType == nameof(PlatformUser) && item.Action == "Modified"
            && item.Changes!.Any(change => change.Field == nameof(PlatformUser.MustChangePassword)));   // C-11: its administrator's password reset
        Assert.All(aboutClient, item => Assert.Equal(("Audit Lab", "مختبر التدقيق"), (item.ClientNameEn, item.ClientNameAr)));
        Assert.All(all.Where(item => item.EntityType is nameof(Plan) or nameof(Country) or nameof(Governorate)), item => Assert.Null(item.ClientTenantId));

        var filtered = await reader.ListAsync(new AuditEntryFilter(null, null, null, null, null, tenant.Id), PageRequest.Normalized(1, 200), CancellationToken.None);
        Assert.Equal(aboutClient.Count, filtered.TotalCount);
        Assert.All(filtered.Items, item => Assert.Equal(tenant.Id, item.ClientTenantId));
        Assert.Equal(0, (await reader.ListAsync(new AuditEntryFilter(null, null, null, null, null, Guid.CreateVersion7()), PageRequest.Normalized(1, 50), CancellationToken.None)).TotalCount);

        var exported = await platform.ServiceProvider.GetRequiredService<IAuditExportReader>().ReadAsync(new AuditEntryFilter(null, null, null, null, null, tenant.Id), 100, CancellationToken.None);
        Assert.Equal(aboutClient.Count, exported.Count);
        Assert.Contains(exported, item => item.Kind == AuditEntryItem.KindFieldChange && item.EntityType == nameof(Tenant) && item.Action == "Modified" && item.ClientNameEn == "Audit Lab");
        Assert.Contains(filtered.Items, item => item.Kind == AuditEntryItem.KindFieldChange && item.EntityType == nameof(Tenant) && item.Action == "Modified");
        Assert.Contains(filtered.Items, item => item.Kind == AuditEntryItem.KindFieldChange && item.EntityType == nameof(PlatformUser) && item.Action == "Modified");
        Assert.Contains(exported, item => item.EntityType == nameof(PlatformUser) && item.ClientNameEn == "Audit Lab");

        var clients = await reader.ClientsAsync(CancellationToken.None);
        Assert.Equal(("Audit Lab", "مختبر التدقيق"), clients.Where(client => client.TenantId == tenant.Id).Select(client => (client.NameEn, client.NameAr)).Single());
        Assert.DoesNotContain(clients, client => client.TenantId == TenantIds.Platform);

        // A laboratory's own log: no clients, and the client filter does not apply there.
        using var laboratory = fixture.AppScope(tenant.Id, Guid.CreateVersion7());
        var labReader = laboratory.ServiceProvider.GetRequiredService<IAuditReader>();
        Assert.Empty(await labReader.ClientsAsync(CancellationToken.None));
        Assert.All((await labReader.ListAsync(new AuditEntryFilter(null, null, null, null, null), PageRequest.Normalized(1, 50), CancellationToken.None)).Items, item => Assert.Null(item.ClientTenantId));
    }
}
