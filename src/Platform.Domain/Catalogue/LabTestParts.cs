using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

public enum RangeAgeUnit
{
    Days = 0,
    Months = 1,
    Years = 2,
}

/// <summary>A range band as the editor sends it (limits are optional; NonNumeric bands carry only the text).</summary>
public sealed record RangeBandSpec(
    GenderRestriction Sex,
    int AgeFrom,
    int AgeTo,
    RangeAgeUnit AgeUnit,
    Guid? PatientConditionId,
    decimal? Low,
    decimal? High,
    decimal? PanicLow,
    decimal? PanicHigh,
    decimal? ReportableLow,
    decimal? ReportableHigh,
    string? TextRange);

public sealed record ListValueSpec(string Value, bool IsNormal, bool IsCritical, bool IsDefault, bool IsPlaceholder, string? Colour);

public sealed record ParagraphSpec(string Title, string? Text);

/// <summary>What the reference-range resolver needs of a band (BR-08); implemented by <see cref="RangeBand"/> and by the read port's band records.</summary>
public interface IReferenceBand
{
    GenderRestriction Sex { get; }

    int AgeFrom { get; }

    int AgeTo { get; }

    RangeAgeUnit AgeUnit { get; }

    Guid? PatientConditionId { get; }

    int DisplayOrder { get; }
}

/// <summary>
/// A reference-range band of a test (SRS CP-41; LDM §3.3–§3.4): for a sex, an age interval <c>[AgeFrom, AgeTo)</c> in
/// <see cref="AgeUnit"/> and optionally a patient condition, the normal, panic and reportable limits and a printed
/// text. Child of <see cref="LabTest"/> in <c>app.test_range_bands</c>; replaced as a whole by the editor.
/// </summary>
public sealed class RangeBand : Entity, ITenantScoped, IReferenceBand
{
    public const int MaxTextRangeLength = 200;
    public const int MaxAgeYears = 150;

    private RangeBand()
    {
    }

    private RangeBand(Guid id, Guid labTestId, RangeBandSpec spec, int displayOrder, string? textRange)
        : base(id)
    {
        LabTestId = labTestId;
        Sex = spec.Sex;
        AgeFrom = spec.AgeFrom;
        AgeTo = spec.AgeTo;
        AgeUnit = spec.AgeUnit;
        PatientConditionId = spec.PatientConditionId == Guid.Empty ? null : spec.PatientConditionId;
        Low = spec.Low;
        High = spec.High;
        PanicLow = spec.PanicLow;
        PanicHigh = spec.PanicHigh;
        ReportableLow = spec.ReportableLow;
        ReportableHigh = spec.ReportableHigh;
        TextRange = textRange;
        DisplayOrder = displayOrder;
    }

    public Guid TenantId { get; private set; }

    public Guid LabTestId { get; private set; }

    public GenderRestriction Sex { get; private set; }

    public int AgeFrom { get; private set; }

    public int AgeTo { get; private set; }

    public RangeAgeUnit AgeUnit { get; private set; }

    public Guid? PatientConditionId { get; private set; }

    public decimal? Low { get; private set; }

    public decimal? High { get; private set; }

    public decimal? PanicLow { get; private set; }

    public decimal? PanicHigh { get; private set; }

    public decimal? ReportableLow { get; private set; }

    public decimal? ReportableHigh { get; private set; }

    public string? TextRange { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool HasLimits => Low is not null || High is not null || PanicLow is not null || PanicHigh is not null || ReportableLow is not null || ReportableHigh is not null;

    /// <summary>The largest age a band may reach, in its unit.</summary>
    public static int MaxAge(RangeAgeUnit unit) => unit switch
    {
        RangeAgeUnit.Days => MaxAgeYears * 366,
        RangeAgeUnit.Months => MaxAgeYears * 12,
        _ => MaxAgeYears,
    };

    /// <summary>Average length of the unit in days, for overlap checks only (assumption Q7).</summary>
    public static double DaysPerUnit(RangeAgeUnit unit) => unit switch
    {
        RangeAgeUnit.Days => 1d,
        RangeAgeUnit.Months => PatientAge.DaysPerMonth,
        _ => PatientAge.DaysPerYear,
    };

    internal static RangeBand From(Guid labTestId, RangeBandSpec spec, int displayOrder, LabTestType type)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!Enum.IsDefined(spec.Sex) || !Enum.IsDefined(spec.AgeUnit))
        {
            CatalogueAGuard.Refuse(CatalogueACodes.TextInvalid, "Unknown sex or age unit.");
        }

