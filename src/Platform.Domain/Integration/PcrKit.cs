using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>One gene of a kit's interpretation sheet (LDM manual §14.5): Ct ≤ <see cref="PositiveCtMax"/> is positive, up to <see cref="BorderlineCtMax"/> borderline.</summary>
public sealed record PcrGeneSpec(string Name, bool IsInternalControl, decimal PositiveCtMax, decimal BorderlineCtMax);

/// <summary>The editable data of a kit (everything but the instrument).</summary>
public sealed record PcrKitDefinition(
    string Name,
    string AnalyserTestCode,
    string? SerialLock,
    PcrRunFileLayout Layout,
    string ValueColumn,
    IReadOnlyList<PcrGeneSpec> Genes,
    PcrVerdictRules Rules,
    string ResultFolder,
    string BackupFolder,
    string FailedFolder);

/// <summary>
/// The interpretation of one kit on one instrument (SRS CP-62; LDM §10, manual §14): the instrument (an analyser) and
/// its serial lock (files from any other instrument are refused, INV-INT-009), the run-file layout and the value column,
/// the genes with their thresholds, the verdict rules, the analyser test code whose mapping lands the verdict (its Auto
/// Send and conversion table apply, intake A4) and the folders the agent uses (in-tray, backup, failed). LDM needed one
/// Windows service per machine and kit; here a kit is configuration the one site agent reads.
/// </summary>
public sealed class PcrKit : AggregateRoot, ITenantScoped
{
    public const int MaxNameLength = 100;
    public const int MaxSerialLength = 64;
    public const int MaxColumnLength = 64;
    public const int MaxFolderLength = 260;
    public const int MaxGenes = 12;
    public const int MaxGeneNameLength = 64;
    public const decimal MaxCt = 60m;

    private readonly List<PcrGene> _genes = [];

    private PcrKit()
    {
        Name = string.Empty;
        AnalyserTestCode = string.Empty;
        Layout = null!;
        ValueColumn = string.Empty;
        Rules = null!;
        ResultFolder = string.Empty;
        BackupFolder = string.Empty;
        FailedFolder = string.Empty;
    }

