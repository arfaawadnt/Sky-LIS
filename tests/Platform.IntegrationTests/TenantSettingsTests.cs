using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Settings;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Audit;
using Platform.Infrastructure.Persistence.Settings;
using Platform.IntegrationTests.Support;

namespace Platform.IntegrationTests;

/// <summary>SRS CP-57 on a real PostgreSQL 17: defaults, stored values, per-scope caching, tenant invisibility and the field audit of a parameter change.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TenantSettingsTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_module_reads_the_default_until_the_tenant_stores_a_value_and_a_bad_row_reads_as_the_default()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        using (var fresh = fixture.AppScope(tenant, actor))
        {
            var settings = fresh.ServiceProvider.GetRequiredService<ITenantSettings>();
            Assert.Equal(1, await settings.GetAsync(SettingKeys.LabelCopies, CancellationToken.None));
            Assert.True(await settings.GetAsync(SettingKeys.AutoCollect, CancellationToken.None));
            Assert.Equal(25, (await settings.GetAllAsync(CancellationToken.None)).Count);
        }

        using (var writer = fixture.AppScope(tenant, actor, "Lab Admin"))
        {
            var store = writer.ServiceProvider.GetRequiredService<ITenantSettingsStore>();
            await store.UpsertAsync(SettingKeys.LabelCopies.Key, "3", actor, Now, CancellationToken.None);
            await store.UpsertAsync(SettingKeys.AutoCollect.Key, "false", actor, Now, CancellationToken.None);
            await store.UpsertAsync(SettingKeys.DiscountCapPercent.Key, "999", actor, Now, CancellationToken.None);   // bypasses the handler's validation on purpose
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant, actor);
        var read = reader.ServiceProvider.GetRequiredService<ITenantSettings>();
        Assert.Equal(3, await read.GetAsync(SettingKeys.LabelCopies, CancellationToken.None));
        Assert.False(await read.GetAsync(SettingKeys.AutoCollect, CancellationToken.None));
        Assert.Equal(20, await read.GetAsync(SettingKeys.DiscountCapPercent, CancellationToken.None));   // out of range → default
        Assert.Equal(3, (await reader.ServiceProvider.GetRequiredService<ITenantSettingsStore>().ListAsync(CancellationToken.None)).Count);

        var changes = await reader.ServiceProvider.GetRequiredService<AppDbContext>().Set<FieldChange>()
            .Where(change => change.EntityType == nameof(TenantSetting))
            .OrderBy(change => change.EntityId)
            .ToListAsync();
        Assert.Equal(3, changes.Count);
        Assert.Equal(["autoCollect", "discountCapPercent", "labelCopies"], changes.Select(change => change.EntityId).ToArray());
        Assert.All(changes, change => Assert.Equal(FieldChangeAction.Created, change.Action));
        Assert.Equal((null, "3"), Field(changes[2], "Value"));
        Assert.Equal("Lab Admin", changes[2].ActorName);
    }

    [DatabaseFact]
    public async Task Values_are_cached_per_scope_and_invisible_to_another_tenant()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var actor = Guid.CreateVersion7();

        using var scopeA = fixture.AppScope(tenantA, actor);
        var settingsA = scopeA.ServiceProvider.GetRequiredService<ITenantSettings>();
        Assert.Equal("en", await settingsA.GetAsync(SettingKeys.ResultLanguage, CancellationToken.None));   // first read loads the (empty) set

        using (var writer = fixture.AppScope(tenantA, actor))
        {
            await writer.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.ResultLanguage.Key, "ar", actor, Now, CancellationToken.None);
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal("en", await settingsA.GetAsync(SettingKeys.ResultLanguage, CancellationToken.None));   // the scope keeps what it loaded: one query per request

        using var scopeA2 = fixture.AppScope(tenantA, actor);
        Assert.Equal("ar", await scopeA2.ServiceProvider.GetRequiredService<ITenantSettings>().GetAsync(SettingKeys.ResultLanguage, CancellationToken.None));

        using var scopeB = fixture.AppScope(tenantB, actor);
        Assert.Equal("en", await scopeB.ServiceProvider.GetRequiredService<ITenantSettings>().GetAsync(SettingKeys.ResultLanguage, CancellationToken.None));
        Assert.Equal(0, await scopeB.ServiceProvider.GetRequiredService<AppDbContext>().Set<TenantSetting>().IgnoreQueryFilters().CountAsync());   // the engine hides tenant A

        // Updating in place: one Modified field change with before/after.
        using (var writer = fixture.AppScope(tenantA, actor))
        {
            await writer.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.ResultLanguage.Key, "en", actor, Now.AddMinutes(1), CancellationToken.None);
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var auditScope = fixture.AppScope(tenantA, actor);
        var modified = await auditScope.ServiceProvider.GetRequiredService<AppDbContext>().Set<FieldChange>()
            .SingleAsync(change => change.EntityType == nameof(TenantSetting) && change.Action == FieldChangeAction.Modified);
        Assert.Equal(("ar", "en"), Field(modified, "Value"));
    }

    private static (string? Before, string? After) Field(FieldChange change, string field)
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<List<Platform.Contracts.Audit.FieldChangeItem>>(change.Changes, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!
            .Single(candidate => candidate.Field == field);
        return (item.Before, item.After);
    }
}