        if (spec.AgeFrom < 0 || spec.AgeTo <= spec.AgeFrom || spec.AgeTo > MaxAge(spec.AgeUnit))
        {
            CatalogueAGuard.Refuse(LabTestCodes.RangeAgeInvalid, $"Band {displayOrder}: the age interval is [from, to) with 0 ≤ from < to ≤ {MaxAge(spec.AgeUnit)} {spec.AgeUnit.ToString().ToLowerInvariant()}.");
        }

        var textRange = CatalogueAGuard.Optional(spec.TextRange, MaxTextRangeLength, $"Band {displayOrder}: the text range");
        var band = new RangeBand(Guid.CreateVersion7(), labTestId, spec, displayOrder, textRange);

        if (type == LabTestType.NonNumeric)
        {
            if (band.HasLimits)
            {
                CatalogueAGuard.Refuse(LabTestCodes.RangeLimitsInvalid, $"Band {displayOrder}: a non-numeric test prints a reference text, not numeric limits.");
            }

            if (textRange is null)
            {
                CatalogueAGuard.Refuse(LabTestCodes.RangeBandEmpty, $"Band {displayOrder}: a non-numeric band needs its reference text.");
            }
        }
        else if (band.Low is null && band.High is null && textRange is null)
        {
            CatalogueAGuard.Refuse(LabTestCodes.RangeBandEmpty, $"Band {displayOrder}: give a low limit, a high limit or a reference text.");
        }

