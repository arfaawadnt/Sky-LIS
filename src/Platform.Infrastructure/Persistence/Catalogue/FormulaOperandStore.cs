using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// The jsonb shape of <see cref="LabTest.FormulaOperands"/> (column <c>formula_operands</c>, LIS-CR-015):
/// <c>[{"testCode":"GLU","serviceId":"…"|null,"visibleOnReceipt":false}]</c>, <c>[]</c> when none; compared by its JSON text.
/// </summary>
internal static class FormulaOperandJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<IReadOnlyList<FormulaOperand>, string> Converter = new(operands => Text(operands), json => Restore(json));

    public static readonly ValueComparer<IReadOnlyList<FormulaOperand>> Comparer = new(
        (left, right) => Text(left) == Text(right),
        operands => Text(operands).GetHashCode(StringComparison.Ordinal),
        operands => Restore(Text(operands)));

    public static string Text(IReadOnlyList<FormulaOperand>? operands) =>
        JsonSerializer.Serialize((operands ?? []).Select(operand => new StoredOperand(operand.TestCode, operand.ServiceId, operand.VisibleOnReceipt)).ToList(), Options);

    public static IReadOnlyList<FormulaOperand> Restore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var stored = JsonSerializer.Deserialize<List<StoredOperand>>(json, Options) ?? [];
        return stored.Where(operand => !string.IsNullOrWhiteSpace(operand.TestCode))
            .Select(operand => new FormulaOperand(operand.TestCode!, operand.ServiceId, operand.VisibleOnReceipt))
            .ToList();
    }

    private sealed record StoredOperand(string? TestCode, Guid? ServiceId, bool VisibleOnReceipt);
}

/// <summary>The reads the formula editor, the save and the operand lookup share (LIS-CR-015), bounded to the current tenant.</summary>
internal static class FormulaOperandQueries
{
    /// <summary>The tests with the given codes and the services carrying each as a direct member, in sequence then code order.</summary>
    public static async Task<IReadOnlyList<FormulaOperandTestRead>> TestsAsync(AppDbContext context, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(codes);
        var wanted = codes.Select(FormulaOperandRules.CleanCode).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var tests = await context.Set<LabTest>().AsNoTracking().IgnoreAutoIncludes()
            .Where(test => wanted.Contains(test.Code))
            .Select(test => new { test.Id, test.Code, NameEn = test.Name.En, NameAr = test.Name.Ar, test.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = tests.Select(test => test.Id).ToList();
        var carriers = await (
            from member in context.Set<ServiceMember>().AsNoTracking()
            join service in context.Set<Service>().AsNoTracking().IgnoreAutoIncludes()
                on new { member.TenantId, Id = member.ServiceId } equals new { service.TenantId, service.Id }
            where member.Kind == ServiceMemberKind.Test && ids.Contains(member.MemberId)
            orderby service.Sequence, service.Code
            select new
            {
                TestId = member.MemberId,
                service.Id,
                service.Code,
                NameEn = service.Name.En,
                NameAr = service.Name.Ar,
                service.Type,
                service.IsActive,
                service.IsOrderable,
                MemberCount = context.Set<ServiceMember>().Count(other => other.TenantId == service.TenantId && other.ServiceId == service.Id),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var byTest = carriers.ToLookup(carrier => carrier.TestId);
        return tests.OrderBy(test => test.Code, StringComparer.Ordinal).Select(test => new FormulaOperandTestRead(
            test.Id,
            test.Code,
            test.NameEn,
            test.NameAr,
            test.IsActive,
            byTest[test.Id].Select(carrier => new FormulaOperandServiceRead(carrier.Id, carrier.Code, carrier.NameEn, carrier.NameAr, carrier.Type, carrier.IsActive, carrier.IsOrderable, carrier.MemberCount)).ToList()))
            .ToList();
    }
}