    private PcrKit(Guid id, Guid analyserId)
        : base(id)
    {
        Name = string.Empty;
        AnalyserTestCode = string.Empty;
        Layout = null!;
        ValueColumn = string.Empty;
        Rules = null!;
        ResultFolder = string.Empty;
        BackupFolder = string.Empty;
        FailedFolder = string.Empty;
        AnalyserId = analyserId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant; the agent names the kit of a run file by it (LDM's <c>&lt;machine&gt;_&lt;kit&gt;</c>).</summary>
    public string Name { get; private set; }

    /// <summary>The instrument (an analyser of the tenant).</summary>
    public Guid AnalyserId { get; private set; }

    /// <summary>The instrument's mapping code the verdict lands through (its test, Auto Send and conversions).</summary>
    public string AnalyserTestCode { get; private set; }

    /// <summary>The instrument serial number every run file must carry (LDM "Parameter"); <see langword="null"/> = no lock.</summary>
    public string? SerialLock { get; private set; }

    public PcrRunFileLayout Layout { get; private set; }

    /// <summary>The Ct column (BioRad <c>Cq</c>, QuantStudio <c>CT</c>).</summary>
    public string ValueColumn { get; private set; }

    public IReadOnlyList<PcrGene> Genes => _genes;

    public PcrVerdictRules Rules { get; private set; }

    /// <summary>The in-tray the instrument exports to.</summary>
    public string ResultFolder { get; private set; }

    /// <summary>Where the agent moves a file the cloud read.</summary>
    public string BackupFolder { get; private set; }

    /// <summary>Where the agent moves a file the cloud refused.</summary>
    public string FailedFolder { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>K1 Define (<c>integration.edit</c>) on an analyser of the tenant; name uniqueness is the handler's check.</summary>
    public static PcrKit Define(Analyser analyser, PcrKitDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(definition);
        var kit = new PcrKit(Guid.CreateVersion7(), analyser.Id);
        kit.Apply(definition);
        kit.Raise(new PcrKitDefined(kit.Id, kit.Name, analyser.Id, nowUtc));
        return kit;
    }

    /// <summary>K2 Update (<c>integration.edit</c>): every field, the instrument included.</summary>
    public void Update(Analyser analyser, PcrKitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(definition);
        AnalyserId = analyser.Id;
        Apply(definition);
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new PcrKitDeactivated(Id, Name, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new PcrKitReactivated(Id, Name, nowUtc));
    }

    /// <summary>Reads a run file with this kit (pure; used by the agent API and by the simulation).</summary>
    public PcrRunInterpretation Interpret(string? declaredSerial, string content) => PcrInterpreter.Interpret(this, declaredSerial, content);

    public static string CleanName(string name) =>
        IntegrationGuard.Required(name, MaxNameLength, IntegrationCodes.KitNameInvalid, "The kit name");

    private void Apply(PcrKitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition.Layout);
        ArgumentNullException.ThrowIfNull(definition.Rules);
        var name = CleanName(definition.Name);
        var testCode = AnalyserTestMapping.CleanAnalyserCode(definition.AnalyserTestCode);
        var serial = IntegrationGuard.Optional(definition.SerialLock, MaxSerialLength, IntegrationCodes.SerialLockInvalid, "The serial lock");
        var valueColumn = IntegrationGuard.Required(definition.ValueColumn, MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The value column");
        var genes = CleanGenes(definition.Genes ?? []);
        var result = Folder(definition.ResultFolder, "The result folder");
        var backup = Folder(definition.BackupFolder, "The backup folder");
        var failed = Folder(definition.FailedFolder, "The failed folder");
        Require(
            !string.Equals(result, backup, StringComparison.OrdinalIgnoreCase) && !string.Equals(result, failed, StringComparison.OrdinalIgnoreCase) && !string.Equals(backup, failed, StringComparison.OrdinalIgnoreCase),
            IntegrationCodes.FoldersInvalid,
            "The result, backup and failed folders must be three different folders.",
            DomainFault.Validation);

        Name = name;
        AnalyserTestCode = testCode;
        SerialLock = serial;
        Layout = definition.Layout;
        ValueColumn = valueColumn;
        Rules = definition.Rules;
        ResultFolder = result;
        BackupFolder = backup;
        FailedFolder = failed;
        _genes.Clear();
        for (var index = 0; index < genes.Count; index++)
        {
            _genes.Add(new PcrGene(index + 1, genes[index]));
        }
    }

    private static string Folder(string? folder, string field) =>
        IntegrationGuard.Required(folder, MaxFolderLength, IntegrationCodes.FoldersInvalid, field);

    private static List<PcrGeneSpec> CleanGenes(IReadOnlyList<PcrGeneSpec> genes)
    {
        Require(genes.Count is >= 1 and <= MaxGenes, IntegrationCodes.GenesInvalid, $"A kit has 1 to {MaxGenes} genes.", DomainFault.Validation);
        var cleaned = new List<PcrGeneSpec>(genes.Count);
        foreach (var gene in genes)
        {
            Require(gene is not null, IntegrationCodes.GenesInvalid, "A gene is required.", DomainFault.Validation);
            var name = IntegrationGuard.Required(gene!.Name, MaxGeneNameLength, IntegrationCodes.GenesInvalid, "A gene name");
            Require(!cleaned.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)), IntegrationCodes.GenesInvalid, $"The gene '{name}' appears twice.", DomainFault.Validation);
            Require(
                gene.PositiveCtMax > 0m && gene.PositiveCtMax <= gene.BorderlineCtMax && gene.BorderlineCtMax <= MaxCt,
                IntegrationCodes.GenesInvalid,
                $"For '{name}' the thresholds must satisfy 0 < positive ≤ borderline ≤ {MaxCt}.",
                DomainFault.Validation);
            cleaned.Add(new PcrGeneSpec(name, gene.IsInternalControl, gene.PositiveCtMax, gene.BorderlineCtMax));
        }

        Require(cleaned.Any(gene => !gene.IsInternalControl), IntegrationCodes.GenesInvalid, "A kit needs at least one target gene.", DomainFault.Validation);
        return cleaned;
    }
}

/// <summary>One gene row of a kit (owned row of <c>app.pcr_kit_genes</c>).</summary>
public sealed class PcrGene : Entity
{
    private PcrGene()
    {
        Name = string.Empty;
    }

