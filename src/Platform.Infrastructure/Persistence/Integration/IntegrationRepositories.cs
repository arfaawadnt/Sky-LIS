using Microsoft.EntityFrameworkCore;
using Platform.Application.Integration;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Integration;

namespace Platform.Infrastructure.Persistence.Integration;

/// <summary>Search patterns for <c>ILIKE</c>: the user's text is matched literally (wildcards escaped).</summary>
internal static class SearchPattern
{
    public static string Contains(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

internal sealed class AnalyserRepository(AppDbContext context) : Repository<Analyser>(context), IAnalyserRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(analyser => analyser.Code == cleaned, cancellationToken);
    }

    public Task<Analyser?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(analyser => analyser.Code == cleaned, cancellationToken);
    }

    public async Task<IReadOnlyList<Analyser>> ListForAgentAsync(Guid agentId, CancellationToken cancellationToken) =>
        await Set.Where(analyser => analyser.SiteAgentId == agentId).OrderBy(analyser => analyser.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Paged<AnalyserListItem>> ListAsync(AnalyserFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(analyser => analyser.IsActive == active);
        }

        if (filter.ClinicalGroupId is { } group)
        {
            query = query.Where(analyser => analyser.ClinicalGroupId == group);
        }

        if (filter.SiteAgentId is { } agentFilter)
        {
            query = query.Where(analyser => analyser.SiteAgentId == agentFilter);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = SearchPattern.Contains(filter.Search);
            query = query.Where(analyser => EF.Functions.ILike(analyser.Code, pattern) || EF.Functions.ILike(analyser.Name.En, pattern) || EF.Functions.ILike(analyser.Name.Ar, pattern));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from analyser in query.OrderBy(analyser => analyser.Code).Skip(page.Skip).Take(page.PageSize)
            from agent in Context.Set<SiteAgent>().AsNoTracking().Where(agent => agent.Id == analyser.SiteAgentId).DefaultIfEmpty()
            select new
            {
                analyser.Id,
                analyser.Code,
                NameEn = analyser.Name.En,
                NameAr = analyser.Name.Ar,
                analyser.ClinicalGroupId,
                analyser.Mode,
                analyser.Connection.Kind,
                analyser.SiteAgentId,
                AgentName = agent == null ? null : agent.Name,
                Mappings = Context.Set<AnalyserTestMapping>().Count(mapping => mapping.AnalyserId == analyser.Id && mapping.IsActive),
                analyser.IsActive,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row => new AnalyserListItem(
            row.Id, row.Code, row.NameEn, row.NameAr, row.ClinicalGroupId, null, null, row.Mode.ToString(), row.Kind.ToString(),
            row.SiteAgentId, row.AgentName, nameof(AnalyserLinkStatus.Unknown), null, row.Mappings, row.IsActive)).ToList();
        return new Paged<AnalyserListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<AnalyserDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var analyser = await Set.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        return analyser is null
            ? null
            : new AnalyserDetail(
                analyser.Id,
                analyser.Code,
                analyser.Name.En,
                analyser.Name.Ar,
                analyser.ClinicalGroupId,
                analyser.DefaultSampleTypeId,
                analyser.AnalyserModelId,
                analyser.Mode.ToString(),
                IntegrationMapping.ToDto(analyser.Connection),
                analyser.AutoPrint,
                analyser.DownloadOrders,
                analyser.AppendUnregisteredTests,
                analyser.AppendUnregisteredSamples,
                analyser.OverwriteReviewed,
                analyser.OverwriteVerified,
                analyser.PrintCopies,
                IntegrationMapping.ToDto(analyser.Flags),
                analyser.SiteAgentId,
                analyser.IsActive);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByAgentAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        var ids = agentIds.Distinct().Select(id => (Guid?)id).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await Set.AsNoTracking()
            .Where(analyser => ids.Contains(analyser.SiteAgentId))
            .GroupBy(analyser => analyser.SiteAgentId)
            .Select(group => new { AgentId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return counts.Where(row => row.AgentId is not null).ToDictionary(row => row.AgentId!.Value, row => row.Count);
    }
}

internal sealed class AnalyserTestMappingRepository(AppDbContext context) : Repository<AnalyserTestMapping>(context), IAnalyserTestMappingRepository
{
    public Task<bool> CodeExistsAsync(Guid analyserId, string analyserCode, Guid? exceptMappingId, CancellationToken cancellationToken)
    {
        var code = (analyserCode ?? string.Empty).Trim();
        return Set.AnyAsync(mapping => mapping.AnalyserId == analyserId && mapping.AnalyserCode == code && (exceptMappingId == null || mapping.Id != exceptMappingId), cancellationToken);
    }

    public Task<AnalyserTestMapping?> GetInAnalyserAsync(Guid analyserId, Guid mappingId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(mapping => mapping.Id == mappingId && mapping.AnalyserId == analyserId, cancellationToken);

    public async Task<IReadOnlyDictionary<string, AnalyserTestMapping>> FindByCodesAsync(Guid analyserId, IReadOnlyCollection<string> analyserCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserCodes);
        var codes = analyserCodes.Distinct(StringComparer.Ordinal).ToArray();
        if (codes.Length == 0)
        {
            return new Dictionary<string, AnalyserTestMapping>(StringComparer.Ordinal);
        }

        var found = await Set.Where(mapping => mapping.AnalyserId == analyserId && codes.Contains(mapping.AnalyserCode)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(mapping => mapping.AnalyserCode, StringComparer.Ordinal);
    }

    public async Task<Paged<MappingListItem>> ListAsync(Guid analyserId, string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking().Where(mapping => mapping.AnalyserId == analyserId);
        if (isActive is { } active)
        {
            query = query.Where(mapping => mapping.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Contains(search);
            query = query.Where(mapping => EF.Functions.ILike(mapping.AnalyserCode, pattern) || (mapping.HostCode != null && EF.Functions.ILike(mapping.HostCode, pattern)));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(mapping => mapping.AnalyserCode).ThenBy(mapping => mapping.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<MappingListItem>(rows.Select(ToItem).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<MappingListItem>> ListActiveAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserIds);
        var ids = analyserIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await Set.AsNoTracking().Where(mapping => ids.Contains(mapping.AnalyserId) && mapping.IsActive).OrderBy(mapping => mapping.AnalyserCode).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToItem).ToList();
    }

    private static MappingListItem ToItem(AnalyserTestMapping mapping) => new(
        mapping.Id,
        mapping.AnalyserId,
        mapping.TestId,
        null,
        null,
        null,
        mapping.AnalyserCode,
        mapping.HostCode,
        mapping.ResultSelection,
        mapping.Slope,
        mapping.Intercept,
        mapping.UnitId,
        null,
        mapping.Query,
        mapping.QueryDetails,
        mapping.AutoSend,
        mapping.Conversions.OrderBy(conversion => conversion.Order).Select(conversion => new ConversionDto(conversion.FromValue, conversion.ToValue)).ToList(),
        mapping.RangeOverride is { } range ? new RangeOverrideDto(range.Low, range.High, range.Text) : null,
        mapping.Methodology,
        mapping.IsActive);
}

internal sealed class AnalyserSampleCodeRepository(AppDbContext context) : Repository<AnalyserSampleCode>(context), IAnalyserSampleCodeRepository
{
    public Task<bool> ExistsAsync(Guid analyserId, Guid sampleTypeId, CancellationToken cancellationToken) =>
        Set.AnyAsync(code => code.AnalyserId == analyserId && code.SampleTypeId == sampleTypeId, cancellationToken);

    public Task<AnalyserSampleCode?> GetInAnalyserAsync(Guid analyserId, Guid sampleCodeId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(code => code.Id == sampleCodeId && code.AnalyserId == analyserId, cancellationToken);

    public async Task<IReadOnlyList<SampleCodeItem>> ListAsync(IReadOnlyCollection<Guid> analyserIds, bool activeOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserIds);
        var ids = analyserIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var query = Set.AsNoTracking().Where(code => ids.Contains(code.AnalyserId));
        if (activeOnly)
        {
            query = query.Where(code => code.IsActive);
        }

        return await query.OrderBy(code => code.Code)
            .Select(code => new SampleCodeItem(code.Id, code.AnalyserId, code.SampleTypeId, null, null, null, code.Code, code.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class SiteAgentRepository(AppDbContext context) : Repository<SiteAgent>(context), ISiteAgentRepository
{
    public Task<bool> NameExistsAsync(string name, Guid? exceptAgentId, CancellationToken cancellationToken)
    {
        var lowered = (name ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(agent => agent.Name.ToLower() == lowered && (exceptAgentId == null || agent.Id != exceptAgentId), cancellationToken);
    }

    public Task<SiteAgent?> FindByServiceUserAsync(Guid serviceUserId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(agent => agent.ServiceUserId == serviceUserId, cancellationToken);

    public async Task<Paged<SiteAgentRow>> ListAsync(SiteAgentFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(agent => agent.IsActive == active);
        }

        if (filter.BranchId is { } branch)
        {
            query = query.Where(agent => agent.BranchId == branch);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = SearchPattern.Contains(filter.Search);
            query = query.Where(agent => EF.Functions.ILike(agent.Name, pattern));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(agent => agent.Name).ThenBy(agent => agent.Id).Skip(page.Skip).Take(page.PageSize)
            .Select(agent => new SiteAgentRow(agent.Id, agent.Name, agent.BranchId, agent.MachineCredentialId, agent.ConfigJson, agent.ConfigVersion, agent.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new Paged<SiteAgentRow>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        var ids = agentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await Set.AsNoTracking().Where(agent => ids.Contains(agent.Id)).ToDictionaryAsync(agent => agent.Id, agent => agent.Name, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PcrKitRepository(AppDbContext context) : Repository<PcrKit>(context), IPcrKitRepository
{
    public Task<bool> NameExistsAsync(string name, Guid? exceptKitId, CancellationToken cancellationToken)
    {
        var lowered = (name ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(kit => kit.Name.ToLower() == lowered && (exceptKitId == null || kit.Id != exceptKitId), cancellationToken);
    }

    public Task<PcrKit?> FindByNameAsync(string name, IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserIds);
        var lowered = (name ?? string.Empty).Trim().ToLowerInvariant();
        var ids = analyserIds.Distinct().ToArray();
        return Set.AsNoTracking().FirstOrDefaultAsync(kit => kit.Name.ToLower() == lowered && ids.Contains(kit.AnalyserId), cancellationToken);
    }

    public async Task<IReadOnlyList<PcrKit>> ListActiveAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserIds);
        var ids = analyserIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await Set.AsNoTracking().Where(kit => kit.IsActive && ids.Contains(kit.AnalyserId)).OrderBy(kit => kit.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Paged<PcrKitListItem>> ListAsync(PcrKitFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(kit => kit.IsActive == active);
        }

        if (filter.AnalyserId is { } analyserId)
        {
            query = query.Where(kit => kit.AnalyserId == analyserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = SearchPattern.Contains(filter.Search);
            query = query.Where(kit => EF.Functions.ILike(kit.Name, pattern));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await (
            from kit in query.OrderBy(kit => kit.Name).ThenBy(kit => kit.Id).Skip(page.Skip).Take(page.PageSize)
            from analyser in Context.Set<Analyser>().AsNoTracking().Where(analyser => analyser.Id == kit.AnalyserId).DefaultIfEmpty()
            select new PcrKitListItem(
                kit.Id,
                kit.Name,
                kit.AnalyserId,
                analyser == null ? null : analyser.Code,
                analyser == null ? null : analyser.Name.En,
                analyser == null ? null : analyser.Name.Ar,
                kit.AnalyserTestCode,
                kit.SerialLock,
                kit.Genes.Count,
                kit.IsActive)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<PcrKitListItem>(items, page.Page, page.PageSize, total);
    }
}
