using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// One commit per command (01 §2.3): the state change, the tenant stamp, the audit stamp and the outbox rows land
/// together; the xmin token turns a lost update into <c>CONCURRENCY-409</c>; a unique index into <c>UNIQUE-409</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UnitOfWorkTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_commit_writes_the_rows_the_stamps_and_the_outbox_events_together()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        using var scope = fixture.AppScope(tenant, actor, "Mona Adel");
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = PlatformUser.Register($"uow.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Unit Of Work", "hash", Now);
        var membership = TenantMembership.Create(tenant, user.Id, "UOW-1", new LocalizedText("Unit", "وحدة"), "ar", Now);
        users.Add(user);
        memberships.Add(membership);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(user.DomainEvents);
        Assert.Empty(membership.DomainEvents);
        Assert.Equal(actor, membership.CreatedByUserId);
        Assert.Equal("Mona Adel", membership.CreatedByDisplayName);
        Assert.NotEqual(default, membership.CreatedAtUtc);
        Assert.NotEqual(0u, membership.RowVersion);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registeredType = typeof(PlatformUserRegistered).FullName;
        var outbox = (await context.OutboxEvents.Where(e => e.TenantId == tenant || e.Type == registeredType).OrderBy(e => e.OccurredAtUtc).ToListAsync())
            .Where(e => e.TenantId == tenant || e.Payload.Contains(user.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(outbox, e => e.Type == typeof(PlatformUserRegistered).FullName && e.TenantId == null);
        Assert.Contains(outbox, e => e.Type == typeof(TenantMembershipCreated).FullName && e.TenantId == tenant);
        Assert.All(outbox, e => Assert.Null(e.ProcessedAtUtc));
        Assert.Contains("\"membershipId\"", outbox.Single(e => e.Type == typeof(TenantMembershipCreated).FullName).Payload, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_lost_update_is_a_concurrency_conflict_through_xmin()
    {
        var tenant = Guid.CreateVersion7();
        var userId = await RegisterUserAsync();
        var membershipId = await CreateMembershipAsync(tenant, userId, "XMIN-1");

        using var first = fixture.AppScope(tenant);
        using var second = fixture.AppScope(tenant);
        var membershipA = (await first.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().GetForUpdateAsync(membershipId, CancellationToken.None))!;
        var membershipB = (await second.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().GetForUpdateAsync(membershipId, CancellationToken.None))!;

        membershipA.SetPreferredLanguage("ar");
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        membershipB.SetPreferredLanguage("ar"); // the same change on a stale xmin: a real modification, refused by the token
        var refusal = await Assert.ThrowsAsync<DomainException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        Assert.Equal(ProblemCodes.ConcurrencyConflict, refusal.Code);
        Assert.Equal(DomainFault.Conflict, refusal.Fault);
    }

    [DatabaseFact]
    public async Task A_duplicate_employee_code_in_the_same_tenant_is_a_unique_violation_and_allowed_in_another_tenant()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var userId = await RegisterUserAsync();
        var otherUser = await RegisterUserAsync();
        await CreateMembershipAsync(tenantA, userId, "DUP-1");

        await CreateMembershipAsync(tenantB, userId, "DUP-1"); // business uniqueness includes the tenant

        using var scope = fixture.AppScope(tenantA);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>()
            .Add(TenantMembership.Create(tenantA, otherUser, "DUP-1", new LocalizedText("Dup", "مكرر"), "en", Now));
        var refusal = await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);
    }

    [DatabaseFact]
    public async Task A_session_round_trips_its_hash_address_and_enums()
    {
        var userId = await RegisterUserAsync();
        var hash = Enumerable.Range(0, RefreshSession.TokenHashLength).Select(i => (byte)i).ToArray();

        using (var scope = fixture.AppScope(null))
        {
            scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>()
                .Add(RefreshSession.Open(userId, null, SessionKind.Browser, SessionScope.MfaPending, hash, Now, TimeSpan.FromHours(8), TimeSpan.FromHours(12), System.Net.IPAddress.Parse("10.1.2.3"), "agent"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(null);
        var session = await reader.ServiceProvider.GetRequiredService<IRefreshSessionRepository>().FindByTokenHashAsync(hash, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(SessionScope.MfaPending, session.Scope);
        Assert.Equal(System.Net.IPAddress.Parse("10.1.2.3"), session.IpAddress);
        Assert.Single(await reader.ServiceProvider.GetRequiredService<IRefreshSessionRepository>().ListLiveForUserAsync(userId, CancellationToken.None));
    }

    private async Task<Guid> RegisterUserAsync()
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"uow.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", "Unit Of Work", "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task<Guid> CreateMembershipAsync(Guid tenantId, Guid userId, string employeeCode)
    {
        using var scope = fixture.AppScope(tenantId);
        var membership = TenantMembership.Create(tenantId, userId, employeeCode, new LocalizedText("Member", "عضو"), "en", Now);
        scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>().Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return membership.Id;
    }
}
