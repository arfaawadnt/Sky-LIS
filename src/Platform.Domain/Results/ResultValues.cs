using System.Globalization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Results;

/// <summary>
/// The reference range that applied to a value when it was entered (BR-08, LDM §3.3–§3.4): normal limits, panic limits,
/// reportable limits and the printed text, copied from the band the resolver chose for the patient's sex, age and
/// condition. Owned by <see cref="Result"/> (columns <c>range_*</c>); null when the test has no band for the patient.
/// </summary>
public sealed class ResolvedRange : ValueObject
{
    public const int MaxTextLength = 200;

    private ResolvedRange()
    {
    }

    private ResolvedRange(decimal? low, decimal? high, decimal? panicLow, decimal? panicHigh, decimal? reportableLow, decimal? reportableHigh, string? text)
    {
        Low = low;
        High = high;
        PanicLow = panicLow;
        PanicHigh = panicHigh;
        ReportableLow = reportableLow;
        ReportableHigh = reportableHigh;
        Text = text;
    }

    public decimal? Low { get; private set; }

    public decimal? High { get; private set; }

    public decimal? PanicLow { get; private set; }

    public decimal? PanicHigh { get; private set; }

    public decimal? ReportableLow { get; private set; }

    public decimal? ReportableHigh { get; private set; }

    /// <summary>The printed reference text (non-numeric tests, or a comment range of a numeric one).</summary>
    public string? Text { get; private set; }

    public bool HasLimits => Low is not null || High is not null;

    /// <summary>A range, or null when the band carries nothing at all.</summary>
    public static ResolvedRange? Create(decimal? low, decimal? high, decimal? panicLow, decimal? panicHigh, decimal? reportableLow, decimal? reportableHigh, string? text)
    {
        var cleaned = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (cleaned is { Length: > MaxTextLength })
        {
            cleaned = cleaned[..MaxTextLength];
        }

        if (low is null && high is null && panicLow is null && panicHigh is null && reportableLow is null && reportableHigh is null && cleaned is null)
        {
            return null;
        }

        return new ResolvedRange(low, high, panicLow, panicHigh, reportableLow, reportableHigh, cleaned);
    }

    /// <summary>The range as printed: "3.5 – 5.0", "&lt; 5.0", "&gt; 3.5", or the text when there are no limits.</summary>
    public string Display(int decimals)
    {
        if (Low is { } low && High is { } high)
        {
            return $"{ResultNumbers.Format(low, decimals)} – {ResultNumbers.Format(high, decimals)}";
        }

        if (High is { } onlyHigh)
        {
            return $"< {ResultNumbers.Format(onlyHigh, decimals)}";
        }

        if (Low is { } onlyLow)
        {
            return $"> {ResultNumbers.Format(onlyLow, decimals)}";
        }

        return Text ?? string.Empty;
    }

    public override string ToString() => Display(2);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Low;
        yield return High;
        yield return PanicLow;
        yield return PanicHigh;
        yield return ReportableLow;
        yield return ReportableHigh;
        yield return Text;
    }
}

/// <summary>A paragraph of a formatted-text report (LDM §3.7): its title (the catalogue paragraph header) and the text written for this patient.</summary>
public sealed record FormattedParagraph(string Title, string Text);

/// <summary>The paragraphs of a formatted-text result (histology, cytology; LDM §3.7). Stored as JSON on the result.</summary>
public sealed class FormattedReport
{
    public const int MaxParagraphs = 50;
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 4000;

    private FormattedReport(IReadOnlyList<FormattedParagraph> paragraphs) => Paragraphs = paragraphs;

    public IReadOnlyList<FormattedParagraph> Paragraphs { get; }

    /// <summary>A report entered by a user: titles 1–200, texts ≤ 4 000, at most 50 paragraphs, at least one paragraph with text (RES-007).</summary>
    public static FormattedReport Create(IEnumerable<FormattedParagraph>? paragraphs)
    {
        var cleaned = new List<FormattedParagraph>();
        foreach (var paragraph in paragraphs ?? [])
        {
            if (paragraph is null)
            {
                continue;
            }

            var title = (paragraph.Title ?? string.Empty).Trim();
            var text = (paragraph.Text ?? string.Empty).Trim();
            if (title.Length == 0 && text.Length == 0)
            {
                continue;
            }

            if (title.Length > MaxTitleLength || text.Length > MaxTextLength)
            {
                throw new DomainException(ResultsCodes.ParagraphsInvalid, $"A paragraph has a title of at most {MaxTitleLength} characters and a text of at most {MaxTextLength}.", DomainFault.Validation);
            }

            cleaned.Add(new FormattedParagraph(title, text));
        }

        if (cleaned.Count > MaxParagraphs)
        {
            throw new DomainException(ResultsCodes.ParagraphsInvalid, $"A report holds at most {MaxParagraphs} paragraphs.", DomainFault.Validation);
        }

        if (!cleaned.Any(paragraph => paragraph.Text.Length > 0))
        {
            throw new DomainException(ResultsCodes.ParagraphsInvalid, "Write the text of at least one paragraph.", DomainFault.Validation);
        }

        return new FormattedReport(cleaned);
    }

