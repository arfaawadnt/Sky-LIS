using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>The result kinds of a test (SRS CP-41; LDM §3.2–§3.7).</summary>
public enum LabTestType
{
    /// <summary>A number flagged against range bands (LDM "Age Dependant").</summary>
    Numeric = 0,

    /// <summary>Free text with a printed reference text (LDM "Non_numeric Test").</summary>
    NonNumeric = 1,

    /// <summary>A value picked from a list, free text allowed.</summary>
    OptionalList = 2,

    /// <summary>A value that must come from the list.</summary>
    RestrictedList = 3,

    /// <summary>Computed from other tests; never typed (LDM §3.3).</summary>
    Formula = 4,

    /// <summary>A paragraph report (histology, cytology; LDM §3.7).</summary>
    FormattedText = 5,

    /// <summary>Organisms and sensitivities (LDM §3.6); entered on the culture screen.</summary>
    Culture = 6,
}

public enum FrequencyCheckAction
{
    /// <summary>A repeat order inside the interval shows a warning (BR-12).</summary>
    Alert = 0,

    /// <summary>A repeat order inside the interval is refused (BR-12).</summary>
    Prevent = 1,
}

/// <summary>The editable general data of a test (everything but the immutable code and the per-type configuration).</summary>
public sealed record LabTestDefinition(
    LocalizedText Name,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    LabTestType Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    GenderRestriction Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix);

/// <summary>Turnaround and repeat controls of a test (LDM §3.4; BR-11, BR-12, BR-13).</summary>
/// <param name="TestDays">Weekdays the test runs: bit 0 = Sunday … bit 6 = Saturday (<see cref="DayOfWeek"/> order); 127 = every day.</param>
public sealed record LabTestTiming(int DurationHours, int MinDurationHours, int? FrequencyCheckDays, FrequencyCheckAction FrequencyAction, decimal? DeltaPercent, int? DeltaWindowDays, int TestDays)
{
    public const int EveryDay = 127;

    public static LabTestTiming Default { get; } = new(24, 0, null, FrequencyCheckAction.Alert, null, null, EveryDay);

    /// <summary>True when the test runs on <paramref name="day"/> (BR-13: other days do not count toward the duration).</summary>
    public bool RunsOn(DayOfWeek day) => (TestDays & (1 << (int)day)) != 0;
}

