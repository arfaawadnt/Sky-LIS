using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// Every <c>EF.Functions.ILike</c> names its escape character (validation 2026-10-11, B-08). Npgsql sends a two-argument
/// <c>ILike(x, pattern)</c> as <c>ILIKE pattern ESCAPE ''</c>, so a pattern whose wildcards were escaped with a backslash
/// stops matching a typed <c>_</c> or <c>%</c>: the search silently finds nothing. The search helpers of the modules escape
/// with a backslash, so every call passes <c>"\\"</c>.
/// </summary>
public sealed class SearchPatternGateTests
{
    [Fact]
    public void Every_ILike_call_passes_its_escape_character()
    {
        var source = Path.Combine(SolutionAssemblies.RepositoryRoot, "src");
        var offenders = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => CallsWithoutEscape(File.ReadAllText(path)).Select(line => $"{Path.GetRelativePath(source, path)}:{line}"))
            .ToList();

        Assert.True(offenders.Count == 0, "ILike without an escape argument (use EF.Functions.ILike(x, pattern, \"\\\\\")):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_gate_finds_a_two_argument_call_and_accepts_a_three_argument_one()
    {
        const string Wrong = "q.Where(x => EF.Functions.ILike(x.Name, Pattern(\"a,b\")) ||\n EF.Functions.ILike(x.Code, pattern, \"\\\\\"));";

        Assert.Equal([1], CallsWithoutEscape(Wrong));
        Assert.Empty(CallsWithoutEscape("EF.Functions.ILike(x.Name, Make(p, q), \"\\\\\")"));
    }

    /// <summary>The 1-based lines of the <c>ILike(</c> calls with fewer than three top-level arguments.</summary>
    internal static IReadOnlyList<int> CallsWithoutEscape(string text)
    {
        var lines = new List<int>();
        var index = 0;
        while ((index = text.IndexOf("ILike(", index, StringComparison.Ordinal)) >= 0)
        {
            var depth = 0;
            var commas = 0;
            var position = index + "ILike".Length;
            for (; position < text.Length; position++)
            {
                var c = text[position];
                if (c == '"')
                {
                    position++;
                    while (position < text.Length && text[position] != '"')
                    {
                        position += text[position] == '\\' ? 2 : 1;
                    }
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && --depth == 0)
                {
                    break;
                }
                else if (c == ',' && depth == 1)
                {
                    commas++;
                }
            }

            if (commas < 2)
            {
                lines.Add(text.AsSpan(0, index).Count('\n') + 1);
            }

            index = position;
        }

        return lines;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
