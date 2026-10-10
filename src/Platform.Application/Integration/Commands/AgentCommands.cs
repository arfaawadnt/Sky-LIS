using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Integration.Commands;

// The site agent's writes (intake rows H1, R1, R2). Callers are machine principals holding integration.create AND
// recognised as an active site agent (INT-035). A batch is never refused for one item: every item gets an outcome and
// an intake row, and only Accepted items raise AnalyserResultReceived — in the same commit as the intake rows.

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Create)]
public sealed record RecordAgentHeartbeatCommand(AgentHeartbeatRequest Heartbeat) : ICommand<AgentHeartbeatResponse>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Create)]
public sealed record AppendAgentLogsCommand(AgentLogBatchRequest Batch) : ICommand<AgentLogBatchResponse>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Create)]
public sealed record SubmitAgentResultsCommand(AgentResultBatchRequest Batch) : ICommand<AgentResultBatchResponse>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Create)]
public sealed record SubmitPcrRunCommand(AgentPcrRunRequest Run) : ICommand<AgentPcrRunResponse>;

/// <summary>Size limits of the agent API (intake §13).</summary>
public static class AgentLimits
{
    public const int MaxAnalysersPerHeartbeat = 200;
    public const int MaxLogEntries = 500;
    public const int MaxLogMessageLength = 2000;
    public const int MaxResults = 500;
    public const int MaxSampleNumberLength = 64;
    public const int MaxValueLength = AnalyserTestMapping.MaxValueLength;
    public const int MaxFlags = 20;
    public const int MaxFlagLength = 64;
    public const int MaxUnitLength = 32;
    public const int MaxVersionLength = 64;
    public const int MaxMessageLength = 500;
    public const int MaxFileNameLength = 260;
}

public sealed class RecordAgentHeartbeatValidator : AbstractValidator<RecordAgentHeartbeatCommand>
{
    public RecordAgentHeartbeatValidator()
    {
        RuleFor(command => command.Heartbeat).NotNull();
        RuleFor(command => command.Heartbeat.Version).MaximumLength(AgentLimits.MaxVersionLength).When(command => command.Heartbeat is not null);
        RuleFor(command => command.Heartbeat.Message).MaximumLength(AgentLimits.MaxMessageLength).When(command => command.Heartbeat is not null);
        RuleFor(command => command.Heartbeat.Status).Must(status => status is null || IntegrationNames.IsName<SiteAgentStatus>(status)).WithMessage("Unknown agent status.").When(command => command.Heartbeat is not null);
        RuleFor(command => command.Heartbeat.Analysers).Must(list => list is null || list.Count <= AgentLimits.MaxAnalysersPerHeartbeat).WithMessage($"At most {AgentLimits.MaxAnalysersPerHeartbeat} analysers.").When(command => command.Heartbeat is not null);
        RuleForEach(command => command.Heartbeat.Analysers).ChildRules(item =>
        {
            item.RuleFor(status => status.Code).NotEmpty().MaximumLength(Analyser.MaxCodeLength);
            item.RuleFor(status => status.Status).NotEmpty().Must(IntegrationNames.IsName<AnalyserLinkStatus>).WithMessage("Unknown link status.");
            item.RuleFor(status => status.Message).MaximumLength(AgentLimits.MaxMessageLength);
        }).When(command => command.Heartbeat is not null);
    }
}

public sealed class AppendAgentLogsValidator : AbstractValidator<AppendAgentLogsCommand>
{
    public AppendAgentLogsValidator()
    {
        RuleFor(command => command.Batch).NotNull();
        RuleFor(command => command.Batch.Entries).NotNull().Must(entries => entries is { Count: >= 1 and <= AgentLimits.MaxLogEntries }).WithMessage($"Between 1 and {AgentLimits.MaxLogEntries} entries.").When(command => command.Batch is not null);
        RuleForEach(command => command.Batch.Entries).ChildRules(entry =>
        {
            entry.RuleFor(item => item.Level).NotEmpty().Must(IntegrationNames.IsName<AgentLogLevel>).WithMessage("Unknown level.");
            entry.RuleFor(item => item.Message).NotEmpty().MaximumLength(AgentLimits.MaxLogMessageLength);
            entry.RuleFor(item => item.SampleNumber).MaximumLength(AgentLimits.MaxSampleNumberLength);
            entry.RuleFor(item => item.AnalyserCode).MaximumLength(Analyser.MaxCodeLength);
        }).When(command => command.Batch is not null);
    }
}

