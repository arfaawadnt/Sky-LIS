using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Queries;

// The site agent's reads (integration.view + site-agent principal, INT-035): its configuration and the download orders.

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetAgentConfigQuery : IQuery<AgentConfigView>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetAgentOrdersQuery(string AnalyserCode, DateTimeOffset? Since) : IQuery<AgentOrdersView>;

public sealed class GetAgentOrdersValidator : AbstractValidator<GetAgentOrdersQuery>
{
    public GetAgentOrdersValidator() => RuleFor(query => query.AnalyserCode).NotEmpty().MaximumLength(Analyser.MaxCodeLength);
}

/// <summary>Everything the agent runs: its active analysers with flags, active mappings and sample codes, and their active kits.</summary>
public sealed class GetAgentConfigHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IAnalyserSampleCodeRepository sampleCodes,
    IPcrKitRepository kits,
    IIntegrationReferenceLookup lookup,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetAgentConfigQuery, AgentConfigView>
{
    public async Task<AgentConfigView> Handle(GetAgentConfigQuery query, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var hosted = (await analysers.ListForAgentAsync(agent.Id, cancellationToken)).Where(analyser => analyser.IsActive).OrderBy(analyser => analyser.Code, StringComparer.Ordinal).ToList();
        var ids = hosted.Select(analyser => analyser.Id).ToList();
        var activeMappings = await MappingNames.FillAsync(await mappings.ListActiveAsync(ids, cancellationToken), lookup, cancellationToken);
        var codes = await sampleCodes.ListAsync(ids, activeOnly: true, cancellationToken);
        var activeKits = await kits.ListActiveAsync(ids, cancellationToken);

        var analyserConfigs = hosted.Select(analyser => new AgentAnalyserConfig(
            analyser.Id,
            analyser.Code,
            analyser.Name.En,
            analyser.Mode.ToString(),
            IntegrationMapping.ToDto(analyser.Connection),
            analyser.AutoPrint,
            analyser.DownloadOrders,
            analyser.AppendUnregisteredTests,
            analyser.AppendUnregisteredSamples,
            analyser.PrintCopies,
            IntegrationMapping.ToDto(analyser.Flags),
            activeMappings.Where(mapping => mapping.AnalyserId == analyser.Id)
                .OrderBy(mapping => mapping.AnalyserCode, StringComparer.Ordinal)
                .Select(mapping => new AgentMappingConfig(mapping.AnalyserCode, mapping.HostCode, mapping.TestId, mapping.TestCode, mapping.ResultSelection, mapping.Query, mapping.QueryDetails, mapping.AutoSend))
                .ToList(),
            codes.Where(code => code.AnalyserId == analyser.Id).Select(code => new AgentSampleCodeConfig(code.SampleTypeId, code.Code)).ToList())).ToList();

        var kitConfigs = activeKits.OrderBy(kit => kit.Name, StringComparer.OrdinalIgnoreCase).Select(kit => new AgentPcrKitConfig(
            kit.Id,
            kit.Name,
            hosted.First(analyser => analyser.Id == kit.AnalyserId).Code,
            kit.SerialLock,
            IntegrationMapping.ToDto(kit.Layout),
            kit.ValueColumn,
            kit.ResultFolder,
            kit.BackupFolder,
            kit.FailedFolder)).ToList();

        return new AgentConfigView(agent.Id, agent.Name, agent.BranchId, agent.ConfigVersion, agent.ConfigJson, analyserConfigs, kitConfigs, clock.UtcNow);
    }
}

/// <summary>Download / query orders (intake A7): the branch's collected or received samples whose ordered tests are mapped on the analyser.</summary>
public sealed class GetAgentOrdersHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IAnalyserSampleCodeRepository sampleCodes,
    IIntegrationSampleLookup samples,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetAgentOrdersQuery, AgentOrdersView>
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);
    public const int MaxSamples = 500;

    public async Task<AgentOrdersView> Handle(GetAgentOrdersQuery query, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var now = clock.UtcNow;
        var code = query.AnalyserCode.Trim().ToUpperInvariant();
        var analyser = (await analysers.ListForAgentAsync(agent.Id, cancellationToken)).FirstOrDefault(candidate => candidate.Code == code && candidate.IsActive)
            ?? throw new DomainException(IntegrationCodes.AnalyserNotOnAgent, $"Analyser '{code}' is unknown, inactive or not hosted by this agent.", DomainFault.Refused);
        var since = query.Since is { } requested && requested <= now ? requested : now - DefaultWindow;

        var active = (await mappings.ListActiveAsync([analyser.Id], cancellationToken)).ToList();
        var byTest = active.GroupBy(mapping => mapping.TestId).ToDictionary(group => group.Key, group => group.OrderBy(mapping => mapping.ResultSelection).First());
        if (byTest.Count == 0)
        {
            return new AgentOrdersView(analyser.Code, since, now, []);
        }

        var codes = (await sampleCodes.ListAsync([analyser.Id], activeOnly: true, cancellationToken)).ToDictionary(item => item.SampleTypeId, item => item.Code);
        var rows = await samples.ListOrdersAsync(agent.BranchId, byTest.Keys.ToList(), since, MaxSamples, cancellationToken);
        var orders = rows.GroupBy(row => row.SampleNumber, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            var tests = group.Select(row => row.TestId).Distinct().Where(byTest.ContainsKey)
                .Select(testId => new AgentOrderTest(testId, byTest[testId].AnalyserCode, byTest[testId].HostCode))
                .OrderBy(test => test.AnalyserCode, StringComparer.Ordinal)
                .ToList();
            return new AgentOrder(first.SampleNumber, first.Accession, codes.TryGetValue(first.SampleTypeId, out var sampleCode) ? sampleCode : null, first.Urgent, first.CollectedAtUtc, first.ReceivedAtUtc, tests);
        }).Where(order => order.Tests.Count > 0).ToList();

        return new AgentOrdersView(analyser.Code, since, now, orders);
    }
}
