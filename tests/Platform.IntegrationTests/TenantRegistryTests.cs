using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Account;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// Database team review 2026-10-26, comments 3–4 (migration <c>DbReviewTenantKeys</c>) on a real PostgreSQL 17: <c>saas.tenants</c>
/// is the registry of every tenant, the platform tenant included (kind Platform, no country / governorate / plan), and no reader of
/// the model returns that row; every <c>tenant_id</c> references the registry; the platform-level tables are keyed
/// <c>(tenant_id, id)</c> and referenced by <c>(tenant_id, x_id)</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TenantRegistryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_platform_tenant_has_a_registry_row_that_no_reader_of_the_model_returns()
    {
        var laboratory = Guid.CreateVersion7();
        await TestTenants.EnsureLaboratoryAsync(laboratory);

        // The row is there, as the migration (and every reset of the tests) writes it.
        Assert.Equal(("Platform", true, true, true, "platform"), await PlatformRowAsync());

        using var scope = fixture.AppScope(TenantIds.Platform);
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<AppDbContext>();
        Assert.True(await context.Set<Tenant>().IgnoreQueryFilters().AnyAsync(tenant => tenant.Id == TenantIds.Platform));
        Assert.False(await context.Set<Tenant>().AnyAsync(tenant => tenant.Id == TenantIds.Platform));
        Assert.True(await context.Set<Tenant>().AnyAsync(tenant => tenant.Id == laboratory));

        // SA-04 list and detail, the status and profile lookups, sign-in by address and the public card.
        var tenants = services.GetRequiredService<ITenantRepository>();
        Assert.Null(await tenants.GetForUpdateAsync(TenantIds.Platform, CancellationToken.None));
        Assert.Null(await tenants.FindStatusAsync(TenantIds.Platform, CancellationToken.None));
        Assert.Null(await tenants.GetDetailAsync(TenantIds.Platform, "https://lis.example", CancellationToken.None));
        Assert.False(await tenants.SlugExistsAsync("platform", CancellationToken.None));
        var clients = await tenants.ListAsync(new ClientFilter(null, null, null, null), PageRequest.Normalized(1, 200), "https://lis.example", CancellationToken.None);
        Assert.DoesNotContain(clients.Items, item => item.Id == TenantIds.Platform);
        Assert.Contains(clients.Items, item => item.Id == laboratory);
        Assert.True(await services.GetRequiredService<ITenantStatusLookup>().IsActiveAsync(TenantIds.Platform, CancellationToken.None));   // as before the row existed
        Assert.Null(await services.GetRequiredService<ILaboratoryProfileLookup>().FindAsync(TenantIds.Platform, CancellationToken.None));
        Assert.Null(await services.GetRequiredService<ILaboratoryAddresses>().FindAsync("platform", CancellationToken.None));
        Assert.Null(await services.GetRequiredService<IPublicLaboratoryDirectory>().FindUsableAsync("platform", CancellationToken.None));

        // The dashboard count, the silent-clients and subscription readers (and with them the reminder job), the announcement audience.
        var counts = await services.GetRequiredService<IPlatformDashboardReader>().CountClientsAsync(Now, CancellationToken.None);
        Assert.Equal(await CountAsync("SELECT count(*) FROM saas.tenants WHERE kind = 'Laboratory'"), counts.Total);
        Assert.DoesNotContain(await services.GetRequiredService<IClientLastActivityReader>().ListClientsAsync(CancellationToken.None), row => row.TenantId == TenantIds.Platform);
        var subscriptions = services.GetRequiredService<ISubscriptionStateReader>();
        Assert.DoesNotContain(await subscriptions.ListClientsAsync(CancellationToken.None), row => row.TenantId == TenantIds.Platform);
        Assert.Null(await subscriptions.FindAsync(TenantIds.Platform, CancellationToken.None));
        var audience = services.GetRequiredService<IAnnouncementAudienceDirectory>();
        Assert.Null(await audience.FindViewerAsync(TenantIds.Platform, CancellationToken.None));
        Assert.Equal([TenantIds.Platform], await audience.MissingClientsAsync([TenantIds.Platform, laboratory], CancellationToken.None));

        // The wallet charge run never meets the platform: it has no wallet, and none can be listed for it.
        Assert.DoesNotContain(await services.GetRequiredService<IWalletRepository>().ListDueForChargeAsync(Now.AddYears(10), CancellationToken.None), id => id == TenantIds.Platform);
        Assert.Null(await services.GetRequiredService<IWalletRepository>().FindChargeTermsAsync(TenantIds.Platform, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task A_row_of_an_unregistered_tenant_is_refused_by_its_foreign_key_to_the_registry()
    {
        var unknown = Guid.CreateVersion7();
        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (gen_random_uuid(), @tenant, 'Ghost', 'شبح', '{}', TRUE, now())",
            ("tenant", unknown)));
        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, "fk_roles_tenant"), (refused.SqlState, refused.ConstraintName));

        var nullable = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO audit.security_events (id, tenant_id, name, severity, at_utc) VALUES (gen_random_uuid(), @tenant, 'TEST', 'info', now())",
            ("tenant", unknown)));
        Assert.Equal("fk_security_events_tenant", nullable.ConstraintName);
        await ExecuteAsync("INSERT INTO audit.security_events (id, tenant_id, name, severity, at_utc) VALUES (gen_random_uuid(), NULL, 'TEST', 'info', now())");   // a nullable tenant stays nullable

        // The registry refuses a laboratory without its place; only the platform row may lack it.
        var placeless = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            """
            INSERT INTO saas.tenants (id, kind, name_en, name_ar, address, mobile, manager_name, manager_mobile, portal_slug, source, status, preferred_language, created_at_utc)
            VALUES (@tenant, 'Laboratory', 'No place', 'بلا مكان', '-', '-', '-', '-', @slug, 'Manual', 'Active', 'en', now())
            """,
            ("tenant", unknown), ("slug", TestTenants.SlugOf(unknown))));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_tenants_laboratory_place"), (placeless.SqlState, placeless.ConstraintName));
        var badKind = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE saas.tenants SET kind = 'Branch' WHERE id = @tenant", ("tenant", TenantIds.Platform)));
        Assert.Equal("ck_tenants_kind_domain", badKind.ConstraintName);

        // The registry row of a tenant that owns data cannot be deleted (ON DELETE RESTRICT).
        var laboratory = Guid.CreateVersion7();
        using (var scope = fixture.AppScope(laboratory))
        {
            await ExecuteAsync("INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (gen_random_uuid(), @tenant, 'Kept', 'باق', '{}', TRUE, now())", ("tenant", laboratory));
        }

        var restricted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM saas.tenants WHERE id = @tenant", ("tenant", laboratory)));
        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, "fk_roles_tenant"), (restricted.SqlState, restricted.ConstraintName));
    }

    [DatabaseFact]
    public async Task The_platform_level_tables_are_keyed_tenant_first_and_their_children_carry_the_parents_tenant()
    {
        string[] tables = ["saas.wallets", "saas.wallet_transactions", "saas.wallet_top_up_requests", "saas.subscription_reminders", "saas.support_tickets", "saas.support_ticket_replies", "identity.machine_credentials"];
        foreach (var table in tables)
        {
            Assert.Equal("tenant_id,id", await ScalarAsync<string>(
                """
                SELECT string_agg(a.attname, ',' ORDER BY array_position(i.indkey::int2[], a.attnum))
                FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
                WHERE i.indisprimary AND i.indrelid = @table::regclass
                """,
                ("table", table)));
        }

        Assert.Equal("FOREIGN KEY (tenant_id, wallet_id) REFERENCES saas.wallets(tenant_id, id) ON DELETE RESTRICT", await ConstraintAsync("fk_wallet_transactions_wallet"));
        Assert.Equal("FOREIGN KEY (tenant_id, wallet_id) REFERENCES saas.wallets(tenant_id, id) ON DELETE RESTRICT", await ConstraintAsync("fk_wallet_top_up_requests_wallet"));
        Assert.Equal("FOREIGN KEY (tenant_id, support_ticket_id) REFERENCES saas.support_tickets(tenant_id, id) ON DELETE CASCADE", await ConstraintAsync("fk_support_ticket_replies_ticket"));
        Assert.Equal("FOREIGN KEY (tenant_id, machine_credential_id) REFERENCES identity.machine_credentials(tenant_id, id) ON DELETE RESTRICT", await ConstraintAsync("fk_site_agents_machine_credential"));

        // Through the model: a movement, a top-up request and a reply take their parent's tenant (EF fills the shadow key).
        var (laboratory, other) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await TestTenants.EnsureLaboratoryAsync(laboratory);
        await TestTenants.EnsureLaboratoryAsync(other);
        var user = await RegisterUserAsync();
        var wallet = Wallet.Open(laboratory, "EGP", Now.AddMonths(1));
        var movement = wallet.TopUp(250m, "TOPUP-1", null, user, Now);
        var request = wallet.RequestTopUp(100m, "please", user, Now);
        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(wallet);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        Guid ticketId;
        using (var scope = fixture.AppScope(laboratory, user))
        {
            var ticket = SupportTicket.Open(laboratory, "TKT-REG-1", "Keys", "Composite keys", SupportTicketPriority.Normal, user, "Lab Admin", null, null, Now);
            ticket.ReplyFromLaboratory(user, "Lab Admin", "More details.", Now.AddMinutes(1));
            scope.ServiceProvider.GetRequiredService<ISupportTicketRepository>().Add(ticket);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            ticketId = ticket.Id;
        }

        Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.wallet_transactions WHERE id = @id", ("id", movement.Id)));
        Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.wallet_top_up_requests WHERE id = @id", ("id", request.Id)));
        Assert.Equal(laboratory, await ScalarAsync<Guid>("SELECT tenant_id FROM saas.support_ticket_replies WHERE support_ticket_id = @id", ("id", ticketId)));

        // The composite reference: a movement of another tenant cannot point at this wallet.
        var crossed = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO saas.wallet_transactions (tenant_id, id, wallet_id, kind, amount, balance_after, reference, at_utc) VALUES (@other, gen_random_uuid(), @wallet, 'TopUp', 1, 1, 'X', now())",
            ("other", other), ("wallet", wallet.Id)));
        Assert.Equal("fk_wallet_transactions_wallet", crossed.ConstraintName);

        // The repositories find the re-keyed rows by their id as before.
        using var reader = fixture.AppScope(TenantIds.Platform);
        var wallets = reader.ServiceProvider.GetRequiredService<IWalletRepository>();
        Assert.Equal(wallet.Id, (await wallets.GetForUpdateAsync(wallet.Id, CancellationToken.None))?.Id);
        Assert.Equal(request.Id, (await wallets.FindTopUpRequestAsync(wallet.Id, request.Id, CancellationToken.None))?.Id);
        Assert.Equal(ticketId, (await reader.ServiceProvider.GetRequiredService<ISupportTicketRepository>().GetForUpdateAsync(ticketId, CancellationToken.None))?.Id);
    }

    private static async Task<(string Kind, bool NoCountry, bool NoGovernorate, bool NoPlan, string Slug)> PlatformRowAsync()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT kind, country_id IS NULL, governorate_id IS NULL, plan_id IS NULL, portal_slug FROM saas.tenants WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", TenantIds.Platform);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the platform tenant has no registry row");
        return (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetString(4));
    }

    private static Task<string> ConstraintAsync(string name) =>
        ScalarAsync<string>("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @name", ("name", name));

    private static async Task<int> CountAsync(string sql) => (int)await ScalarAsync<long>(sql);

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
