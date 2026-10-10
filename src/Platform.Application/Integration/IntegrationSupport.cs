using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration;

/// <summary>The permission keys a site agent's credential carries (intake §4, A11).</summary>
public static class AgentScopes
{
    public static readonly IReadOnlyList<string> All =
    [
        PermissionCatalog.Key(PermissionCatalog.Integration, PermissionAction.Create),
        PermissionCatalog.Key(PermissionCatalog.Integration, PermissionAction.View),
    ];
}

/// <summary>INV-INT-010: the agent API serves only an active site agent; the caller is recognised by its session's (service) user.</summary>
public static class AgentContext
{
    public static async Task<SiteAgent> RequireAsync(ISiteAgentRepository agents, ICurrentUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated caller is required.", DomainFault.Unauthenticated);
        var agent = await agents.FindByServiceUserAsync(userId, cancellationToken);
        return agent is { IsActive: true }
            ? agent
            : throw new DomainException(IntegrationCodes.CallerIsNotAgent, "Only an active site agent may call the agent API.", DomainFault.Refused);
    }
}

/// <summary>Coded checks of the ids other modules own (fail closed: an unknown id, or a table that does not exist yet, is refused).</summary>
public static class IntegrationReferences
{
    public static async Task RequireAsync(IIntegrationReferenceLookup lookup, ReferenceKind kind, Guid? id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (id is not { } value || value == Guid.Empty)
        {
            return;
        }

        var found = await lookup.FindAsync(kind, [value], cancellationToken);
        if (!found.ContainsKey(value))
        {
            var (code, what) = kind switch
            {
                ReferenceKind.ClinicalGroup => (IntegrationCodes.ClinicalGroupUnknown, "clinical group"),
                ReferenceKind.SampleType => (IntegrationCodes.SampleTypeUnknown, "sample type"),
                ReferenceKind.Test => (IntegrationCodes.TestUnknown, "test"),
                ReferenceKind.Unit => (IntegrationCodes.UnitUnknown, "unit"),
                _ => (IntegrationCodes.BranchUnknown, "branch"),
            };
            throw new DomainException(code, $"The {what} does not exist in this laboratory.", DomainFault.Validation);
        }
    }

    public static async Task<IReadOnlyDictionary<Guid, ReferenceName>> NamesAsync(IIntegrationReferenceLookup lookup, ReferenceKind kind, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var wanted = ids.Where(id => id is { } value && value != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        return wanted.Count == 0 ? new Dictionary<Guid, ReferenceName>() : await lookup.FindAsync(kind, wanted, cancellationToken);
    }
}

/// <summary>Enum names on the wire, parsed with a coded refusal (validators reject unknown names first).</summary>
public static class IntegrationNames
{
    public static TEnum Parse<TEnum>(string? value, string code)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException(code, $"'{value}' is not a valid {typeof(TEnum).Name}.", DomainFault.Validation);

    public static bool IsName<TEnum>(string? value)
        where TEnum : struct, Enum =>
        value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _);
}

/// <summary>DTO ↔ domain translations shared by commands and queries.</summary>
public static class IntegrationMapping
{
    public static AnalyserDefinition ToDefinition(SaveAnalyserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = IntegrationNames.Parse<AnalyserMode>(request.Mode, IntegrationCodes.ModeConnectionMismatch);
        var connection = ToConnection(request.Connection);
        var flags = (request.Flags ?? []).Select(flag => new AnalyserFlagSpec(flag.Code, IntegrationNames.Parse<FlagAction>(flag.Action, IntegrationCodes.FlagsInvalid), flag.Description)).ToList();
        return new AnalyserDefinition(
            new LocalizedText(request.NameEn, request.NameAr),
            request.ClinicalGroupId,
            request.DefaultSampleTypeId,
            request.AnalyserModelId,
            mode,
            connection,
            new AnalyserSwitches(request.AutoPrint, request.DownloadOrders, request.AppendUnregisteredTests, request.AppendUnregisteredSamples, request.OverwriteReviewed, request.OverwriteVerified),
            request.PrintCopies,
            flags,
            request.SiteAgentId);
    }

    public static AnalyserConnection ToConnection(AnalyserConnectionDto connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var kind = IntegrationNames.Parse<ConnectionKind>(connection.Kind, IntegrationCodes.ModeConnectionMismatch);
        TcpRole? role = string.IsNullOrWhiteSpace(connection.TcpRole) ? null : IntegrationNames.Parse<TcpRole>(connection.TcpRole, IntegrationCodes.TcpEndpointInvalid);
        return AnalyserConnection.From(kind, connection.SerialSettings, connection.SerialPort, connection.Host, connection.Port, role, connection.FolderPath);
    }

    public static AnalyserConnectionDto ToDto(AnalyserConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new AnalyserConnectionDto(connection.Kind.ToString(), connection.SerialSettings, connection.SerialPort, connection.Host, connection.Port, connection.TcpRole?.ToString(), connection.FolderPath);
    }