        band.EnsureOrderedLimits();
        return band;
    }

    /// <summary>INV-CAT-007: two bands of the same sex and condition must not share an age.</summary>
    internal static void EnsureNoOverlap(IReadOnlyList<RangeBand> bands)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            for (var j = i + 1; j < bands.Count; j++)
            {
                var (a, b) = (bands[i], bands[j]);
                if (a.Sex != b.Sex || a.PatientConditionId != b.PatientConditionId)
                {
                    continue;
                }

                var (aFrom, aTo) = (a.AgeFrom * DaysPerUnit(a.AgeUnit), a.AgeTo * DaysPerUnit(a.AgeUnit));
                var (bFrom, bTo) = (b.AgeFrom * DaysPerUnit(b.AgeUnit), b.AgeTo * DaysPerUnit(b.AgeUnit));
                if (aFrom < bTo && bFrom < aTo)
                {
                    CatalogueAGuard.Refuse(LabTestCodes.RangeBandsOverlap, $"Bands {a.DisplayOrder} and {b.DisplayOrder} cover the same ages for the same sex and condition.");
                }
            }
        }
    }

    private void EnsureOrderedLimits()
    {
        var ordered = Ordered(Low, High)
            && Ordered(PanicLow, Low) && Ordered(High, PanicHigh)
            && Strict(PanicLow, High) && Strict(Low, PanicHigh) && Strict(PanicLow, PanicHigh)
            && Ordered(ReportableLow, Min(PanicLow, Low)) && Ordered(Max(PanicHigh, High), ReportableHigh)
            && Strict(ReportableLow, ReportableHigh);
        if (!ordered)
        {
            CatalogueAGuard.Refuse(
                LabTestCodes.RangeLimitsInvalid,
                $"Band {DisplayOrder}: limits must read reportable low ≤ panic low ≤ low ≤ high ≤ panic high ≤ reportable high.");
        }

        static bool Ordered(decimal? lower, decimal? upper) => lower is null || upper is null || lower <= upper;
        static bool Strict(decimal? lower, decimal? upper) => lower is null || upper is null || lower < upper;
        static decimal? Min(decimal? a, decimal? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
        static decimal? Max(decimal? a, decimal? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
    }
}

/// <summary>
/// An allowed value of a list test (SRS CP-41; LDM §3.3 "Test Conditions" Normal / Exact). BR-19: the default is a
/// placeholder (it does not read as a result and does not print), at most one exists, and a placeholder is never
/// critical. Child of <see cref="LabTest"/> in <c>app.test_list_values</c>.
/// </summary>
public sealed class ListValue : Entity, ITenantScoped
{
    public const int MaxValueLength = 200;

    private ListValue()
    {
        Value = string.Empty;
    }

    private ListValue(Guid id, Guid labTestId, string value, ListValueSpec spec, string? colour, int displayOrder)
        : base(id)
    {
        LabTestId = labTestId;
        Value = value;
        IsNormal = spec.IsNormal;
        IsCritical = spec.IsCritical;
        IsDefault = spec.IsDefault;
        IsPlaceholder = spec.IsPlaceholder;
        Colour = colour;
        DisplayOrder = displayOrder;
    }

    public Guid TenantId { get; private set; }

    public Guid LabTestId { get; private set; }

    public string Value { get; private set; }

    /// <summary>Not flagged on the report.</summary>
    public bool IsNormal { get; private set; }

    /// <summary>A critical (panic) answer.</summary>
    public bool IsCritical { get; private set; }

    /// <summary>Offered when the result row is created (BR-19: only a placeholder may be the default).</summary>
    public bool IsDefault { get; private set; }

    /// <summary>A value that does not read as a result (LDM "." / blank) and never prints.</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary><c>#RRGGBB</c>, or null.</summary>
    public string? Colour { get; private set; }

    public int DisplayOrder { get; private set; }

    internal static ListValue From(Guid labTestId, ListValueSpec spec, int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var value = (spec.Value ?? string.Empty).Trim();
        if (value.Length is < 1 or > MaxValueLength)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ListValueInvalid, $"Value {displayOrder}: 1 to {MaxValueLength} characters.");
        }

        string? colour = null;
        if (!string.IsNullOrWhiteSpace(spec.Colour))
        {
            colour = spec.Colour.Trim().ToUpperInvariant();
            if (colour.Length != 7 || colour[0] != '#' || !colour.Skip(1).All(char.IsAsciiHexDigit))
            {
                CatalogueAGuard.Refuse(LabTestCodes.ListValueInvalid, $"Value {displayOrder}: the colour is written #RRGGBB.");
            }
        }

        if (spec.IsDefault && !spec.IsPlaceholder)
        {
            CatalogueAGuard.Refuse(LabTestCodes.DefaultNotPlaceholder, $"Value {displayOrder}: only a placeholder can be the default; a default must never read as a result (BR-19).");
        }

        if (spec.IsPlaceholder && spec.IsCritical)
        {
            CatalogueAGuard.Refuse(LabTestCodes.PlaceholderCritical, $"Value {displayOrder}: a placeholder is never critical.");
        }

        if (spec.IsNormal && spec.IsCritical)
        {
            CatalogueAGuard.Refuse(LabTestCodes.ListValueNormalAndCritical, $"Value {displayOrder}: a value is Normal or Critical, not both.");
        }

        return new ListValue(Guid.CreateVersion7(), labTestId, value, spec, colour, displayOrder);
    }
}

/// <summary>A paragraph of a formatted-text report (LDM §3.7: S. No., header). Child of <see cref="LabTest"/> in <c>app.test_paragraphs</c>.</summary>
public sealed class TestParagraph : Entity, ITenantScoped
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 4000;

    private TestParagraph()
    {
        Title = string.Empty;
    }

    private TestParagraph(Guid id, Guid labTestId, string title, string? text, int displayOrder)
        : base(id)
    {
        LabTestId = labTestId;
        Title = title;
        Text = text;
        DisplayOrder = displayOrder;
    }

    public Guid TenantId { get; private set; }

    public Guid LabTestId { get; private set; }

    public string Title { get; private set; }

    /// <summary>The template text offered when the result is written.</summary>
    public string? Text { get; private set; }

    public int DisplayOrder { get; private set; }

    internal static TestParagraph From(Guid labTestId, ParagraphSpec spec, int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var title = (spec.Title ?? string.Empty).Trim();
        var text = string.IsNullOrWhiteSpace(spec.Text) ? null : spec.Text.Trim();
        if (title.Length is < 1 or > MaxTitleLength || text is { Length: > MaxTextLength })
        {
            CatalogueAGuard.Refuse(LabTestCodes.ParagraphInvalid, $"Paragraph {displayOrder}: a title of 1 to {MaxTitleLength} characters and a text of at most {MaxTextLength}.");
        }

        return new TestParagraph(Guid.CreateVersion7(), labTestId, title, text, displayOrder);
    }
}
