using System.Globalization;
using System.Text;

namespace Platform.Application.Catalogue.Services;

/// <summary>
/// The implementation of <see cref="IFormulaEvaluator"/>: tokenizer → recursive-descent parser → syntax tree →
/// renderer (normalisation) and evaluator. Pure and thread-safe; registered through the infrastructure adapter
/// <c>CatalogueFormulaEvaluator</c> and usable directly (<see cref="Instance"/>).
/// </summary>
public class FormulaEvaluator : IFormulaEvaluator
{
    public const int MaxLength = 1000;
    public const int MaxTokens = 400;
    public const int MaxDepth = 32;
    public const int MaxCodeLength = 16;

    private static readonly Dictionary<string, (int Min, int Max)> Functions = new(StringComparer.Ordinal)
    {
        ["ROUND"] = (1, 2),
        ["IF"] = (3, 3),
        ["MIN"] = (1, 32),
        ["MAX"] = (1, 32),
        ["ABS"] = (1, 1),
    };

    public static FormulaEvaluator Instance { get; } = new();

    public FormulaParseResult Parse(string? expression)
    {
        try
        {
            var tree = Compile(expression);
            var references = new List<string>();
            Collect(tree, references);
            return new FormulaParseResult(true, Render(tree), references, null, null);
        }
        catch (FormulaSyntaxException error)
        {
            return FormulaParseResult.Failure(error.Message, error.Position);
        }
    }

    public FormulaEvaluation Evaluate(string expression, IReadOnlyDictionary<string, decimal?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        FormulaNode tree;
        try
        {
            tree = Compile(expression);
        }
        catch (FormulaSyntaxException error)
        {
            return new FormulaEvaluation(FormulaOutcome.Invalid, null, error.Message);
        }

        try
        {
            return new FormulaEvaluation(FormulaOutcome.Value, new Evaluator(values).Eval(tree), null);
        }
        catch (FormulaEvaluationException fault)
        {
            return new FormulaEvaluation(fault.Outcome, null, fault.Detail);
        }
        catch (OverflowException)
        {
            return new FormulaEvaluation(FormulaOutcome.Overflow, null, "The value is too large.");
        }
    }

    private static FormulaNode Compile(string? expression)
    {
        var text = expression ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormulaSyntaxException("The formula is empty.", 1);
        }

        if (text.Length > MaxLength)
        {
            throw new FormulaSyntaxException($"A formula holds at most {MaxLength} characters.", MaxLength + 1);
        }