    /// <summary>Rebuilds a stored report without validation (persistence only).</summary>
    public static FormattedReport Restore(IReadOnlyList<FormattedParagraph>? paragraphs) => new([.. paragraphs ?? []]);

    /// <summary>A one-line summary for logs: "Title: text …" (at most 400 characters).</summary>
    public string Describe()
    {
        var text = string.Join(" | ", Paragraphs.Select(paragraph => paragraph.Title.Length == 0 ? paragraph.Text : $"{paragraph.Title}: {paragraph.Text}"));
        return text.Length <= 400 ? text : string.Concat(text.AsSpan(0, 399), "…");
    }

    public override string ToString() => Describe();
}

/// <summary>The interpretation of one antibiotic for an isolate (LDM §3.6): S / I / R (catalogue sensitivity value code) and an optional MIC.</summary>
public sealed record CultureSensitivity(Guid AntibioticId, string AntibioticCode, string Abbreviation, string NameEn, string NameAr, string Interpretation, string? Mic);

/// <summary>An organism grown by a culture, with its quantity, comment and sensitivity panel; names are copied as of the entry.</summary>
public sealed record CultureIsolate(Guid OrganismId, string OrganismCode, string OrganismNameEn, string OrganismNameAr, string? Quantity, string? Comment, IReadOnlyList<CultureSensitivity> Sensitivities);

/// <summary>
/// A culture result (LDM §3.6, CP-19 "culture entry"): either No growth (prints without a table) or the isolates with
/// their antibiotic sensitivities (prints the sensitivity table). Stored as JSON on the result.
/// </summary>
public sealed class CultureReport
{
    public const int MaxIsolates = 10;
    public const int MaxSensitivities = 80;
    public const int MaxQuantityLength = 100;
    public const int MaxCommentLength = 500;
    public const int MaxInterpretationLength = 10;
    public const int MaxMicLength = 20;

    private CultureReport(bool noGrowth, IReadOnlyList<CultureIsolate> isolates)
    {
        NoGrowth = noGrowth;
        Isolates = isolates;
    }

    public bool NoGrowth { get; }

    public IReadOnlyList<CultureIsolate> Isolates { get; }

    /// <summary>A culture entered by a user (RES-008): No growth with no isolate, or 1–10 distinct organisms, each antibiotic once with an interpretation.</summary>
    public static CultureReport Create(bool noGrowth, IEnumerable<CultureIsolate>? isolates)
    {
        var list = (isolates ?? []).Where(isolate => isolate is not null).ToList();
        if (noGrowth)
        {
            Refuse(list.Count == 0, "A culture with no growth has no organism.");
            return new CultureReport(true, []);
        }

        Refuse(list.Count is >= 1 and <= MaxIsolates, $"Record No growth, or 1 to {MaxIsolates} organisms.");
        Refuse(list.Select(isolate => isolate.OrganismId).Distinct().Count() == list.Count, "An organism is listed once.");

        var cleaned = new List<CultureIsolate>(list.Count);
        foreach (var isolate in list)
        {
            Refuse(isolate.OrganismId != Guid.Empty, "Every isolate names its organism.");
            var sensitivities = (isolate.Sensitivities ?? []).Where(item => item is not null).ToList();
            Refuse(sensitivities.Count <= MaxSensitivities, $"An isolate holds at most {MaxSensitivities} antibiotics.");
            Refuse(sensitivities.Select(item => item.AntibioticId).Distinct().Count() == sensitivities.Count, "An antibiotic is listed once per organism.");
            var cleanedSensitivities = sensitivities.Select(item =>
            {
                var interpretation = (item.Interpretation ?? string.Empty).Trim().ToUpperInvariant();
                Refuse(item.AntibioticId != Guid.Empty && interpretation.Length is >= 1 and <= MaxInterpretationLength, "Every antibiotic needs its interpretation (S, I, R …).");
                var mic = Optional(item.Mic, MaxMicLength, "The MIC");
                return item with { Interpretation = interpretation, Mic = mic };
            }).ToList();

            cleaned.Add(isolate with
            {
                Quantity = Optional(isolate.Quantity, MaxQuantityLength, "The quantity"),
                Comment = Optional(isolate.Comment, MaxCommentLength, "The comment"),
                Sensitivities = cleanedSensitivities,
            });
        }

        return new CultureReport(false, cleaned);
    }

