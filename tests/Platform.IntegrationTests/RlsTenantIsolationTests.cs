using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Security;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// RlsTenantIsolationTests (01 §8, D-06) on a real PostgreSQL 17 with the NOBYPASSRLS runtime role: with tenant A's
/// setting, tenant B's rows are invisible and un-writable on <c>app.tenant_memberships</c>; an unresolved tenant
/// sees nothing; the elevated scope sees everything; the application layer refuses before the engine does.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RlsTenantIsolationTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Rows_of_another_tenant_are_invisible_even_without_the_query_filter()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync();
        await CreateMembershipAsync(tenantA, user, "A-1");
        await CreateMembershipAsync(tenantB, user, "B-1");

        using var scopeA = fixture.AppScope(tenantA);
        var contextA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await contextA.TenantMemberships.CountAsync());
        Assert.Equal(1, await contextA.TenantMemberships.IgnoreQueryFilters().CountAsync()); // the engine, not the filter, hides tenant B
        Assert.Equal("A-1", (await contextA.TenantMemberships.SingleAsync()).EmployeeCode);

        using var unresolved = fixture.AppScope(null);
        var contextNil = unresolved.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await contextNil.TenantMemberships.IgnoreQueryFilters().CountAsync());

        using var elevatedScope = fixture.AppScope(null);
        var accessor = elevatedScope.ServiceProvider.GetRequiredService<TenantContextAccessor>();
        using (accessor.Elevate())
        {
            var contextElevated = elevatedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await contextElevated.TenantMemberships.Where(m => m.PlatformUserId == user).CountAsync());
        }
    }

    [DatabaseFact]
    public async Task A_write_for_another_tenant_is_refused_by_the_application_layer_first()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync();

        using var scope = fixture.AppScope(tenantA);
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        memberships.Add(TenantMembership.Create(tenantB, user, "B-9", new LocalizedText("Cross", "عبر"), "en", Now));

        var refusal = await Assert.ThrowsAsync<DomainException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
        Assert.Equal(ProblemCodes.TenantForbidden, refusal.Code);
    }

    [DatabaseFact]
    public async Task The_engine_refuses_a_cross_tenant_insert_with_its_WITH_CHECK_clause()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync();

        using var scope = fixture.AppScope(tenantA);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Raw SQL bypasses the interceptor on purpose: only FORCE ROW LEVEL SECURITY stands between the row and tenant B.
        var engineRefusal = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.tenant_memberships (tenant_id, id, platform_user_id, employee_code, full_name_en, full_name_ar, is_active, branch_ids, department_ids, preferred_language, created_at_utc)
            VALUES ({tenantB}, {Guid.CreateVersion7()}, {user}, 'RAW-1', 'Raw', 'خام', true, ARRAY[]::uuid[], ARRAY[]::uuid[], 'en', {Now})
            """));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, engineRefusal.SqlState);
        Assert.Contains("row-level security", engineRefusal.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [DatabaseFact]
    public async Task The_runtime_role_cannot_bypass_row_level_security_and_the_tables_force_it()
    {
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await context.Database.SqlQueryRaw<bool>("SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync();
        Assert.False(role);

        var forced = await context.Database.SqlQueryRaw<bool>("SELECT (relrowsecurity AND relforcerowsecurity) AS \"Value\" FROM pg_class WHERE oid = 'app.tenant_memberships'::regclass").SingleAsync();
        Assert.True(forced);

        var policies = await context.Database.SqlQueryRaw<string>("SELECT policyname AS \"Value\" FROM pg_policies WHERE schemaname = 'app' AND tablename = 'tenant_memberships'").ToListAsync();
        Assert.Equal(["tenant_isolation"], policies);
    }

    private async Task<Guid> RegisterUserAsync()
    {
        using var scope = fixture.AppScope(null);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = PlatformUser.Register($"user.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Integration User", "hash", Now);
        context.PlatformUsers.Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task CreateMembershipAsync(Guid tenantId, Guid userId, string employeeCode)
    {
        using var scope = fixture.AppScope(tenantId);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>()
            .Add(TenantMembership.Create(tenantId, userId, employeeCode, new LocalizedText("Member", "عضو"), "en", Now));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
