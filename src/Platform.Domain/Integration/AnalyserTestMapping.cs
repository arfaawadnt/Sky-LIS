using System.Globalization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Integration;

/// <summary>A row of the conversion table: a source value (exact text, or a comparison <c>&lt;10</c>, <c>&lt;=10</c>, <c>&gt;10</c>, <c>&gt;=10</c>) and the text it becomes.</summary>
public sealed record ConversionSpec(string FromValue, string ToValue);

/// <summary>The analyser-specific reference range (LDM manual §13.5): numeric bounds and/or a text range; overrides the test's range.</summary>
public sealed record RangeOverride(decimal? Low, decimal? High, string? Text);

/// <summary>The editable data of a mapping (everything but the analyser).</summary>
public sealed record MappingDefinition(
    Guid TestId,
    string AnalyserCode,
    string? HostCode,
    int ResultSelection,
    decimal Slope,
    decimal Intercept,
    Guid? UnitId,
    bool Query,
    string? QueryDetails,
    bool AutoSend,
    IReadOnlyList<ConversionSpec> Conversions,
    RangeOverride? RangeOverride,
    string? Methodology);

/// <summary>
/// Maps an instrument's code to a catalogue test (SRS CP-59; LDM §9 "Analyzers Tests", manual §13.4–§13.5): analyser
/// code (unique per analyser, INV-INT-003), host code expected in queries, result selection (which value when a message
/// carries several), slope and intercept (unit conversion), unit, query flags, the conversion table (numbers or codes to
/// text), the analyser-specific range and the methodology. <see cref="AutoSend"/> is the permission to land incoming
/// results: a mapping without it never lands one (INV-INT-004), and every switch raises <see cref="AnalyserAutoSendChanged"/>.
/// </summary>
public sealed class AnalyserTestMapping : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 32;
    public const int MaxConversions = 50;
    public const int MaxConversionValueLength = 100;
    public const int MaxTextLength = 500;
    public const int MaxRangeTextLength = 200;
    public const int MaxResultSelection = 99;
    public const int MaxValueLength = 500;

    private readonly List<ValueConversion> _conversions = [];

    private AnalyserTestMapping()
    {
        AnalyserCode = string.Empty;
    }

    private AnalyserTestMapping(Guid id, Guid analyserId)
        : base(id)
    {
        AnalyserId = analyserId;
        AnalyserCode = string.Empty;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public Guid AnalyserId { get; private set; }

    /// <summary>The catalogue test (app.tests) the code means.</summary>
    public Guid TestId { get; private set; }

    /// <summary>The code the instrument sends (case kept as the instrument writes it).</summary>
    public string AnalyserCode { get; private set; }

    /// <summary>The code the instrument expects in order queries, when it differs.</summary>
    public string? HostCode { get; private set; }

    public int ResultSelection { get; private set; }

    public decimal Slope { get; private set; }

    public decimal Intercept { get; private set; }

    public Guid? UnitId { get; private set; }

    public bool Query { get; private set; }

    /// <summary>Sub-value codes of a query (LDM "Query Details", e.g. PT → INR).</summary>
    public string? QueryDetails { get; private set; }

    public bool AutoSend { get; private set; }

    public IReadOnlyList<ValueConversion> Conversions => _conversions;

    public decimal? RangeLow { get; private set; }

    public decimal? RangeHigh { get; private set; }

    public string? RangeText { get; private set; }

    public string? Methodology { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The analyser-specific range, or <see langword="null"/> when the test's own range applies.</summary>
    public RangeOverride? RangeOverride => RangeLow is null && RangeHigh is null && RangeText is null ? null : new RangeOverride(RangeLow, RangeHigh, RangeText);

    /// <summary>T1 Map (<c>integration.edit</c>) on an active analyser (INT-030). Code uniqueness and the test are the handler's checks.</summary>
    public static AnalyserTestMapping Map(Analyser analyser, MappingDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(definition);
        Require(analyser.IsActive, IntegrationCodes.AnalyserInactive, "Tests cannot be mapped on an inactive analyser.", DomainFault.Conflict);

        var mapping = new AnalyserTestMapping(Guid.CreateVersion7(), analyser.Id);
        mapping.Apply(definition);
        mapping.Raise(new AnalyserTestMapped(mapping.Id, analyser.Id, mapping.TestId, mapping.AnalyserCode, mapping.AutoSend, nowUtc));
        return mapping;
    }

    /// <summary>T2 Update (<c>integration.edit</c>): every field; a changed Auto Send raises its event.</summary>
    public void Update(MappingDefinition definition, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var autoSendBefore = AutoSend;
        Apply(definition);
        if (AutoSend != autoSendBefore)
        {
            Raise(new AnalyserAutoSendChanged(Id, AnalyserId, TestId, AnalyserCode, AutoSend, nowUtc));
        }
    }

    /// <summary>T3 Switch Auto Send (<c>integration.edit</c>). Unchanged values raise nothing.</summary>
    public void SetAutoSend(bool enabled, DateTimeOffset nowUtc)
    {
        if (AutoSend == enabled)
        {
            return;
        }

        AutoSend = enabled;
        Raise(new AnalyserAutoSendChanged(Id, AnalyserId, TestId, AnalyserCode, enabled, nowUtc));
    }

    /// <summary>Replaces the conversion table (INV-INT-005: ≤ 50 rows, distinct source values).</summary>
    public void ReplaceConversions(IReadOnlyList<ConversionSpec> conversions)
    {
        var cleaned = CleanConversions(conversions);
        _conversions.Clear();
        for (var index = 0; index < cleaned.Count; index++)
        {
            _conversions.Add(new ValueConversion(index + 1, cleaned[index].FromValue, cleaned[index].ToValue));
        }
    }

    /// <summary>Sets or clears (<see langword="null"/>) the analyser-specific range (INV-INT-005: low ≤ high).</summary>
    public void SetRangeOverride(RangeOverride? range)
    {
        var cleaned = CleanRange(range);
        RangeLow = cleaned?.Low;
        RangeHigh = cleaned?.High;
        RangeText = cleaned?.Text;
    }

    /// <summary>T4 (<c>integration.manage</c>). Idempotent; an inactive mapping makes incoming values Unmapped.</summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>
    /// The value the laboratory records for <paramref name="rawValue"/> (LDM manual §13.5): a number is multiplied by the
    /// slope and shifted by the intercept (an identity mapping keeps the instrument's text as is); then the first matching
    /// row of the conversion table — a comparison on the converted number, or the exact raw text, case-insensitive —
    /// replaces it with its text.
    /// </summary>
    public string Transform(string rawValue)
    {
        var trimmed = (rawValue ?? string.Empty).Trim();
        decimal? number = null;
        var value = trimmed;
        if (TryParseNumber(trimmed, out var parsed))
        {
            if (Slope == 1m && Intercept == 0m)
            {
                number = parsed;
            }
            else
            {
                var scale = Math.Clamp(Math.Max(DecimalPlaces(trimmed), 2), 2, 6);
                number = Math.Round((parsed * Slope) + Intercept, scale, MidpointRounding.AwayFromZero);
                value = number.Value.ToString("0.######", CultureInfo.InvariantCulture);
            }
        }

        foreach (var conversion in _conversions.OrderBy(row => row.Order))
        {
            if (conversion.Matches(trimmed, number))
            {
                return conversion.ToValue;
            }
        }

        return value;
    }

    public static string CleanAnalyserCode(string code) =>
        IntegrationGuard.Required(code, MaxCodeLength, IntegrationCodes.AnalyserCodeInvalid, "The analyser code");

    internal static bool TryParseNumber(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);

    private static int DecimalPlaces(string text)
    {
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return 0;
        }

        var exponent = text.IndexOfAny(['e', 'E'], dot);
        return (exponent < 0 ? text.Length : exponent) - dot - 1;
    }

    private void Apply(MappingDefinition definition)
    {
        Require(definition.TestId != Guid.Empty, IntegrationCodes.TestRequired, "A mapping names a test.", DomainFault.Validation);
        var code = CleanAnalyserCode(definition.AnalyserCode);
        var host = IntegrationGuard.Optional(definition.HostCode, MaxCodeLength, IntegrationCodes.AnalyserCodeInvalid, "The host code");
        Require(definition.ResultSelection is >= 1 and <= MaxResultSelection, IntegrationCodes.ResultSelectionInvalid, $"The result selection is 1 to {MaxResultSelection}.", DomainFault.Validation);
        Require(definition.Slope != 0m, IntegrationCodes.SlopeInvalid, "The slope cannot be zero.", DomainFault.Validation);
        var details = IntegrationGuard.Optional(definition.QueryDetails, MaxTextLength, IntegrationCodes.TextTooLong, "The query details");
        var methodology = IntegrationGuard.Optional(definition.Methodology, MaxTextLength, IntegrationCodes.TextTooLong, "The methodology");
        var conversions = CleanConversions(definition.Conversions ?? []);
        var range = CleanRange(definition.RangeOverride);

        TestId = definition.TestId;
        AnalyserCode = code;
        HostCode = host;
        ResultSelection = definition.ResultSelection;
        Slope = definition.Slope;
        Intercept = definition.Intercept;
        UnitId = definition.UnitId == Guid.Empty ? null : definition.UnitId;
        Query = definition.Query;
        QueryDetails = details;
        AutoSend = definition.AutoSend;
        Methodology = methodology;
        RangeLow = range?.Low;
        RangeHigh = range?.High;
        RangeText = range?.Text;
        _conversions.Clear();
        for (var index = 0; index < conversions.Count; index++)
        {
            _conversions.Add(new ValueConversion(index + 1, conversions[index].FromValue, conversions[index].ToValue));
        }
    }

    private static List<ConversionSpec> CleanConversions(IReadOnlyList<ConversionSpec> conversions)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        Require(conversions.Count <= MaxConversions, IntegrationCodes.ConversionsInvalid, $"At most {MaxConversions} conversions.", DomainFault.Validation);
        var cleaned = new List<ConversionSpec>(conversions.Count);
        foreach (var conversion in conversions)
        {
            Require(conversion is not null, IntegrationCodes.ConversionsInvalid, "A conversion is required.", DomainFault.Validation);
            var from = IntegrationGuard.Required(conversion!.FromValue, MaxConversionValueLength, IntegrationCodes.ConversionsInvalid, "A source value");
            var to = IntegrationGuard.Required(conversion.ToValue, MaxConversionValueLength, IntegrationCodes.ConversionsInvalid, "A converted value");
            Require(!cleaned.Any(other => string.Equals(other.FromValue, from, StringComparison.OrdinalIgnoreCase)), IntegrationCodes.ConversionsInvalid, $"The source value '{from}' appears twice.", DomainFault.Validation);
            cleaned.Add(new ConversionSpec(from, to));
        }

        return cleaned;
    }

    private static RangeOverride? CleanRange(RangeOverride? range)
    {
        if (range is null)
        {
            return null;
        }

        var text = IntegrationGuard.Optional(range.Text, MaxRangeTextLength, IntegrationCodes.RangeOverrideInvalid, "The range text");
        if (range.Low is null && range.High is null && text is null)
        {
            return null;
        }

        Require(range.Low is null || range.High is null || range.Low <= range.High, IntegrationCodes.RangeOverrideInvalid, "The low bound cannot exceed the high bound.", DomainFault.Validation);
        return new RangeOverride(range.Low, range.High, text);
    }
}

