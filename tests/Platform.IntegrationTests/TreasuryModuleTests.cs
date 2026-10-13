using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Treasury;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Treasury;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>The engine side of intake LIS-INT-TRS-001: the live-request unique index, tenant isolation of the module's tables, and the atomic reference counter (pitfall P-12).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TreasuryModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_partial_unique_index_allows_one_live_request_per_user_treasury_and_device()
    {
        var tenant = Guid.CreateVersion7();
        var user = await RegisterUserAsync();
        var treasuryId = await CreateTreasuryAsync(tenant, "LIVE");

        var first = await RequestAsync(tenant, treasuryId, user, "device-1");
        var refusal = await Assert.ThrowsAsync<DomainException>(() => RequestAsync(tenant, treasuryId, user, "device-1"));
        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);

        using (var scope = fixture.AppScope(tenant, user))
        {
            var requests = scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>();
            var request = (await requests.GetForUpdateAsync(first, CancellationToken.None))!;
            request.Cancel(user, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var again = await RequestAsync(tenant, treasuryId, user, "device-1");   // the cancelled one no longer counts
        Assert.NotEqual(first, again);
    }

    [DatabaseFact]
    public async Task Treasuries_and_requests_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var user = await RegisterUserAsync();
        var treasuryA = await CreateTreasuryAsync(tenantA, "ISO");
        await RequestAsync(tenantA, treasuryA, user, "device-iso");

        using var scopeB = fixture.AppScope(tenantB, user);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Treasuries.IgnoreQueryFilters().CountAsync(t => t.Id == treasuryA));
        Assert.Equal(0, await context.TreasuryAccessRequests.IgnoreQueryFilters().CountAsync(r => r.TreasuryId == treasuryA));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<ITreasuryRepository>().GetForUpdateAsync(treasuryA, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Fifty_parallel_callers_receive_fifty_distinct_reference_numbers()
    {
        var tenant = Guid.CreateVersion7();
        var numbers = new System.Collections.Concurrent.ConcurrentBag<string>();

        // At most 10 connections at a time: the private PostgreSQL serves every slot, the installed service and the
        // validation API under max_connections = 100, and 50 simultaneous opens could be refused (53300) while other
        // suites run. Ten concurrent callers still race on the first insert and on every increment.
        using var gate = new SemaphoreSlim(10);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                using var scope = fixture.AppScope(tenant);
                numbers.Add(await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync("parallel-test", "PAR", Now, CancellationToken.None));
            }
            finally
            {
                gate.Release();
            }
        }));

        Assert.Equal(50, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(numbers, number => Assert.Matches("^PAR-2026-\\d{5}$", number));
        Assert.Contains("PAR-2026-00050", numbers);
    }

    [DatabaseFact]
    public async Task Projections_join_names_and_the_detail_carries_the_approval()
    {
        var tenant = Guid.CreateVersion7();
        var requester = await RegisterUserAsync("Requester Person");
        var approver = await RegisterUserAsync("Approver Person");
        var treasuryId = await CreateTreasuryAsync(tenant, "PRJ");
        var requestId = await RequestAsync(tenant, treasuryId, requester, "device-prj");

        using (var scope = fixture.AppScope(tenant, approver))
        {
            var request = (await scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>().GetForUpdateAsync(requestId, CancellationToken.None))!;
            request.Approve(approver, Now, "ok");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant, requester);
        var repository = reader.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>();
        var page = await repository.ListAsync(new TreasuryAccessRequestFilter(TreasuryAccessStatus.Approved, treasuryId, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.StartsWith("PRJ", item.TreasuryCode, StringComparison.Ordinal);
        Assert.Equal(("Requester Person", "Approver Person", "Approved"), (item.RequestedByName, item.DecidedByName, item.Status));
        Assert.Equal(("ok", (string?)null), (item.ApprovalComment, item.DecisionReason));   // validation v0.5.0 D-05: the row carries the comment

        var detail = (await repository.GetDetailAsync(requestId, CancellationToken.None))!;
        Assert.Equal("Approver Person", detail.Approval!.ActorName);
        Assert.Equal("ok", detail.Approval.Comment);
        Assert.StartsWith("TRQ-2026-", detail.Number, StringComparison.Ordinal);
    }

    private async Task<Guid> RegisterUserAsync(string displayName = "Treasury Tester")
    {
        using var scope = fixture.AppScope(null);
        var user = PlatformUser.Register($"trs.{Guid.CreateVersion7():N}"[..30], $"{Guid.CreateVersion7():N}@example.test", displayName, "hash", Now);
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private async Task<Guid> CreateTreasuryAsync(Guid tenantId, string code)
    {
        using var scope = fixture.AppScope(tenantId);
        var treasury = Platform.Domain.Treasury.Treasury.Create(code + Guid.CreateVersion7().ToString("N")[..6], new LocalizedText("Drawer", "خزينة"), Guid.CreateVersion7(), Now);
        scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().Add(treasury);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return treasury.Id;
    }

    private async Task<Guid> RequestAsync(Guid tenantId, Guid treasuryId, Guid userId, string deviceId)
    {
        using var scope = fixture.AppScope(tenantId, userId);
        var treasury = (await scope.ServiceProvider.GetRequiredService<ITreasuryRepository>().GetForUpdateAsync(treasuryId, CancellationToken.None))!;
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(TreasuryAccessRequest.NumberKind, TreasuryAccessRequest.NumberPrefix, Now, CancellationToken.None);
        var request = TreasuryAccessRequest.Request(treasury, number, userId, deviceId, "Test device", Now);
        scope.ServiceProvider.GetRequiredService<ITreasuryAccessRequestRepository>().Add(request);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return request.Id;
    }
}
