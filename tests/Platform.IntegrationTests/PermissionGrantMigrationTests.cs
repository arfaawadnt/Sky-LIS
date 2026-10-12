using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests;

/// <summary>
/// Validation 2026-10-11, C-01 on a real PostgreSQL 17: roles stored before migration GrantExportPermissions (the state of
/// v0.5.0) are migrated, and afterwards (1) every role holding a module's <c>.view</c> and <c>.manage</c> also holds its
/// export key, (2) nothing else is granted, and (3) the Platform Administrator holds the whole catalogue. The third assertion
/// is the standing gate: the platform administrator is seeded with two keys only, so a key added to
/// <see cref="PermissionCatalog"/> without a grant migration fails here (otherwise nobody could ever grant it: ORG-030).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PermissionGrantMigrationTests(DatabaseFixture fixture)
{
    /// <summary>The migration just before GrantExportPermissions: the schema the v0.5.0 roles were written with.</summary>
    private const string BeforeGrants = "20261011124135_AppendOnlyTruncateGuard";

    private static readonly Guid Laboratory = new("0199d000-0000-7000-8000-00000000c001");

    [DatabaseFact]
    public async Task The_platform_administrator_holds_every_catalogued_key_after_the_migrations()
    {
        Assert.NotNull(fixture.AppProvider);
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = context.GetService<IMigrator>();

        var labAdministratorBefore = EffectivePrivileges.TenantGrantableKeys.Where(key => key is not "organization.export" and not "treasury.export").ToArray();
        try
        {
            await DatabaseFixture.TruncateAsync(context);
            await migrator.MigrateAsync(BeforeGrants);

            var platformAdministrator = await InsertRoleAsync(TenantIds.Platform, "Platform Administrator", ["organization.view", "platform.view"]);
            var platformSupport = await InsertRoleAsync(TenantIds.Platform, "Platform Support", ["organization.manage", "organization.view", "tenancy.view"]);
            var labAdministrator = await InsertRoleAsync(Laboratory, "Laboratory Administrator", labAdministratorBefore);
            var treasurer = await InsertRoleAsync(Laboratory, "Treasurer", ["treasury.manage", "treasury.view"]);
            var reception = await InsertRoleAsync(Laboratory, "Reception", ["organization.view", "treasury.create", "treasury.view"]);
            var namesake = await InsertRoleAsync(Laboratory, "Platform Administrator", ["organization.view"]);

            await migrator.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            Assert.Equal(PermissionCatalog.Keys.Order(StringComparer.Ordinal), await KeysAsync(platformAdministrator));
            Assert.Equal(["organization.export", "organization.manage", "organization.view", "tenancy.view"], await KeysAsync(platformSupport));
            Assert.Equal(EffectivePrivileges.TenantGrantableKeys, await KeysAsync(labAdministrator));
            Assert.Equal(["treasury.export", "treasury.manage", "treasury.view"], await KeysAsync(treasurer));
            Assert.Equal(["organization.view", "treasury.create", "treasury.view"], await KeysAsync(reception));
            Assert.Equal(["organization.view"], await KeysAsync(namesake));   // only the platform tenant's administrator gets the catalogue
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

    private static async Task<string[]> KeysAsync(Guid roleId)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT permission_keys FROM app.roles WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", roleId);
        return (string[])(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("role not found"));
    }
}
