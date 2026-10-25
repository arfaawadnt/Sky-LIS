using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.PlatformAdmin;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;
using NotificationTemplate = Platform.Domain.PlatformAdmin.NotificationTemplate;
using Platform.TestSupport;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-PLT-001: the saas grants and unique indexes, the template source served to
/// every sender, the sign-in status port, the cross-tenant count function (platform scope only), wallets and code
/// imports. The fixtures truncate saas too, so every test creates its own reference rows.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlatformAdminModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_runtime_role_reads_and_writes_the_platform_tables_and_unique_keys_are_UNIQUE_409()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();

        var duplicateIso = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(null, scope => scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(NewCountry(country.IsoCode))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateIso.Code);

        var duplicatePlan = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(null, scope => scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(NewPlan(plan.Code))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicatePlan.Code);

        var duplicateGovernorate = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(null, scope =>
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(Governorate.Create(country, governorate.Name.En.ToUpperInvariant(), "مكرر", null, null))));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateGovernorate.Code);   // ux_governorates_country_name is case-insensitive

        var first = await ProvisionAsync(country, governorate, plan, "unique-slug");
        Assert.NotEqual(Guid.Empty, first);
        var duplicateSlug = await Assert.ThrowsAsync<DomainException>(() => ProvisionAsync(country, governorate, plan, "unique-slug"));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateSlug.Code);
    }

    [DatabaseFact]
    public async Task A_client_and_its_wallet_are_listed_with_their_reference_names()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var tenantId = await ProvisionAsync(country, governorate, plan, "listed-lab");

        using var scope = fixture.AppScope(TenantIds.Platform);
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var page = await tenants.ListAsync(new ClientFilter("listed", TenantSource.Manual, plan.Id, TenantStatus.Active), PageRequest.Normalized(1, 20), "https://lis.example", CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.Equal((tenantId, "https://lis.example/lab/listed-lab", country.Name.En, governorate.Name.En, plan.Code), (item.Id, item.PortalUrl, item.CountryNameEn, item.GovernorateNameEn, item.PlanCode));
        Assert.Equal((0m, "EGP"), (item.WalletBalance, item.WalletCurrency));

        var detail = (await tenants.GetDetailAsync(tenantId, "https://lis.example", CancellationToken.None))!;
        Assert.Equal(("+201012345678", "Active", "Manual"), (detail.Mobile, detail.Status, detail.Source));
    }

    [DatabaseFact]
    public async Task Wallet_movements_are_inserted_without_loading_the_history_and_projected_newest_first()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var tenantId = await ProvisionAsync(country, governorate, plan, "wallet-lab");

        await SaveAsync(TenantIds.Platform, async scope =>
        {
            var wallet = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(tenantId, CancellationToken.None))!;
            Assert.Empty(wallet.Transactions);
            wallet.TopUp(500m, "INV-1", null, null, Now);
        });
        await SaveAsync(TenantIds.Platform, async scope =>
        {
            var wallet = (await scope.ServiceProvider.GetRequiredService<IWalletRepository>().GetByTenantForUpdateAsync(tenantId, CancellationToken.None))!;
            wallet.DebitUsage(0.5m, "sms", Now.AddMinutes(1));
        });

        using var reader = fixture.AppScope(TenantIds.Platform);
        var wallets = reader.ServiceProvider.GetRequiredService<IWalletRepository>();
        var detail = (await wallets.GetDetailAsync(tenantId, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None))!;
        Assert.Equal((499.5m, 1), (detail.Balance, detail.SmsUsedThisMonth));

        var history = await wallets.ListTransactionsAsync(tenantId, null, PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(["Usage", "TopUp"], history.Items.Select(item => item.Kind));
        Assert.Equal(499.5m, history.Items[0].BalanceAfter);
    }

    [DatabaseFact]
    public async Task Senders_receive_the_active_template_with_aliases_and_the_built_in_text_otherwise()
    {
        await SaveAsync(null, scope =>
        {
            var templates = scope.ServiceProvider.GetRequiredService<INotificationTemplateRepository>();
            templates.Add(NotificationTemplate.Create("portal-url", TemplateChannel.Sms, "{lab} at {url}", "{lab} على {url}", null, null));
            var inactive = NotificationTemplate.Create("otp", TemplateChannel.Sms, "custom {code}", "مخصص {code}", null, null);
            inactive.Deactivate();
            templates.Add(inactive);
        });

        using var scope = fixture.AppScope(Guid.CreateVersion7());   // any tenant (or none) reads the platform templates
        var source = scope.ServiceProvider.GetRequiredService<INotificationTemplateSource>();

        var arabic = (await source.FindAsync("portal-url", "ar-EG", CancellationToken.None))!;
        Assert.Equal(("ar", "{labName} على {url}"), (arabic.Language, arabic.Body));
        Assert.Equal("Noor على https://x", arabic.Render(new Dictionary<string, string> { ["labName"] = "Noor", ["url"] = "https://x" }));

        var fallback = (await source.FindAsync("otp", "en", CancellationToken.None))!;
        Assert.DoesNotContain("custom", fallback.Body, StringComparison.Ordinal);
        Assert.Contains("{code}", fallback.Body, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task The_status_port_blocks_inactive_clients_and_expired_trials_only()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        var active = await ProvisionAsync(country, governorate, plan, "status-active");
        var trial = await ProvisionAsync(country, governorate, plan, "status-trial", TenantSource.Subscription, trialDays: 1, at: Now.AddYears(-1));
        var inactive = await ProvisionAsync(country, governorate, plan, "status-inactive");
        await SaveAsync(TenantIds.Platform, async scope =>
        {
            var tenant = (await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetForUpdateAsync(inactive, CancellationToken.None))!;
            tenant.Deactivate("unpaid", Now);
        });

        using var scope = fixture.AppScope(null);
        var lookup = scope.ServiceProvider.GetRequiredService<ITenantStatusLookup>();
        Assert.True(await lookup.IsActiveAsync(TenantIds.Platform, CancellationToken.None));
        Assert.True(await lookup.IsActiveAsync(Guid.CreateVersion7(), CancellationToken.None));   // no client record (development seeding)
        Assert.True(await lookup.IsActiveAsync(active, CancellationToken.None));
        Assert.False(await lookup.IsActiveAsync(inactive, CancellationToken.None));
        Assert.True(await lookup.IsActiveAsync(trial, CancellationToken.None));                   // a one-day trial started a year ago: LIS-CR-012, no longer CLI-020 — expired (SUB-020), decided by the subscription state
        Assert.True((await scope.ServiceProvider.GetRequiredService<ISubscriptionStateLookup>().FindAsync(trial, CancellationToken.None)).IsExpired);
    }

    [DatabaseFact]
    public async Task Cross_tenant_counts_are_served_to_the_platform_tenant_only()
    {
        var tenantA = Guid.CreateVersion7();
        var user = await RegisterMemberAsync(tenantA);
        Assert.NotEqual(Guid.Empty, user);

        using (var platform = fixture.AppScope(TenantIds.Platform))
        {
            var counts = await platform.ServiceProvider.GetRequiredService<IClientUsageCounts>().GetAsync([tenantA, Guid.CreateVersion7()], CancellationToken.None);
            Assert.Equal(1, counts[tenantA].Users);
        }

        using var client = fixture.AppScope(tenantA);
        await Assert.ThrowsAnyAsync<Exception>(() => client.ServiceProvider.GetRequiredService<IClientUsageCounts>().GetAsync([tenantA], CancellationToken.None));
    }

    [DatabaseFact]
    public async Task A_code_import_upserts_by_code_and_lists_the_items()
    {
        var codeSystemId = Guid.Empty;
        await SaveAsync(null, scope =>
        {
            var codeSystem = CodeSystem.Create(CodeSystemName.Icd10, "2026-" + Guid.CreateVersion7().ToString("N")[..6], "diagnoses");
            codeSystemId = codeSystem.Id;
            scope.ServiceProvider.GetRequiredService<ICodeSystemRepository>().Add(codeSystem);
        });

        await ImportAsync(codeSystemId, [("E11", "Type 2 diabetes"), ("I10", "Hypertension")]);
        await ImportAsync(codeSystemId, [("E11", "Type 2 diabetes mellitus"), ("J45", "Asthma")]);

        using var reader = fixture.AppScope(TenantIds.Platform);
        var repository = reader.ServiceProvider.GetRequiredService<ICodeSystemRepository>();
        var page = await repository.ListItemsAsync(codeSystemId, null, PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal([("E11", "Type 2 diabetes mellitus"), ("I10", "Hypertension"), ("J45", "Asthma")], page.Items.Select(item => (item.Code, item.Display)));
        Assert.Equal(3, (await repository.GetForUpdateAsync(codeSystemId, CancellationToken.None))!.ItemCount);
    }

    [DatabaseFact]
    public async Task The_session_directory_lists_live_sessions_of_one_tenant()
    {
        var userId = await RegisterMemberAsync(TenantIds.Platform);
        await SaveAsync(null, scope => scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>().Add(
            RefreshSession.Open(userId, TenantIds.Platform, SessionKind.Browser, SessionScope.Full, System.Security.Cryptography.RandomNumberGenerator.GetBytes(RefreshSession.TokenHashLength), Now, TimeSpan.FromDays(3650), TimeSpan.FromDays(3650))));

        using var scope = fixture.AppScope(TenantIds.Platform);
        var rows = await scope.ServiceProvider.GetRequiredService<IPlatformSessionDirectory>().ListLiveAsync(TenantIds.Platform, Now.AddMinutes(1), CancellationToken.None);
        var row = Assert.Single(rows, item => item.UserId == userId);
        Assert.Equal(Now, row.SinceUtc);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IPlatformSessionDirectory>().ListLiveAsync(Guid.CreateVersion7(), Now, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_provisioning_scope_reads_the_platform_tenant_writes_the_new_one_and_commits_or_rolls_back_as_a_whole()
    {
        var (country, governorate, plan) = await SeedPlaceAsync();
        await RegisterMemberAsync(TenantIds.Platform);

        var committed = NewClient(country, governorate, plan, "scope-ok");
        var adminUser = await RegisterUserOnlyAsync();
        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            var accessor = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<ITenantProvisioningScope>().RunAsync(committed.Id, async ct =>
            {
                Assert.Equal((committed.Id, false), (accessor.TenantId, accessor.IsElevated));   // the work itself is not elevated
                Assert.True(await context.TenantMemberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == TenantIds.Platform, ct));   // the connection may read the platform tenant (BR-25)
                scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(committed);
                scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(TenantMembership.Create(committed.Id, adminUser, "ADMIN", new LocalizedText("Admin", "مدير"), "en", Now));
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            }, CancellationToken.None);
            Assert.Equal((TenantIds.Platform, false), (accessor.TenantId, accessor.IsElevated));
        }

        var rolledBack = NewClient(country, governorate, plan, "scope-abort");
        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ITenantProvisioningScope>().RunAsync(rolledBack.Id, async ct =>
            {
                scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(rolledBack);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
                throw new InvalidOperationException("a later step failed");
            }, CancellationToken.None));
        }

        using var reader = fixture.AppScope(null);
        var tenants = reader.ServiceProvider.GetRequiredService<ITenantRepository>();
        Assert.NotNull(await tenants.FindStatusAsync(committed.Id, CancellationToken.None));
        Assert.Null(await tenants.FindStatusAsync(rolledBack.Id, CancellationToken.None));
        using var newTenant = fixture.AppScope(committed.Id);
        Assert.Equal(1, await newTenant.ServiceProvider.GetRequiredService<AppDbContext>().TenantMemberships.CountAsync());
    }

    private static Tenant NewClient(Country country, Governorate governorate, Plan plan, string slug) =>
        Tenant.Provision(ClientProfile.Create("Lab " + slug, "مختبر", country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null), country, governorate, plan, slug, TenantSource.Manual, "en", 14, Now);

    private async Task<Guid> RegisterUserOnlyAsync()
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"adm.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Admin", "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static Country NewCountry(string iso) =>
        Country.Create(new CountryData("Testland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));

    private static Plan NewPlan(string code) =>
        Plan.Create(code, new PlanTerms(new LocalizedText("Plan " + code, "باقة " + code), 100m, "EGP", 1, 5, 100, 1));

    private async Task<(Country Country, Governorate Governorate, Plan Plan)> SeedPlaceAsync()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var iso = TestCountryCodes.Next();
        var country = NewCountry(iso);
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = NewPlan("P" + suffix[^8..].ToUpperInvariant());
        await SaveAsync(null, scope =>
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
        });
        return (country, governorate, plan);
    }

    private async Task<Guid> ProvisionAsync(Country country, Governorate governorate, Plan plan, string slug, TenantSource source = TenantSource.Manual, int trialDays = 14, DateTimeOffset? at = null)
    {
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345678", null, "Manager", "01112223333", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, source, "en", trialDays, at ?? Now);
        using var scope = fixture.AppScope(tenant.Id);   // the provisioning scope: the new tenant (intake §11)
        scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
        scope.ServiceProvider.GetRequiredService<IWalletRepository>().Add(Wallet.Open(tenant.Id, country.CurrencyCode, Now.AddMonths(1)));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tenant.Id;
    }

    private async Task<Guid> RegisterMemberAsync(Guid tenantId)
    {
        using var scope = fixture.AppScope(tenantId);
        var user = PlatformUser.Register($"plt.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Platform Tester", "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(TenantMembership.Create(tenantId, user.Id, "E-" + user.Username[^6..], new LocalizedText("Tester", "مختبر"), "en", Now));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task ImportAsync(Guid codeSystemId, IReadOnlyList<(string, string)> rows)
    {
        await SaveAsync(TenantIds.Platform, async scope =>
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICodeSystemRepository>();
            var codeSystem = (await repository.GetForUpdateAsync(codeSystemId, CancellationToken.None))!;
            var result = codeSystem.Import(await repository.LoadItemsAsync(codeSystemId, CancellationToken.None), rows, Now);
            repository.AddItems(result.NewItems);
        });
    }

    private Task SaveAsync(Guid? tenantId, Action<IServiceScope> stage) => SaveAsync(tenantId, scope =>
    {
        stage(scope);
        return Task.CompletedTask;
    });

    private async Task SaveAsync(Guid? tenantId, Func<IServiceScope, Task> stage)
    {
        using var scope = fixture.AppScope(tenantId);
        await stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
