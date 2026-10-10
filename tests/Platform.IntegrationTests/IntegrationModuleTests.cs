using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Identity;
using Platform.Application.Identity.MachineCredentials;
using Platform.Application.Integration;
using Platform.Contracts.Common;
using Platform.Domain.Identity;
using Platform.Domain.Integration;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-INT-001: tenant isolation of every table of the module, the unique indexes behind
/// INV-INT-001/003/006/007, the machine credential's service identity in one commit, the operational stores and the
/// raw-SQL read ports over other modules' tables (fail closed).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IntegrationModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    // The persistence fixture has no security edge (no master key); the keyed hash is covered by the functional tests.
    private static readonly TestMachineSecrets Secrets = new();

    [DatabaseFact]
    public async Task Analysers_mappings_codes_agents_kits_and_logs_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (analyserId, mappingId, sampleCodeId, kitId) = await SeedAnalyserAsync(tenantA, "ISO");
        var agentId = await SeedAgentAsync(tenantA, "Isolation agent");

        using (var scope = fixture.AppScope(tenantA))
        {
            scope.ServiceProvider.GetRequiredService<IAgentLogStore>().Append([new AgentLogRecord(agentId, Now, AgentLogLevel.Info, "StartSend", "S1", "ISO")], Now);
            await scope.ServiceProvider.GetRequiredService<IAgentTelemetryStore>().RecordHeartbeatAsync(new AgentHeartbeat(agentId, "1.0.0", false, null, Now, [new AnalyserLinkReport(analyserId, AnalyserLinkStatus.Connected, null)]), CancellationToken.None);
            scope.ServiceProvider.GetRequiredService<IResultIntakeLog>().Append(new ResultIntakeRecord(Guid.CreateVersion7(), agentId, analyserId, "ISO", "S1", "WBC", null, null, "4.5", null, [], ResultIntakeStatus.UnknownSample, "x", ResultSources.Analyser, null, Now, Now));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Analyser>().IgnoreQueryFilters().CountAsync(row => row.Id == analyserId));
        Assert.Equal(0, await context.Set<AnalyserTestMapping>().IgnoreQueryFilters().CountAsync(row => row.Id == mappingId));
        Assert.Equal(0, await context.Set<AnalyserSampleCode>().IgnoreQueryFilters().CountAsync(row => row.Id == sampleCodeId));
        Assert.Equal(0, await context.Set<PcrKit>().IgnoreQueryFilters().CountAsync(row => row.Id == kitId));
        Assert.Equal(0, await context.Set<SiteAgent>().IgnoreQueryFilters().CountAsync(row => row.Id == agentId));
        Assert.Empty(await scopeB.ServiceProvider.GetRequiredService<IAgentTelemetryStore>().AgentStatesAsync([agentId], CancellationToken.None));
        Assert.Empty(await scopeB.ServiceProvider.GetRequiredService<IAgentTelemetryStore>().LinkStatesAsync([analyserId], CancellationToken.None));
        Assert.Equal(0, (await scopeB.ServiceProvider.GetRequiredService<IAgentLogStore>().ListAsync(new AgentLogFilter(agentId, null, null, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).TotalCount);
        Assert.Equal(0, (await scopeB.ServiceProvider.GetRequiredService<IResultIntakeLog>().ListAsync(new ResultIntakeFilter(null, null, null, analyserId, null, null), PageRequest.Normalized(1, 10), CancellationToken.None)).TotalCount);

        using var scopeA = fixture.AppScope(tenantA);
        Assert.Equal(1, (await scopeA.ServiceProvider.GetRequiredService<IResultIntakeLog>().ListAsync(new ResultIntakeFilter(null, null, ResultIntakeStatus.UnknownSample, analyserId, agentId, "S1"), PageRequest.Normalized(1, 10), CancellationToken.None)).TotalCount);
        var link = Assert.Single(await scopeA.ServiceProvider.GetRequiredService<IAgentTelemetryStore>().LinkStatesAsync([analyserId], CancellationToken.None));
        Assert.Equal(AnalyserLinkStatus.Connected, link.Value.Status);
    }

    [DatabaseFact]
    public async Task Unique_indexes_back_the_handler_checks()
    {
        var tenant = Guid.CreateVersion7();
        var (analyserId, _, _, _) = await SeedAnalyserAsync(tenant, "UNQ");

        var analyserCode = await Assert.ThrowsAsync<DomainException>(() => InTenantAsync(tenant, scope =>
        {
            var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Analyser>().Single(row => row.Id == analyserId);
            scope.ServiceProvider.GetRequiredService<IAnalyserRepository>().Add(Analyser.Create(existing.Code, Definition(), Now));
            return Task.CompletedTask;
        }));
        Assert.Equal(ProblemCodes.UniqueViolation, analyserCode.Code);

        var mappingCode = await Assert.ThrowsAsync<DomainException>(() => InTenantAsync(tenant, async scope =>
        {
            var analyser = (await scope.ServiceProvider.GetRequiredService<IAnalyserRepository>().GetForUpdateAsync(analyserId, CancellationToken.None))!;
            scope.ServiceProvider.GetRequiredService<IAnalyserTestMappingRepository>().Add(AnalyserTestMapping.Map(analyser, Mapping(), Now));
        }));
        Assert.Equal(ProblemCodes.UniqueViolation, mappingCode.Code);

        await SeedAgentAsync(tenant, "Twin");
        var agentName = await Assert.ThrowsAsync<DomainException>(() => SeedAgentAsync(tenant, "Twin"));
        Assert.Equal(ProblemCodes.UniqueViolation, agentName.Code);
    }

    [DatabaseFact]
    public async Task A_machine_credential_commits_its_service_user_role_and_membership_together()
    {
        var tenant = Guid.CreateVersion7();
        IssuedMachineCredential issued;
        using (var scope = fixture.AppScope(tenant))
        {
            issued = MachineCredentialIssuance.Issue(
                new MachineCredentialSpec(tenant, "Reporting robot", ["integration.view"], Guid.CreateVersion7(), null),
                scope.ServiceProvider.GetRequiredService<IMachineCredentialRepository>(),
                scope.ServiceProvider.GetRequiredService<IMachineServiceIdentityStore>(),
                Secrets,
                Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var credentials = reader.ServiceProvider.GetRequiredService<IMachineCredentialRepository>();
        var stored = (await credentials.FindByClientIdAsync(issued.Credential.ClientId, CancellationToken.None))!;
        Assert.True(Secrets.Verify(stored.SecretHash, issued.Secret));
        Assert.False(Secrets.Verify(stored.SecretHash, issued.Secret + "x"));

        var user = (await reader.ServiceProvider.GetRequiredService<IPlatformUserRepository>().FindByUsernameAsync("svc." + stored.ClientId, CancellationToken.None))!;
        Assert.Equal(stored.ServiceUserId, user.Id);
        var membership = await context.Set<TenantMembership>().SingleAsync(row => row.PlatformUserId == user.Id);
        var role = await context.Set<Role>().SingleAsync(row => row.Id == membership.RoleId);
        Assert.Equal(["integration.view"], role.PermissionKeys);
        Assert.Equal(tenant, role.TenantId);
        Assert.Equal([stored.BranchId!.Value], membership.BranchIds);

        var page = await credentials.ListAsync(tenant, includeRevoked: false, Now, PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(("Reporting robot", "Live"), (Assert.Single(page.Items).Name, page.Items[0].Status));
        Assert.Empty((await credentials.ListAsync(Guid.CreateVersion7(), includeRevoked: true, Now, PageRequest.Normalized(1, 10), CancellationToken.None)).Items);
    }

    [DatabaseFact]
    public async Task Read_ports_fail_closed_on_unknown_ids_and_return_no_orders_without_data()
    {
        var tenant = Guid.CreateVersion7();
        using var scope = fixture.AppScope(tenant);
        var lookup = scope.ServiceProvider.GetRequiredService<IIntegrationReferenceLookup>();
        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            Assert.Empty(await lookup.FindAsync(kind, [Guid.CreateVersion7()], CancellationToken.None));
        }

        Assert.False(await lookup.AnalyserModelExistsAsync(Guid.CreateVersion7(), CancellationToken.None));
        _ = await lookup.ListAnalyserModelsAsync("zz-none", CancellationToken.None);

        var samples = scope.ServiceProvider.GetRequiredService<IIntegrationSampleLookup>();
        Assert.Empty(await samples.FindByNumbersAsync(["SMP-NONE-1"], CancellationToken.None));
        Assert.Empty(await samples.ListOrdersAsync(Guid.CreateVersion7(), [Guid.CreateVersion7()], Now.AddDays(-1), 50, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Projections_join_the_agent_and_count_active_mappings()
    {
        var tenant = Guid.CreateVersion7();
        var agentId = await SeedAgentAsync(tenant, "Projection agent");
        var (analyserId, _, _, _) = await SeedAnalyserAsync(tenant, "PRJ", agentId);

        using var scope = fixture.AppScope(tenant);
        var analysers = scope.ServiceProvider.GetRequiredService<IAnalyserRepository>();
        var page = await analysers.ListAsync(new AnalyserFilter("prj", true, null, agentId), PageRequest.Normalized(1, 10), Now, CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.Equal((analyserId, "Projection agent", 1, "Serial"), (item.Id, item.SiteAgentName, item.MappingCount, item.ConnectionKind));
        Assert.Equal(1, (await analysers.CountByAgentAsync([agentId], CancellationToken.None))[agentId]);

        var detail = (await analysers.GetDetailAsync(analyserId, CancellationToken.None))!;
        Assert.Equal(("9600,n,8,1", 2), (detail.Connection.SerialSettings, detail.Flags.Count));
        var mappings = await scope.ServiceProvider.GetRequiredService<IAnalyserTestMappingRepository>().ListAsync(analyserId, null, null, PageRequest.Normalized(1, 10), CancellationToken.None);
        Assert.Equal(["<10"], Assert.Single(mappings.Items).Conversions.Select(conversion => conversion.FromValue).ToArray());
    }

    private static AnalyserDefinition Definition(Guid? agentId = null) => new(
        new LocalizedText("Sysmex", "سيسمكس"),
        Guid.CreateVersion7(),
        null,
        null,
        AnalyserMode.Unidirectional,
        AnalyserConnection.Serial("9600,n,8,1", "COM3"),
        new AnalyserSwitches(false, false, false, false, false, false),
        1,
        [new AnalyserFlagSpec("H", FlagAction.Record, null), new AnalyserFlagSpec("CLOT", FlagAction.Stop, null)],
        agentId);

    private static MappingDefinition Mapping() =>
        new(Guid.CreateVersion7(), "WBC", null, 1, 1m, 0m, null, false, null, true, [new ConversionSpec("<10", "Low")], null, null);

    private async Task InTenantAsync(Guid tenant, Func<IServiceScope, Task> act)
    {
        using var scope = fixture.AppScope(tenant);
        await act(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(Guid Analyser, Guid Mapping, Guid SampleCode, Guid Kit)> SeedAnalyserAsync(Guid tenant, string prefix, Guid? agentId = null)
    {
        using var scope = fixture.AppScope(tenant);
        var analyser = Analyser.Create(prefix + Guid.CreateVersion7().ToString("N")[..6], Definition(agentId), Now);
        var mapping = AnalyserTestMapping.Map(analyser, Mapping(), Now);
        var code = AnalyserSampleCode.Create(analyser, Guid.CreateVersion7(), "SER");
        var kit = PcrKit.Define(analyser, new PcrKitDefinition(
            "Kit " + Guid.CreateVersion7().ToString("N")[..8],
            "COVID",
            null,
            PcrRunFileLayout.Create(",", "Sample", "Target", null, null, null, null, null),
            "Cq",
            [new PcrGeneSpec("N", false, 38m, 40m), new PcrGeneSpec("IC", true, 40m, 40m)],
            PcrVerdictRules.Default,
            @"D:\in",
            @"D:\backup",
            @"D:\failed"), Now);
        scope.ServiceProvider.GetRequiredService<IAnalyserRepository>().Add(analyser);
        scope.ServiceProvider.GetRequiredService<IAnalyserTestMappingRepository>().Add(mapping);
        scope.ServiceProvider.GetRequiredService<IAnalyserSampleCodeRepository>().Add(code);
        scope.ServiceProvider.GetRequiredService<IPcrKitRepository>().Add(kit);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (analyser.Id, mapping.Id, code.Id, kit.Id);
    }

    private async Task<Guid> SeedAgentAsync(Guid tenant, string name)
    {
        using var scope = fixture.AppScope(tenant);
        var issued = MachineCredentialIssuance.Issue(
            new MachineCredentialSpec(tenant, "Site agent: " + name + " " + Guid.CreateVersion7().ToString("N")[..6], AgentScopes.All.ToList(), Guid.CreateVersion7(), null),
            scope.ServiceProvider.GetRequiredService<IMachineCredentialRepository>(),
            scope.ServiceProvider.GetRequiredService<IMachineServiceIdentityStore>(),
            Secrets,
            Now);
        var agent = SiteAgent.Register(name, issued.Credential.BranchId!.Value, issued.Credential.Id, issued.Credential.ServiceUserId, null, Now);
        scope.ServiceProvider.GetRequiredService<ISiteAgentRepository>().Add(agent);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return agent.Id;
    }

    /// <summary>Random ids and secrets with an unkeyed SHA-256 stand-in for the keyed hash (test double of <see cref="IMachineSecrets"/>).</summary>
    private sealed class TestMachineSecrets : IMachineSecrets
    {
        public string NewClientId() => "lis-" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

        public MachineSecret NewSecret()
        {
            var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            return new MachineSecret(secret, Hash(secret));
        }

        public bool Verify(string storedHash, string presentedSecret) => storedHash == Hash(presentedSecret);

        public string UnusablePasswordHash() => "!test-machine!" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

        private static string Hash(string secret) => "v1." + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    }
}
