using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue.Services;

/// <summary>The verdict on a formula for a test: valid with its normalised text and references, or the refusal code, message and position.</summary>
public sealed record FormulaVerdict(bool Valid, string? Normalized, IReadOnlyList<string> References, string? Code, string? Message, int? Position)
{
    public static FormulaVerdict Ok(string normalized, IReadOnlyList<string> references) => new(true, normalized, references, null, null, null);

    public static FormulaVerdict Fail(string code, string message, int? position = null) => new(false, null, [], code, message, position);
}

/// <summary>
/// INV-CAT-009 (T17 and the check of T20): syntax (CAT-063), every reference is an active test of the tenant (CAT-064),
/// never the test itself (CAT-065), and no cycle through other formulas (CAT-066). Shared by the command and the
/// check query, so the editor shows exactly the refusal the save would give.
/// </summary>
internal static class LabTestFormulaRules
{
    public static async Task<FormulaVerdict> CheckAsync(IFormulaEvaluator evaluator, ILabTestRepository tests, string expression, Guid? testId, CancellationToken cancellationToken)
    {
        var parsed = evaluator.Parse(expression);
        if (!parsed.IsValid)
        {
            return FormulaVerdict.Fail(LabTestCodes.FormulaInvalid, FormulaMessages.Describe(parsed), parsed.Position);
        }

        var graph = await tests.FormulaGraphAsync(cancellationToken);
        var selfCode = testId is { } id ? graph.FirstOrDefault(node => node.Id == id)?.Code : null;
        if (selfCode is not null && parsed.References.Contains(selfCode, StringComparer.Ordinal))
        {
            return FormulaVerdict.Fail(LabTestCodes.FormulaSelfReference, $"The formula reads its own test [{selfCode}].");
        }

        var active = graph.Where(node => node.IsActive).Select(node => node.Code).ToHashSet(StringComparer.Ordinal);
        var unknown = parsed.References.Where(code => !active.Contains(code)).ToList();
        if (unknown.Count > 0)
        {
            return FormulaVerdict.Fail(LabTestCodes.FormulaUnknownReference, $"Unknown or inactive test code(s): {string.Join(", ", unknown.Select(code => $"[{code}]"))}.");
        }

        if (selfCode is not null)
        {
            var edges = graph.ToDictionary(node => node.Code, node => node.References, StringComparer.Ordinal);
            edges[selfCode] = parsed.References;
            var path = FindPathBack(edges, selfCode);
            if (path is not null)
            {
                return FormulaVerdict.Fail(LabTestCodes.FormulaCycle, $"The formula would compute in a circle: {string.Join(" → ", path.Select(code => $"[{code}]"))}.");
            }
        }

        return FormulaVerdict.Ok(parsed.Normalized!, parsed.References);
    }

    /// <summary>A path start → … → start through the formula references, or null.</summary>
    private static List<string>? FindPathBack(IReadOnlyDictionary<string, IReadOnlyList<string>> edges, string start)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string> { start };
        return Visit(start) ? path : null;

        bool Visit(string code)
        {
            if (!edges.TryGetValue(code, out var next))
            {
                return false;
            }

            foreach (var reference in next)
            {
                if (string.Equals(reference, start, StringComparison.Ordinal))
                {
                    path.Add(reference);
                    return true;
                }

                if (!visited.Add(reference))
                {
                    continue;
                }

                path.Add(reference);
                if (Visit(reference))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }
}
