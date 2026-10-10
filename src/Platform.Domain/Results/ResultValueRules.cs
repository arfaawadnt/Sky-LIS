using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Results;

/// <summary>A value after the rules of its test type: what is stored and displayed, and its flag.</summary>
public sealed record NormalizedResultValue(decimal? Numeric, string? Text, FormattedReport? Formatted, CultureReport? Culture, ResultFlag? Flag, bool IsPanic)
{
    /// <summary>The value as a log row shows it.</summary>
    public string Describe() => Formatted?.Describe() ?? Culture?.Describe() ?? Text ?? string.Empty;
}

/// <summary>
/// The entry rules of each test type (SRS CP-19; LDM §3.3, §5.5) and the flagging against the resolved range (BR-08):
/// numbers rounded to the test's decimals and read "&lt; low" / "&gt; high" outside the reportable limits; restricted
/// lists accept listed values only and no list accepts its placeholder (BR-19); formulas are never typed; formatted
/// text needs a paragraph; a culture is No growth or organisms. Pure functions.
/// </summary>
public static class ResultValueRules
{
    /// <summary>Normalises a typed or received value for the test, or refuses it with its code.</summary>
    public static NormalizedResultValue Normalize(ResultValueInput input, ResultTestRules rules, ResolvedRange? range, bool computed = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        switch (rules.Type)
        {
            case LabTestType.Formula when !computed:
                throw new DomainException(ResultsCodes.FormulaNotTyped, "A calculated test computes itself from its inputs.", DomainFault.Validation);
            case LabTestType.Numeric:
            case LabTestType.Formula:
                return Numeric(input, rules, range);
            case LabTestType.NonNumeric:
                return new NormalizedResultValue(null, RequiredText(input.Text), null, null, null, false);
            case LabTestType.OptionalList:
            case LabTestType.RestrictedList:
                return List(input, rules);
            case LabTestType.FormattedText:
                var report = input.Formatted ?? throw new DomainException(ResultsCodes.ParagraphsInvalid, "Write the report paragraphs.", DomainFault.Validation);
                return new NormalizedResultValue(null, null, report, null, null, false);
            case LabTestType.Culture:
                var culture = input.Culture ?? throw new DomainException(ResultsCodes.CultureInvalid, "Record No growth or the organisms.", DomainFault.Validation);
                return new NormalizedResultValue(null, culture.Describe(), null, culture, culture.NoGrowth ? ResultFlag.Normal : ResultFlag.Abnormal, false);
            default:
                throw new DomainException(ResultsCodes.ValueRequired, "The test type takes no value.", DomainFault.Validation);
        }
    }

    /// <summary>The flag of a number against a range (LDM §5.3 colour key); null when the range has no limits.</summary>
    public static ResultFlag? Flag(decimal value, ResolvedRange? range)
    {
        if (range is null)
        {
            return null;
        }

        if (range.PanicLow is { } panicLow && value < panicLow)
        {
            return ResultFlag.CriticalLow;
        }

        if (range.PanicHigh is { } panicHigh && value > panicHigh)
        {
            return ResultFlag.CriticalHigh;
        }

        if (range.Low is { } low && value < low)
        {
            return ResultFlag.Low;
        }

        if (range.High is { } high && value > high)
        {
            return ResultFlag.High;
        }

        return range.HasLimits || range.PanicLow is not null || range.PanicHigh is not null ? ResultFlag.Normal : null;
    }

    /// <summary>The display text of a number: the test's decimals, or "&lt; reportable low" / "&gt; reportable high" (LDM §3.4).</summary>
    public static string DisplayNumber(decimal value, int decimals, ResolvedRange? range)
    {
        if (range?.ReportableLow is { } reportableLow && value < reportableLow)
        {
            return "< " + ResultNumbers.Format(reportableLow, decimals);
        }

        if (range?.ReportableHigh is { } reportableHigh && value > reportableHigh)
        {
            return "> " + ResultNumbers.Format(reportableHigh, decimals);
        }

        return ResultNumbers.Format(value, decimals);
    }

    /// <summary>BR-11: the change against the previous value in percent (capped), and whether it exceeds the test's threshold.</summary>
    public static (bool Flag, decimal? Percent) Delta(decimal? value, PreviousResultValue? previous, decimal? thresholdPercent)
    {
        if (value is not { } current || previous is null || thresholdPercent is not { } threshold)
        {
            return (false, null);
        }

        const decimal Cap = 99999m;
        decimal change;
        if (previous.Value == 0m)
        {
            change = current == 0m ? 0m : Cap;
        }
        else
        {
            change = Math.Min(Cap, Math.Abs((current - previous.Value) / previous.Value) * 100m);
        }

        change = Math.Round(change, 1, MidpointRounding.AwayFromZero);
        return (change > threshold, change);
    }

    private static NormalizedResultValue Numeric(ResultValueInput input, ResultTestRules rules, ResolvedRange? range)
    {
        var parsed = input.Numeric ?? ResultNumbers.Parse(input.Text);
        if (parsed is null)
        {
            throw string.IsNullOrWhiteSpace(input.Text)
                ? new DomainException(ResultsCodes.ValueRequired, "A value is required.", DomainFault.Validation)
                : new DomainException(ResultsCodes.NumberInvalid, "The result of a numeric test is a number.", DomainFault.Validation);
        }

        var rounded = Math.Round(parsed.Value, rules.Decimals, MidpointRounding.AwayFromZero);
        var flag = Flag(rounded, range);
        var panic = flag is ResultFlag.CriticalLow or ResultFlag.CriticalHigh;
        return new NormalizedResultValue(rounded, DisplayNumber(rounded, rules.Decimals, range), null, null, flag, panic);
    }

    private static NormalizedResultValue List(ResultValueInput input, ResultTestRules rules)
    {
        var text = RequiredText(input.Text);
        var option = rules.ListValues.FirstOrDefault(candidate => string.Equals(candidate.Value.Trim(), text, StringComparison.OrdinalIgnoreCase));
        if (option is not null && option.IsPlaceholder)
        {
            throw new DomainException(ResultsCodes.PlaceholderNotAResult, "A placeholder is not a result (BR-19).", DomainFault.Validation);
        }

        if (option is null)
        {
            if (rules.Type == LabTestType.RestrictedList)
            {
                throw new DomainException(ResultsCodes.ValueNotInList, "The value is not in the test's list.", DomainFault.Validation);
            }

            return new NormalizedResultValue(null, text, null, null, null, false);
        }

        var flag = option.IsCritical || !option.IsNormal ? ResultFlag.Abnormal : ResultFlag.Normal;
        return new NormalizedResultValue(null, option.Value.Trim(), null, null, flag, option.IsCritical);
    }

    private static string RequiredText(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new DomainException(ResultsCodes.ValueRequired, "A value is required.", DomainFault.Validation);
        }

        if (value.Length > Result.MaxTextLength)
        {
            throw new DomainException(ResultsCodes.TextTooLong, $"A result holds at most {Result.MaxTextLength} characters.", DomainFault.Validation);
        }

        return value;
    }
}