    public static string Summary(AnalyserConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.Kind switch
        {
            ConnectionKind.Serial => $"Serial {connection.SerialPort} {connection.SerialSettings}",
            ConnectionKind.Tcp => $"TCP {connection.TcpRole} {connection.Host}:{connection.Port}",
            _ => $"Folder {connection.FolderPath}",
        };
    }

    public static IReadOnlyList<AnalyserFlagDto> ToDto(IEnumerable<AnalyserFlag> flags) =>
        flags.OrderBy(flag => flag.Code, StringComparer.OrdinalIgnoreCase).Select(flag => new AnalyserFlagDto(flag.Code, flag.Action.ToString(), flag.Description)).ToList();

    public static MappingDefinition ToDefinition(SaveMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new MappingDefinition(
            request.TestId,
            request.AnalyserCode,
            request.HostCode,
            request.ResultSelection,
            request.Slope,
            request.Intercept,
            request.UnitId,
            request.Query,
            request.QueryDetails,
            request.AutoSend,
            (request.Conversions ?? []).Select(conversion => new ConversionSpec(conversion.FromValue, conversion.ToValue)).ToList(),
            request.RangeOverride is null ? null : new RangeOverride(request.RangeOverride.Low, request.RangeOverride.High, request.RangeOverride.Text),
            request.Methodology);
    }

    public static PcrKitDefinition ToDefinition(SavePcrKitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Layout);
        ArgumentNullException.ThrowIfNull(request.Rules);
        var layout = PcrRunFileLayout.Create(
            request.Layout.Delimiter,
            request.Layout.SampleColumn,
            request.Layout.GeneColumn,
            request.Layout.WellColumn,
            request.Layout.ContentColumn,
            request.Layout.PatientContentValue,
            request.Layout.SerialLabel,
            request.Layout.NoAmplificationTokens);
        var rules = PcrVerdictRules.Create(request.Rules.DetectedMinPositiveTargets, request.Rules.DetectedText, request.Rules.NotDetectedText, request.Rules.PresumptiveText, request.Rules.InvalidText);
        return new PcrKitDefinition(
            request.Name,
            request.AnalyserTestCode,
            request.SerialLock,
            layout,
            request.ValueColumn,
            (request.Genes ?? []).Select(gene => new PcrGeneSpec(gene.Name, gene.IsInternalControl, gene.PositiveCtMax, gene.BorderlineCtMax)).ToList(),
            rules,
            request.ResultFolder,
            request.BackupFolder,
            request.FailedFolder);
    }

    public static PcrRunFileLayoutDto ToDto(PcrRunFileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new PcrRunFileLayoutDto(layout.Delimiter, layout.SampleColumn, layout.GeneColumn, layout.WellColumn, layout.ContentColumn, layout.PatientContentValue, layout.SerialLabel, layout.NoAmplificationTokens);
    }

    public static PcrKitDetail ToDetail(PcrKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return new PcrKitDetail(
            kit.Id,
            kit.Name,
            kit.AnalyserId,
            kit.AnalyserTestCode,
            kit.SerialLock,
            ToDto(kit.Layout),
            kit.ValueColumn,
            kit.Genes.OrderBy(gene => gene.Order).Select(gene => new PcrGeneDto(gene.Name, gene.IsInternalControl, gene.PositiveCtMax, gene.BorderlineCtMax)).ToList(),
            new PcrVerdictRulesDto(kit.Rules.DetectedMinPositiveTargets, kit.Rules.DetectedText, kit.Rules.NotDetectedText, kit.Rules.PresumptiveText, kit.Rules.InvalidText),
            kit.ResultFolder,
            kit.BackupFolder,
            kit.FailedFolder,
            kit.IsActive);
    }

    public static PcrRunView ToView(PcrRunInterpretation run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new PcrRunView(
            run.Readable,
            run.Disposition.ToString(),
            run.RefusalCode,
            run.RefusalReason,
            run.FileSerial,
            run.Samples.Select(sample => new PcrSampleView(
                sample.SampleNumber,
                sample.Well,
                sample.Genes.Select(gene => new PcrGeneReadingView(gene.Gene, gene.Ct, gene.Call.ToString(), gene.IsInternalControl)).ToList(),
                sample.Verdict?.ToString(),
                sample.VerdictText,
                sample.Problem)).ToList());
    }

    /// <summary>Unknown when the last report is older than the offline threshold (the agent no longer vouches for it).</summary>
    public static string FreshLinkStatus(AnalyserLinkState? state, DateTimeOffset nowUtc) =>
        state?.LastSeenAtUtc is { } seen && nowUtc - seen <= SiteAgent.OfflineAfter ? state.Status.ToString() : nameof(AnalyserLinkStatus.Unknown);
}