    internal PcrGene(int order, PcrGeneSpec spec)
        : base(Guid.CreateVersion7())
    {
        Order = order;
        Name = spec.Name;
        IsInternalControl = spec.IsInternalControl;
        PositiveCtMax = spec.PositiveCtMax;
        BorderlineCtMax = spec.BorderlineCtMax;
    }

    public int Order { get; private set; }

    public string Name { get; private set; }

    public bool IsInternalControl { get; private set; }

    public decimal PositiveCtMax { get; private set; }

    public decimal BorderlineCtMax { get; private set; }

    /// <summary>LDM manual §14.5: no value (or 0) or above the borderline bound is negative; a low Ct is positive.</summary>
    public PcrGeneCall Call(decimal? ct) => ct switch
    {
        null => PcrGeneCall.Negative,
        <= 0m => PcrGeneCall.Negative,
        var value when value <= PositiveCtMax => PcrGeneCall.Positive,
        var value when value <= BorderlineCtMax => PcrGeneCall.Borderline,
        _ => PcrGeneCall.Negative,
    };
}

/// <summary>
/// How to read a run file the agent sends as delimited text (intake A10; LDM manual §14.3–§14.4): the delimiter, the
/// sample, gene, well and content columns, the content value of patient rows (controls are skipped), the label of the
/// instrument-serial line above the header, and the tokens that mean "no amplification".
/// </summary>
public sealed class PcrRunFileLayout : ValueObject
{
    public const int MaxTokens = 10;
    public const int MaxTokenLength = 20;
    public const int MaxLabelLength = 100;
    private static readonly string[] AllowedDelimiters = [",", ";", "\t", "|"];

    private PcrRunFileLayout()
    {
        Delimiter = ",";
        SampleColumn = string.Empty;
        GeneColumn = string.Empty;
        NoAmplificationTokens = [];
    }

    public string Delimiter { get; private set; }

    public string SampleColumn { get; private set; }

    public string GeneColumn { get; private set; }

    public string? WellColumn { get; private set; }

    public string? ContentColumn { get; private set; }

    /// <summary>With <see cref="ContentColumn"/>: only rows carrying this value are patients (BioRad <c>Unkn</c>).</summary>
    public string? PatientContentValue { get; private set; }

    /// <summary>The label of the serial line (BioRad <c>Optical Head Serial Number</c>, QuantStudio <c>Instrument Serial Number</c>).</summary>
    public string? SerialLabel { get; private set; }

    /// <summary>Values meaning no amplification besides blank and 0 (<c>N/A</c>, <c>Undetermined</c>).</summary>
    public string[] NoAmplificationTokens { get; private set; }

    public char DelimiterChar => Delimiter[0];

