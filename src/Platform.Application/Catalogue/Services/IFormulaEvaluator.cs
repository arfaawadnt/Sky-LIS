namespace Platform.Application.Catalogue.Services;

/// <summary>
/// The formula language of calculated tests (SRS CP-41 "check formula"; LDM §3.3 "Formula Setup"): numbers, test
/// references <c>[CODE]</c> (or a bare <c>CODE</c> of letters, digits and <c>_</c>), <c>+ - * / ^ ( )</c>, comparisons
/// <c>&lt; &lt;= &gt; &gt;= = &lt;&gt;</c> (value 1 or 0), and the functions <c>ROUND(x[, n])</c>, <c>IF(c, a, b)</c>,
/// <c>MIN(a, …)</c>, <c>MAX(a, …)</c>, <c>ABS(x)</c>, and the patient variables <c>@AGE</c> and <c>@FEMALE</c>
/// (<see cref="FormulaVariables"/>; e.g. MDRD eGFR <c>175 * [CREA] ^ -1.154 * @AGE ^ -0.203 * IF(@FEMALE, 0.742, 1)</c>). Decimal arithmetic; a safe tokenizer and recursive-descent
/// parser with size and depth limits; no reflection, no dynamic code. Used by the catalogue to validate and
/// normalise formulas and by the Results module to compute them.
/// </summary>
public interface IFormulaEvaluator
{
    /// <summary>Syntax check: the normalised expression and the referenced test codes (patient variables are not references), or the error and its 1-based position.</summary>
    FormulaParseResult Parse(string? expression);

    /// <summary>Computes the expression from the values of the referenced tests (keyed by code) and of the patient variables (keyed <c>@AGE</c>, <c>@FEMALE</c>); a missing or null value yields <see cref="FormulaOutcome.MissingValue"/>.</summary>
    FormulaEvaluation Evaluate(string expression, IReadOnlyDictionary<string, decimal?> values);
}

/// <summary>
/// The patient variables a formula may read (validation v0.5.0, MDRD eGFR). The caller supplies them with the test values:
/// <see cref="Age"/> in completed years, <see cref="Female"/> 1 for a female patient and 0 for a male; null when unknown,
/// so the formula waits (<see cref="FormulaOutcome.MissingValue"/>) instead of computing with a guess.
/// </summary>
public static class FormulaVariables
{
    public const string Age = "@AGE";
    public const string Female = "@FEMALE";

    public static IReadOnlyList<string> All { get; } = [Age, Female];
}

public sealed record FormulaParseResult(bool IsValid, string? Normalized, IReadOnlyList<string> References, string? Error, int? Position)
{
    public static FormulaParseResult Failure(string error, int? position) => new(false, null, [], error, position);
}

public enum FormulaOutcome
{
    /// <summary>A value was computed.</summary>
    Value = 0,

    /// <summary>A referenced test has no value yet (the formula waits; nothing is shown).</summary>
    MissingValue = 1,

    DivisionByZero = 2,

    Overflow = 3,

    /// <summary>The expression is not valid, or a function received an argument it cannot use.</summary>
    Invalid = 4,
}

/// <param name="Detail">The missing code, or the reason the computation failed.</param>
public sealed record FormulaEvaluation(FormulaOutcome Outcome, decimal? Value, string? Detail)
{
    public bool HasValue => Outcome == FormulaOutcome.Value;
}