public sealed class SubmitAgentResultsValidator : AbstractValidator<SubmitAgentResultsCommand>
{
    public SubmitAgentResultsValidator()
    {
        RuleFor(command => command.Batch).NotNull();
        RuleFor(command => command.Batch.AnalyserCode).NotEmpty().MaximumLength(Analyser.MaxCodeLength).When(command => command.Batch is not null);
        RuleFor(command => command.Batch.Results).NotNull().Must(results => results is { Count: >= 1 and <= AgentLimits.MaxResults }).WithMessage($"Between 1 and {AgentLimits.MaxResults} results.").When(command => command.Batch is not null);
        RuleForEach(command => command.Batch.Results).ChildRules(result =>
        {
            result.RuleFor(item => item.SampleNumber).NotEmpty().MaximumLength(AgentLimits.MaxSampleNumberLength);
            result.RuleFor(item => item.AnalyserTestCode).NotEmpty().MaximumLength(AnalyserTestMapping.MaxCodeLength);
            result.RuleFor(item => item.Value).NotNull().MaximumLength(AgentLimits.MaxValueLength);
            result.RuleFor(item => item.Unit).MaximumLength(AgentLimits.MaxUnitLength);
            result.RuleFor(item => item.Flags).Must(flags => flags is null || (flags.Count <= AgentLimits.MaxFlags && flags.All(flag => flag is not null && flag.Length <= AgentLimits.MaxFlagLength)))
                .WithMessage($"At most {AgentLimits.MaxFlags} flags of {AgentLimits.MaxFlagLength} characters.");
        }).When(command => command.Batch is not null);
    }
}

public sealed class SubmitPcrRunValidator : AbstractValidator<SubmitPcrRunCommand>
{
    public SubmitPcrRunValidator()
    {
        RuleFor(command => command.Run).NotNull();
        RuleFor(command => command.Run.KitName).NotEmpty().MaximumLength(PcrKit.MaxNameLength).When(command => command.Run is not null);
        RuleFor(command => command.Run.InstrumentSerial).MaximumLength(PcrKit.MaxSerialLength).When(command => command.Run is not null);
        RuleFor(command => command.Run.FileName).NotEmpty().MaximumLength(AgentLimits.MaxFileNameLength).When(command => command.Run is not null);
        RuleFor(command => command.Run.Content).NotEmpty().MaximumLength(PcrInterpreter.MaxContentLength).When(command => command.Run is not null);
    }
}

public sealed class RecordAgentHeartbeatHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAgentTelemetryStore telemetry,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RecordAgentHeartbeatCommand, AgentHeartbeatResponse>
{
    public async Task<AgentHeartbeatResponse> Handle(RecordAgentHeartbeatCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var now = clock.UtcNow;
        var hosted = (await analysers.ListForAgentAsync(agent.Id, cancellationToken)).ToDictionary(analyser => analyser.Code, analyser => analyser.Id, StringComparer.OrdinalIgnoreCase);

        var reports = new List<AnalyserLinkReport>();
        var unknown = new List<string>();
        foreach (var status in command.Heartbeat.Analysers ?? [])
        {
            var code = status.Code.Trim();
            if (hosted.TryGetValue(code, out var analyserId))
            {
                reports.Add(new AnalyserLinkReport(analyserId, IntegrationNames.Parse<AnalyserLinkStatus>(status.Status, IntegrationCodes.HeartbeatInvalid), Trim(status.Message)));
            }
            else
            {
                unknown.Add(code);
            }
        }

        var reportedError = string.Equals(command.Heartbeat.Status, nameof(SiteAgentStatus.Error), StringComparison.OrdinalIgnoreCase)
            || reports.Exists(report => report.Status == AnalyserLinkStatus.Error);
        await telemetry.RecordHeartbeatAsync(new AgentHeartbeat(agent.Id, Trim(command.Heartbeat.Version), reportedError, Trim(command.Heartbeat.Message), now, reports), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AgentHeartbeatResponse(agent.Id, agent.ConfigVersion, now, unknown);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AppendAgentLogsHandler(ISiteAgentRepository agents, IAgentLogStore logs, ICurrentUser user, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<AppendAgentLogsCommand, AgentLogBatchResponse>
{
    public async Task<AgentLogBatchResponse> Handle(AppendAgentLogsCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var entries = command.Batch.Entries.Select(entry => new AgentLogRecord(
            agent.Id,
            entry.AtUtc,
            IntegrationNames.Parse<AgentLogLevel>(entry.Level, IntegrationCodes.LogBatchInvalid),
            entry.Message.Trim(),
            string.IsNullOrWhiteSpace(entry.SampleNumber) ? null : entry.SampleNumber.Trim(),
            string.IsNullOrWhiteSpace(entry.AnalyserCode) ? null : entry.AnalyserCode.Trim().ToUpperInvariant())).ToList();

        logs.Append(entries, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentLogBatchResponse(entries.Count);
    }
}

/// <summary>R1: each value is checked against the analyser (hosted by the caller, active), the sample, the mapping and Auto Send, logged, and raised when accepted.</summary>
public sealed class SubmitAgentResultsHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IIntegrationSampleLookup samples,
    IResultIntakeLog intake,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SubmitAgentResultsCommand, AgentResultBatchResponse>
{
    public async Task<AgentResultBatchResponse> Handle(SubmitAgentResultsCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var now = clock.UtcNow;
        var analyserCode = command.Batch.AnalyserCode.Trim().ToUpperInvariant();
        var analyser = await analysers.FindByCodeAsync(analyserCode, cancellationToken);
        var hosted = analyser is not null && analyser.SiteAgentId == agent.Id;

        var items = command.Batch.Results;
        var byCode = hosted
            ? await mappings.FindByCodesAsync(analyser!.Id, items.Select(item => item.AnalyserTestCode.Trim()).Distinct(StringComparer.Ordinal).ToList(), cancellationToken)
            : new Dictionary<string, AnalyserTestMapping>();
        var known = await samples.FindByNumbersAsync(items.Select(item => item.SampleNumber.Trim()).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

        var outcomes = new List<AgentResultOutcome>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var sampleNumber = item.SampleNumber.Trim();
            var testCode = item.AnalyserTestCode.Trim();
            var flags = (item.Flags ?? []).Where(flag => !string.IsNullOrWhiteSpace(flag)).Select(flag => flag.Trim()).ToList();
            var intakeId = Guid.CreateVersion7();
            byCode.TryGetValue(testCode, out var mapping);
            known.TryGetValue(sampleNumber, out var sample);

            var outcome = !hosted
                ? new AnalyserResultOutcome(ResultIntakeStatus.Refused, null, [], $"Analyser '{analyserCode}' is unknown or not hosted by this agent.")
                : sample is null
                    ? new AnalyserResultOutcome(ResultIntakeStatus.UnknownSample, null, [], "No sample carries this number.")
                    : IsUnusable(sample)
                        ? new AnalyserResultOutcome(ResultIntakeStatus.Refused, null, [], $"The sample is {sample.Status}.")
                        : mapping is null
                            ? new AnalyserResultOutcome(ResultIntakeStatus.Unmapped, null, [], $"The analyser code '{testCode}' is not mapped.")
                            : analyser!.AcceptResult(mapping, new IncomingAnalyserResult(sampleNumber, sample.SampleId, testCode, item.Value.Trim(), flags, item.Unit, item.MeasuredAtUtc ?? now), intakeId, ResultSources.Analyser, now);

            intake.Append(new ResultIntakeRecord(
                intakeId,
                agent.Id,
                hosted ? analyser!.Id : null,
                analyserCode,
                sampleNumber,
                testCode,
                mapping?.TestId,
                mapping?.Id,
                item.Value.Trim(),
                outcome.ConvertedValue,
                flags,
                outcome.Status,
                outcome.Reason,
                ResultSources.Analyser,
                null,
                item.MeasuredAtUtc,
                now));
            outcomes.Add(new AgentResultOutcome(index, sampleNumber, testCode, outcome.Status.ToString(), outcome.ConvertedValue, outcome.Reason));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentResultBatchResponse(outcomes);
    }

    internal static bool IsUnusable(SampleRef sample) => sample.Status is "Rejected" or "Cancelled" or "Replaced";
}

/// <summary>
/// R2: the run file is read with the named kit of one of the caller's instruments; a refused file goes to the failed
/// folder (logged), a readable one to backup, and every sample verdict lands through the kit's mapping like an analyser
/// value (Auto Send, conversions) with the gene readings as its flags (LDM's <c>&lt;well&gt;-&lt;gene&gt;-&lt;Ct&gt;</c> trail).
/// </summary>
public sealed class SubmitPcrRunHandler(
    ISiteAgentRepository agents,
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IPcrKitRepository kits,
    IIntegrationSampleLookup samples,
    IResultIntakeLog intake,
    IAgentLogStore logs,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SubmitPcrRunCommand, AgentPcrRunResponse>
{
    public async Task<AgentPcrRunResponse> Handle(SubmitPcrRunCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentContext.RequireAsync(agents, user, cancellationToken);
        var now = clock.UtcNow;
        var run = command.Run;
        var fileName = run.FileName.Trim();
        var hosted = await analysers.ListForAgentAsync(agent.Id, cancellationToken);
        var kit = await kits.FindByNameAsync(run.KitName.Trim(), hosted.Where(analyser => analyser.IsActive).Select(analyser => analyser.Id).ToList(), cancellationToken);

        AgentPcrRunResponse response;
        if (kit is null || !kit.IsActive)
        {
            var reason = kit is null ? $"No PCR kit '{run.KitName.Trim()}' on an active instrument of this agent." : "The PCR kit is inactive.";
            logs.Append([new AgentLogRecord(agent.Id, now, AgentLogLevel.Error, $"Run file {fileName} failed: {reason}", null, null)], now);
            response = new AgentPcrRunResponse(nameof(RunFileDisposition.Failed), kit is null ? IntegrationCodes.KitNotFound : IntegrationCodes.KitInactive, reason, []);
        }
        else
        {
            var analyser = hosted.First(candidate => candidate.Id == kit.AnalyserId);
            var interpretation = kit.Interpret(run.InstrumentSerial, run.Content);
            response = interpretation.Readable
                ? await LandAsync(agent.Id, analyser, kit, interpretation, fileName, now, cancellationToken)
                : Refused(agent.Id, analyser.Code, interpretation, fileName, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    private AgentPcrRunResponse Refused(Guid agentId, string analyserCode, PcrRunInterpretation interpretation, string fileName, DateTimeOffset now)
    {
        logs.Append([new AgentLogRecord(agentId, now, AgentLogLevel.Error, $"Run file {fileName} failed ({interpretation.RefusalCode}): {interpretation.RefusalReason}", null, analyserCode)], now);
        return new AgentPcrRunResponse(nameof(RunFileDisposition.Failed), interpretation.RefusalCode, interpretation.RefusalReason, []);
    }

    private async Task<AgentPcrRunResponse> LandAsync(Guid agentId, Analyser analyser, PcrKit kit, PcrRunInterpretation interpretation, string fileName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var byCode = await mappings.FindByCodesAsync(analyser.Id, [kit.AnalyserTestCode], cancellationToken);
        byCode.TryGetValue(kit.AnalyserTestCode, out var mapping);
        var known = await samples.FindByNumbersAsync(interpretation.Samples.Select(sample => sample.SampleNumber).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

        var outcomes = new List<AgentPcrSampleOutcome>(interpretation.Samples.Count);
        foreach (var verdict in interpretation.Samples)
        {
            var intakeId = Guid.CreateVersion7();
            known.TryGetValue(verdict.SampleNumber, out var sample);
            var trail = verdict.Trail;
            var outcome = verdict.VerdictText is null
                ? new AnalyserResultOutcome(ResultIntakeStatus.Refused, null, [], verdict.Problem)
                : sample is null
                    ? new AnalyserResultOutcome(ResultIntakeStatus.UnknownSample, verdict.VerdictText, [], "No sample carries this number.")
                    : SubmitAgentResultsHandler.IsUnusable(sample)
                        ? new AnalyserResultOutcome(ResultIntakeStatus.Refused, verdict.VerdictText, [], $"The sample is {sample.Status}.")
                        : mapping is null
                            ? new AnalyserResultOutcome(ResultIntakeStatus.Unmapped, verdict.VerdictText, [], $"The kit's analyser code '{kit.AnalyserTestCode}' is not mapped.")
                            : analyser.AcceptResult(mapping, new IncomingAnalyserResult(verdict.SampleNumber, sample.SampleId, kit.AnalyserTestCode, verdict.VerdictText, trail, null, now), intakeId, ResultSources.Pcr, now);

            intake.Append(new ResultIntakeRecord(
                intakeId,
                agentId,
                analyser.Id,
                analyser.Code,
                verdict.SampleNumber,
                kit.AnalyserTestCode,
                mapping?.TestId,
                mapping?.Id,
                verdict.VerdictText ?? string.Empty,
                outcome.ConvertedValue,
                trail,
                outcome.Status,
                outcome.Reason,
                ResultSources.Pcr,
                fileName,
                now,
                now));
            outcomes.Add(new AgentPcrSampleOutcome(verdict.SampleNumber, verdict.Verdict?.ToString(), verdict.VerdictText, outcome.Status.ToString(), outcome.Reason));
        }

        var accepted = outcomes.Count(outcome => outcome.Status == nameof(ResultIntakeStatus.Accepted));
        logs.Append([new AgentLogRecord(agentId, now, AgentLogLevel.Info, $"Run file {fileName} read with kit {kit.Name}: {outcomes.Count} sample(s), {accepted} accepted; moved to backup.", null, analyser.Code)], now);
        return new AgentPcrRunResponse(nameof(RunFileDisposition.Backup), null, null, outcomes);
    }
}
