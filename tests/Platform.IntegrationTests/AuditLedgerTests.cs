using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Audit;
using Platform.Application.Billing;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Audit;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;

namespace Platform.IntegrationTests;

/// <summary>
/// 01 §4.8 / 03 §8 on a real PostgreSQL 17: a commit writes the field changes and the chained trail entries with
/// it; the chain verifies; a tampered row is detected; the runtime role cannot update or delete an audit row; the
/// unified log pages both kinds.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditLedgerTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_commit_writes_field_changes_and_chained_trail_entries_in_the_same_transaction()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var methodId = await CreateMethodAsync(tenant, actor, "AUD1");

        using (var scope = fixture.AppScope(tenant, actor, "Mona Adel", correlationId: "corr-1"))
        {
            var methods = scope.ServiceProvider.GetRequiredService<IPaymentMethodRepository>();
            var method = (await methods.GetForUpdateAsync(methodId, CancellationToken.None))!;
            method.Update(new LocalizedText("Main card", "البطاقة الرئيسية"), PaymentMethod.Card, true, 1, otherActiveCashExists: true, Now.AddMinutes(4));
            method.Deactivate(otherActiveCashExists: true, Now.AddMinutes(5));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant, actor);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();

        var changes = await context.Set<FieldChange>().Where(change => change.EntityId == methodId.ToString()).OrderBy(change => change.AtUtc).ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.Equal(FieldChangeAction.Created, changes[0].Action);
        var created = Fields(changes[0]);
        Assert.Equal((null, "AUD1"), (created["Code"].Before, created["Code"].After));
        Assert.Equal("Card AUD1", created["Name.En"].After);
        Assert.DoesNotContain("RowVersion", created.Keys);
        Assert.DoesNotContain("CreatedAtUtc", created.Keys);
        Assert.DoesNotContain("TenantId", created.Keys);
        Assert.Equal(FieldChangeAction.Modified, changes[1].Action);
        var modified = Fields(changes[1]);
        Assert.Equal(("true", "false"), (modified["IsActive"].Before, modified["IsActive"].After));
        Assert.Equal(("Card AUD1", "Main card"), (modified["Name.En"].Before, modified["Name.En"].After));
        Assert.DoesNotContain("Code", modified.Keys);
        Assert.Equal(("Mona Adel", actor, "corr-1"), (changes[1].ActorName, changes[1].ActorUserId, changes[1].CorrelationId));

        var trail = await context.Set<AuditTrailEntry>().OrderBy(entry => entry.Sequence).ToListAsync();
        // the edit is an event of its own (validation v0.5.0 O-5), so it is in the trail beside its field changes
        Assert.Equal([1L, 2L, 3L], trail.Select(entry => entry.Sequence).ToArray());
        Assert.Equal(typeof(PaymentMethodDefined).FullName, trail[0].EventType);
        Assert.Equal(typeof(PaymentMethodUpdated).FullName, trail[1].EventType);
        Assert.Equal(typeof(PaymentMethodDeactivated).FullName, trail[2].EventType);
        Assert.Null(trail[0].PrevHash);
        Assert.Equal(trail[0].EntryHash, trail[1].PrevHash);
        Assert.Equal(trail[1].EntryHash, trail[2].PrevHash);
        Assert.Equal("corr-1", trail[2].CorrelationId);
        Assert.Equal(nameof(PaymentMethodDefinition), trail[2].AggregateType);
        Assert.Equal(methodId.ToString(), trail[2].AggregateId);

        var verified = await reader.ServiceProvider.GetRequiredService<IAuditReader>().VerifyChainAsync(CancellationToken.None);
        Assert.Equal(new ChainVerificationResult(true, 3, null), verified);
    }

    [DatabaseFact]
    public async Task A_tampered_or_removed_entry_breaks_the_chain_at_its_sequence()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        await CreateMethodAsync(tenant, actor, "TMP1");
        await CreateMethodAsync(tenant, actor, "TMP2");
        await CreateMethodAsync(tenant, actor, "TMP3");

        // An attacker with the owner role and DDL rights disables the append-only trigger and edits history.
        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var ownerScope = owner.CreateScope();
        var ownerContext = ownerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ownerContext.Database.ExecuteSqlRawAsync("ALTER TABLE audit.audit_trail DISABLE TRIGGER audit_trail_append_only");
        try
        {
            await ownerContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit.audit_trail SET aggregate_id = 'forged' WHERE tenant_id = {tenant} AND sequence = 2");
        }
        finally
        {
            await ownerContext.Database.ExecuteSqlRawAsync("ALTER TABLE audit.audit_trail ENABLE TRIGGER audit_trail_append_only");
        }

        using var scope = fixture.AppScope(tenant, actor);
        var reader = scope.ServiceProvider.GetRequiredService<IAuditReader>();
        Assert.Equal(new ChainVerificationResult(false, 1, 2), await reader.VerifyChainAsync(CancellationToken.None));

        await ownerContext.Database.ExecuteSqlRawAsync("ALTER TABLE audit.audit_trail DISABLE TRIGGER audit_trail_append_only");
        try
        {
            await ownerContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit.audit_trail WHERE tenant_id = {tenant} AND sequence = 2");
        }
        finally
        {
            await ownerContext.Database.ExecuteSqlRawAsync("ALTER TABLE audit.audit_trail ENABLE TRIGGER audit_trail_append_only");
        }

        Assert.Equal(new ChainVerificationResult(false, 1, 2), await reader.VerifyChainAsync(CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_runtime_role_can_only_append_and_even_the_owner_is_stopped_by_the_trigger()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        await CreateMethodAsync(tenant, actor, "RO1");

        using var scope = fixture.AppScope(tenant, actor);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var statement in new[]
                 {
                     "UPDATE audit.audit_trail SET aggregate_id = 'x'",
                     "DELETE FROM audit.audit_trail",
                     "UPDATE audit.field_changes SET reason = 'x'",
                     "DELETE FROM audit.field_changes",
                     "UPDATE audit.security_events SET detail = 'x'",
                     "DELETE FROM audit.security_events",
                 })
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(statement));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);
        }

        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var ownerScope = owner.CreateScope();
        var ownerContext = ownerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trigger = await Assert.ThrowsAsync<PostgresException>(() => ownerContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit.audit_trail SET aggregate_id = 'x' WHERE tenant_id = {tenant}"));
        Assert.Contains("append-only", trigger.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [DatabaseFact]
    public async Task Security_events_are_appended_with_the_request_context_and_read_only_by_their_tenant()
    {
        var tenant = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        await TestTenants.EnsureLaboratoryAsync(tenant);   // a signed-in tenant always has its registry row (DbReviewTenantKeys)
        using (var anonymous = fixture.AppScope(null, correlationId: "corr-anon"))
        {
            var events = anonymous.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Audit.ISecurityEventLog>();
            await events.WriteAsync(new Platform.Application.Abstractions.Audit.SecurityEvent("SIGN_IN_FAILED", "warning", Detail: "unknown_account"), CancellationToken.None);
            await events.WriteAsync(new Platform.Application.Abstractions.Audit.SecurityEvent("SIGN_IN_SUCCEEDED", "info", tenant, actor), CancellationToken.None);   // pre-authentication scope: any tenant
        }

        using (var tenantScope = fixture.AppScope(tenant, actor, correlationId: "corr-t"))
        {
            var events = tenantScope.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Audit.ISecurityEventLog>();
            await events.WriteAsync(new Platform.Application.Abstractions.Audit.SecurityEvent("SESSION_CONTEXT_CHANGED", "info", tenant, actor, "branch"), CancellationToken.None);
            await events.WriteAsync(new Platform.Application.Abstractions.Audit.SecurityEvent("SESSION_REVOKED", "info", other, actor), CancellationToken.None);   // refused by WITH CHECK, logged, never thrown
        }

        using var reader = fixture.AppScope(tenant, actor);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.Set<SecurityEventRow>().Where(row => row.ActorUserId == actor).OrderBy(row => row.AtUtc).ToListAsync();
        Assert.Equal(["SIGN_IN_SUCCEEDED", "SESSION_CONTEXT_CHANGED"], rows.Select(row => row.Name).ToArray());
        Assert.Equal("corr-t", rows[1].CorrelationId);
        Assert.NotNull(rows[1].UserAgentHash);
        Assert.Equal(System.Net.IPAddress.Loopback, rows[1].ClientIp);
        Assert.Equal(0, await context.Set<SecurityEventRow>().CountAsync(row => row.TenantId == null));   // a null-tenant row is invisible to a tenant scope
    }

    [DatabaseFact]
    public async Task The_unified_log_pages_field_changes_and_events_newest_first_with_filters()
    {
        var tenant = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var id = await CreateMethodAsync(tenant, actor, "LOG1");
        await CreateMethodAsync(tenant, Guid.CreateVersion7(), "LOG2");

        using var scope = fixture.AppScope(tenant, actor);
        var reader = scope.ServiceProvider.GetRequiredService<IAuditReader>();

        var all = await reader.ListAsync(new AuditEntryFilter(null, null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(4, all.TotalCount);   // two creations: one field change + one event each
        Assert.Contains(all.Items, item => item.Kind == AuditEntryItem.KindFieldChange && item.Action == "Created");
        Assert.Contains(all.Items, item => item.Kind == AuditEntryItem.KindEvent && item.Action == nameof(PaymentMethodDefined) && item.Sequence is not null);

        var mine = await reader.ListAsync(new AuditEntryFilter(null, null, actor, nameof(PaymentMethodDefinition), id.ToString()), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(2, mine.TotalCount);
        Assert.All(mine.Items, item => Assert.Equal(id.ToString(), item.EntityId));

        var secondPage = await reader.ListAsync(new AuditEntryFilter(null, null, null, null, null), PageRequest.Normalized(2, 3), CancellationToken.None);
        Assert.Single(secondPage.Items);
        Assert.Equal(4, secondPage.TotalCount);

        var none = await reader.ListAsync(new AuditEntryFilter(Now.AddYears(5), null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(0, none.TotalCount);
    }

    private static Dictionary<string, FieldChangeItem> Fields(FieldChange change) =>
        System.Text.Json.JsonSerializer.Deserialize<List<FieldChangeItem>>(change.Changes, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!
            .ToDictionary(item => item.Field, StringComparer.Ordinal);

    /// <summary>One aggregate with a create / edit / deactivate life (a payment method definition; the treasury served before LIS-CR-004).</summary>
    private async Task<Guid> CreateMethodAsync(Guid tenantId, Guid actor, string code)
    {
        using var scope = fixture.AppScope(tenantId, actor, "Mona Adel");
        var method = PaymentMethodDefinition.Define(code, new LocalizedText("Card " + code, "بطاقة " + code), PaymentMethod.Card, true, 1, Now);
        scope.ServiceProvider.GetRequiredService<IPaymentMethodRepository>().Add(method);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return method.Id;
    }
}
