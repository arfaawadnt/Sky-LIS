using Platform.Application.Catalogue.Services;

namespace Platform.Application.UnitTests.Catalogue;

/// <summary>Scenario S-FORMULA of intake LIS-INT-CAT-001: the formula language of calculated tests (T17, T20; Results computes with it).</summary>
public sealed class FormulaEvaluatorTests
{
    private static readonly FormulaEvaluator Evaluator = FormulaEvaluator.Instance;

    [Theory]
    [InlineData("([alt] / [AST]) * 100", "([ALT] / [AST]) * 100")]
    [InlineData("chol-hdl-tg/5", "[CHOL] - [HDL] - [TG] / 5")]
    [InlineData("round( [A.1] * 2.50 , 1 )", "ROUND([A.1] * 2.50, 1)")]
    [InlineData("if(a>=10,a,-b)", "IF([A] >= 10, [A], -[B])")]
    [InlineData("a != b", "[A] <> [B]")]
    [InlineData("max(1, min(a, 2), abs(-3))", "MAX(1, MIN([A], 2), ABS(-3))")]
    [InlineData("2^-1", "2 ^ -1")]
    public void Parse_normalises_and_is_idempotent(string source, string normalized)
    {
        var parsed = Evaluator.Parse(source);

        Assert.True(parsed.IsValid, parsed.Error);
        Assert.Equal(normalized, parsed.Normalized);
        Assert.Equal(normalized, Evaluator.Parse(parsed.Normalized).Normalized);
    }

    [Fact]
    public void Parse_lists_each_reference_once_in_order_of_appearance()
    {
        Assert.Equal(["CHOL", "HDL", "TG"], Evaluator.Parse("[CHOL] - [hdl] - [TG] / 5 + [chol] * 0").References);
        Assert.Empty(Evaluator.Parse("1 + 2").References);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("1 +", 4)]
    [InlineData("(1 + 2", 7)]
    [InlineData("[A", 1)]
    [InlineData("[]", 1)]
    [InlineData("1 $ 2", 3)]
    [InlineData("FOO(1)", 1)]
    [InlineData("ROUND(1, 2, 3)", 1)]
    [InlineData("IF(1, 2)", 1)]
    [InlineData("1 < 2 < 3", 7)]
    [InlineData("1 2", 3)]
    [InlineData(")", 1)]
    public void Parse_reports_the_error_and_its_position(string source, int position)
    {
        var parsed = Evaluator.Parse(source);

        Assert.False(parsed.IsValid);
        Assert.Equal(position, parsed.Position);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
        Assert.Contains($"position {position}", FormulaMessages.Describe(parsed), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_refuses_formulas_beyond_the_size_and_depth_limits()
    {
        Assert.False(Evaluator.Parse(new string('1', FormulaEvaluator.MaxLength + 1)).IsValid);
        Assert.False(Evaluator.Parse(string.Join(" + ", Enumerable.Repeat("1", 250))).IsValid);
        Assert.False(Evaluator.Parse(new string('(', 40) + "1" + new string(')', 40)).IsValid);
        Assert.True(Evaluator.Parse(new string('(', 20) + "1" + new string(')', 20)).IsValid);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("2 ^ -1", 0.5)]
    [InlineData("7 / 2", 3.5)]
    [InlineData("ROUND(2.345, 2)", 2.35)]
    [InlineData("ROUND(2.5)", 3)]
    [InlineData("ROUND(-2.5)", -3)]
    [InlineData("MIN(4, 2, 8)", 2)]
    [InlineData("MAX(4, 2, 8)", 8)]
    [InlineData("ABS(-1.5)", 1.5)]
    [InlineData("3 > 2", 1)]
    [InlineData("3 <= 2", 0)]
    [InlineData("2 = 2", 1)]
    [InlineData("IF(3 > 2, 10, 20)", 10)]
    [InlineData("4 ^ 0.5", 2)]
    public void Evaluate_computes_with_decimal_arithmetic_and_the_usual_precedence(string expression, double expected)
    {
        var result = Evaluator.Evaluate(expression, new Dictionary<string, decimal?>());

        Assert.Equal(FormulaOutcome.Value, result.Outcome);
        Assert.Equal((decimal)expected, result.Value);
    }

    [Fact]
    public void Evaluate_reads_the_referenced_values_by_code_ignoring_case()
    {
        var values = new Dictionary<string, decimal?> { ["CHOL"] = 200m, ["hdl"] = 50m, ["TG"] = 150m };

        var ldl = Evaluator.Evaluate("[CHOL] - [HDL] - [TG] / 5", values);

        Assert.True(ldl.HasValue);
        Assert.Equal(120m, ldl.Value);
    }

    [Fact]
    public void Evaluate_waits_for_a_missing_value_and_names_it()
    {
        var result = Evaluator.Evaluate("[A] + [B]", new Dictionary<string, decimal?> { ["A"] = 1m, ["B"] = null });
        Assert.Equal((FormulaOutcome.MissingValue, "B"), (result.Outcome, result.Detail));

        var absent = Evaluator.Evaluate("[A] + [C]", new Dictionary<string, decimal?> { ["A"] = 1m });
        Assert.Equal((FormulaOutcome.MissingValue, "C"), (absent.Outcome, absent.Detail));
    }

    [Fact]
    public void IF_computes_only_the_chosen_branch()
    {
        var result = Evaluator.Evaluate("IF([A] > 0, [A], [MISSING])", new Dictionary<string, decimal?> { ["A"] = 5m });

        Assert.Equal((FormulaOutcome.Value, 5m), (result.Outcome, result.Value));
    }

    [Fact]
    public void Evaluate_reports_division_by_zero_overflow_and_invalid_input()
    {
        Assert.Equal(FormulaOutcome.DivisionByZero, Evaluator.Evaluate("1 / ([A] - [A])", new Dictionary<string, decimal?> { ["A"] = 3m }).Outcome);
        Assert.Equal(FormulaOutcome.DivisionByZero, Evaluator.Evaluate("0 ^ -1", new Dictionary<string, decimal?>()).Outcome);
        Assert.Equal(FormulaOutcome.Overflow, Evaluator.Evaluate("79228162514264337593543950 * 100000", new Dictionary<string, decimal?>()).Outcome);
        Assert.Equal(FormulaOutcome.Overflow, Evaluator.Evaluate("10 ^ 40.5", new Dictionary<string, decimal?>()).Outcome);
        Assert.Equal(FormulaOutcome.Invalid, Evaluator.Evaluate("ROUND(1.5, 0.5)", new Dictionary<string, decimal?>()).Outcome);
        Assert.Equal(FormulaOutcome.Invalid, Evaluator.Evaluate("(-8) ^ 0.5", new Dictionary<string, decimal?>()).Outcome);
        Assert.Equal(FormulaOutcome.Invalid, Evaluator.Evaluate("1 +", new Dictionary<string, decimal?>()).Outcome);
    }
}