        return new Parser(Tokenize(text)).ParseAll();
    }

    private static void Collect(FormulaNode node, List<string> references)
    {
        switch (node)
        {
            case ReferenceNode reference when !references.Contains(reference.Code, StringComparer.Ordinal):
                references.Add(reference.Code);
                break;
            case UnaryNode unary:
                Collect(unary.Operand, references);
                break;
            case BinaryNode binary:
                Collect(binary.Left, references);
                Collect(binary.Right, references);
                break;
            case GroupNode group:
                Collect(group.Inner, references);
                break;
            case CallNode call:
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, references);
                }

                break;
        }
    }

    /// <summary>The canonical text: references in brackets, upper-case functions, one space around binary operators, the author's parentheses kept.</summary>
    private static string Render(FormulaNode node) => node switch
    {
        NumberNode number => number.Value.ToString(CultureInfo.InvariantCulture),
        ReferenceNode reference => $"[{reference.Code}]",
        UnaryNode unary => unary.Operator + Render(unary.Operand),
        BinaryNode binary => $"{Render(binary.Left)} {binary.Operator} {Render(binary.Right)}",
        GroupNode group => $"({Render(group.Inner)})",
        CallNode call => $"{call.Name}({string.Join(", ", call.Arguments.Select(Render))})",
        _ => throw new InvalidOperationException("Unknown formula node."),
    };

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var position = i + 1;
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }
                }

                var literal = text[start..i];
                if (literal.Length > 28 || !decimal.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw new FormulaSyntaxException($"'{literal}' is not a valid number.", position);
                }

                tokens.Add(new Token(TokenKind.Number, literal, position, number));
            }
            else if (c == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close < 0)
                {
                    throw new FormulaSyntaxException("A test reference '[' is not closed with ']'.", position);
                }

                var code = text[(i + 1)..close].Trim().ToUpperInvariant();
                if (code.Length is < 1 or > MaxCodeLength || !char.IsAsciiLetterOrDigit(code[0]) || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_'))
                {
                    throw new FormulaSyntaxException($"'[{code}]' is not a valid test code.", position);
                }

                tokens.Add(new Token(TokenKind.Reference, code, position));
                i = close + 1;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                var name = text[start..i].ToUpperInvariant();
                if (name.Length > MaxCodeLength && !Functions.ContainsKey(name))
                {
                    throw new FormulaSyntaxException($"'{name}' is longer than a test code.", position);
                }

                tokens.Add(new Token(TokenKind.Identifier, name, position));
            }
            else
            {
                var (symbol, length) = c switch
                {
                    '+' or '-' or '*' or '/' or '^' => (c.ToString(), 1),
                    '(' => ("(", 1),
                    ')' => (")", 1),
                    ',' => (",", 1),
                    '<' when Next(text, i) == '=' => ("<=", 2),
                    '<' when Next(text, i) == '>' => ("<>", 2),
                    '<' => ("<", 1),
                    '>' when Next(text, i) == '=' => (">=", 2),
                    '>' => (">", 1),
                    '=' when Next(text, i) == '=' => ("=", 2),
                    '=' => ("=", 1),
                    '!' when Next(text, i) == '=' => ("<>", 2),
                    _ => throw new FormulaSyntaxException($"Unexpected character '{c}'.", position),
                };

                var kind = symbol switch
                {
                    "(" => TokenKind.LeftParen,
                    ")" => TokenKind.RightParen,
                    "," => TokenKind.Comma,
                    _ => TokenKind.Operator,
                };
                tokens.Add(new Token(kind, symbol, position));
                i += length;
            }

            if (tokens.Count > MaxTokens)
            {
                throw new FormulaSyntaxException($"A formula holds at most {MaxTokens} elements.", position);
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length + 1));
        return tokens;

        static char Next(string source, int index) => index + 1 < source.Length ? source[index + 1] : '\0';
    }

    private enum TokenKind
    {
        Number,
        Reference,
        Identifier,
        Operator,
        LeftParen,
        RightParen,
        Comma,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Position, decimal Number = 0m);

    private abstract record FormulaNode;

    private sealed record NumberNode(decimal Value) : FormulaNode;

    private sealed record ReferenceNode(string Code) : FormulaNode;

    private sealed record UnaryNode(string Operator, FormulaNode Operand) : FormulaNode;

    private sealed record BinaryNode(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

    private sealed record GroupNode(FormulaNode Inner) : FormulaNode;

    private sealed record CallNode(string Name, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

    /// <summary>Grammar: comparison := additive [cmp additive]; additive := multiplicative {(+|-) multiplicative}; multiplicative := unary {(*|/) unary}; unary := (-|+) unary | power; power := primary [^ unary]; primary := number | reference | name '(' args ')' | '(' comparison ')'.</summary>
    private sealed class Parser(List<Token> tokens)
    {
        private static readonly HashSet<string> Comparisons = new(StringComparer.Ordinal) { "<", "<=", ">", ">=", "=", "<>" };
        private int _index;
        private int _depth;

        private Token Current => tokens[_index];

        public FormulaNode ParseAll()
        {
            var tree = ParseComparison();
            if (Current.Kind != TokenKind.End)
            {
                throw new FormulaSyntaxException($"Unexpected '{Current.Text}'.", Current.Position);
            }

            return tree;
        }

        private FormulaNode ParseComparison()
        {
            Enter();
            var left = ParseAdditive();
            if (Current.Kind == TokenKind.Operator && Comparisons.Contains(Current.Text))
            {
                var op = Advance().Text;
                var right = ParseAdditive();
                if (Current.Kind == TokenKind.Operator && Comparisons.Contains(Current.Text))
                {
                    throw new FormulaSyntaxException("Comparisons cannot be chained; use IF.", Current.Position);
                }

                left = new BinaryNode(op, left, right);
            }

            _depth--;
            return left;
        }

        private FormulaNode ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var op = Advance().Text;
                left = new BinaryNode(op, left, ParseMultiplicative());
            }

            return left;
        }

        private FormulaNode ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/")
            {
                var op = Advance().Text;
                left = new BinaryNode(op, left, ParseUnary());
            }

            return left;
        }

        private FormulaNode ParseUnary()
        {
            if (Current.Kind == TokenKind.Operator && Current.Text is "-" or "+")
            {
                Enter();
                var op = Advance().Text;
                var operand = ParseUnary();
                _depth--;
                return op == "-" ? new UnaryNode("-", operand) : operand;
            }

            return ParsePower();
        }

        private FormulaNode ParsePower()
        {
            var operand = ParsePrimary();
            if (Current.Kind == TokenKind.Operator && Current.Text == "^")
            {
                Advance();
                Enter();
                var exponent = ParseUnary();
                _depth--;
                return new BinaryNode("^", operand, exponent);
            }

            return operand;
        }

        private FormulaNode ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    Advance();
                    return new NumberNode(token.Number);
                case TokenKind.Reference:
                    Advance();
                    return new ReferenceNode(token.Text);
                case TokenKind.Identifier when tokens[_index + 1].Kind == TokenKind.LeftParen:
                    return ParseCall();
                case TokenKind.Identifier:
                    Advance();
                    return new ReferenceNode(token.Text);
                case TokenKind.LeftParen:
                {
                    Advance();
                    var inner = ParseComparison();
                    Expect(TokenKind.RightParen, "')' is missing.");
                    return new GroupNode(inner);
                }

                case TokenKind.End:
                    throw new FormulaSyntaxException("The formula ends too early.", token.Position);
                default:
                    throw new FormulaSyntaxException($"Expected a number, a test reference, a function or '(' but found '{token.Text}'.", token.Position);
            }
        }

        private CallNode ParseCall()
        {
            var name = Advance();
            if (!Functions.TryGetValue(name.Text, out var arity))
            {
                throw new FormulaSyntaxException($"Unknown function '{name.Text}'. Use ROUND, IF, MIN, MAX or ABS.", name.Position);
            }

            Advance();   // '('
            var arguments = new List<FormulaNode>();
            if (Current.Kind != TokenKind.RightParen)
            {
                arguments.Add(ParseComparison());
                while (Current.Kind == TokenKind.Comma)
                {
                    Advance();
                    arguments.Add(ParseComparison());
                }
            }

            Expect(TokenKind.RightParen, $"')' is missing after the arguments of {name.Text}.");
            if (arguments.Count < arity.Min || arguments.Count > arity.Max)
            {
                var expected = arity.Min == arity.Max ? $"{arity.Min}" : $"{arity.Min} to {arity.Max}";
                throw new FormulaSyntaxException($"{name.Text} takes {expected} argument(s).", name.Position);
            }

            return new CallNode(name.Text, arguments);
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
            {
                throw new FormulaSyntaxException($"The formula is nested more than {MaxDepth} levels.", Current.Position);
            }
        }

        private Token Advance() => tokens[_index++];

        private void Expect(TokenKind kind, string message)
        {
            if (Current.Kind != kind)
            {
                throw new FormulaSyntaxException(message, Current.Position);
            }

            Advance();
        }
    }

    private sealed class Evaluator(IReadOnlyDictionary<string, decimal?> values)
    {
        private const int MaxIntegerExponent = 64;
        private const int MaxRoundDigits = 10;

        public decimal Eval(FormulaNode node) => node switch
        {
            NumberNode number => number.Value,
            ReferenceNode reference => Value(reference.Code),
            UnaryNode unary => -Eval(unary.Operand),
            GroupNode group => Eval(group.Inner),
            BinaryNode binary => Binary(binary),
            CallNode call => Call(call),
            _ => throw new FormulaEvaluationException(FormulaOutcome.Invalid, "Unknown formula node."),
        };

        private decimal Value(string code)
        {
            if (values.TryGetValue(code, out var exact))
            {
                return exact ?? throw new FormulaEvaluationException(FormulaOutcome.MissingValue, code);
            }

            foreach (var (key, value) in values)
            {
                if (string.Equals(key, code, StringComparison.OrdinalIgnoreCase))
                {
                    return value ?? throw new FormulaEvaluationException(FormulaOutcome.MissingValue, code);
                }
            }

            throw new FormulaEvaluationException(FormulaOutcome.MissingValue, code);
        }

        private decimal Binary(BinaryNode node)
        {
            var left = Eval(node.Left);
            var right = Eval(node.Right);
            return node.Operator switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0m ? throw new FormulaEvaluationException(FormulaOutcome.DivisionByZero, "Division by zero.") : left / right,
                "^" => Power(left, right),
                "<" => left < right ? 1m : 0m,
                "<=" => left <= right ? 1m : 0m,
                ">" => left > right ? 1m : 0m,
                ">=" => left >= right ? 1m : 0m,
                "=" => left == right ? 1m : 0m,
                "<>" => left != right ? 1m : 0m,
                _ => throw new FormulaEvaluationException(FormulaOutcome.Invalid, $"Unknown operator '{node.Operator}'."),
            };
        }

        private decimal Call(CallNode call)
        {
            switch (call.Name)
            {
                case "IF":
                    // only the chosen branch is computed: a missing value in the other branch does not matter
                    return Eval(call.Arguments[0]) != 0m ? Eval(call.Arguments[1]) : Eval(call.Arguments[2]);
                case "ROUND":
                {
                    var value = Eval(call.Arguments[0]);
                    var digits = call.Arguments.Count > 1 ? Eval(call.Arguments[1]) : 0m;
                    if (digits != decimal.Truncate(digits) || digits < 0 || digits > MaxRoundDigits)
                    {
                        throw new FormulaEvaluationException(FormulaOutcome.Invalid, $"ROUND takes 0 to {MaxRoundDigits} decimals.");
                    }

                    return Math.Round(value, (int)digits, MidpointRounding.AwayFromZero);
                }

                case "MIN":
                    return call.Arguments.Select(Eval).Min();
                case "MAX":
                    return call.Arguments.Select(Eval).Max();
                case "ABS":
                    return Math.Abs(Eval(call.Arguments[0]));
                default:
                    throw new FormulaEvaluationException(FormulaOutcome.Invalid, $"Unknown function '{call.Name}'.");
            }
        }

        private static decimal Power(decimal value, decimal exponent)
        {
            if (exponent == decimal.Truncate(exponent) && Math.Abs(exponent) <= MaxIntegerExponent)
            {
                var times = (int)Math.Abs(exponent);
                var result = 1m;
                for (var i = 0; i < times; i++)
                {
                    result *= value;
                }

                if (exponent >= 0)
                {
                    return result;
                }

                return result == 0m ? throw new FormulaEvaluationException(FormulaOutcome.DivisionByZero, "Zero raised to a negative power.") : 1m / result;
            }

            if (value < 0)
            {
                throw new FormulaEvaluationException(FormulaOutcome.Invalid, "A negative number has no fractional power.");
            }

            var computed = Math.Pow((double)value, (double)exponent);
            if (double.IsNaN(computed) || double.IsInfinity(computed) || Math.Abs(computed) > (double)decimal.MaxValue)
            {
                throw new FormulaEvaluationException(FormulaOutcome.Overflow, "The value is too large.");
            }

            return (decimal)computed;
        }
    }

    private sealed class FormulaSyntaxException(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }

    private sealed class FormulaEvaluationException(FormulaOutcome outcome, string detail) : Exception(detail)
    {
        public FormulaOutcome Outcome { get; } = outcome;

        public string Detail { get; } = detail;
    }
}

/// <summary>Builds the canonical message of a parse failure for problem details and the check endpoint.</summary>
public static class FormulaMessages
{
    public static string Describe(FormulaParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new StringBuilder(result.Error ?? "The formula is not valid.");
        if (result.Position is { } position)
        {
            text.Append(CultureInfo.InvariantCulture, $" (position {position})");
        }

        return text.ToString();
    }
}
