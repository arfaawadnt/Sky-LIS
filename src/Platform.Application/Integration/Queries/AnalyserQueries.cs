using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Queries;

// Registers of analysers (CP-58), their connection state ("test connection") and the platform model library (SA-15 read).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAnalysersQuery(string? Search, bool? IsActive, Guid? ClinicalGroupId, Guid? SiteAgentId, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<AnalyserListItem>>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetAnalyserQuery(Guid AnalyserId) : IQuery<AnalyserDetail>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetAnalyserConnectionQuery(Guid AnalyserId) : IQuery<AnalyserConnectionState>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAnalyserModelsQuery(string? Search) : IQuery<IReadOnlyList<AnalyserModelItem>>;

public sealed class ListAnalysersValidator : AbstractValidator<ListAnalysersQuery>
{
    public ListAnalysersValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(IntegrationSortKeys.Analysers);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListAnalyserModelsValidator : AbstractValidator<ListAnalyserModelsQuery>
{
    public ListAnalyserModelsValidator() => RuleFor(query => query.Search).MaximumLength(100);
}

public sealed class ListAnalysersHandler(IAnalyserRepository analysers, IAgentTelemetryStore telemetry, IIntegrationReferenceLookup lookup, IClock clock) : IQueryHandler<ListAnalysersQuery, Paged<AnalyserListItem>>
{
    public async Task<Paged<AnalyserListItem>> Handle(ListAnalysersQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var page = await analysers.ListAsync(new AnalyserFilter(query.Search, query.IsActive, query.ClinicalGroupId, query.SiteAgentId, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), now, cancellationToken);
        var groups = await IntegrationReferences.NamesAsync(lookup, ReferenceKind.ClinicalGroup, page.Items.Select(item => (Guid?)item.ClinicalGroupId), cancellationToken);
        var links = await telemetry.LinkStatesAsync(page.Items.Select(item => item.Id).ToList(), cancellationToken);

        var items = page.Items.Select(item =>
        {
            groups.TryGetValue(item.ClinicalGroupId, out var group);
            links.TryGetValue(item.Id, out var link);
            return item with
            {
                ClinicalGroupNameEn = group?.NameEn,
                ClinicalGroupNameAr = group?.NameAr,
                LinkStatus = IntegrationMapping.FreshLinkStatus(link, now),
                LastSeenAtUtc = link?.LastSeenAtUtc,
            };
        }).ToList();
        return page with { Items = items };
    }
}

public sealed class GetAnalyserHandler(IAnalyserRepository analysers) : IQueryHandler<GetAnalyserQuery, AnalyserDetail>
{
    public async Task<AnalyserDetail> Handle(GetAnalyserQuery query, CancellationToken cancellationToken) =>
        await analysers.GetDetailAsync(query.AnalyserId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
}

/// <summary>CP-58 "test connection": the last link state and the hosting agent's heartbeat; nothing is sent to the instrument from the cloud.</summary>
public sealed class GetAnalyserConnectionHandler(
    IAnalyserRepository analysers,
    ISiteAgentRepository agents,
    IAgentTelemetryStore telemetry,
    IClock clock) : IQueryHandler<GetAnalyserConnectionQuery, AnalyserConnectionState>
{
    public async Task<AnalyserConnectionState> Handle(GetAnalyserConnectionQuery query, CancellationToken cancellationToken)
    {
        var analyser = await analysers.GetDetailAsync(query.AnalyserId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
        var now = clock.UtcNow;
        var links = await telemetry.LinkStatesAsync([analyser.Id], cancellationToken);
        links.TryGetValue(analyser.Id, out var link);

        string? agentName = null;
        AgentState? agentState = null;
        if (analyser.SiteAgentId is { } agentId)
        {
            (await agents.NamesAsync([agentId], cancellationToken)).TryGetValue(agentId, out agentName);
            (await telemetry.AgentStatesAsync([agentId], cancellationToken)).TryGetValue(agentId, out agentState);
        }

        var agentStatus = SiteAgent.StatusAt(agentState?.LastHeartbeatAtUtc, agentState?.ReportedError ?? false, now);
        return new AnalyserConnectionState(
            analyser.Id,
            analyser.SiteAgentId,
            agentName,
            agentStatus.ToString(),
            agentState?.LastHeartbeatAtUtc,
            IntegrationMapping.FreshLinkStatus(link, now),
            link?.Message,
            link?.LastSeenAtUtc,
            now);
    }
}

public sealed class ListAnalyserModelsHandler(IIntegrationReferenceLookup lookup) : IQueryHandler<ListAnalyserModelsQuery, IReadOnlyList<AnalyserModelItem>>
{
    public Task<IReadOnlyList<AnalyserModelItem>> Handle(ListAnalyserModelsQuery query, CancellationToken cancellationToken) =>
        lookup.ListAnalyserModelsAsync(string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), cancellationToken);
}
