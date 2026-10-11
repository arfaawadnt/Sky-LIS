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
using Platform.SharedKernel.MultiTenancy;

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

    [DatabaseFact]
    public async Task A_key_the_tenant_never_set_reads_the_platform_value_before_the_descriptor_default()
    {
        // Intake LIS-INT-FND-001 §6 / SA-19 (UAT dry run D-16): the platform parameters are the defaults of every laboratory.
        var laboratory = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        using (var platform = fixture.AppScope(TenantIds.Platform, actor))
        {
            await platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.PasswordMinLength.Key, "10", actor, Now, CancellationToken.None);
            await platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.LabelCopies.Key, "oops", actor, Now, CancellationToken.None);   // an invalid platform row is skipped
            await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        try
        {
            using (var inherited = fixture.AppScope(laboratory, actor))
            {
                var settings = inherited.ServiceProvider.GetRequiredService<ITenantSettings>();
                Assert.Equal(10, await settings.GetAsync(SettingKeys.PasswordMinLength, CancellationToken.None));   // platform value
                Assert.Equal(1, await settings.GetAsync(SettingKeys.LabelCopies, CancellationToken.None));          // descriptor default
                Assert.Equal("10", (await settings.GetAllAsync(CancellationToken.None))[SettingKeys.PasswordMinLength.Key]);
                Assert.Empty(await inherited.ServiceProvider.GetRequiredService<ITenantSettingsStore>().ListAsync(CancellationToken.None));   // nothing stored for the laboratory
            }

            using (var writer = fixture.AppScope(laboratory, actor))
            {
                await writer.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.PasswordMinLength.Key, "12", actor, Now, CancellationToken.None);
                await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            }

            using var own = fixture.AppScope(laboratory, actor);
            Assert.Equal(12, await own.ServiceProvider.GetRequiredService<ITenantSettings>().GetAsync(SettingKeys.PasswordMinLength, CancellationToken.None));   // the tenant's own value wins
        }
        finally
        {
            using var cleanup = fixture.AppScope(TenantIds.Platform, actor);
            var context = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Set<TenantSetting>().RemoveRange(await context.Set<TenantSetting>().ToListAsync());
            await cleanup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }
    }

    [DatabaseFact]
    public async Task The_parameters_page_shows_the_effective_value_and_its_source()
    {
        // Validation 2026-10-11 C-09: a laboratory that never set labelCopies reads the platform's 2, and the page says so.
        var laboratory = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        using (var platform = fixture.AppScope(TenantIds.Platform, actor))
        {
            var store = platform.ServiceProvider.GetRequiredService<ITenantSettingsStore>();
            await store.UpsertAsync(SettingKeys.LabelCopies.Key, "2", actor, Now, CancellationToken.None);
            await store.UpsertAsync(SettingKeys.PasswordMinLength.Key, "10", actor, Now, CancellationToken.None);
            await store.UpsertAsync(SettingKeys.OtpLength.Key, "oops", actor, Now, CancellationToken.None);   // invalid: skipped
            await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        try
        {
            using (var writer = fixture.AppScope(laboratory, actor))
            {
                await writer.ServiceProvider.GetRequiredService<ITenantSettingsStore>().UpsertAsync(SettingKeys.PasswordMinLength.Key, "12", actor, Now, CancellationToken.None);
                await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            }

            using (var reader = fixture.AppScope(laboratory, actor))
            {
                var page = await Describe(reader);
                Assert.Equal(("2", "Platform", true, "2", "1"), Item(page, SettingKeys.LabelCopies.Key));
                Assert.Equal(("12", "Laboratory", false, "10", "8"), Item(page, SettingKeys.PasswordMinLength.Key));
                var otp = page.Settings.Single(item => item.Key == SettingKeys.OtpLength.Key);
                Assert.Equal((otp.DefaultValue, "Default", true, otp.DefaultValue), (otp.Value, otp.Source, otp.IsDefault, otp.InheritedValue));

                // The page agrees with what the modules read.
                var runtime = await reader.ServiceProvider.GetRequiredService<ITenantSettings>().GetAllAsync(CancellationToken.None);
                Assert.All(page.Settings, item => Assert.Equal(runtime[item.Key], item.Value));
            }

            using (var platformReader = fixture.AppScope(TenantIds.Platform, actor))
            {
                var page = await Describe(platformReader);
                Assert.Equal(("2", "Platform", false, "1", "1"), Item(page, SettingKeys.LabelCopies.Key));
            }
        }
        finally
        {
            using var cleanup = fixture.AppScope(TenantIds.Platform, actor);
            var context = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Set<TenantSetting>().RemoveRange(await context.Set<TenantSetting>().ToListAsync());
            await cleanup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        static Task<Platform.Contracts.Settings.TenantSettingsResponse> Describe(IServiceScope scope) =>
            new Platform.Application.Settings.Queries.GetTenantSettingsQueryHandler(
                    scope.ServiceProvider.GetRequiredService<ITenantSettingsStore>(),
                    scope.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Security.ICurrentTenant>())
                .Handle(new Platform.Application.Settings.Queries.GetTenantSettingsQuery(), CancellationToken.None);

        static (string Value, string Source, bool IsDefault, string Inherited, string Default) Item(Platform.Contracts.Settings.TenantSettingsResponse page, string key)
        {
            var item = page.Settings.Single(candidate => candidate.Key == key);
            return (item.Value, item.Source, item.IsDefault, item.InheritedValue, item.DefaultValue);
        }
    }

    private static (string? Before, string? After) Field(FieldChange change, string field)
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<List<Platform.Contracts.Audit.FieldChangeItem>>(change.Changes, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!
            .Single(candidate => candidate.Field == field);
        return (item.Before, item.After);
    }
}
