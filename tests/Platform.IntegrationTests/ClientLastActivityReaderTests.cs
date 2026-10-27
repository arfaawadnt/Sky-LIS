using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// LIS-CR-007 A-01, the engine side of the silent clients: the reader lists the Trial and Active clients (never the
/// Inactive ones) with the latest browser session issued to their users (machine sessions do not count), and reads the
/// latest registration of each asked client in that client's own tenant scope (RLS, no elevation). Visits are written
/// with raw SQL on their MODULES.md §5 columns (<see cref="RawRows"/>).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ClientLastActivityReaderTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(8);
    private static readonly TimeSpan Absolute = TimeSpan.FromHours(12);

    [DatabaseFact]
    public async Task S_SIL_the_reader_brings_the_last_browser_sign_in_and_the_last_registration_of_each_client()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var active = await ProvisionAsync(country, governorate, plan);
        var quiet = await ProvisionAsync(country, governorate, plan);
        var closed = await ProvisionAsync(country, governorate, plan);
        await DeactivateAsync(closed);
        var (user, _) = await RegisterUserAsync();

        // Two browser families of the active client (the later issue wins, a rotation included) and a newer machine session that does not count.
        var older = RefreshSession.Open(user, active, SessionKind.Browser, SessionScope.Full, Hash(), Now.AddDays(-5), Sliding, Absolute);
        var newer = RefreshSession.Open(user, active, SessionKind.Browser, SessionScope.Full, Hash(), Now.AddDays(-2), Sliding, Absolute);
        var rotated = newer.Rotate(Hash(), Now.AddDays(-2).AddMinutes(14), Sliding);
        var machine = RefreshSession.Open(user, active, SessionKind.Machine, SessionScope.Full, Hash(), Now, Sliding, Absolute);
        var closedSession = RefreshSession.Open(user, closed, SessionKind.Browser, SessionScope.Full, Hash(), Now, Sliding, Absolute);
        using (var scope = fixture.AppScope(null))
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
            foreach (var session in new[] { older, newer, rotated, machine, closedSession })
            {
                sessions.Add(session);
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await InsertVisitAsync(active, Now.AddDays(-9));
        await InsertVisitAsync(active, Now.AddDays(-1));
        await InsertVisitAsync(closed, Now);

        using var platform = fixture.AppScope(TenantIds.Platform);
        var reader = platform.ServiceProvider.GetRequiredService<IClientLastActivityReader>();

        var rows = (await reader.ListClientsAsync(CancellationToken.None)).ToDictionary(row => row.TenantId);
        Assert.False(rows.ContainsKey(closed));
        Assert.False(rows.ContainsKey(TenantIds.Platform));
        Assert.Equal(Now.AddDays(-2).AddMinutes(14), rows[active].LastSignInUtc!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(rows[quiet].LastSignInUtc);
        Assert.Equal((TenantStatus.Trial, "Manager", "+201112223333"), (rows[quiet].Status, rows[quiet].AdministratorName, rows[quiet].AdministratorMobile));

        var registrations = await reader.LastRegistrationsAsync([active, quiet, closed], CancellationToken.None);
        Assert.Equal(Now.AddDays(-1), registrations[active], TimeSpan.FromMilliseconds(1));
        Assert.False(registrations.ContainsKey(quiet));
        Assert.Equal(Now, registrations[closed], TimeSpan.FromMilliseconds(1));   // the port reads what it is asked; the finder never asks for an inactive client

        // The platform scope is back after the per-client reads: a platform read still sees the platform rows only.
        Assert.Equal(TenantIds.Platform, platform.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Security.ICurrentTenant>().TenantId);

        // The finder over the real reader: the quiet trial client (no activity, aged below) is silent, the active one is not.
        await AgeClientAsync(quiet, TimeSpan.FromDays(10));
        var silent = await SilentClientFinder.FindAsync(reader, "https://lis.example.test", Now, 3, CancellationToken.None);
        Assert.Contains(silent, item => item.Id == quiet);
        Assert.DoesNotContain(silent, item => item.Id == active || item.Id == closed);
    }

    private static byte[] Hash() => RandomNumberGenerator.GetBytes(RefreshSession.TokenHashLength);

    private async Task<(Guid UserId, string Username)> RegisterUserAsync()
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"sil.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Silent Tester", "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (user.Id, user.Username);
    }

    private async Task<(Country Country, Governorate Governorate, Plan Plan)> SeedPlaceAsync()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = Country.Create(new CountryData("Silentland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("S" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[^4..], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        using var scope = fixture.AppScope(null);
        scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
        scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
        scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (country, governorate, plan);
    }

    /// <summary>A subscription client in trial (14 days), manager "Manager" on 01112223333.</summary>
    private async Task<Guid> ProvisionAsync(Country country, Governorate governorate, Plan plan)
    {
        var slug = "sil-" + Guid.CreateVersion7().ToString("N")[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Subscription, "en", 14, Now);
        using var scope = fixture.AppScope(tenant.Id, registerTenant: false);
        scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(Wallet.Open(tenant.Id, country.CurrencyCode, Now.AddMonths(1)));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }

    private static Task DeactivateAsync(Guid tenantId) => OwnerSqlAsync("UPDATE saas.tenants SET status = 'Inactive' WHERE id = @id", tenantId);

    private static Task AgeClientAsync(Guid tenantId, TimeSpan by) =>
        OwnerSqlAsync($"UPDATE saas.tenants SET created_at_utc = @at WHERE id = @id", tenantId, Now - by);

    private static async Task OwnerSqlAsync(string sql, Guid id, DateTimeOffset? at = null)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        if (at is { } value)
        {
            command.Parameters.AddWithValue("at", value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static Task InsertVisitAsync(Guid tenant, DateTimeOffset registeredAtUtc)
    {
        var id = Guid.CreateVersion7();
        return RawRows.InsertAsync("app.visits", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenant,
            ["id"] = id,
            ["accession"] = $"ACC-SIL-{id.ToString("N")[^10..].ToUpperInvariant()}",
            ["patient_id"] = Guid.CreateVersion7(),
            ["branch_id"] = Guid.CreateVersion7(),
            ["status"] = "Open",
            ["registered_at_utc"] = registeredAtUtc,
            ["registered_by_user_id"] = Guid.CreateVersion7(),
            ["urgent"] = false,
            ["total_list_price"] = 100m,
            ["discount_total"] = 0m,
            ["patient_share_total"] = 100m,
            ["payer_share_total"] = 0m,
        });
    }
}
