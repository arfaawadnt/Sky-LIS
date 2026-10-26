using Microsoft.EntityFrameworkCore;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// The adapter of <see cref="IFormulaOperandLookup"/> (LIS-CR-015, package P-FCT): for the given services, the formula tests among
/// their direct test members (a test service's own test, a profile's tests) with the operands stored on each test, the operand test
/// resolved by code in the tenant (inactive or unknown operand tests are skipped). A formula saved before the change (no stored
/// operands) gets, per reference, the default service (the active orderable single-test service of the test) off the receipt, so
/// an existing formula still pulls its operands. Rows come per service in the given order, then member order, then reference order.
/// </summary>
internal sealed class FormulaOperandLookup(AppDbContext context) : IFormulaOperandLookup
{
    public async Task<IReadOnlyList<CatalogueFormulaOperandInfo>> ForServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var ids = serviceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var formulas = await (
            from member in context.Set<ServiceMember>().AsNoTracking()
            join test in context.Set<LabTest>().AsNoTracking().IgnoreAutoIncludes()
                on new { member.TenantId, Id = member.MemberId } equals new { test.TenantId, test.Id }
            where member.Kind == ServiceMemberKind.Test
                && ids.Contains(member.ServiceId)
                && test.Type == LabTestType.Formula
                && test.FormulaExpression != null
            select new { member.ServiceId, member.Order, TestId = test.Id, test.FormulaReferences, test.FormulaOperands })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (formulas.Count == 0)
        {
            return [];
        }

        var codes = formulas.SelectMany(formula => formula.FormulaOperands.Count > 0 ? formula.FormulaOperands.Select(operand => operand.TestCode) : formula.FormulaReferences)
            .Select(FormulaOperandRules.CleanCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var operandTests = (await FormulaOperandQueries.TestsAsync(context, codes, cancellationToken).ConfigureAwait(false))
            .ToDictionary(test => test.Code, StringComparer.Ordinal);

        var position = ids.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        var rows = new List<CatalogueFormulaOperandInfo>();
        foreach (var formula in formulas.OrderBy(formula => position[formula.ServiceId]).ThenBy(formula => formula.Order))
        {
            var operands = formula.FormulaOperands.Count > 0
                ? formula.FormulaOperands
                : formula.FormulaReferences.Select(code => new FormulaOperand(
                    code,
                    operandTests.TryGetValue(FormulaOperandRules.CleanCode(code), out var known) ? known.Choices.DefaultServiceId : null,
                    false)).ToList();

            foreach (var operand in operands)
            {
                if (!operandTests.TryGetValue(FormulaOperandRules.CleanCode(operand.TestCode), out var operandTest) || !operandTest.IsActive)
                {
                    continue;
                }

                rows.Add(new CatalogueFormulaOperandInfo(formula.ServiceId, formula.TestId, operandTest.Id, operandTest.Code, operand.ServiceId, operand.VisibleOnReceipt));
            }
        }

        return rows;
    }
}
