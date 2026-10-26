namespace Platform.Domain.Catalogue;

/// <summary>
/// One test a formula reads and what registration orders for it (change request LIS-CR-015, M3b / M3c): the service added to the
/// visit when the formula is registered without that test (<see cref="ServiceId"/>; null = nothing is ordered) and whether that
/// added line shows on the receipt (<see cref="VisibleOnReceipt"/>). Stored with the formula test, one per referenced code, in the
/// order the expression reads them.
/// </summary>
public sealed record FormulaOperand(string TestCode, Guid? ServiceId, bool VisibleOnReceipt);

/// <summary>A service that carries a referenced test as a direct member, as the operand rules need it.</summary>
/// <param name="MemberCount">Every member of the service (tests and services); a single-test service has one.</param>
public sealed record FormulaOperandCarrier(Guid ServiceId, ServiceType Type, bool IsActive, bool IsOrderable, int MemberCount);

/// <summary>
/// The services that may be ordered for one referenced test (CAT-067): the active, orderable services whose members include the
/// test; the default is the first active orderable <see cref="ServiceType.Test"/> service whose only member is the test, else none.
/// </summary>
public sealed record FormulaOperandChoices(string TestCode, IReadOnlyList<Guid> ServiceIds, Guid? DefaultServiceId)
{
    /// <summary>The choices from the carriers of the test, given in the laboratory's preference order (service sequence, code).</summary>
    public static FormulaOperandChoices From(string testCode, IEnumerable<FormulaOperandCarrier> carriers)
    {
        ArgumentNullException.ThrowIfNull(carriers);
        var eligible = carriers.Where(carrier => carrier.IsActive && carrier.IsOrderable).ToList();
        var fallback = eligible.FirstOrDefault(carrier => carrier is { Type: ServiceType.Test, MemberCount: 1 });
        return new FormulaOperandChoices(
            FormulaOperandRules.CleanCode(testCode),
            eligible.Select(carrier => carrier.ServiceId).Distinct().ToList(),
            fallback?.ServiceId);
    }

    public bool Allows(Guid serviceId) => ServiceIds.Contains(serviceId);
}

/// <summary>The normalisation of the operands of a formula (LIS-CR-015 §3).</summary>
public static class FormulaOperandRules
{
    public static string CleanCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Exactly one entry per reference, in reference order: a given entry keeps its service and receipt flag (the first one when a
    /// code is given twice), a missing one takes the default service and stays off the receipt, an entry for a code the formula no
    /// longer reads is dropped. A chosen service must be one of the choices of its test (CAT-067); an empty id means none.
    /// </summary>
    public static IReadOnlyList<FormulaOperand> Normalize(
        IEnumerable<string> references,
        IEnumerable<FormulaOperand> given,
        IReadOnlyCollection<FormulaOperandChoices> choices)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(choices);

        var byCode = new Dictionary<string, FormulaOperand>(StringComparer.Ordinal);
        foreach (var entry in given)
        {
            ArgumentNullException.ThrowIfNull(entry);
            byCode.TryAdd(CleanCode(entry.TestCode), entry);
        }

        var choicesByCode = new Dictionary<string, FormulaOperandChoices>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            choicesByCode.TryAdd(CleanCode(choice.TestCode), choice);
        }

        var operands = new List<FormulaOperand>();
        foreach (var code in references.Select(CleanCode).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal))
        {
            choicesByCode.TryGetValue(code, out var choice);
            if (!byCode.TryGetValue(code, out var entry))
            {
                operands.Add(new FormulaOperand(code, choice?.DefaultServiceId, false));
                continue;
            }

            var serviceId = entry.ServiceId == Guid.Empty ? null : entry.ServiceId;
            if (serviceId is { } id && (choice is null || !choice.Allows(id)))
            {
                CatalogueAGuard.Refuse(LabTestCodes.FormulaOperandServiceInvalid, $"The service chosen for [{code}] does not carry that test.");
            }

            operands.Add(new FormulaOperand(code, serviceId, serviceId is not null && entry.VisibleOnReceipt));   // nothing ordered: nothing on the receipt
        }

        return operands;
    }
}
