using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// SA-08b client login logs (change request LIS-CR-003 §2): the reader groups <c>identity.refresh_sessions</c> by
/// rotation family and computes login, logout, status and duration on the server; filters, order and paging are
/// applied before the page is cut, the platform tenant's own sessions and machine sessions stay out.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ClientLoginLogReaderTests(DatabaseFixture fixture)
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(8);
    private static readonly TimeSpan Absolute = TimeSpan.FromHours(12);

    [DatabaseFact]
    public async Task Families_are_one_row_each_with_their_end_status_duration_and_first_members_address()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var slug = "logs-" + Guid.CreateVersion7().ToString("N")[^8..];
        var tenantA = await ProvisionAsync(country, governorate, plan, slug);
        var tenantB = Guid.CreateVersion7();   // no client record: names are null
        var (user1, _) = await RegisterUserAsync("Noor Reception");
        var (user2, _) = await RegisterUserAsync("Sara Cashier");
        var (user3, _) = await RegisterUserAsync("Omar Technician");
        var (platformUser, _) = await RegisterUserAsync("Platform Operator");

        // F1: signed in, refreshed once, signed out after 30 minutes.
        var first = RefreshSession.Open(user1, tenantA, SessionKind.Browser, SessionScope.Full, Hash(), Now, Sliding, Absolute, IPAddress.Parse("10.0.0.7"), Chrome);
        var successor = first.Rotate(Hash(), Now.AddMinutes(10), Sliding, IPAddress.Parse("10.0.0.8"), Chrome);
        successor.Revoke(RevocationReason.Logout, Now.AddMinutes(30));
        // F2: ended by an administrator after 15 minutes.
        var second = RefreshSession.Open(user2, tenantA, SessionKind.Browser, SessionScope.Full, Hash(), Now.AddMinutes(5), Sliding, Absolute, IPAddress.Parse("10.0.0.9"), Iphone);
        second.Revoke(RevocationReason.Admin, Now.AddMinutes(20));
        // F3: yesterday, never signed out: expired after its sliding lifetime.
        var third = RefreshSession.Open(user3, tenantB, SessionKind.Browser, SessionScope.Full, Hash(), Now.AddDays(-1), Sliding, Absolute, null, null);
        // F4: live, last seen 3 minutes after the sign-in.
        var fourth = RefreshSession.Open(user3, tenantB, SessionKind.Browser, SessionScope.Full, Hash(), Now, Sliding, Absolute, IPAddress.Parse("10.0.0.10"), Chrome);
        fourth.Touch(Now.AddMinutes(3));
        // Excluded by default: the platform tenant's session (SA-08 shows it) and a machine session.
        var platform = RefreshSession.Open(platformUser, TenantIds.Platform, SessionKind.Browser, SessionScope.Full, Hash(), Now, Sliding, Absolute);
        var machine = RefreshSession.Open(user1, tenantA, SessionKind.Machine, SessionScope.Full, Hash(), Now, Sliding, Absolute);
        await SaveAsync(scope =>
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
            foreach (var session in new[] { first, successor, second, third, fourth, platform, machine })
            {
                sessions.Add(session);
            }
        });

        var at = Now.AddMinutes(31);
        using var scope = fixture.AppScope(TenantIds.Platform);
        var reader = scope.ServiceProvider.GetRequiredService<IClientLoginLogReader>();
        var everything = new ClientLoginLogFilter(null, null, null, null, null, null, null, null, null);

        var page = await reader.ListAsync(everything, ListSort.None, PageRequest.Normalized(1, 50), at, CancellationToken.None);
        var rows = page.Items.Where(row => row.TenantId == tenantA || row.TenantId == tenantB).ToList();   // other tests' sessions may be present
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(page.Items, row => row.TenantId == TenantIds.Platform);
        Assert.Equal(second.FamilyId, rows[0].FamilyId);   // newest login first
        Assert.Equal(third.FamilyId, rows[^1].FamilyId);

        var f1 = rows.Single(row => row.FamilyId == first.FamilyId);
        Assert.Equal((Now, Now.AddMinutes(30), 30, "SignedOut", "10.0.0.7", Chrome, "Noor Reception", "Lab " + slug, "مختبر " + slug), (f1.LoginAtUtc, f1.EndedAtUtc, f1.DurationMinutes, f1.Status, f1.IpAddress, f1.UserAgent, f1.DisplayName, f1.ClientNameEn, f1.ClientNameAr));
        Assert.Null(f1.RevocationReason);
        Assert.Equal(Now.AddMinutes(10), f1.LastSeenAtUtc);   // the rotation touched the first member

        var f2 = rows.Single(row => row.FamilyId == second.FamilyId);
        Assert.Equal((Now.AddMinutes(20), 15, "Revoked", "Admin", "10.0.0.9"), (f2.EndedAtUtc, f2.DurationMinutes, f2.Status, f2.RevocationReason, f2.IpAddress));

        var f3 = rows.Single(row => row.FamilyId == third.FamilyId);
        Assert.Equal((Now.AddDays(-1) + Sliding, 480, "Expired"), (f3.EndedAtUtc, f3.DurationMinutes, f3.Status));
        Assert.Null(f3.ClientNameEn);
        Assert.Null(f3.IpAddress);

        var f4 = rows.Single(row => row.FamilyId == fourth.FamilyId);
        Assert.Equal((null, Now.AddMinutes(3), 3, "Active"), (f4.EndedAtUtc, f4.LastSeenAtUtc, f4.DurationMinutes, f4.Status));

        // The platform tenant only when asked for by id; a client by id.
        var platformOnly = await reader.ListAsync(everything with { TenantId = TenantIds.Platform }, ListSort.None, PageRequest.Normalized(1, 50), at, CancellationToken.None);
        Assert.All(platformOnly.Items, row => Assert.Equal(TenantIds.Platform, row.TenantId));
        Assert.Contains(platformOnly.Items, row => row.FamilyId == platform.FamilyId);
        var clientA = await reader.ListAsync(everything with { TenantId = tenantA }, ListSort.None, PageRequest.Normalized(1, 50), at, CancellationToken.None);
        Assert.Equal([second.FamilyId, first.FamilyId], clientA.Items.Select(row => row.FamilyId));

        // Filters: user search (literal, case-insensitive), the windows, the minutes and the status.
        Assert.Equal([first.FamilyId], (await ListAsync(reader, everything with { Search = "noor recep" }, at)).Select(row => row.FamilyId));
        Assert.Equal([first.FamilyId], (await ListAsync(reader, everything with { LogoutFromUtc = Now.AddMinutes(25) }, at)).Where(row => row.TenantId == tenantA || row.TenantId == tenantB).Select(row => row.FamilyId));
        Assert.Equal([second.FamilyId], (await ListAsync(reader, everything with { LogoutFromUtc = Now, LogoutToUtc = Now.AddMinutes(25) }, at)).Where(row => row.TenantId == tenantA || row.TenantId == tenantB).Select(row => row.FamilyId));
        Assert.Equal([second.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantA, LoginFromUtc = Now.AddMinutes(1) }, at)).Select(row => row.FamilyId));
        Assert.Equal([first.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantA, LoginToUtc = Now.AddMinutes(1) }, at)).Select(row => row.FamilyId));
        Assert.Equal([first.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantA, MinMinutes = 20 }, at)).Select(row => row.FamilyId));
        Assert.Equal([second.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantA, MaxMinutes = 20 }, at)).Select(row => row.FamilyId));
        Assert.Equal([fourth.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantB, Status = "Active" }, at)).Select(row => row.FamilyId));
        Assert.Equal([third.FamilyId], (await ListAsync(reader, everything with { TenantId = tenantB, Status = "Expired" }, at)).Select(row => row.FamilyId));

        // Before the sliding expiry F3 was still active.
        Assert.Equal("Active", (await ListAsync(reader, everything with { TenantId = tenantB, Status = "Active" }, Now.AddDays(-1).AddHours(1))).Single(row => row.FamilyId == third.FamilyId).Status);

        // Every sort key is served in both directions; the duration order is checked.
        foreach (var key in PlatformAdminSortKeys.ClientLoginLogs.Names)
        {
            foreach (var descending in new[] { false, true })
            {
                var sorted = await reader.ListAsync(everything, new ListSort(key, descending), PageRequest.Normalized(1, 50), at, CancellationToken.None);
                Assert.Equal(page.TotalCount, sorted.TotalCount);
            }
        }

        var byDuration = (await ListAsync(reader, everything, at, new ListSort("duration", false))).Where(row => row.TenantId == tenantA || row.TenantId == tenantB).Select(row => row.FamilyId).ToList();
        Assert.Equal([fourth.FamilyId, second.FamilyId, first.FamilyId, third.FamilyId], byDuration);
        var byClient = (await ListAsync(reader, everything with { TenantId = tenantA }, at, new ListSort("user", true))).Select(row => row.DisplayName).ToList();
        Assert.Equal(["Sara Cashier", "Noor Reception"], byClient);

        // Paging on the server: page 2 continues page 1; the export read answers one row over its cap.
        var one = await reader.ListAsync(everything with { TenantId = tenantA }, new ListSort("login", false), PageRequest.Normalized(1, 1), at, CancellationToken.None);
        var two = await reader.ListAsync(everything with { TenantId = tenantA }, new ListSort("login", false), PageRequest.Normalized(2, 1), at, CancellationToken.None);
        Assert.Equal((2L, first.FamilyId, second.FamilyId), (one.TotalCount, Assert.Single(one.Items).FamilyId, Assert.Single(two.Items).FamilyId));
        Assert.Equal(2, (await reader.ListAllAsync(everything with { TenantId = tenantA }, ListSort.None, 1, at, CancellationToken.None)).Count);
    }

    private static async Task<IReadOnlyList<ClientLoginLogRow>> ListAsync(IClientLoginLogReader reader, ClientLoginLogFilter filter, DateTimeOffset at, ListSort sort = default) =>
        (await reader.ListAsync(filter, sort, PageRequest.Normalized(1, 100), at, CancellationToken.None)).Items;

    private static byte[] Hash() => RandomNumberGenerator.GetBytes(RefreshSession.TokenHashLength);

    private async Task<(Guid UserId, string Username)> RegisterUserAsync(string displayName)
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"log.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (user.Id, user.Username);
    }

    private async Task<(Country Country, Governorate Governorate, Plan Plan)> SeedPlaceAsync()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Testland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("L" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[^4..], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        await SaveAsync(scope =>
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
        });
        return (country, governorate, plan);
    }

    private async Task<Guid> ProvisionAsync(Country country, Governorate governorate, Plan plan, string slug)
    {
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 14, Now);
        using var scope = fixture.AppScope(tenant.Id);
        scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(Wallet.Open(tenant.Id, country.CurrencyCode, Now.AddMonths(1)));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }

    private async Task SaveAsync(Action<IServiceScope> stage)
    {
        using var scope = fixture.AppScope(null);
        stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
