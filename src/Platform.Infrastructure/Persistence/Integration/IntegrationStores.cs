using Microsoft.EntityFrameworkCore;
using Platform.Application.Integration;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Integration;

namespace Platform.Infrastructure.Persistence.Integration;

/// <summary><see cref="IAgentTelemetryStore"/>: upserts the heartbeat rows in the handler's unit of work (no save here).</summary>
internal sealed class AgentTelemetryStore(AppDbContext context) : IAgentTelemetryStore
{
    public async Task RecordHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        var state = await context.Set<SiteAgentStateRow>().FirstOrDefaultAsync(row => row.Id == heartbeat.AgentId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            state = SiteAgentStateRow.Create(heartbeat.AgentId);
            context.Set<SiteAgentStateRow>().Add(state);
        }

        state.Record(heartbeat.Version, heartbeat.ReportedError, heartbeat.Message, heartbeat.AtUtc);

        var ids = heartbeat.Analysers.Select(report => report.AnalyserId).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var links = await context.Set<AnalyserConnectionStateRow>().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken).ConfigureAwait(false);
        foreach (var report in heartbeat.Analysers.GroupBy(report => report.AnalyserId).Select(group => group.Last()))
        {
            if (!links.TryGetValue(report.AnalyserId, out var link))
            {
                link = AnalyserConnectionStateRow.Create(report.AnalyserId);
                context.Set<AnalyserConnectionStateRow>().Add(link);
            }

            link.Record(heartbeat.AgentId, report.Status, report.Message, heartbeat.AtUtc);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, AgentState>> AgentStatesAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        var ids = agentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, AgentState>();
        }

        return await context.Set<SiteAgentStateRow>().AsNoTracking().Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new AgentState(row.Id, row.Version, row.ReportedError, row.Message, row.LastHeartbeatAtUtc), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, AnalyserLinkState>> LinkStatesAsync(IReadOnlyCollection<Guid> analyserIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyserIds);
        var ids = analyserIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, AnalyserLinkState>();
        }

        return await context.Set<AnalyserConnectionStateRow>().AsNoTracking().Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new AnalyserLinkState(row.Id, row.Status, row.Message, row.LastSeenAtUtc), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary><see cref="IAgentLogStore"/>: append-only rows, newest first in the viewer.</summary>
internal sealed class AgentLogStore(AppDbContext context) : IAgentLogStore
{
    public void Append(IEnumerable<AgentLogRecord> entries, DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        context.Set<AgentLogEntryRow>().AddRange(entries.Select(entry => AgentLogEntryRow.Create(entry.AgentId, entry.AtUtc, entry.Level, entry.Message, entry.SampleNumber, entry.AnalyserCode, receivedAtUtc)));
    }

    public async Task<Paged<AgentLogItem>> ListAsync(AgentLogFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = context.Set<AgentLogEntryRow>().AsNoTracking().Where(row => row.AgentId == filter.AgentId);
        if (filter.From is { } from)
        {
            query = query.Where(row => row.AtUtc >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.AtUtc <= to);
        }

        if (filter.MinLevel is { } level)
        {
            var levels = Enum.GetValues<AgentLogLevel>().Where(candidate => candidate >= level).ToArray();
            query = query.Where(row => levels.Contains(row.Level));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = SearchPattern.Contains(filter.Search);
            query = query.Where(row => EF.Functions.ILike(row.Message, pattern) || (row.SampleNumber != null && EF.Functions.ILike(row.SampleNumber, pattern)) || (row.AnalyserCode != null && EF.Functions.ILike(row.AnalyserCode, pattern)));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(row => row.AtUtc).ThenByDescending(row => row.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(row => new AgentLogItem(row.Id, row.AgentId, row.AtUtc, row.Level.ToString(), row.Message, row.SampleNumber, row.AnalyserCode, row.ReceivedAtUtc)).ToList();
        return new Paged<AgentLogItem>(items, page.Page, page.PageSize, total);
    }
}

/// <summary><see cref="IResultIntakeLog"/>: one row per value an agent relayed, newest first in the register.</summary>
internal sealed class ResultIntakeLog(AppDbContext context) : IResultIntakeLog
{
    public void Append(ResultIntakeRecord record) => context.Set<ResultIntakeRow>().Add(ResultIntakeRow.From(record));

    public async Task<Paged<IntakeLogItem>> ListAsync(ResultIntakeFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = context.Set<ResultIntakeRow>().AsNoTracking();
        if (filter.From is { } from)
        {
            query = query.Where(row => row.AtUtc >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.AtUtc <= to);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }

        if (filter.AnalyserId is { } analyserId)
        {
            query = query.Where(row => row.AnalyserId == analyserId);
        }

        if (filter.AgentId is { } agentId)
        {
            query = query.Where(row => row.AgentId == agentId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = SearchPattern.Contains(filter.Search);
            query = query.Where(row => EF.Functions.ILike(row.SampleNumber, pattern) || EF.Functions.ILike(row.AnalyserTestCode, pattern) || EF.Functions.ILike(row.AnalyserCode, pattern));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(row => row.AtUtc).ThenByDescending(row => row.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(row => new IntakeLogItem(
            row.Id, row.AgentId, null, row.AnalyserId, row.AnalyserCode, row.SampleNumber, row.AnalyserTestCode, row.TestId, row.RawValue, row.ConvertedValue,
            row.Flags, row.Status.ToString(), row.Reason, row.Source, row.FileName, row.MeasuredAtUtc, row.AtUtc)).ToList();
        return new Paged<IntakeLogItem>(items, page.Page, page.PageSize, total);
    }
}