/// <summary>
/// A measured parameter and its configuration per type (SRS CP-41): range bands with normal, panic and reportable
/// limits by sex, age and patient condition (BR-08), list values (BR-19), a formula over other tests, paragraphs of
/// a text report, and the timing that drives turnaround (BR-13), frequency (BR-12) and delta (BR-11) checks. A hidden
/// helper never prints (BR-20). Each configuration exists only for the types that use it (INV-CAT-006).
/// </summary>
public sealed class LabTest : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MaxReportNameLength = 200;
    public const int MaxReferenceCodeLength = 20;
    public const int MaxMethodologyLength = 200;
    public const int MaxPrefixLength = 10;
    public const int MaxDecimals = 6;
    public const int MaxLabelCount = 20;
    public const int MaxFormulaLength = 1000;
    public const int MaxDurationHours = 8760;
    public const int MaxIntervalDays = 3650;
    public const decimal MaxDeltaPercent = 1000m;
    public const int MaxRangeBands = 100;
    public const int MaxListValues = 100;
    public const int MaxParagraphs = 50;

    private readonly List<RangeBand> _rangeBands = [];
    private readonly List<ListValue> _listValues = [];
    private readonly List<TestParagraph> _paragraphs = [];

    private LabTest()
    {
        Code = string.Empty;
        Name = null!;
        FormulaReferences = [];
        FormulaOperands = [];
    }

    private LabTest(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
        FormulaReferences = [];
        FormulaOperands = [];
        IsActive = true;
        ApplyTiming(LabTestTiming.Default);
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant (INV-CAT-001), immutable; formulas refer to it.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public string? ReportName { get; private set; }

    public string? Loinc { get; private set; }

    public string? Cpt { get; private set; }

    public Guid ClinicalGroupId { get; private set; }

    public LabTestType Type { get; private set; }

    public Guid? SampleTypeId { get; private set; }

    public Guid? SampleConditionId { get; private set; }

    public Guid? UnitId { get; private set; }

    public GenderRestriction Gender { get; private set; }

    public string? Methodology { get; private set; }

    /// <summary>Result format (LDM §3.4): decimals shown for numeric and formula results.</summary>
    public int Decimals { get; private set; }

    /// <summary>BR-20: a helper feeding formulas or sums; never printed.</summary>
    public bool IsHiddenHelper { get; private set; }

    /// <summary>Print the reference range on the report.</summary>
    public bool PrintReference { get; private set; }

    public int LabelCount { get; private set; }

    /// <summary>Label/report prefix (LDM §3.4, e.g. <c>S.</c>).</summary>
    public string? Prefix { get; private set; }

    public int DurationHours { get; private set; }

    public int MinDurationHours { get; private set; }

    public int? FrequencyCheckDays { get; private set; }

    public FrequencyCheckAction FrequencyAction { get; private set; }

    public decimal? DeltaPercent { get; private set; }

    public int? DeltaWindowDays { get; private set; }

    public int TestDays { get; private set; }

    /// <summary>The normalised expression (references written <c>[CODE]</c>), or null.</summary>
    public string? FormulaExpression { get; private set; }

    /// <summary>The test codes the formula reads, upper-case, distinct (cycle checks and lookups).</summary>
    public string[] FormulaReferences { get; private set; }

    /// <summary>
    /// What registration orders for each referenced test (LIS-CR-015): one entry per reference in reference order, or empty for a
    /// formula saved before the change (readers then derive the default service per reference, off the receipt).
    /// </summary>
    public IReadOnlyList<FormulaOperand> FormulaOperands { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Incremented by every configuration change, so the root row carries the concurrency check and the modification stamp.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<RangeBand> RangeBands => _rangeBands.AsReadOnly();

    public IReadOnlyList<ListValue> ListValues => _listValues.AsReadOnly();

    public IReadOnlyList<TestParagraph> Paragraphs => _paragraphs.AsReadOnly();

    public LabTestTiming Timing => new(DurationHours, MinDurationHours, FrequencyCheckDays, FrequencyAction, DeltaPercent, DeltaWindowDays, TestDays);

    public static bool AllowsRangeBands(LabTestType type) => type is LabTestType.Numeric or LabTestType.Formula or LabTestType.NonNumeric;

    public static bool AllowsListValues(LabTestType type) => type is LabTestType.OptionalList or LabTestType.RestrictedList;

    public static bool AllowsFormula(LabTestType type) => type == LabTestType.Formula;

    public static bool AllowsParagraphs(LabTestType type) => type == LabTestType.FormattedText;

    /// <summary>T13 Create (<c>catalogue.create</c>) with the default timing (24 h, every day).</summary>
    public static LabTest Create(string code, LabTestDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var test = new LabTest(Guid.CreateVersion7(), CleanCode(code));
        test.Apply(definition);
        test.Raise(new LabTestCreated(test.Id, test.Code, test.Type, nowUtc));
        return test;
    }

    /// <summary>T14 Edit (<c>catalogue.edit</c>); a type change is refused while configuration of the old type exists (CAT-071).</summary>
    public void Change(LabTestDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Type != Type)
        {
            var blocked = (_rangeBands.Count > 0 && !AllowsRangeBands(definition.Type))
                || (_rangeBands.Count > 0 && definition.Type == LabTestType.NonNumeric && _rangeBands.Any(band => band.HasLimits))
                || (_listValues.Count > 0 && !AllowsListValues(definition.Type))
                || (FormulaExpression is not null && !AllowsFormula(definition.Type))
                || (_paragraphs.Count > 0 && !AllowsParagraphs(definition.Type));
            if (blocked)
            {
                CatalogueAGuard.Refuse(LabTestCodes.TypeChangeBlocked, $"Remove the configuration of a {Type} test before changing it to {definition.Type}.", DomainFault.Conflict);
            }
        }

        Apply(definition);
        Raise(new LabTestChanged(Id, Code, Type, nowUtc));
    }

    /// <summary>T19 Set timing (<c>catalogue.edit</c>).</summary>
    public void SetTiming(LabTestTiming timing, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ApplyTiming(timing);
        Raise(new LabTestTimingChanged(Id, Code, nowUtc));
    }

    /// <summary>T15 Replace range bands (<c>catalogue.edit</c>): INV-CAT-006 and INV-CAT-007.</summary>
    public void ReplaceRangeBands(IReadOnlyList<RangeBandSpec> bands, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count > 0 && !AllowsRangeBands(Type))
        {
            CatalogueAGuard.Refuse(LabTestCodes.RangeBandsNotAllowed, $"A {Type} test has no range bands.");
        }

        if (bands.Count > MaxRangeBands)
        {
            CatalogueAGuard.Refuse(CatalogueACodes.NumberOutOfRange, $"A test has at most {MaxRangeBands} range bands.");
        }

        var staged = bands.Select((spec, index) => RangeBand.From(Id, spec, index + 1, Type)).ToList();
        RangeBand.EnsureNoOverlap(staged);

        _rangeBands.Clear();
        _rangeBands.AddRange(staged);
        Revision++;
        Raise(new LabTestRangesChanged(Id, Code, _rangeBands.Count, nowUtc));
    }

    /// <summary>T16 Replace list values (<c>catalogue.edit</c>): INV-CAT-008 (BR-19).</summary>
    public void ReplaceListValues(IReadOnlyList<ListValueSpec> values, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 0 && !AllowsListValues(Type))
        {
            CatalogueAGuard.Refuse(LabTestCodes.ListValuesNotAllowed, $"A {Type} test has no list values.");
        }

        if (values.Count > MaxListValues)
        {
            CatalogueAGuard.Refuse(CatalogueACodes.NumberOutOfRange, $"A test has at most {MaxListValues} list values.");
        }

        var staged = values.Select((spec, index) => ListValue.From(Id, spec, index + 1)).ToList();
        if (staged.Select(value => value.Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() != staged.Count)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ListValueInvalid, "List values are distinct (ignoring case).");
        }

        if (staged.Count(value => value.IsDefault) > 1)
        {
            CatalogueAGuard.Refuse(LabTestCodes.MultipleDefaults, "At most one list value is the default (BR-19).");
        }

        _listValues.Clear();
        _listValues.AddRange(staged);
        Revision++;
        Raise(new LabTestListValuesChanged(Id, Code, _listValues.Count, nowUtc));
    }

    /// <summary>
    /// T17 Set or clear the formula (<c>catalogue.edit</c>). The handler parses the expression with the formula
    /// evaluator and checks the references against the tenant's active tests and for cycles (CAT-063/064/066);
    /// the aggregate keeps the type rule and refuses a self-reference (CAT-065).
    /// </summary>
    public void SetFormula(string? normalizedExpression, IReadOnlyCollection<string> references, DateTimeOffset nowUtc) =>
        Store(normalizedExpression, references, operands: null, choices: [], nowUtc);

    /// <summary>
    /// T17 with the operands (LIS-CR-015): every reference gets exactly one operand — the given one, else the default service of
    /// <paramref name="choices"/> off the receipt; operands of codes no longer referenced are dropped; a chosen service must carry
    /// the test (CAT-067).
    /// </summary>
    public void SetFormula(
        string? normalizedExpression,
        IReadOnlyCollection<string> references,
        IReadOnlyCollection<FormulaOperand> operands,
        IReadOnlyCollection<FormulaOperandChoices> choices,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(operands);
        ArgumentNullException.ThrowIfNull(choices);
        Store(normalizedExpression, references, operands, choices, nowUtc);
    }

    private void Store(string? normalizedExpression, IReadOnlyCollection<string> references, IReadOnlyCollection<FormulaOperand>? operands, IReadOnlyCollection<FormulaOperandChoices> choices, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(references);
        var expression = string.IsNullOrWhiteSpace(normalizedExpression) ? null : normalizedExpression.Trim();
        if (expression is not null && !AllowsFormula(Type))
        {
            CatalogueAGuard.Refuse(LabTestCodes.FormulaNotAllowed, $"A {Type} test has no formula.");
        }

        if (expression is { Length: > MaxFormulaLength })
        {
            CatalogueAGuard.Refuse(LabTestCodes.FormulaInvalid, $"A formula holds at most {MaxFormulaLength} characters.");
        }

        var codes = expression is null ? [] : references.Select(code => code.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (codes.Contains(Code, StringComparer.Ordinal))
        {
            CatalogueAGuard.Refuse(LabTestCodes.FormulaSelfReference, "A formula cannot read its own test.");
        }

        var stored = expression is null || operands is null ? [] : FormulaOperandRules.Normalize(references, operands, choices);

        FormulaExpression = expression;
        FormulaReferences = codes;
        FormulaOperands = stored;
        Revision++;
        Raise(new LabTestFormulaChanged(Id, Code, expression, nowUtc));
    }

    /// <summary>T18 Replace paragraphs (<c>catalogue.edit</c>).</summary>
    public void ReplaceParagraphs(IReadOnlyList<ParagraphSpec> paragraphs, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (paragraphs.Count > 0 && !AllowsParagraphs(Type))
        {
            CatalogueAGuard.Refuse(LabTestCodes.ParagraphsNotAllowed, $"A {Type} test has no paragraphs.");
        }

        if (paragraphs.Count > MaxParagraphs)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ParagraphInvalid, $"A test has at most {MaxParagraphs} paragraphs.");
        }

        var staged = paragraphs.Select((spec, index) => TestParagraph.From(Id, spec, index + 1)).ToList();
        _paragraphs.Clear();
        _paragraphs.AddRange(staged);
        Revision++;
        Raise(new LabTestParagraphsChanged(Id, Code, _paragraphs.Count, nowUtc));
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new LabTestDeactivated(Id, Code, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new LabTestReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "test");

    private void Apply(LabTestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition.Name);
        if (!Enum.IsDefined(definition.Type) || !Enum.IsDefined(definition.Gender))
        {
            CatalogueAGuard.Refuse(CatalogueACodes.TextInvalid, "Unknown test type or gender restriction.");
        }

        if (definition.ClinicalGroupId == Guid.Empty)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ClinicalGroupRequired, "A test belongs to a clinical group.");
        }

        var sampleTypeId = definition.SampleTypeId == Guid.Empty ? null : definition.SampleTypeId;
        var conditionId = definition.SampleConditionId == Guid.Empty ? null : definition.SampleConditionId;
        if (conditionId is not null && sampleTypeId is null)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ConditionMismatch, "A sample condition needs its sample type.");
        }

        if (definition.Decimals < 0 || definition.Decimals > MaxDecimals)
        {
            CatalogueAGuard.Refuse(LabTestCodes.DecimalsOutOfRange, $"Decimals are between 0 and {MaxDecimals}.");
        }

        Name = definition.Name;
        ReportName = CatalogueAGuard.Optional(definition.ReportName, MaxReportNameLength, "The report name");
        Loinc = CatalogueAGuard.ReferenceCode(definition.Loinc, MaxReferenceCodeLength, "The LOINC code");
        Cpt = CatalogueAGuard.ReferenceCode(definition.Cpt, MaxReferenceCodeLength, "The CPT code");
        ClinicalGroupId = definition.ClinicalGroupId;
        Type = definition.Type;
        SampleTypeId = sampleTypeId;
        SampleConditionId = conditionId;
        UnitId = definition.UnitId == Guid.Empty ? null : definition.UnitId;
        Gender = definition.Gender;
        Methodology = CatalogueAGuard.Optional(definition.Methodology, MaxMethodologyLength, "The methodology");
        Decimals = definition.Decimals;
        IsHiddenHelper = definition.IsHiddenHelper;
        PrintReference = definition.PrintReference;
        LabelCount = CatalogueAGuard.Range(definition.LabelCount, 0, MaxLabelCount, "The number of labels");
        Prefix = CatalogueAGuard.Optional(definition.Prefix, MaxPrefixLength, "The prefix");
    }

    private void ApplyTiming(LabTestTiming timing)
    {
        var valid = (timing.DurationHours is >= 0 and <= MaxDurationHours)
            && timing.MinDurationHours >= 0 && timing.MinDurationHours <= timing.DurationHours
            && (timing.FrequencyCheckDays is null or (>= 1 and <= MaxIntervalDays))
            && Enum.IsDefined(timing.FrequencyAction)
            && (timing.DeltaPercent is null || (timing.DeltaPercent > 0 && timing.DeltaPercent <= MaxDeltaPercent))
            && (timing.DeltaWindowDays is null or (>= 1 and <= MaxIntervalDays))
            && (timing.TestDays is >= 1 and <= LabTestTiming.EveryDay);
        if (!valid)
        {
            CatalogueAGuard.Refuse(
                LabTestCodes.TimingInvalid,
                $"Timing: duration 0–{MaxDurationHours} h, minimum duration not above it, intervals 1–{MaxIntervalDays} days, delta 0–{MaxDeltaPercent} %, at least one test day.");
        }

        DurationHours = timing.DurationHours;
        MinDurationHours = timing.MinDurationHours;
        FrequencyCheckDays = timing.FrequencyCheckDays;
        FrequencyAction = timing.FrequencyAction;
        DeltaPercent = timing.DeltaPercent;
        DeltaWindowDays = timing.DeltaWindowDays;
        TestDays = timing.TestDays;
    }
}