    /// <summary>Rebuilds a stored report without validation (persistence only).</summary>
    public static CultureReport Restore(bool noGrowth, IReadOnlyList<CultureIsolate>? isolates) => new(noGrowth, [.. isolates ?? []]);

    /// <summary>"No growth" or "Growth: E. coli (10^5 CFU/mL); …" (at most 500 characters): the result's display text.</summary>
    public string Describe()
    {
        if (NoGrowth)
        {
            return "No growth";
        }

        var text = "Growth: " + string.Join("; ", Isolates.Select(isolate => isolate.Quantity is null ? isolate.OrganismNameEn : $"{isolate.OrganismNameEn} ({isolate.Quantity})"));
        return text.Length <= Result.MaxTextLength ? text : string.Concat(text.AsSpan(0, Result.MaxTextLength - 1), "…");
    }

    public override string ToString() => Describe();

    private static string? Optional(string? value, int maxLength, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        Refuse(trimmed.Length <= maxLength, $"{what} holds at most {maxLength} characters.");
        return trimmed;
    }

    private static void Refuse(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(ResultsCodes.CultureInvalid, message, DomainFault.Validation);
        }
    }
}

/// <summary>An allowed value of a list test as entry needs it (BR-19): normal / critical / placeholder.</summary>
public sealed record ResultListOption(string Value, bool IsNormal, bool IsCritical, bool IsPlaceholder);

/// <summary>
/// What entry needs of the test's catalogue definition, handed in by the handler from the catalogue read port: the
/// type, decimals, unit, list values and the delta threshold (BR-11).
/// </summary>
public sealed record ResultTestRules(LabTestType Type, int Decimals, string? Unit, IReadOnlyList<ResultListOption> ListValues, decimal? DeltaPercent)
{
    public static ResultTestRules For(LabTestType type, int decimals = 2, string? unit = null, IReadOnlyList<ResultListOption>? listValues = null, decimal? deltaPercent = null) =>
        new(type, Math.Clamp(decimals, 0, LabTest.MaxDecimals), unit, listValues ?? [], deltaPercent);
}

/// <summary>A value as the user (or an analyser) gave it; which part counts depends on the test type.</summary>
public sealed record ResultValueInput(decimal? Numeric, string? Text, FormattedReport? Formatted, CultureReport? Culture)
{
    public static ResultValueInput Number(decimal value) => new(value, null, null, null);

    public static ResultValueInput Of(string text) => new(null, text, null, null);
}

/// <summary>The patient's previous verified value of the same test (BR-11), found by the handler.</summary>
public sealed record PreviousResultValue(Guid ResultId, decimal Value, DateTimeOffset VerifiedAtUtc);

/// <summary>
/// The result rights (CP-37, LDM §8 S R V E P) and the two special rights of the acting user, evaluated by the handler
/// for this result's clinical group and the visit's branch; the aggregate refuses with its codes (RES-050…055).
/// </summary>
public sealed record ResultRightsGrant(bool CanSee, bool CanReview, bool CanVerify, bool CanEdit, bool CanPrint, bool CanModifyVerified, bool CanPreviewWithoutVerify)
{
    public static ResultRightsGrant None { get; } = new(false, false, false, false, false, false, false);

    public static ResultRightsGrant All { get; } = new(true, true, true, true, true, true, true);
}

/// <summary>The acting user of a transition: id, display name as of the action (the signature), and the rights on this result.</summary>
public sealed record ResultActor(Guid UserId, string? DisplayName, ResultRightsGrant Rights);

/// <summary>Number formatting shared by values, ranges and documents: invariant culture, fixed decimals.</summary>
public static class ResultNumbers
{
    public static string Format(decimal value, int decimals) =>
        Math.Round(value, Math.Clamp(decimals, 0, LabTest.MaxDecimals), MidpointRounding.AwayFromZero).ToString("F" + Math.Clamp(decimals, 0, LabTest.MaxDecimals).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Parses a value typed or sent by an instrument ("12.5", "12,5" is refused, "+3" accepted); null when it is not a number.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