    public static PcrRunFileLayout Create(
        string? delimiter,
        string sampleColumn,
        string geneColumn,
        string? wellColumn,
        string? contentColumn,
        string? patientContentValue,
        string? serialLabel,
        IEnumerable<string>? noAmplificationTokens)
    {
        var cleanedDelimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter == "\\t" ? "\t" : delimiter;
        IntegrationGuard.Require(AllowedDelimiters.Contains(cleanedDelimiter, StringComparer.Ordinal), IntegrationCodes.RunFileLayoutInvalid, "The delimiter is one of , ; | or a tab.");
        var content = IntegrationGuard.Optional(contentColumn, PcrKit.MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The content column");
        var patient = IntegrationGuard.Optional(patientContentValue, PcrKit.MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The patient content value");
        IntegrationGuard.Require((content is null) == (patient is null), IntegrationCodes.RunFileLayoutInvalid, "The content column and the patient content value go together.");
        var tokens = (noAmplificationTokens ?? []).Select(token => (token ?? string.Empty).Trim()).Where(token => token.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IntegrationGuard.Require(tokens.Length <= MaxTokens && tokens.All(token => token.Length <= MaxTokenLength), IntegrationCodes.RunFileLayoutInvalid, $"At most {MaxTokens} no-amplification tokens of {MaxTokenLength} characters.");

        return new PcrRunFileLayout
        {
            Delimiter = cleanedDelimiter,
            SampleColumn = IntegrationGuard.Required(sampleColumn, PcrKit.MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The sample column"),
            GeneColumn = IntegrationGuard.Required(geneColumn, PcrKit.MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The gene column"),
            WellColumn = IntegrationGuard.Optional(wellColumn, PcrKit.MaxColumnLength, IntegrationCodes.RunFileLayoutInvalid, "The well column"),
            ContentColumn = content,
            PatientContentValue = patient,
            SerialLabel = IntegrationGuard.Optional(serialLabel, MaxLabelLength, IntegrationCodes.RunFileLayoutInvalid, "The serial label"),
            NoAmplificationTokens = tokens,
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Delimiter;
        yield return SampleColumn;
        yield return GeneColumn;
        yield return WellColumn;
        yield return ContentColumn;
        yield return PatientContentValue;
        yield return SerialLabel;
        yield return string.Join('\u001f', NoAmplificationTokens);
    }
}

/// <summary>
/// The combination rules of a kit (LDM manual §14.6, intake A5): at least <see cref="DetectedMinPositiveTargets"/> positive
/// targets → DETECTED; fewer, or any borderline target → Presumptive; no target signal with the internal control present →
/// NOT DETECTED; nothing at all, the internal control included → INVALID. The texts are what lands as the result value.
/// </summary>
public sealed class PcrVerdictRules : ValueObject
{
    public const int MaxTextLength = 100;
    public const int MaxTargets = 10;

    private PcrVerdictRules()
    {
        DetectedText = string.Empty;
        NotDetectedText = string.Empty;
        PresumptiveText = string.Empty;
        InvalidText = string.Empty;
    }

    public int DetectedMinPositiveTargets { get; private set; }

    public string DetectedText { get; private set; }

    public string NotDetectedText { get; private set; }

    public string PresumptiveText { get; private set; }

    public string InvalidText { get; private set; }

    public static PcrVerdictRules Default => Create(1, "DETECTED", "NOT DETECTED", "Presumptive positive", "INVALID");

    public static PcrVerdictRules Create(int detectedMinPositiveTargets, string detectedText, string notDetectedText, string presumptiveText, string invalidText)
    {
        IntegrationGuard.Require(detectedMinPositiveTargets is >= 1 and <= MaxTargets, IntegrationCodes.VerdictRulesInvalid, $"The minimum positive targets for DETECTED is 1 to {MaxTargets}.");
        return new PcrVerdictRules
        {
            DetectedMinPositiveTargets = detectedMinPositiveTargets,
            DetectedText = IntegrationGuard.Required(detectedText, MaxTextLength, IntegrationCodes.VerdictRulesInvalid, "The DETECTED text"),
            NotDetectedText = IntegrationGuard.Required(notDetectedText, MaxTextLength, IntegrationCodes.VerdictRulesInvalid, "The NOT DETECTED text"),
            PresumptiveText = IntegrationGuard.Required(presumptiveText, MaxTextLength, IntegrationCodes.VerdictRulesInvalid, "The presumptive text"),
            InvalidText = IntegrationGuard.Required(invalidText, MaxTextLength, IntegrationCodes.VerdictRulesInvalid, "The INVALID text"),
        };
    }

    public string TextFor(PcrVerdict verdict) => verdict switch
    {
        PcrVerdict.Detected => DetectedText,
        PcrVerdict.NotDetected => NotDetectedText,
        PcrVerdict.Presumptive => PresumptiveText,
        _ => InvalidText,
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DetectedMinPositiveTargets;
        yield return DetectedText;
        yield return NotDetectedText;
        yield return PresumptiveText;
        yield return InvalidText;
    }
}
