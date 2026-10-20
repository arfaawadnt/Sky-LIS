using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Organization;
using Platform.Application.PlatformAdmin;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Organization;
using Platform.Infrastructure.Security;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-ORG-001: tenant isolation of branches, roles and result rights, the unique
/// indexes, the owned collections, the effective privileges of the session principal (BR-17), cache eviction, the
/// branch list of an unrestricted membership at sign-in, the users register and the provisioning adapters.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrganizationModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Branches_roles_and_result_rights_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var branchId = await CreateBranchAsync(tenantA, "ISO");
        var roleId = await CreateRoleAsync(tenantA, "Isolated", ["results.view"], [new ResultRight(Guid.CreateVersion7(), null, true, false, true, false, false)]);

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Branch>().IgnoreQueryFilters().CountAsync(branch => branch.Id == branchId));
        Assert.Equal(0, await context.Roles.IgnoreQueryFilters().CountAsync(role => role.Id == roleId));
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM app.role_result_rights WHERE role_id = {roleId}").SingleAsync());
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IBranchLookup>().FindAsync(branchId, CancellationToken.None));

        using var scopeA = fixture.AppScope(tenantA);
        Assert.Equal(1, await scopeA.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM app.role_result_rights WHERE role_id = {roleId}").SingleAsync());
    }

    [DatabaseFact]
    public async Task A_duplicate_branch_code_or_role_name_is_a_unique_violation()
    {
        var tenant = Guid.CreateVersion7();
        await CreateBranchAsync(tenant, "DUP", exactCode: true);
        var branch = await Assert.ThrowsAsync<DomainException>(() => CreateBranchAsync(tenant, "DUP", exactCode: true));
        Assert.Equal(ProblemCodes.UniqueViolation, branch.Code);

        await CreateRoleAsync(tenant, "Reception", ["registration.view"], [], exactName: true);
        var role = await Assert.ThrowsAsync<DomainException>(() => CreateRoleAsync(tenant, "RECEPTION", ["registration.view"], [], exactName: true));   // ux_roles_tenant_name_en (module SQL)
        Assert.Equal(ProblemCodes.UniqueViolation, role.Code);

        await CreateBranchAsync(Guid.CreateVersion7(), "DUP", exactCode: true);   // another tenant may reuse the code
    }

    [DatabaseFact]
    public async Task Result_rights_round_trip_update_in_place_and_project_into_the_detail()
    {
        var tenant = Guid.CreateVersion7();
        var (chemistry, haematology, branch) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var roleId = await CreateRoleAsync(tenant, "Chemist", ["results.view"], [new ResultRight(chemistry, null, true, false, false, false, false), new ResultRight(haematology, branch, true, true, false, false, false)]);

        using (var scope = fixture.AppScope(tenant))
        {
            var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().GetForUpdateAsync(roleId, CancellationToken.None))!;
            Assert.Equal(2, role.ResultRights.Count);
            role.SetResultRights([new ResultRight(chemistry, null, true, true, true, false, true)], Now);
            role.SetSpecialRights(new SpecialRights(15, true, true, false, false, false, false, true, false), Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var detail = (await reader.ServiceProvider.GetRequiredService<IRoleRepository>().GetDetailAsync(roleId, CancellationToken.None))!;
        var right = Assert.Single(detail.ResultRights);
        Assert.Equal((chemistry, null, true, true, true, false, true), (right.ClinicalGroupId, right.BranchId, right.CanSee, right.CanReview, right.CanVerify, right.CanEdit, right.CanPrint));
        Assert.Equal(15, detail.SpecialRights.DiscountCapPercent);
        Assert.True(detail.SpecialRights.CanRefund);
        Assert.Equal(0, detail.MemberCount);
    }

    [DatabaseFact]
    public async Task The_principal_carries_role_plus_added_minus_removed_without_platform_keys_and_the_special_and_result_rights()
    {
        var tenant = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var userId = await RegisterUserAsync();
        var roleId = await CreateRoleAsync(tenant, "Front desk", ["registration.view", "registration.create", "tenancy.view"], [new ResultRight(group, null, true, false, false, false, true)]);
        var membershipId = await CreateMembershipAsync(tenant, userId, roleId, membership =>
            membership.SetOverrides(["patients.view"], ["registration.create"], new SpecialRights(5, true, false, false, false, false, false, false, false), Now));
        var sessionId = await OpenSessionAsync(tenant, userId);

        using var scope = fixture.AppScope(null);
        var principal = (await PrincipalQuery(scope).ResolveAsync(sessionId, userId, CancellationToken.None))!;

        Assert.Equal(["patients.view", "registration.view"], principal.PermissionKeys.Order(StringComparer.Ordinal));   // tenancy.view filtered outside the platform tenant
        Assert.Equal(5, principal.SpecialRights.DiscountCapPercent);
        Assert.True(principal.SpecialRights.CanPay);
        Assert.Equal(new ResultRight(group, null, true, false, false, false, true), Assert.Single(principal.ResultRights));
        Assert.Equal((membershipId, roleId), (principal.MembershipId!.Value, principal.RoleId!.Value));
        Assert.StartsWith("Front desk ", principal.RoleName, StringComparison.Ordinal);   // the helper suffixes names to keep them unique
    }

    [DatabaseFact]
    public async Task A_role_change_reaches_the_principal_once_the_members_cache_is_evicted()
    {
        var tenant = Guid.CreateVersion7();
        var userId = await RegisterUserAsync();
        var roleId = await CreateRoleAsync(tenant, "Evicted", ["registration.view"], []);
        await CreateMembershipAsync(tenant, userId, roleId);
        var sessionId = await OpenSessionAsync(tenant, userId);

        using var scope = fixture.AppScope(tenant);
        var query = PrincipalQuery(scope);
        Assert.DoesNotContain("billing.view", (await query.ResolveAsync(sessionId, userId, CancellationToken.None))!.PermissionKeys);

        using (var writer = fixture.AppScope(tenant))
        {
            var role = (await writer.ServiceProvider.GetRequiredService<IRoleRepository>().GetForUpdateAsync(roleId, CancellationToken.None))!;
            role.SetPermissions(["registration.view", "billing.view"], Now);
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        Assert.DoesNotContain("billing.view", (await query.ResolveAsync(sessionId, userId, CancellationToken.None))!.PermissionKeys);   // cached ≤ 10 s

        await new PrincipalCacheInvalidator(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<TenantContextAccessor>(), query)
            .EvictRoleMembersAsync(roleId, CancellationToken.None);

        Assert.Contains("billing.view", (await query.ResolveAsync(sessionId, userId, CancellationToken.None))!.PermissionKeys);
    }

    [DatabaseFact]
    public async Task An_unrestricted_membership_offers_every_active_branch_of_its_tenant_at_sign_in()
    {
        var tenant = Guid.CreateVersion7();
        var userId = await RegisterUserAsync();
        await CreateMembershipAsync(tenant, userId, null);
        var main = await CreateBranchAsync(tenant, "MAIN");
        var closed = await CreateBranchAsync(tenant, "CLOSED");
        await CreateBranchAsync(Guid.CreateVersion7(), "OTHER");   // another tenant's branch never appears
        using (var scope = fixture.AppScope(tenant))
        {
            var branch = (await scope.ServiceProvider.GetRequiredService<IBranchRepository>().GetForUpdateAsync(closed, CancellationToken.None))!;
            branch.Deactivate(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(null);
        var directory = new MembershipDirectory(reader.ServiceProvider.GetRequiredService<AppDbContext>(), reader.ServiceProvider.GetRequiredService<TenantContextAccessor>(), reader.ServiceProvider.GetRequiredService<ITenantStatusLookup>());
        var membership = Assert.Single(await directory.ListActiveForUserAsync(userId, CancellationToken.None));

        Assert.Equal([main], membership.BranchIds);   // exactly one: sign-in auto-selects it

        using var tenantScope = fixture.AppScope(tenant);
        var lookup = tenantScope.ServiceProvider.GetRequiredService<IBranchLookup>();
        Assert.True(await lookup.IsActiveAsync(main, CancellationToken.None));
        Assert.False(await lookup.IsActiveAsync(closed, CancellationToken.None));
        Assert.Equal([main], (await lookup.ListActiveAsync(CancellationToken.None)).Select(branch => branch.Id));
    }

    [DatabaseFact]
    public async Task The_users_register_filters_by_status_and_search_and_names_come_from_the_membership()
    {
        var tenant = Guid.CreateVersion7();
        var roleId = await CreateRoleAsync(tenant, "Lab", ["results.view"], []);
        var active = await RegisterUserAsync("Platform Name");
        var locked = await RegisterUserAsync();
        await CreateMembershipAsync(tenant, active, roleId);
        await CreateMembershipAsync(tenant, locked, roleId);
        using (var scope = fixture.AppScope(null))
        {
            var user = (await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().GetForUpdateAsync(locked, CancellationToken.None))!;
            user.LockByAdministrator(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var directory = reader.ServiceProvider.GetRequiredService<IStaffDirectory>();
        var lockedPage = await directory.ListAsync(new StaffFilter(null, roleId, StaffStatus.Locked), PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        var lockedItem = Assert.Single(lockedPage.Items);
        Assert.Equal((locked, "Locked"), (lockedItem.UserId, lockedItem.Status));
        Assert.StartsWith("Lab ", lockedItem.RoleNameEn, StringComparison.Ordinal);   // the helper suffixes names to keep them unique

        var all = await directory.ListAsync(new StaffFilter(null, null, null), PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var account = (await directory.GetAsync(active, CancellationToken.None))!;
        Assert.Equal(["results.view"], account.RolePermissionKeys);
        Assert.True(account.RoleActive);

        var names = await reader.ServiceProvider.GetRequiredService<IUserDirectory>().GetNamesAsync([active, Guid.CreateVersion7()], CancellationToken.None);
        Assert.Equal("Member Name", Assert.Single(names).Value.DisplayName);   // the membership's name wins over the platform one
    }

    [DatabaseFact]
    public async Task Provisioning_stages_the_main_branch_and_the_primary_administrator_for_one_commit()
    {
        var tenant = Guid.CreateVersion7();
        var username = $"adm.{Guid.CreateVersion7():N}"[..24];

        Guid userId;
        using (var scope = fixture.AppScope(tenant))
        {
            var branchId = await scope.ServiceProvider.GetRequiredService<IDefaultBranchProvisioner>().ProvisionAsync(tenant, new LocalizedText("Al Noor Lab", "معمل النور"), CancellationToken.None);
            userId = await scope.ServiceProvider.GetRequiredService<ITenantAdministratorProvisioner>().ProvisionAsync(tenant, username, "Lab Manager", "+201000000123", "$argon2id$v=19$m=65536,t=3,p=1$c2FsdA$aGFzaA", CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            Assert.NotEqual(Guid.Empty, branchId);
        }

        using var reader = fixture.AppScope(tenant);
        var membership = (await reader.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().FindByPlatformUserAsync(userId, CancellationToken.None))!;
        Assert.True(membership.IsPrimaryAdministrator);
        Assert.Empty(membership.BranchIds);
        var role = (await reader.ServiceProvider.GetRequiredService<IRoleRepository>().GetForUpdateAsync(membership.RoleId!.Value, CancellationToken.None))!;
        Assert.Equal(EffectivePrivileges.TenantGrantableKeys, role.PermissionKeys);
        Assert.Equal(SpecialRights.All, role.SpecialRights);
        var branch = Assert.Single(await reader.ServiceProvider.GetRequiredService<IBranchLookup>().ListActiveAsync(CancellationToken.None));
        Assert.Equal(("MAIN", "EGP"), (branch.Code, branch.Currency));

        // A second primary administrator in the same tenant is refused by the partial unique index.
        var other = await RegisterUserAsync();
        var refusal = await Assert.ThrowsAsync<DomainException>(() => CreateMembershipAsync(tenant, other, null, candidate => candidate.MarkPrimaryAdministrator()));
        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);
    }

    /// <summary>The random tail of a version-7 UUID: its leading characters are the millisecond clock and repeat for about a minute.</summary>
    private static string Unique(int length) => Guid.CreateVersion7().ToString("N")[^length..].ToUpperInvariant();

    private static SessionPrincipalQuery PrincipalQuery(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<TenantContextAccessor>(), new MemoryCache(new MemoryCacheOptions()), scope.ServiceProvider.GetRequiredService<ITenantStatusLookup>());

    private async Task<Guid> RegisterUserAsync(string displayName = "Organization Tester")
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"org.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    [DatabaseFact]
    public async Task The_geography_port_reads_the_country_of_a_governorate_from_a_laboratory_scope()
    {
        var country = Country.Create(new CountryData("Branchland", "أرض الفروع", "ZB", "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + Unique(6), "محافظة", null, null);
        using (var platform = fixture.AppScope(null))
        {
            platform.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            platform.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            await platform.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        // UAT B-02: saas.governorates is platform reference data, readable by lis_app without elevation
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var geography = scope.ServiceProvider.GetRequiredService<IBranchGeography>();
        Assert.Equal(country.Id, await geography.CountryOfGovernorateAsync(governorate.Id, CancellationToken.None));
        Assert.Null(await geography.CountryOfGovernorateAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_branch_port_names_the_branches_of_the_current_tenant_only_active_or_not()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var open = await CreateBranchAsync(tenantA, "OPEN");
        var closed = await CreateBranchAsync(tenantA, "SHUT");
        var foreign = await CreateBranchAsync(tenantB, "ALIEN");
        using (var scope = fixture.AppScope(tenantA))
        {
            var branch = (await scope.ServiceProvider.GetRequiredService<IBranchRepository>().GetForUpdateAsync(closed, CancellationToken.None))!;
            branch.Deactivate(Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        // UAT A-04: the registers read the names of their branches through this port
        using var reader = fixture.AppScope(tenantA);
        var names = await reader.ServiceProvider.GetRequiredService<IBranchLookup>().GetManyAsync([open, closed, foreign, open], CancellationToken.None);

        Assert.Equal(new[] { closed, open }.Order(), names.Keys.Order());
        Assert.StartsWith("OPEN", names[open].Code, StringComparison.Ordinal);
        Assert.False(names[closed].IsActive);
    }

    private async Task<Guid> CreateBranchAsync(Guid tenantId, string code, bool exactCode = false)
    {
        using var scope = fixture.AppScope(tenantId);
        var branch = Branch.Create(exactCode ? code : code + Unique(6), new LocalizedText("Branch", "فرع"), null, null, null, null, "EGP", Now);
        scope.ServiceProvider.GetRequiredService<IBranchRepository>().Add(branch);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return branch.Id;
    }

    private async Task<Guid> CreateRoleAsync(Guid tenantId, string name, string[] keys, ResultRight[] resultRights, bool exactName = false)
    {
        using var scope = fixture.AppScope(tenantId);
        var role = Role.Create(new LocalizedText(exactName ? name : name + " " + Unique(6), "دور"), keys, Now);
        role.SetResultRights(resultRights, Now);
        scope.ServiceProvider.GetRequiredService<IRoleRepository>().Add(role);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return role.Id;
    }

    private async Task<Guid> CreateMembershipAsync(Guid tenantId, Guid userId, Guid? roleId, Action<TenantMembership>? configure = null)
    {
        using var scope = fixture.AppScope(tenantId);
        var membership = TenantMembership.Create(tenantId, userId, "E-" + Unique(8), new LocalizedText("Member Name", "اسم العضو"), "en", Now);
        if (roleId is { } id)
        {
            membership.AssignRole((await scope.ServiceProvider.GetRequiredService<IRoleRepository>().GetForUpdateAsync(id, CancellationToken.None))!, Now);
        }

        configure?.Invoke(membership);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return membership.Id;
    }

    private async Task<Guid> OpenSessionAsync(Guid tenantId, Guid userId)
    {
        using var scope = fixture.AppScope(tenantId);
        var session = RefreshSession.Open(userId, tenantId, SessionKind.Browser, SessionScope.Full, RandomNumberGenerator.GetBytes(RefreshSession.TokenHashLength), Now, TimeSpan.FromHours(8), TimeSpan.FromHours(12));
        scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>().Add(session);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return session.Id;
    }
}
