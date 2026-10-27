using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Account;
using Platform.Application.PlatformAdmin;
using Platform.Domain.Account;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// Migration <c>DbReviewTenantKeys</c> on data written before it (database team review 2026-10-26, comments 3–4): tenant ids that
/// own rows without a client record become inactive laboratories before the foreign keys are added, the platform row is written,
/// the new tenant columns take their parent's tenant; without a country, governorate or plan to register orphans the migration
/// stops with a clear error and applies once they exist.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TenantKeysMigrationTests(DatabaseFixture fixture)
{
    /// <summary>The migration just before DbReviewTenantKeys.</summary>
    private const string Before = "20261026205534_DbReviewTextAndNames";

    private const string Reason = "Registered by migration DbReviewTenantKeys: tenant data without a client record";

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Tenants_without_a_client_record_are_registered_and_the_new_tenant_columns_are_backfilled()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = context.GetService<IMigrator>();

        var laboratory = Guid.CreateVersion7();
        var (orphan, orphanByEvent) = (new Guid("0199d000-0000-7000-8000-00000000d001"), new Guid("0199d000-0000-7000-8000-00000000d002"));
        try
        {
            await DatabaseFixture.TruncateAsync(context);
            await TestTenants.EnsureLaboratoryAsync(laboratory);   // a client with a record, a wallet and a ticket (and the reference data)
            var (walletId, movementId, requestId, ticketId) = await WalletAndTicketAsync(laboratory);
            await migrator.MigrateAsync(Before);

            // Written as an older release did: rows of tenants that have no client record, and of the platform tenant.
            await ExecuteAsync("INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (gen_random_uuid(), @tenant, 'Orphan role', 'دور', '{}', TRUE, now())", ("tenant", orphan));
            await ExecuteAsync("INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (gen_random_uuid(), @tenant, 'Platform role', 'دور', '{}', TRUE, now())", ("tenant", TenantIds.Platform));
            await ExecuteAsync("INSERT INTO audit.security_events (id, tenant_id, name, severity, at_utc) VALUES (gen_random_uuid(), @tenant, 'SIGN_IN_SUCCEEDED', 'info', now())", ("tenant", orphanByEvent));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'saas' AND table_name = 'wallet_transactions' AND column_name = 'tenant_id'"));

            await migrator.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            foreach (var tenant in new[] { orphan, orphanByEvent })
            {
                var prefix = tenant.ToString("N")[..8];
                Assert.Equal(
                    ("Laboratory", "Unregistered laboratory " + prefix, "مختبر غير مسجل " + prefix, "Inactive", "Manual", TestTenants.SlugOf(tenant), Reason),
                    await ScalarRowAsync("SELECT kind, name_en, name_ar, status, source, portal_slug, deactivation_reason FROM saas.tenants WHERE id = @id", tenant));
                Assert.Equal(
                    (TestTenants.CountryId, TestTenants.GovernorateId, TestTenants.PlanId),
                    (await ScalarAsync<Guid>("SELECT country_id FROM saas.tenants WHERE id = @id", ("id", tenant)),
                     await ScalarAsync<Guid>("SELECT governorate_id FROM saas.tenants WHERE id = @id", ("id", tenant)),
                     await ScalarAsync<Guid>("SELECT plan_id FROM saas.tenants WHERE id = @id", ("id", tenant))));
            }

            Assert.Equal("Platform", await ScalarAsync<string>("SELECT kind FROM saas.tenants WHERE id = @id", ("id", TenantIds.Platform)));
            Assert.Equal("Laboratory", await ScalarAsync<string>("SELECT kind FROM saas.tenants WHERE id = @id", ("id", laboratory)));   // a client keeps its record
            Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM saas.tenants WHERE id = @id AND deactivation_reason IS NULL", ("id", laboratory)));
            Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM saas.tenants WHERE deactivation_reason = '{Reason}' AND id NOT IN ('{orphan}', '{orphanByEvent}')"));

            Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.wallet_transactions WHERE id = @id", ("id", movementId)));
            Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.wallet_top_up_requests WHERE id = @id", ("id", requestId)));
            Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.support_ticket_replies WHERE support_ticket_id = @id", ("id", ticketId)));
            Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.wallets WHERE id = @id", ("id", walletId)));

            // Every table with a tenant column references the registry, the retired treasury tables included.
            Assert.Equal(0L, await ScalarAsync<long>(
                """
                SELECT count(*)
                FROM information_schema.columns c
                JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('identity', 'app', 'ops', 'audit', 'read', 'saas')
                  AND NOT (c.table_schema = 'saas' AND c.table_name = 'tenants')
                  AND NOT EXISTS (SELECT 1 FROM pg_constraint k
                                  WHERE k.contype = 'f' AND k.conrelid = format('%I.%I', c.table_schema, c.table_name)::regclass
                                    AND k.confrelid = 'saas.tenants'::regclass AND k.conname = 'fk_' || c.table_name || '_tenant')
                """));
            Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE conname = 'fk_treasuries_tenant'"));
        }
        finally
        {
            await migrator.MigrateAsync();
            await DatabaseFixture.TruncateAsync(context);
        }
    }

    [DatabaseFact]
    public async Task Orphans_without_reference_data_stop_the_migration_with_a_clear_error_until_a_country_and_a_plan_exist()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = context.GetService<IMigrator>();

        var orphan = new Guid("0199d000-0000-7000-8000-00000000d003");
        try
        {
            await DatabaseFixture.TruncateAsync(context);   // no country, governorate or plan left
            await migrator.MigrateAsync(Before);
            await ExecuteAsync("INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (gen_random_uuid(), @tenant, 'Orphan role', 'دور', '{}', TRUE, now())", ("tenant", orphan));

            var refused = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Equal("P0001", refused.SqlState);
            Assert.Contains("DbReviewTenantKeys: 1 tenant id(s) own data without a client record", refused.MessageText, StringComparison.Ordinal);
            Assert.Contains("20261026212018_DbReviewTenantKeys", await context.Database.GetPendingMigrationsAsync());   // nothing of it was applied

            await TestTenants.EnsureReferenceDataAsync();
            await migrator.MigrateAsync();
            Assert.Equal("Inactive", await ScalarAsync<string>("SELECT status FROM saas.tenants WHERE id = @id", ("id", orphan)));
        }
        finally
        {
            await TestTenants.EnsureReferenceDataAsync();
            await migrator.MigrateAsync();
            await DatabaseFixture.TruncateAsync(context);
        }
    }

    private async Task<(Guid Wallet, Guid Movement, Guid Request, Guid Ticket)> WalletAndTicketAsync(Guid laboratory)
    {
        var user = await RegisterUserAsync();
        var wallet = Wallet.Open(laboratory, "EGP", Now.AddMonths(1));
        var movement = wallet.TopUp(100m, "TOPUP-MIG", null, user, Now);
        var request = wallet.RequestTopUp(50m, null, user, Now);
        using (var platform = fixture.AppScope(TenantIds.Platform))
        {
            platform.ServiceProvider.GetRequiredService<IWalletRepository>().Add(wallet);
            await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var lab = fixture.AppScope(laboratory, user);
        var ticket = SupportTicket.Open(laboratory, "TKT-MIG-1", "Migration", "Before the keys", SupportTicketPriority.Normal, user, "Lab Admin", null, null, Now);
        ticket.ReplyFromLaboratory(user, "Lab Admin", "A reply.", Now.AddMinutes(1));
        lab.ServiceProvider.GetRequiredService<ISupportTicketRepository>().Add(ticket);
        await lab.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (wallet.Id, movement.Id, request.Id, ticket.Id);
    }

    private static async Task<(string, string, string, string, string, string, string)> ScalarRowAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"tenant {id} is not registered");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6));
    }

    private async Task<Guid> RegisterUserAsync()
    {
        using var scope = fixture.AppScope(null);
        var user = Platform.Domain.Identity.PlatformUser.Register($"reg.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Registry Tester", "hash", Now);
        scope.ServiceProvider.GetRequiredService<Platform.Application.Identity.IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("no value: " + sql));
    }

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
