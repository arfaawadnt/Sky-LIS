using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// Validation F-07 on a real PostgreSQL 17: roles and membership overrides stored before migration
/// RemovePlatformKeysFromLaboratoryRoles (the demo laboratory's administrator held the whole catalogue) are migrated, and
/// afterwards (1) no role or override outside the platform tenant holds a platform-only key (<c>platform.*</c>,
/// <c>tenancy.*</c>), (2) every other key stays, distinct and ordinally sorted, and (3) the platform tenant is untouched.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlatformKeyRemovalMigrationTests(DatabaseFixture fixture)
{
    /// <summary>The migration just before RemovePlatformKeysFromLaboratoryRoles.</summary>
    private const string BeforeRemoval = "20261012021635_LaboratoryLogo";

    private static readonly Guid Laboratory = new("0199d000-0000-7000-8000-00000000c002");
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Laboratory_roles_and_overrides_lose_the_platform_only_keys_and_keep_everything_else()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = context.GetService<IMigrator>();

        var everyKey = PermissionCatalog.Keys.Order(StringComparer.Ordinal).ToArray();
        try
        {
            await DatabaseFixture.TruncateAsync(context);
            // The memberships go in through the production services on the current schema (the EF model writes every column,
            // including those later migrations added); the roles go in by SQL on the schema just before the removal.
            var overridden = await InsertMembershipAsync(Laboratory, added: ["platform.view", "results.view", "tenancy.manage"], removed: ["registration.view", "tenancy.view"]);
            var plain = await InsertMembershipAsync(Laboratory, added: ["results.view"], removed: []);
            var platformMember = await InsertMembershipAsync(TenantIds.Platform, added: ["tenancy.view"], removed: ["platform.manage"]);
            await migrator.MigrateAsync(BeforeRemoval);

            var demoAdministrator = await InsertRoleAsync(Laboratory, "Laboratory Administrator", everyKey);   // what seed-demo-lab stored
            var reception = await InsertRoleAsync(Laboratory, "Reception", ["registration.create", "registration.view"]);
            var platformAdministrator = await InsertRoleAsync(TenantIds.Platform, "Platform Administrator", everyKey);

            await migrator.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            Assert.Equal(EffectivePrivileges.TenantGrantableKeys, await ArrayAsync("SELECT permission_keys FROM app.roles WHERE id = @id", demoAdministrator));
            Assert.Equal("Migration RemovePlatformKeysFromLaboratoryRoles", await StampAsync("app.roles", demoAdministrator));
            Assert.Equal(["registration.create", "registration.view"], await ArrayAsync("SELECT permission_keys FROM app.roles WHERE id = @id", reception));
            Assert.Null(await StampAsync("app.roles", reception));   // a role without platform keys is not touched
            Assert.Equal(everyKey, await ArrayAsync("SELECT permission_keys FROM app.roles WHERE id = @id", platformAdministrator));

            Assert.Equal(["results.view"], await ArrayAsync("SELECT added_permission_keys FROM app.tenant_memberships WHERE id = @id", overridden));
            Assert.Equal(["registration.view"], await ArrayAsync("SELECT removed_permission_keys FROM app.tenant_memberships WHERE id = @id", overridden));
            Assert.Equal(["results.view"], await ArrayAsync("SELECT added_permission_keys FROM app.tenant_memberships WHERE id = @id", plain));
            Assert.Equal(["tenancy.view"], await ArrayAsync("SELECT added_permission_keys FROM app.tenant_memberships WHERE id = @id", platformMember));
            Assert.Equal(["platform.manage"], await ArrayAsync("SELECT removed_permission_keys FROM app.tenant_memberships WHERE id = @id", platformMember));

            // the standing rule: no row outside the platform tenant holds a platform-only key
            Assert.Equal(0L, await CountAsync("""
                SELECT count(*) FROM app.roles r
                WHERE r.tenant_id <> '00000000-0000-0000-0000-000000000001'
                  AND EXISTS (SELECT 1 FROM unnest(r.permission_keys) k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'))
                """));
            Assert.Equal(0L, await CountAsync("""
                SELECT count(*) FROM app.tenant_memberships m
                WHERE m.tenant_id <> '00000000-0000-0000-0000-000000000001'
                  AND EXISTS (SELECT 1 FROM unnest(m.added_permission_keys || m.removed_permission_keys) k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'))
                """));
            Assert.All(EffectivePrivileges.PlatformOnlyModules, module => Assert.Contains(module, new[] { "platform", "tenancy" }));   // the SQL names every platform-only module
        }
        finally
        {
            await migrator.MigrateAsync();
            await DatabaseFixture.TruncateAsync(context);
        }
    }

    private static async Task<Guid> InsertRoleAsync(Guid tenantId, string name, string[] keys)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO app.roles (id, tenant_id, name_en, name_ar, permission_keys, is_active, created_at_utc) VALUES (@id, @tenant, @name, @name, @keys, TRUE, now())",
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("keys", keys.Order(StringComparer.Ordinal).ToArray());
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>A user and a membership through the production services, then the override keys written as stored before the migration.</summary>
    private async Task<Guid> InsertMembershipAsync(Guid tenantId, string[] added, string[] removed)
    {
        Guid membershipId;
        using (var scope = fixture.AppScope(tenantId))
        {
            var user = PlatformUser.Register($"pk.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Key Tester", "hash", Now);
            scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
            var membership = TenantMembership.Create(tenantId, user.Id, "PK-" + Guid.CreateVersion7().ToString("N")[^9..], new LocalizedText("Key Tester", "مختبر"), "en", Now);
            scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(membership);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            membershipId = membership.Id;
        }

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE app.tenant_memberships SET added_permission_keys = @added, removed_permission_keys = @removed WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", membershipId);
        command.Parameters.AddWithValue("added", added.Order(StringComparer.Ordinal).ToArray());
        command.Parameters.AddWithValue("removed", removed.Order(StringComparer.Ordinal).ToArray());
        await command.ExecuteNonQueryAsync();
        return membershipId;
    }

    private static async Task<string[]> ArrayAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (string[])(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("row not found"));
    }

    private static async Task<string?> StampAsync(string table, Guid id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT modified_by_display_name FROM {table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