/// <summary>A row of a mapping's conversion table (owned row of <c>app.analyser_value_conversions</c>), applied in <see cref="Order"/>.</summary>
public sealed class ValueConversion : Entity
{
    private ValueConversion()
    {
        FromValue = string.Empty;
        ToValue = string.Empty;
    }

    internal ValueConversion(int order, string fromValue, string toValue)
        : base(Guid.CreateVersion7())
    {
        Order = order;
        FromValue = fromValue;
        ToValue = toValue;
    }

    public int Order { get; private set; }

    public string FromValue { get; private set; }

    public string ToValue { get; private set; }

    /// <summary>
    /// A comparison (<c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> and a number) tests the converted number of a numeric
    /// value; otherwise (any other source value, or a non-numeric raw text such as <c>&lt;0.01</c>) the raw text must equal
    /// the source value, case-insensitive.
    /// </summary>
    public bool Matches(string rawValue, decimal? number)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        if (number is { } value && TryComparison(FromValue, out var comparison, out var bound))
        {
            return comparison switch
            {
                "<" => value < bound,
                "<=" => value <= bound,
                ">" => value > bound,
                ">=" => value >= bound,
                _ => false,
            };
        }

        return string.Equals(rawValue.Trim(), FromValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryComparison(string fromValue, out string comparison, out decimal bound)
    {
        comparison = fromValue.StartsWith("<=", StringComparison.Ordinal) ? "<="
            : fromValue.StartsWith(">=", StringComparison.Ordinal) ? ">="
            : fromValue.StartsWith('<') ? "<"
            : fromValue.StartsWith('>') ? ">"
            : string.Empty;
        bound = 0m;
        return comparison.Length > 0 && AnalyserTestMapping.TryParseNumber(fromValue[comparison.Length..].Trim(), out bound);
    }
}
