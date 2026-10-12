using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity.MachineCredentials;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Queries;

// Site agents (CP-61): the register with derived status and heartbeat age, the detail, the agent log and the result intake log.

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListSiteAgentsQuery(string? Search, Guid? BranchId, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<SiteAgentListItem>>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record GetSiteAgentQuery(Guid AgentId) : IQuery<SiteAgentDetail>;

/// <summary><paramref name="Level"/> is the minimum level shown (Warning shows warnings and errors).</summary>
[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListAgentLogsQuery(Guid AgentId, DateTimeOffset? From, DateTimeOffset? To, string? Level, string? Search, int? Page, int? PageSize) : IQuery<Paged<AgentLogItem>>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.View)]
public sealed record ListResultIntakeQuery(DateTimeOffset? From, DateTimeOffset? To, string? Status, Guid? AnalyserId, Guid? AgentId, string? Search, int? Page, int? PageSize) : IQuery<Paged<IntakeLogItem>>;

public sealed class ListSiteAgentsValidator : AbstractValidator<ListSiteAgentsQuery>
{
    public ListSiteAgentsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(IntegrationSortKeys.SiteAgents);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListAgentLogsValidator : AbstractValidator<ListAgentLogsQuery>
{
    public ListAgentLogsValidator()
    {
        RuleFor(query => query.AgentId).NotEmpty();
        RuleFor(query => query.Level).Must(level => level is null || IntegrationNames.IsName<AgentLogLevel>(level)).WithMessage("Unknown level.");
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.To).GreaterThanOrEqualTo(query => query.From).When(query => query.From is not null && query.To is not null);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListResultIntakeValidator : AbstractValidator<ListResultIntakeQuery>
{
    public ListResultIntakeValidator()
    {
        RuleFor(query => query.Status).Must(status => status is null || IntegrationNames.IsName<ResultIntakeStatus>(status)).WithMessage("Unknown status.");
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.To).GreaterThanOrEqualTo(query => query.From).When(query => query.From is not null && query.To is not null);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListSiteAgentsHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAgentTelemetryStore telemetry,
    IMachineCredentialRepository credentials,
    IIntegrationReferenceLookup lookup,
    ICurrentTenant tenant,
    IClock clock) : IQueryHandler<ListSiteAgentsQuery, Paged<SiteAgentListItem>>
{
    public async Task<Paged<SiteAgentListItem>> Handle(ListSiteAgentsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var now = clock.UtcNow;
        var page = await agents.ListAsync(new SiteAgentFilter(string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), query.BranchId, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var ids = page.Items.Select(item => item.Id).ToList();
        var states = await telemetry.AgentStatesAsync(ids, cancellationToken);
        var counts = await analysers.CountByAgentAsync(ids, cancellationToken);
        var summaries = await credentials.SummariesAsync(tenantId, page.Items.Select(item => item.MachineCredentialId).ToList(), now, cancellationToken);
        var branches = await IntegrationReferences.NamesAsync(lookup, ReferenceKind.Branch, page.Items.Select(item => (Guid?)item.BranchId), cancellationToken);

        var items = page.Items.Select(row =>
        {
            states.TryGetValue(row.Id, out var state);
            summaries.TryGetValue(row.MachineCredentialId, out var credential);
            branches.TryGetValue(row.BranchId, out var branch);
            return new SiteAgentListItem(
                row.Id,
                row.Name,
                row.BranchId,
                branch?.NameEn,
                branch?.NameAr,
                credential?.ClientId,
                credential?.Status,
                state?.Version,
                SiteAgent.StatusAt(state?.LastHeartbeatAtUtc, state?.ReportedError ?? false, now).ToString(),
                state?.LastHeartbeatAtUtc,
                counts.TryGetValue(row.Id, out var count) ? count : 0,
                row.ConfigVersion,
                row.IsActive);
        }).ToList();
        return new Paged<SiteAgentListItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

public sealed class GetSiteAgentHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAgentTelemetryStore telemetry,
    IMachineCredentialRepository credentials,
    IIntegrationReferenceLookup lookup,
    ICurrentTenant tenant,
    IClock clock) : IQueryHandler<GetSiteAgentQuery, SiteAgentDetail>
{
    public async Task<SiteAgentDetail> Handle(GetSiteAgentQuery query, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var agent = await agents.GetForUpdateAsync(query.AgentId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AgentNotFound, "Site agent not found.", DomainFault.NotFound);
        var now = clock.UtcNow;
        (await telemetry.AgentStatesAsync([agent.Id], cancellationToken)).TryGetValue(agent.Id, out var state);
        (await credentials.SummariesAsync(tenantId, [agent.MachineCredentialId], now, cancellationToken)).TryGetValue(agent.MachineCredentialId, out var credential);
        (await IntegrationReferences.NamesAsync(lookup, ReferenceKind.Branch, [agent.BranchId], cancellationToken)).TryGetValue(agent.BranchId, out var branch);

        var hosted = await analysers.ListForAgentAsync(agent.Id, cancellationToken);
        var links = await telemetry.LinkStatesAsync(hosted.Select(analyser => analyser.Id).ToList(), cancellationToken);
        var analyserItems = hosted.OrderBy(analyser => analyser.Code, StringComparer.Ordinal).Select(analyser =>
        {
            links.TryGetValue(analyser.Id, out var link);
            return new SiteAgentAnalyserItem(analyser.Id, analyser.Code, analyser.Name.En, analyser.Name.Ar, IntegrationMapping.FreshLinkStatus(link, now), link?.LastSeenAtUtc, link?.Message, analyser.IsActive);
        }).ToList();

        return new SiteAgentDetail(
            agent.Id,
            agent.Name,
            agent.BranchId,
            branch?.NameEn,
            branch?.NameAr,
            agent.ConfigJson,
            agent.ConfigVersion,
            credential?.ClientId,
            credential?.Status,
            credential?.ExpiresAtUtc,
            state?.Version,
            SiteAgent.StatusAt(state?.LastHeartbeatAtUtc, state?.ReportedError ?? false, now).ToString(),
            state?.Message,
            state?.LastHeartbeatAtUtc,
            analyserItems,
            agent.IsActive);
    }
}

public sealed class ListAgentLogsHandler(ISiteAgentRepository agents, IAgentLogStore logs) : IQueryHandler<ListAgentLogsQuery, Paged<AgentLogItem>>
{
    public async Task<Paged<AgentLogItem>> Handle(ListAgentLogsQuery query, CancellationToken cancellationToken)
    {
        _ = await agents.GetForUpdateAsync(query.AgentId, cancellationToken)
            ?? throw new DomainException(IntegrationCodes.AgentNotFound, "Site agent not found.", DomainFault.NotFound);
        AgentLogLevel? level = string.IsNullOrWhiteSpace(query.Level) ? null : IntegrationNames.Parse<AgentLogLevel>(query.Level, IntegrationCodes.LogBatchInvalid);
        var filter = new AgentLogFilter(query.AgentId, query.From, query.To, level, string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim());
        return await logs.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

public sealed class ListResultIntakeHandler(IResultIntakeLog intake, ISiteAgentRepository agents) : IQueryHandler<ListResultIntakeQuery, Paged<IntakeLogItem>>
{
    public async Task<Paged<IntakeLogItem>> Handle(ListResultIntakeQuery query, CancellationToken cancellationToken)
    {
        ResultIntakeStatus? status = string.IsNullOrWhiteSpace(query.Status) ? null : IntegrationNames.Parse<ResultIntakeStatus>(query.Status, IntegrationCodes.ResultBatchInvalid);
        var filter = new ResultIntakeFilter(query.From, query.To, status, query.AnalyserId, query.AgentId, string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim());
        var page = await intake.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
        var names = await agents.NamesAsync(page.Items.Select(item => item.AgentId).Distinct().ToList(), cancellationToken);
        return page with { Items = page.Items.Select(item => item with { AgentName = names.TryGetValue(item.AgentId, out var name) ? name : null }).ToList() };
    }
}
