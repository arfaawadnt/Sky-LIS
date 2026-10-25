namespace Platform.Domain.Catalogue;

/// <summary>
/// The layout of a printed laboratory report (SRS CP-13 / CP-19, change request LIS-CR-010, reviewer point C-14). The
/// shared vocabulary of Results (the report document), Catalogue (<see cref="ReportTemplate.Design"/>) and Pricing (the
/// payer contract's override). Stored as the member name; the laboratory parameter stores the lower-case name.
/// </summary>
public enum ReportDesign
{
    /// <summary>The report as it was before LIS-CR-010: letterhead, patient block with QR, one table per profile.</summary>
    Classic = 0,

    /// <summary>Two-column header, coloured flag bars and a range bar per numeric result.</summary>
    Modern = 1,

    /// <summary>Dense layout that keeps a report on one page where possible.</summary>
    Compact = 2,

    /// <summary>A blank 45 mm top band for pre-printed paper, no logo and no laboratory header.</summary>
    Letterhead = 3,

    /// <summary>The last three previous values of each test printed beside the current one.</summary>
    Cumulative = 4,
}

/// <summary>Where the design of a report came from (the first override that is set wins).</summary>
public enum ReportDesignSource
{
    Laboratory = 0,
    Template = 1,
    Contract = 2,
}

/// <summary>The resolved design of a report and the level that chose it.</summary>
public readonly record struct ReportDesignChoice(ReportDesign Design, ReportDesignSource Source);

/// <summary>
/// The selection rule of report designs (LIS-CR-010 §4): the payer contract's override, else the report template's
/// override, else the laboratory parameter <c>reportDesign</c> (Printing group, default <c>classic</c>).
/// </summary>
public static class ReportDesigns
{
    /// <summary>The parameter's default, in its stored (lower-case) form.</summary>
    public const string DefaultSettingValue = "classic";

    /// <summary>How many previous values of a test the Cumulative design prints.</summary>
    public const int CumulativeHistory = 3;

    /// <summary>The allowed values of the laboratory parameter, in display order (the lower-case member names).</summary>
    public static IReadOnlyList<string> SettingValues { get; } = [.. Enum.GetValues<ReportDesign>().Select(ToSettingValue)];

    /// <summary>The stored form of a design in the laboratory parameter.</summary>
    public static string ToSettingValue(ReportDesign design) => design.ToString().ToLowerInvariant();

    /// <summary>The design of a parameter value (case-insensitive); anything unknown is <see cref="ReportDesign.Classic"/>.</summary>
    public static ReportDesign FromSettingValue(string? value) =>
        Enum.GetValues<ReportDesign>().FirstOrDefault(design => string.Equals(design.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The selection order: contract override → template override → laboratory default.</summary>
    public static ReportDesignChoice Resolve(ReportDesign? contract, ReportDesign? template, ReportDesign laboratory) =>
        contract is { } byContract ? new(byContract, ReportDesignSource.Contract)
        : template is { } byTemplate ? new(byTemplate, ReportDesignSource.Template)
        : new(laboratory, ReportDesignSource.Laboratory);

    /// <summary>A design override is meaningful on the templates of result reports only (Results, Culture, Text).</summary>
    public static bool AppliesTo(ReportTemplateType type) => type is ReportTemplateType.Results or ReportTemplateType.Culture or ReportTemplateType.Text;
}
