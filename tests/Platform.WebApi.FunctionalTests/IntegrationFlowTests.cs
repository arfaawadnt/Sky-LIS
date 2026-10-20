using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Application.Integration;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// WF-07 over the real API (intake LIS-INT-INT-001 §16): an engineer defines a site agent, an analyser, its mappings and
/// sample codes and a PCR kit; the agent authenticates with its credential, reports a heartbeat, reads its configuration
/// and relays results and a run file; every value is logged with its outcome. The catalogue and samples of other modules
/// are replaced by in-memory read ports (their tables belong to other agents' migrations); everything else is real.
/// </summary>
public sealed class IntegrationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Analysers = "/api/v1/integration/analysers";
    private const string Agents = "/api/v1/integration/site-agents";
    private const string Kits = "/api/v1/integration/pcr-kits";
    private const string Serial = "785BR29753";

    private static readonly string[] EngineerPermissions = ["integration.view", "integration.edit", "integration.manage", "integration.export", "integration.create"];

    [DatabaseFact]
    public async Task An_agent_relays_results_that_land_only_through_mapped_Auto_Send_codes()
    {
        var host = WithFakes(out var samples);
        var tenant = Guid.CreateVersion7();
        var engineer = await SignedInAsync(host, tenant, EngineerPermissions);

        var agent = await CreateAgentAsync(engineer, "Main lab agent");
        var analyserId = await CreateAnalyserAsync(engineer, "XN550", agent.AgentId);
        await MapAsync(engineer, analyserId, "WBC", autoSend: true, slope: 2m);
        await MapAsync(engineer, analyserId, "HGB", autoSend: false, slope: 1m);
        var sampleType = Guid.CreateVersion7();
        Assert.Equal(HttpStatusCode.Created, (await engineer.PostAsJsonAsync($"{Analysers}/{analyserId}/sample-codes", new SaveSampleCodeRequest(sampleType, "SER"))).StatusCode);
        var twinCode = await engineer.PostAsJsonAsync($"{Analysers}/{analyserId}/sample-codes", new SaveSampleCodeRequest(sampleType, "SER2"));
        Assert.Equal("INT-103", await AuthClient.CodeOf(twinCode));

        samples["SMP-1"] = new SampleRef(Guid.CreateVersion7(), "SMP-1", "Received");
        samples["SMP-9"] = new SampleRef(Guid.CreateVersion7(), "SMP-9", "Rejected");

        var machine = await MachineCredentialFlowTests.MachineClientAsync(host, agent.ClientId, agent.ClientSecret);
        var heartbeat = (await (await machine.PostAsJsonAsync("/api/v1/agents/heartbeat", new AgentHeartbeatRequest("2.1.0", "Online", null, [new AgentAnalyserStatus("xn550", "Connected", null), new AgentAnalyserStatus("GHOST", "Connected", null)]))).Content.ReadFromJsonAsync<AgentHeartbeatResponse>())!;
        Assert.Equal((agent.AgentId, 1, "GHOST"), (heartbeat.AgentId, heartbeat.ConfigVersion, Assert.Single(heartbeat.UnknownAnalyserCodes)));

        var config = (await machine.GetFromJsonAsync<AgentConfigView>("/api/v1/agents/config"))!;
        var analyserConfig = Assert.Single(config.Analysers);
        Assert.Equal(("XN550", 2, "SER"), (analyserConfig.Code, analyserConfig.Mappings.Count, Assert.Single(analyserConfig.SampleCodes).Code));

        var batch = new AgentResultBatchRequest("XN550",
        [
            new AgentResultItem("SMP-1", "WBC", "4.5", ["CLOT", "H"], "10^3/uL", null),
            new AgentResultItem("SMP-1", "HGB", "13.2", null, null, null),
            new AgentResultItem("SMP-1", "PLT", "250", null, null, null),
            new AgentResultItem("SMP-404", "WBC", "1", null, null, null),
            new AgentResultItem("SMP-9", "WBC", "1", null, null, null),
        ]);
        var outcomes = (await (await machine.PostAsJsonAsync("/api/v1/agents/results", batch)).Content.ReadFromJsonAsync<AgentResultBatchResponse>())!;
        Assert.Equal(["Accepted", "AutoSendOff", "Unmapped", "UnknownSample", "Refused"], outcomes.Items.Select(item => item.Status).ToArray());
        Assert.Equal("9", outcomes.Items[0].ConvertedValue);

        var foreignAnalyser = (await (await machine.PostAsJsonAsync("/api/v1/agents/results", batch with { AnalyserCode = "NOPE" })).Content.ReadFromJsonAsync<AgentResultBatchResponse>())!;
        Assert.All(foreignAnalyser.Items, item => Assert.Equal("Refused", item.Status));

        var intake = (await engineer.GetFromJsonAsync<Paged<IntakeLogItem>>($"/api/v1/integration/intake?analyserId={analyserId}&pageSize=50"))!;
        Assert.Equal(5, intake.TotalCount);
        Assert.Equal("Main lab agent", intake.Items[0].AgentName);
        Assert.Single((await engineer.GetFromJsonAsync<Paged<IntakeLogItem>>("/api/v1/integration/intake?status=Accepted"))!.Items);

        var connection = (await engineer.GetFromJsonAsync<AnalyserConnectionState>($"{Analysers}/{analyserId}/connection"))!;
        Assert.Equal(("Online", "Connected"), (connection.AgentStatus, connection.LinkStatus));
        var agents = (await engineer.GetFromJsonAsync<Paged<SiteAgentListItem>>(Agents))!;
        Assert.Equal(("Online", "2.1.0", 1), (agents.Items[0].Status, agents.Items[0].Version, agents.Items[0].AnalyserCount));

        var logs = await machine.PostAsJsonAsync("/api/v1/agents/logs", new AgentLogBatchRequest([new AgentLogEntryDto(factory.Clock.UtcNow, "Info", "StartSend: SaveResult [SMP-1, WBC]", "SMP-1", "XN550"), new AgentLogEntryDto(factory.Clock.UtcNow, "Error", "Exception: port closed", null, "XN550")]));
        Assert.Equal(2, (await logs.Content.ReadFromJsonAsync<AgentLogBatchResponse>())!.Accepted);
        var errors = (await engineer.GetFromJsonAsync<Paged<AgentLogItem>>($"{Agents}/{agent.AgentId}/logs?level=Warning"))!;
        Assert.Equal("Exception: port closed", Assert.Single(errors.Items).Message);

        var sheet = (await engineer.GetFromJsonAsync<AnalyserCodeSheetDocument>($"{Analysers}/{analyserId}/documents/code-sheet"))!;
        Assert.Equal(["HGB", "WBC"], sheet.Lines.Select(line => line.AnalyserCode).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("SER", Assert.Single(sheet.SampleCodes).Code);

        Assert.Equal(HttpStatusCode.NoContent, (await engineer.PostAsync($"{Agents}/{agent.AgentId}/push-config", null)).StatusCode);
        var next = (await (await machine.PostAsJsonAsync("/api/v1/agents/heartbeat", new AgentHeartbeatRequest("2.1.0", null, null, null))).Content.ReadFromJsonAsync<AgentHeartbeatResponse>())!;
        Assert.Equal(2, next.ConfigVersion);
    }

    [DatabaseFact]
    public async Task A_PCR_run_is_interpreted_landed_and_a_file_from_another_instrument_fails()
    {
        var host = WithFakes(out var samples);
        var tenant = Guid.CreateVersion7();
        var engineer = await SignedInAsync(host, tenant, EngineerPermissions);
        var agent = await CreateAgentAsync(engineer, "PCR room agent");
        var analyserId = await CreateAnalyserAsync(engineer, "BIORAD", agent.AgentId, shared: true);
        await MapAsync(engineer, analyserId, "COVID", autoSend: true, slope: 1m);
        var kit = await engineer.PostAsJsonAsync(Kits, Kit(analyserId));
        Assert.Equal(HttpStatusCode.Created, kit.StatusCode);
        var kitId = (await kit.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        Assert.Equal("INT-105", await AuthClient.CodeOf(await engineer.PostAsJsonAsync(Kits, Kit(analyserId))));

        var file = RunFile(Serial, ("B03", "SMP-P1", "35.17", "34.63", "33.33", "30.1"), ("A02", "SMP-P2", "0.00", "", "N/A", "31.2"));
        var simulated = (await (await engineer.PostAsJsonAsync($"{Kits}/{kitId}/simulate", new SimulatePcrRunRequest(null, file))).Content.ReadFromJsonAsync<PcrRunView>())!;
        Assert.Equal(["Detected", "NotDetected"], simulated.Samples.Select(sample => sample.Verdict!).ToArray());

        samples["SMP-P1"] = new SampleRef(Guid.CreateVersion7(), "SMP-P1", "Received");
        var machine = await MachineCredentialFlowTests.MachineClientAsync(host, agent.ClientId, agent.ClientSecret);
        var run = (await (await machine.PostAsJsonAsync("/api/v1/agents/pcr-runs", new AgentPcrRunRequest("biorad_solgent", Serial, "run-1.csv", file))).Content.ReadFromJsonAsync<AgentPcrRunResponse>())!;
        Assert.Equal("Backup", run.Disposition);
        Assert.Equal([("SMP-P1", "Accepted"), ("SMP-P2", "UnknownSample")], run.Samples.Select(sample => (sample.SampleNumber, sample.Status)).ToArray());

        var foreign = (await (await machine.PostAsJsonAsync("/api/v1/agents/pcr-runs", new AgentPcrRunRequest("Biorad_Solgent", null, "run-2.csv", RunFile("OTHER", ("A01", "S", "30", "", "", "30"))))).Content.ReadFromJsonAsync<AgentPcrRunResponse>())!;
        Assert.Equal(("Failed", "INT-037"), (foreign.Disposition, foreign.RefusalCode));

        var accepted = (await engineer.GetFromJsonAsync<Paged<IntakeLogItem>>("/api/v1/integration/intake?status=Accepted"))!;
        var landed = Assert.Single(accepted.Items);
        Assert.Equal(("Pcr", "DETECTED", "run-1.csv"), (landed.Source, landed.ConvertedValue, landed.FileName));
        Assert.Contains("B03-ORF1a-35.17", landed.Flags);
    }

    [DatabaseFact]
    public async Task Refusals_arrive_as_codes_and_permissions_decide_who_may_call()
    {
        var host = WithFakes(out _);
        var tenant = Guid.CreateVersion7();
        var engineer = await SignedInAsync(host, tenant, EngineerPermissions);
        var viewer = await SignedInAsync(host, tenant, ["integration.view"]);
        var outsider = await SignedInAsync(host, tenant, ["billing.view"]);

        var badSerial = await engineer.PostAsJsonAsync(Analysers, Analyser("SER1", null) with { Connection = new AnalyserConnectionDto("Serial", "9600-n-8-1", "COM3", null, null, null, null) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badSerial.StatusCode);
        Assert.Equal("INT-005", await AuthClient.CodeOf(badSerial));

        Assert.Equal(HttpStatusCode.Created, (await engineer.PostAsJsonAsync(Analysers, Analyser("DUP", null))).StatusCode);
        var duplicate = await engineer.PostAsJsonAsync(Analysers, Analyser("dup", null));
        Assert.Equal((HttpStatusCode.Conflict, "INT-100"), (duplicate.StatusCode, await AuthClient.CodeOf(duplicate)));

        var badMode = await engineer.PostAsJsonAsync(Analysers, Analyser("MODE", null) with { Mode = "Telepathy" });
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (badMode.StatusCode, await AuthClient.CodeOf(badMode)));

        var unknownAgent = await engineer.PostAsJsonAsync(Analysers, Analyser("NOAGENT", Guid.CreateVersion7()));
        Assert.Equal("INT-407", await AuthClient.CodeOf(unknownAgent));

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Analysers)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Analysers, Analyser("VIEW", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Agents, new CreateSiteAgentRequest("x", Guid.CreateVersion7(), null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Analysers)).StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(await outsider.GetAsync(Agents)));

        var personAsAgent = await engineer.PostAsJsonAsync("/api/v1/agents/heartbeat", new AgentHeartbeatRequest("1", null, null, null));
        Assert.Equal((HttpStatusCode.Forbidden, "INT-035"), (personAsAgent.StatusCode, await AuthClient.CodeOf(personAsAgent)));
    }

    [DatabaseFact]
    public async Task Rotating_deactivating_and_reactivating_an_agent_move_its_credential()
    {
        var host = WithFakes(out _);
        var engineer = await SignedInAsync(host, Guid.CreateVersion7(), EngineerPermissions);
        var agent = await CreateAgentAsync(engineer, "Rotating agent");

        var rotated = (await (await engineer.PostAsync($"{Agents}/{agent.AgentId}/rotate-secret", null)).Content.ReadFromJsonAsync<SiteAgentSecretResponse>())!;
        Assert.Equal(agent.ClientId, rotated.ClientId);
        Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(await MachineCredentialFlowTests.ExchangeAsync(host.CreateClient(), agent.ClientId, agent.ClientSecret)));
        _ = await MachineCredentialFlowTests.MachineClientAsync(host, rotated.ClientId, rotated.ClientSecret);

        Assert.Equal(HttpStatusCode.NoContent, (await engineer.PostAsync($"{Agents}/{agent.AgentId}/deactivate", null)).StatusCode);
        Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(await MachineCredentialFlowTests.ExchangeAsync(host.CreateClient(), rotated.ClientId, rotated.ClientSecret)));
        Assert.Equal("INT-031", await AuthClient.CodeOf(await engineer.PostAsync($"{Agents}/{agent.AgentId}/push-config", null)));

        var reactivated = (await (await engineer.PostAsync($"{Agents}/{agent.AgentId}/reactivate", null)).Content.ReadFromJsonAsync<SiteAgentSecretResponse>())!;
        Assert.NotEqual(rotated.ClientId, reactivated.ClientId);
        var machine = await MachineCredentialFlowTests.MachineClientAsync(host, reactivated.ClientId, reactivated.ClientSecret);
        Assert.Equal(HttpStatusCode.OK, (await machine.GetAsync("/api/v1/agents/config")).StatusCode);

        var detail = (await engineer.GetFromJsonAsync<SiteAgentDetail>($"{Agents}/{agent.AgentId}"))!;
        Assert.Equal((reactivated.ClientId, "Live", true), (detail.ClientId, detail.CredentialStatus, detail.IsActive));
    }

    private WebApplicationFactory<Program> WithFakes(out ConcurrentDictionary<string, SampleRef> samples)
    {
        var known = new ConcurrentDictionary<string, SampleRef>(StringComparer.Ordinal);
        samples = known;
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIntegrationReferenceLookup>();
            services.AddScoped<IIntegrationReferenceLookup, EverythingExistsLookup>();
            services.RemoveAll<IIntegrationSampleLookup>();
            services.AddScoped<IIntegrationSampleLookup>(_ => new InMemorySampleLookup(known));
        }));
    }

    private async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> host, Guid tenant, string[] permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: "Integration Engineer");
        var client = host.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private static async Task<SiteAgentSecretResponse> CreateAgentAsync(HttpClient engineer, string name)
    {
        var response = await engineer.PostAsJsonAsync(Agents, new CreateSiteAgentRequest(name, Guid.CreateVersion7(), "{\"logRetentionDays\":30}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SiteAgentSecretResponse>())!;
        Assert.StartsWith("lis-", created.ClientId, StringComparison.Ordinal);
        Assert.Equal(43, created.ClientSecret.Length);
        return created;
    }

    private static SaveAnalyserRequest Analyser(string code, Guid? agentId, bool shared = false) => new(
        code,
        "Analyser " + code,
        "جهاز " + code,
        Guid.CreateVersion7(),
        null,
        null,
        shared ? "SharedFolder" : "Unidirectional",
        shared ? new AnalyserConnectionDto("File", null, null, null, null, null, @"D:\COMM\Results") : new AnalyserConnectionDto("Serial", "9600,n,8,1", "COM3", null, null, null, null),
        false,
        false,
        false,
        false,
        true,
        false,
        1,
        [new AnalyserFlagDto("CLOT", "Stop", "Clot"), new AnalyserFlagDto("H", "Record", null)],
        agentId);

    private static async Task<Guid> CreateAnalyserAsync(HttpClient engineer, string code, Guid agentId, bool shared = false)
    {
        var response = await engineer.PostAsJsonAsync(Analysers, Analyser(code, agentId, shared));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static async Task MapAsync(HttpClient engineer, Guid analyserId, string code, bool autoSend, decimal slope)
    {
        var response = await engineer.PostAsJsonAsync($"{Analysers}/{analyserId}/mappings", new SaveMappingRequest(Guid.CreateVersion7(), code, null, 1, slope, 0m, null, false, null, autoSend, [], null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static SavePcrKitRequest Kit(Guid analyserId) => new(
        "Biorad_Solgent",
        analyserId,
        "COVID",
        Serial,
        new PcrRunFileLayoutDto(",", "Sample", "Target", "Well", "Content", "Unkn", "Optical Head Serial Number", ["N/A"]),
        "Cq",
        [new PcrGeneDto("ORF1a", false, 38m, 40m), new PcrGeneDto("RdRp", false, 38m, 40m), new PcrGeneDto("N Gene", false, 38m, 40m), new PcrGeneDto("IC", true, 40m, 40m)],
        new PcrVerdictRulesDto(1, "DETECTED", "NOT DETECTED", "Presumptive positive", "INVALID"),
        @"D:\COMM\Results\Biorad",
        @"D:\COMM\Backup",
        @"D:\COMM\Fail");

    private static string RunFile(string serial, params (string Well, string Sample, string Orf, string Rdrp, string N, string Ic)[] rows)
    {
        var lines = new List<string> { $"Optical Head Serial Number,{serial}", "Well,Fluor,Target,Content,Sample,Cq" };
        foreach (var row in rows)
        {
            lines.Add($"{row.Well},VIC,ORF1a,Unkn,{row.Sample},{row.Orf}");
            lines.Add($"{row.Well},CY5,RdRp,Unkn,{row.Sample},{row.Rdrp}");
            lines.Add($"{row.Well},FAM,N Gene,Unkn,{row.Sample},{row.N}");
            lines.Add($"{row.Well},Cal Red 610,IC,Unkn,{row.Sample},{row.Ic}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>The catalogue and branches of other modules, all present (their own tests cover their tables).</summary>
    private sealed class EverythingExistsLookup : IIntegrationReferenceLookup
    {
        public Task<IReadOnlyDictionary<Guid, ReferenceName>> FindAsync(ReferenceKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ReferenceName>>(ids.Distinct().ToDictionary(id => id, id => new ReferenceName(id, kind + "-" + id.ToString("N")[..6], kind + " name", "اسم", true)));

        public Task<bool> AnalyserModelExistsAsync(Guid analyserModelId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<AnalyserModelItem>> ListAnalyserModelsAsync(string? search, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyserModelItem>>([]);
    }

    private sealed class InMemorySampleLookup(ConcurrentDictionary<string, SampleRef> known) : IIntegrationSampleLookup
    {
        public Task<IReadOnlyDictionary<string, SampleRef>> FindByNumbersAsync(IReadOnlyCollection<string> sampleNumbers, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, SampleRef>>(sampleNumbers.Where(known.ContainsKey).Distinct(StringComparer.Ordinal).ToDictionary(number => number, number => known[number], StringComparer.Ordinal));

        public Task<IReadOnlyList<OrderRow>> ListOrdersAsync(Guid branchId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrderRow>>([]);
    }
}
