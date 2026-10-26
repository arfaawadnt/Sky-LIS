using Platform.Contracts.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>A test a formula reads, with the services that carry it as a direct member (LIS-CR-015), in sequence then code order.</summary>
public sealed record FormulaOperandTestRead(Guid Id, string Code, string NameEn, string NameAr, bool IsActive, IReadOnlyList<FormulaOperandServiceRead> Services)
{
    public FormulaOperandChoices Choices => FormulaOperandChoices.From(Code, Services.Select(service => service.Carrier));
}

/// <summary>A service carrying a referenced test: identity, type, activity, orderability and its number of members.</summary>
public sealed record FormulaOperandServiceRead(Guid Id, string Code, string NameEn, string NameAr, ServiceType Type, bool IsActive, bool IsOrderable, int MemberCount)
{
    public FormulaOperandCarrier Carrier => new(Id, Type, IsActive, IsOrderable, MemberCount);
}

/// <summary>The operand choices of a formula as the editor and the save need them (LIS-CR-015).</summary>
public static class FormulaOperandChoiceViews
{
    /// <summary>Per reference, in reference order: the test (when known), the services that may be ordered for it and the default.</summary>
    public static IReadOnlyList<FormulaOperandChoice> For(IEnumerable<string> references, IReadOnlyCollection<FormulaOperandTestRead> tests)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(tests);
        var byCode = tests.GroupBy(test => test.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return references.Select(FormulaOperandRules.CleanCode).Distinct(StringComparer.Ordinal).Select(code =>
        {
            if (!byCode.TryGetValue(code, out var test))
            {
                return new FormulaOperandChoice(code, null, null, null, [], null);
            }

            var choices = test.Choices;
            var options = test.Services
                .Where(service => choices.Allows(service.Id))
                .GroupBy(service => service.Id)
                .Select(group => group.First())
                .Select(service => new FormulaOperandServiceOption(service.Id, service.Code, service.NameEn, service.NameAr, service.Type.ToString()))
                .ToList();
            return new FormulaOperandChoice(code, test.Id, test.NameEn, test.NameAr, options, choices.DefaultServiceId);
        }).ToList();
    }
}
