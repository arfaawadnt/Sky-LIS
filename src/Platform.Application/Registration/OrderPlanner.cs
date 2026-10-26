using Platform.Application.Catalogue;
using Platform.Application.Pricing;
using Platform.Contracts.Pricing;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration;

/// <summary>The patient an order is planned for: the gender drives restrictions and preparations; the id drives the frequency check.</summary>
public sealed record OrderPatient(Guid PatientId, string Gender);

/// <summary>
/// What to plan (intake §5 "Order planning"). <paramref name="ExistingServiceIds"/> are the live services of a saved
/// visit (duplicates, rules); <paramref name="ExcludeVisitId"/> keeps that visit out of the frequency check;
/// <paramref name="ForQuote"/> plans a price quote (no patient, no duplicates against a visit, no frequency check);
/// <paramref name="ExistingTests"/> are the tests of those live services with the service that orders each one
/// (test-level duplicates, validation O-3; <see cref="Visit.LiveTestHolders"/>).
/// </summary>
public sealed record OrderContext(
    OrderPatient? Patient,
    Guid? PayerId,
    Guid? ContractTierId,
    Guid? DiagnosisId,
    IReadOnlyList<OrderLineRequest> Lines,
    IReadOnlySet<Guid> ExistingServiceIds,
    Guid? ExcludeVisitId,
    DateTimeOffset NowUtc,
    int UtcOffsetMinutes,
    bool ForQuote = false,
    IReadOnlyDictionary<Guid, string>? ExistingTests = null);

/// <summary>A planned line: the domain item plus what the screen shows about it (for a formula operand, the code of its formula's service, LIS-CR-015).</summary>
public sealed record PlannedLine(
    VisitOrderItem Item,
    bool Priced,
    string? Precaution,
    string? AlertCondition,
    string? RuleNameEn,
    string? RuleNameAr,
    string? FormulaServiceCode = null);

/// <summary>The outcome of planning: lines to order, refusals, warnings and the preparation questions that apply.</summary>
public sealed record OrderPlan(
    IReadOnlyList<PlannedLine> Lines,
    IReadOnlyList<OrderIssue> Issues,
    IReadOnlyList<OrderWarning> Warnings,
    IReadOnlyList<PreparationQuestion> Preparations)
{
    public IReadOnlyList<VisitOrderItem> Items => [.. Lines.Select(line => line.Item)];

    public bool HasFrequencyAlerts => Warnings.Any(warning => warning.Code == RegistrationCodes.WarningFrequency);

    /// <summary>The first refusal as the coded exception Register answers; then the unconfirmed frequency alerts (REG-102).</summary>
    public void ThrowIfRefused(bool confirmFrequencyWarnings)
    {
        if (Issues.Count > 0)
        {
            var issue = Issues[0];
            throw new DomainException(issue.Code, issue.Message, FaultOf(issue.Code));
        }

        if (HasFrequencyAlerts && !confirmFrequencyWarnings)
        {
            var alert = Warnings.First(warning => warning.Code == RegistrationCodes.WarningFrequency);
            throw new DomainException(
                RegistrationCodes.FrequencyConfirmationRequired,
                $"'{alert.ServiceCode}' was ordered for this patient within {alert.WindowDays} days ({alert.LastAccession}); confirm the repeat order to continue.",   // the repeated test (A-11)
                DomainFault.Conflict);
        }
    }

    public static DomainFault FaultOf(string code) => code switch
    {
        RegistrationCodes.DuplicateService or RegistrationCodes.DuplicateTests or RegistrationCodes.FrequencyPrevented or RegistrationCodes.FrequencyConfirmationRequired => DomainFault.Conflict,
        _ => DomainFault.Validation,
    };
}

/// <summary>
/// The order planner shared by every entry path (walk-in, saved visit, booking conversion, batch members) and by the dry
/// run and the price quote: catalogue resolution, diagnosis sets, test rules, contract prices (BR-06), gender restriction,
/// duplicates (INV-REG-002), frequency check (BR-12), contract alerts, precautions, expected result dates (BR-13), tube
/// specs (BR-02), formula operands (LIS-CR-015) and preparation questions. Reads only (ports); constructed by the handlers.
/// </summary>
public sealed class OrderPlanner(ICatalogueLookup catalogue, IOrderRulesLookup rules, IPriceLookup prices, IVisitRepository visits, IFormulaOperandLookup formulas)
{
    private enum Origin
    {
        Manual,
        Diagnosis,
        Rule,
    }

    private sealed record Entry(Guid ServiceId, bool Reserved, Origin Origin, Guid? RuleId, string? RuleNameEn, string? RuleNameAr);

    public async Task<OrderPlan> PlanAsync(OrderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var issues = new List<OrderIssue>();
        var warnings = new List<OrderWarning>();

        // 1. requested services in order; a repeated id is a duplicate (REG-100)
        var entries = new List<Entry>();
        var seen = new HashSet<Guid>();
        foreach (var line in context.Lines ?? [])
        {
            if (line.ServiceId == Guid.Empty)
            {
                continue;
            }

            if (!seen.Add(line.ServiceId))
            {
                issues.Add(new OrderIssue(RegistrationCodes.DuplicateService, line.ServiceId, null, "The same service was entered twice."));
                continue;
            }

            entries.Add(new Entry(line.ServiceId, line.Reserved, Origin.Manual, null, null, null));
        }

        // 2. the diagnosis adds its service set
        if (context.DiagnosisId is { } diagnosisId && !context.ForQuote)
        {
            var diagnosis = await rules.DiagnosisAsync(diagnosisId, cancellationToken);
            foreach (var serviceId in diagnosis?.ServiceIds ?? [])
            {
                if (seen.Add(serviceId) && !context.ExistingServiceIds.Contains(serviceId))
                {
                    entries.Add(new Entry(serviceId, false, Origin.Diagnosis, null, null, null));
                }
            }
        }

        // 3. test rules add companions at their own price (LDM §3.8), never a service already present
        if (!context.ForQuote && entries.Count > 0)
        {
            var ordered = entries.Select(entry => entry.ServiceId).Concat(context.ExistingServiceIds).Distinct().ToList();
            foreach (var added in await rules.ServicesAddedByRulesAsync(ordered, cancellationToken))
            {
                if (seen.Add(added.ServiceId) && !context.ExistingServiceIds.Contains(added.ServiceId))
                {
                    entries.Add(new Entry(added.ServiceId, false, Origin.Rule, added.RuleId, added.RuleNameEn, added.RuleNameAr));
                }
            }
        }

        // 4. catalogue, members, tests, groups, sample types, prices
        var services = (await catalogue.GetServicesAsync([.. entries.Select(entry => entry.ServiceId)], cancellationToken)).ToDictionary(service => service.Id);
        var memberIds = services.Values.Where(service => service.Type == ServiceType.Package).SelectMany(service => service.MemberServiceIds).Where(id => !services.ContainsKey(id)).Distinct().ToList();
        if (memberIds.Count > 0)
        {
            foreach (var member in await catalogue.GetServicesAsync(memberIds, cancellationToken))
            {
                services[member.Id] = member;
            }
        }

        var testIds = services.Values.SelectMany(service => service.TestIds).Distinct().ToList();
        var tests = testIds.Count == 0 ? new Dictionary<Guid, CatalogueTestInfo>() : (await catalogue.GetTestsAsync(testIds, cancellationToken)).ToDictionary(test => test.Id);
        var groupIds = services.Values.Where(service => service.ClinicalGroupId.HasValue).Select(service => service.ClinicalGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? new Dictionary<Guid, CatalogueClinicalGroupInfo>() : (await catalogue.GetClinicalGroupsAsync(groupIds, cancellationToken)).ToDictionary(group => group.Id);
        var typeIds = services.Values.Where(service => service.SampleTypeId.HasValue).Select(service => service.SampleTypeId!.Value).Distinct().ToList();
        var sampleTypes = typeIds.Count == 0 ? new Dictionary<Guid, CatalogueSampleTypeInfo>() : (await catalogue.GetSampleTypesAsync(typeIds, cancellationToken)).ToDictionary(type => type.Id);

        var quotedIds = entries.Select(entry => entry.ServiceId).Where(services.ContainsKey).ToList();
        var quote = quotedIds.Count == 0
            ? new Dictionary<Guid, PriceQuoteLine>()
            : (await prices.QuoteAsync(context.PayerId, context.ContractTierId, quotedIds, cancellationToken)).GroupBy(line => line.ServiceId).ToDictionary(group => group.Key, group => group.First());

        // 5. per entry: refusals (manual) or skips (automatic), then the item
        var planned = new List<(Entry Entry, PlannedLine Line, IReadOnlyList<CatalogueServiceInfo> Parts)>();
        var plannedIds = new HashSet<Guid>();
        var orderedTests = new Dictionary<Guid, string>(context.ExistingTests ?? new Dictionary<Guid, string>());
        var repeatedTests = new Dictionary<Guid, string>();
        foreach (var entry in entries)
        {
            var refusal = Check(entry, services, tests, quote, context, plannedIds, out var service, out var parts)
                ?? CheckRepeatedTests(service!, parts, tests, orderedTests, repeatedTests);
            if (refusal is not null)
            {
                if (entry.Origin == Origin.Manual)
                {
                    issues.Add(refusal);
                }
                else if (service is not null && refusal.Code is not (RegistrationCodes.DuplicateService or RegistrationCodes.DuplicateTests))
                {
                    warnings.Add(Warning(RegistrationCodes.WarningSkippedCompanion, service, refusal.Message, null));
                }

                continue;
            }

            foreach (var id in parts.Select(part => part.Id))
            {
                plannedIds.Add(id);
            }

            var price = quote.GetValueOrDefault(service!.Id);
            var item = BuildItem(service, parts, tests, groups, sampleTypes, price, entry, context);
            planned.Add((entry, new PlannedLine(item, price?.Priced ?? false, Blank(service.Precaution), Blank(price?.AlertCondition), entry.RuleNameEn, entry.RuleNameAr), parts));
        }

        // 6. frequency check (BR-12) on the tests of every planned line
        var removedByFrequency = new HashSet<Guid>();
        if (context.Patient is { } patient && !context.ForQuote && planned.Count > 0)
        {
            await CheckFrequencyAsync(planned, tests, patient, context, issues, warnings, removedByFrequency, cancellationToken);
        }

        // 7. advisory warnings of the lines that stay
        var kept = planned.Where(entry => !entry.Line.Item.AllServiceIds.Any(id => removedByFrequency.Contains(id))).ToList();
        foreach (var (entry, line, _) in kept)
        {
            var item = line.Item;
            if (line.AlertCondition is { } alert)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningContractAlert, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, alert, null, null, null, null));
            }

            if (line.Precaution is { } precaution)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningPrecaution, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, precaution, null, null, null, null));
            }

            if (repeatedTests.TryGetValue(item.ServiceId, out var repeated))
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningRepeatedTests, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, repeated, null, null, null, null));
            }

            if (entry.Origin == Origin.Rule)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningAddedByRule, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, entry.RuleNameEn, entry.RuleNameAr, null, null, null));
            }
            else if (entry.Origin == Origin.Diagnosis)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningAddedByDiagnosis, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, null, null, null, null, null));
            }
        }

        // 8. formula operands (LIS-CR-015): each listed under the line whose formula reads it, price 0
        var lines = await WithFormulaOperandsAsync([.. kept.Select(entry => entry.Line)], services, tests, groups, sampleTypes, context, warnings, cancellationToken);

        // 9. preparation questions for every planned service (members and formula operands included)
        var preparationIds = lines.SelectMany(line => line.Item.AllServiceIds).Distinct().ToList();
        var preparations = preparationIds.Count == 0
            ? []
            : await rules.PreparationsForAsync(preparationIds, PreparationGenderOf(context.Patient?.Gender), cancellationToken);

        return new OrderPlan(lines, issues, warnings, preparations);
    }

    /// <summary>
    /// LIS-CR-015 M3b: for every formula test of a planned line (a test service, a profile, or a package's member service), the
    /// service the laboratory chose for each test the formula reads (<see cref="IFormulaOperandLookup"/>) is ordered as a
    /// <see cref="VisitLineKind.FormulaOperand"/> line at price 0 under that line, unless a live line of the visit or of this
    /// order already carries the test (package members included). One operand service is ordered once, for the first formula
    /// that needs it. These lines never meet the duplicate, unpriced, repeated-test or frequency checks: they are not the
    /// clerk's order. A chosen service that is unknown, inactive, not orderable or restricted for the patient's gender is
    /// skipped with REG-207. The operand lines follow their top-level line in the returned list.
    /// </summary>
    private async Task<List<PlannedLine>> WithFormulaOperandsAsync(
        List<PlannedLine> kept,
        Dictionary<Guid, CatalogueServiceInfo> services,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, CatalogueClinicalGroupInfo> groups,
        Dictionary<Guid, CatalogueSampleTypeInfo> sampleTypes,
        OrderContext context,
        List<OrderWarning> warnings,
        CancellationToken cancellationToken)
    {
        // the lines that may carry a formula: each top-level service and each member service of a package
        var carriers = new Dictionary<Guid, (VisitOrderItem Top, VisitOrderItem Carrier)>();
        foreach (var top in kept.Select(line => line.Item))
        {
            foreach (var carrier in top.Kind == VisitLineKind.Package ? top.Members : [top])
            {
                carriers.TryAdd(carrier.ServiceId, (top, carrier));
            }
        }

        var operands = carriers.Count == 0
            ? []
            : (await formulas.ForServicesAsync([.. carriers.Keys], cancellationToken))
                .Where(operand => operand.OperandServiceId is { } id && id != Guid.Empty && carriers.ContainsKey(operand.ServiceId))
                .Where(operand => !tests.TryGetValue(operand.FormulaTestId, out var formulaTest) || formulaTest.IsActive)
                .ToList();
        if (operands.Count == 0)
        {
            return kept;
        }

        await LoadOperandCatalogueAsync(operands, services, tests, groups, sampleTypes, cancellationToken);

        var carried = new HashSet<Guid>(context.ExistingTests?.Keys ?? []);
        var ordered = new HashSet<Guid>(context.ExistingServiceIds);
        foreach (var item in kept.Select(line => line.Item))
        {
            ordered.UnionWith(item.AllServiceIds);
            carried.UnionWith(item.Members.Prepend(item).SelectMany(part => part.TestIds));
        }

        var added = new Dictionary<Guid, List<PlannedLine>>();
        foreach (var operand in operands)
        {
            var serviceId = operand.OperandServiceId!.Value;
            if (carried.Contains(operand.OperandTestId) || ordered.Contains(serviceId))
            {
                continue;   // the test is already on the visit or in this order
            }

            var (top, carrier) = carriers[operand.ServiceId];
            if (!services.TryGetValue(serviceId, out var service))
            {
                continue;
            }

            var refusal = OperandRefusal(service, tests, context);
            if (refusal is not null)
            {
                warnings.Add(Warning(RegistrationCodes.WarningSkippedCompanion, service, $"'{service.Code}' cannot be ordered for the formula of '{carrier.ServiceCode}' ({operand.OperandTestCode}): {refusal}", null));
                continue;
            }

            var item = Single(service, tests, groups, sampleTypes, null, top.Reserved, VisitLineOrigin.Formula, null, context, VisitLineKind.FormulaOperand)
                with { FormulaServiceId = carrier.ServiceId, VisibleOnReceipt = operand.VisibleOnReceipt };
            ordered.Add(serviceId);
            carried.UnionWith(item.TestIds);
            carried.Add(operand.OperandTestId);
            if (!added.TryGetValue(top.ServiceId, out var under))
            {
                added[top.ServiceId] = under = [];
            }

            under.Add(new PlannedLine(item, true, Blank(service.Precaution), null, null, null, carrier.ServiceCode));
        }

        return [.. kept.SelectMany(line => added.TryGetValue(line.Item.ServiceId, out var under) ? under.Prepend(line) : [line])];
    }

    /// <summary>Why a service chosen for a formula operand cannot be ordered (null when it can).</summary>
    private static string? OperandRefusal(CatalogueServiceInfo service, Dictionary<Guid, CatalogueTestInfo> tests, OrderContext context)
    {
        if (!service.IsActive || !service.IsOrderable || service.Type is ServiceType.Package or ServiceType.Additional)
        {
            return "it is inactive or not orderable.";
        }

        if (context.Patient is { } patient)
        {
            var gender = GenderOf(patient.Gender);
            if (!Allows(service.Gender, gender) || service.TestIds.Any(testId => tests.TryGetValue(testId, out var test) && !Allows(test.Gender, gender)))
            {
                return $"it cannot be ordered for a {patient.Gender.ToLowerInvariant()} patient.";
            }
        }

        return null;
    }

    /// <summary>The catalogue rows the operand services need (service, tests, clinical group, sample type) that the plan has not read yet.</summary>
    private async Task LoadOperandCatalogueAsync(
        IReadOnlyList<CatalogueFormulaOperandInfo> operands,
        Dictionary<Guid, CatalogueServiceInfo> services,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, CatalogueClinicalGroupInfo> groups,
        Dictionary<Guid, CatalogueSampleTypeInfo> sampleTypes,
        CancellationToken cancellationToken)
    {
        var serviceIds = operands.Select(operand => operand.OperandServiceId!.Value).Where(id => !services.ContainsKey(id)).Distinct().ToList();
        if (serviceIds.Count > 0)
        {
            foreach (var service in await catalogue.GetServicesAsync(serviceIds, cancellationToken))
            {
                services[service.Id] = service;
            }
        }

        var chosen = operands.Select(operand => operand.OperandServiceId!.Value).Distinct().Where(services.ContainsKey).Select(id => services[id]).ToList();
        var testIds = chosen.SelectMany(service => service.TestIds).Where(id => !tests.ContainsKey(id)).Distinct().ToList();
        if (testIds.Count > 0)
        {
            foreach (var test in await catalogue.GetTestsAsync(testIds, cancellationToken))
            {
                tests[test.Id] = test;
            }
        }

        var groupIds = chosen.Where(service => service.ClinicalGroupId is { } id && !groups.ContainsKey(id)).Select(service => service.ClinicalGroupId!.Value).Distinct().ToList();
        if (groupIds.Count > 0)
        {
            foreach (var group in await catalogue.GetClinicalGroupsAsync(groupIds, cancellationToken))
            {
                groups[group.Id] = group;
            }
        }

        var typeIds = chosen.Where(service => service.SampleTypeId is { } id && !sampleTypes.ContainsKey(id)).Select(service => service.SampleTypeId!.Value).Distinct().ToList();
        if (typeIds.Count > 0)
        {
            foreach (var type in await catalogue.GetSampleTypesAsync(typeIds, cancellationToken))
            {
                sampleTypes[type.Id] = type;
            }
        }
    }

    private static OrderIssue? Check(
        Entry entry,
        Dictionary<Guid, CatalogueServiceInfo> services,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, PriceQuoteLine> quote,
        OrderContext context,
        HashSet<Guid> plannedIds,
        out CatalogueServiceInfo? service,
        out IReadOnlyList<CatalogueServiceInfo> parts)
    {
        parts = [];
        if (!services.TryGetValue(entry.ServiceId, out service))
        {
            return new OrderIssue(RegistrationCodes.ServiceUnknown, entry.ServiceId, null, "The service does not exist.");
        }

        if (!service.IsActive || !service.IsOrderable)
        {
            return new OrderIssue(RegistrationCodes.ServiceNotOrderable, service.Id, service.Code, $"'{service.Code}' is inactive or not orderable.");
        }

        var members = new List<CatalogueServiceInfo>();
        if (service.Type == ServiceType.Package)
        {
            foreach (var memberId in service.MemberServiceIds)
            {
                if (!services.TryGetValue(memberId, out var member) || !member.IsActive)
                {
                    return new OrderIssue(RegistrationCodes.ServiceNotOrderable, service.Id, service.Code, $"A service of package '{service.Code}' is inactive.");
                }

                members.Add(member);
            }
        }

        parts = [service, .. members];

        if (context.Patient is { } patient)
        {
            var gender = GenderOf(patient.Gender);
            foreach (var part in parts)
            {
                var restricted = !Allows(part.Gender, gender) || part.TestIds.Any(testId => tests.TryGetValue(testId, out var test) && !Allows(test.Gender, gender));
                if (restricted)
                {
                    return new OrderIssue(RegistrationCodes.ServiceGenderRestricted, service.Id, service.Code, $"'{part.Code}' cannot be ordered for a {patient.Gender.ToLowerInvariant()} patient.");
                }
            }
        }

        foreach (var part in parts)
        {
            if (context.ExistingServiceIds.Contains(part.Id) || plannedIds.Contains(part.Id))
            {
                return new OrderIssue(RegistrationCodes.DuplicateService, service.Id, service.Code, $"'{part.Code}' is already on this visit.");
            }
        }

        if (!context.ForQuote && (!quote.TryGetValue(service.Id, out var price) || !price.Priced))
        {
            return new OrderIssue(RegistrationCodes.ServiceUnpriced, service.Id, service.Code, $"'{service.Code}' has no price under this contract.");
        }

        return null;
    }

    /// <summary>
    /// Validation O-3 (assumption awaiting the business owner): a service whose every test is already on the visit through
    /// another service is refused (REG-109, naming each test and the service that orders it); a service that adds at least
    /// one new test is allowed and its repeated tests are remembered for the REG-206 warning. A service without tests
    /// (additional services) is never refused here. The tests of an accepted service join <paramref name="orderedTests"/>.
    /// </summary>
    private static OrderIssue? CheckRepeatedTests(
        CatalogueServiceInfo service,
        IReadOnlyList<CatalogueServiceInfo> parts,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, string> orderedTests,
        Dictionary<Guid, string> repeatedTests)
    {
        var own = new List<(Guid TestId, string Holder)>();
        foreach (var part in parts)
        {
            var holder = part.Id == service.Id ? service.Code : $"{service.Code} › {part.Code}";
            own.AddRange(part.TestIds.Where(testId => own.TrueForAll(known => known.TestId != testId)).Select(testId => (testId, holder)));
        }

        if (own.Count == 0)
        {
            return null;
        }

        var repeated = own.Where(test => orderedTests.ContainsKey(test.TestId)).ToList();
        if (repeated.Count > 0)
        {
            var detail = string.Join(", ", repeated.Select(test => HolderLabel(CodeOf(test.TestId, tests), orderedTests[test.TestId])));
            if (repeated.Count == own.Count)
            {
                return new OrderIssue(
                    RegistrationCodes.DuplicateTests,
                    service.Id,
                    service.Code,
                    $"Every test of '{service.Code}' is already on this visit: {detail}.",
                    detail);
            }

            repeatedTests[service.Id] = detail;
        }

        foreach (var (testId, holder) in own)
        {
            orderedTests.TryAdd(testId, holder);
        }

        return null;
    }

    /// <summary>"CREA (RENAL)" for a test ordered through another service; "CREA" when the service is the test itself.</summary>
    private static string HolderLabel(string testCode, string holder) =>
        string.Equals(testCode, holder, StringComparison.OrdinalIgnoreCase) ? testCode : $"{testCode} ({holder})";

    private static string CodeOf(Guid testId, Dictionary<Guid, CatalogueTestInfo> tests) => tests.TryGetValue(testId, out var test) ? test.Code : testId.ToString();

    private static VisitOrderItem BuildItem(
        CatalogueServiceInfo service,
        IReadOnlyList<CatalogueServiceInfo> parts,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, CatalogueClinicalGroupInfo> groups,
        Dictionary<Guid, CatalogueSampleTypeInfo> sampleTypes,
        PriceQuoteLine? price,
        Entry entry,
        OrderContext context)
    {
        var origin = entry.Origin switch
        {
            Origin.Rule => VisitLineOrigin.Rule,
            Origin.Diagnosis => VisitLineOrigin.Diagnosis,
            _ => VisitLineOrigin.Manual,
        };

        if (service.Type == ServiceType.Package)
        {
            var members = parts.Skip(1)
                .Select(member => Single(member, tests, groups, sampleTypes, null, false, origin, entry.RuleId, context, VisitLineKind.PackageMember))
                .ToList();
            return new VisitOrderItem(
                service.Id,
                service.Code,
                service.NameEn,
                service.NameAr,
                service.Type.ToString(),
                VisitLineKind.Package,
                null,
                [],
                price?.ListPrice ?? 0m,
                price?.PatientShare ?? 0m,
                price?.PayerShare ?? 0m,
                service.TurnaroundHours > 0
                    ? TurnaroundCalculator.ExpectedAt(context.NowUtc, service.TurnaroundHours, TurnaroundCalculator.EveryDay, context.UtcOffsetMinutes)
                    : TurnaroundCalculator.Latest(members.Select(member => member.ExpectedResultAtUtc)),
                entry.Reserved,
                origin,
                entry.RuleId,
                members);
        }

        return Single(service, tests, groups, sampleTypes, price, entry.Reserved, origin, entry.RuleId, context, VisitLineKind.Service);
    }

    private static VisitOrderItem Single(
        CatalogueServiceInfo service,
        Dictionary<Guid, CatalogueTestInfo> tests,
        Dictionary<Guid, CatalogueClinicalGroupInfo> groups,
        Dictionary<Guid, CatalogueSampleTypeInfo> sampleTypes,
        PriceQuoteLine? price,
        bool reserved,
        VisitLineOrigin origin,
        Guid? ruleId,
        OrderContext context,
        VisitLineKind kind)
    {
        var testIds = service.Type is ServiceType.Package or ServiceType.Additional ? [] : service.TestIds;
        return new VisitOrderItem(
            service.Id,
            service.Code,
            service.NameEn,
            service.NameAr,
            service.Type.ToString(),
            kind,
            TubeSpecOf(service, groups, sampleTypes),
            testIds,
            price?.ListPrice ?? 0m,
            price?.PatientShare ?? 0m,
            price?.PayerShare ?? 0m,
            ExpectedFor(service, testIds, tests, context),
            reserved,
            origin,
            ruleId,
            []);
    }

    /// <summary>BR-13: the service's turnaround when set, else each test's duration, on that test's days; the latest wins.</summary>
    public static DateTimeOffset? ExpectedFor(CatalogueServiceInfo service, IReadOnlyList<Guid> testIds, IReadOnlyDictionary<Guid, CatalogueTestInfo> tests, OrderContext context)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(context);
        return ExpectedFor(service, testIds, tests, context.NowUtc, context.UtcOffsetMinutes);
    }

    public static DateTimeOffset? ExpectedFor(CatalogueServiceInfo service, IReadOnlyList<Guid> testIds, IReadOnlyDictionary<Guid, CatalogueTestInfo> tests, DateTimeOffset startUtc, int utcOffsetMinutes)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(testIds);
        ArgumentNullException.ThrowIfNull(tests);
        var known = testIds.Where(tests.ContainsKey).Select(id => tests[id]).ToList();
        if (known.Count == 0)
        {
            return service.TurnaroundHours > 0 ? TurnaroundCalculator.ExpectedAt(startUtc, service.TurnaroundHours, TurnaroundCalculator.EveryDay, utcOffsetMinutes) : null;
        }

        return TurnaroundCalculator.Latest(known.Select(test => (DateTimeOffset?)TurnaroundCalculator.ExpectedAt(
            startUtc,
            service.TurnaroundHours > 0 ? service.TurnaroundHours : test.Timing.DurationHours,
            test.Timing.TestDays,
            utcOffsetMinutes)));
    }

    /// <summary>BR-02 key and label names of a sample-carrying service; null for Package, Additional or an incomplete catalogue row.</summary>
    public static TubeSpec? TubeSpecOf(CatalogueServiceInfo service, IReadOnlyDictionary<Guid, CatalogueClinicalGroupInfo> groups, IReadOnlyDictionary<Guid, CatalogueSampleTypeInfo> sampleTypes)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(sampleTypes);
        if (!Visit.SampleCarryingTypes.Contains(service.Type.ToString())
            || service.ClinicalGroupId is not { } groupId
            || service.SampleTypeId is not { } typeId
            || !groups.TryGetValue(groupId, out var group)
            || !sampleTypes.TryGetValue(typeId, out var sampleType))
        {
            return null;
        }

        var condition = service.SampleConditionId is { } conditionId ? sampleType.Conditions.FirstOrDefault(candidate => candidate.Id == conditionId) : null;

        // LIS-CR-015 M4: a compatible condition may join the tube of its active target (same type; the planner decides when)
        var mergesInto = condition?.MergesIntoConditionId is { } targetId && sampleType.Conditions.Any(candidate => candidate.Id == targetId && candidate.IsActive && candidate.MergesIntoConditionId is null)
            ? targetId
            : (Guid?)null;
        return new TubeSpec(
            groupId,
            typeId,
            service.SampleConditionId,
            group.UnitCode,
            group.NameEn,
            group.NameAr,
            sampleType.NameEn,
            sampleType.NameAr,
            condition?.NameEn,
            condition?.NameAr,
            mergesInto);
    }

    private async Task CheckFrequencyAsync(
        List<(Entry Entry, PlannedLine Line, IReadOnlyList<CatalogueServiceInfo> Parts)> planned,
        Dictionary<Guid, CatalogueTestInfo> tests,
        OrderPatient patient,
        OrderContext context,
        List<OrderIssue> issues,
        List<OrderWarning> warnings,
        HashSet<Guid> removedByFrequency,
        CancellationToken cancellationToken)
    {
        var checkedTests = planned
            .SelectMany(entry => entry.Parts.SelectMany(part => part.TestIds).Select(testId => (entry, testId)))
            .Where(pair => tests.TryGetValue(pair.testId, out var test) && test.Timing.FrequencyCheckDays is > 0)
            .ToList();
        if (checkedTests.Count == 0)
        {
            return;
        }

        var longest = checkedTests.Max(pair => tests[pair.testId].Timing.FrequencyCheckDays!.Value);
        var prior = await visits.PriorTestOrdersAsync(patient.PatientId, [.. checkedTests.Select(pair => pair.testId).Distinct()], context.NowUtc.AddDays(-longest), context.ExcludeVisitId, cancellationToken);
        if (prior.Count == 0)
        {
            return;
        }

        var reported = new HashSet<(Guid, Guid)>();
        foreach (var (entry, testId) in checkedTests)
        {
            var test = tests[testId];
            var days = test.Timing.FrequencyCheckDays!.Value;
            var last = prior.Where(order => order.TestId == testId && order.OrderedAtUtc >= context.NowUtc.AddDays(-days)).OrderByDescending(order => order.OrderedAtUtc).FirstOrDefault();
            if (last is null || !reported.Add((entry.Line.Item.ServiceId, testId)))
            {
                continue;
            }

            var item = entry.Line.Item;
            if (test.Timing.FrequencyAction == FrequencyCheckAction.Prevent)
            {
                var message = $"'{test.Code}' was ordered for this patient on {last.OrderedAtUtc:yyyy-MM-dd} ({last.Accession}); repeat orders within {days} days are not allowed.";
                if (entry.Entry.Origin == Origin.Manual)
                {
                    issues.Add(new OrderIssue(RegistrationCodes.FrequencyPrevented, item.ServiceId, item.ServiceCode, message));
                }
                else
                {
                    foreach (var id in item.AllServiceIds)
                    {
                        removedByFrequency.Add(id);
                    }

                    warnings.Add(new OrderWarning(RegistrationCodes.WarningSkippedCompanion, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, message, null, last.OrderedAtUtc, last.Accession, days));
                }

                continue;
            }

            // Validation A-11 (2026-10-11): the alert names the test that repeats; a member test of a profile or package
            // carries the service it came through ("CREA (RENAL)"). The line is still identified by ServiceId.
            warnings.Add(new OrderWarning(RegistrationCodes.WarningFrequency, item.ServiceId, RepeatedLabel(test, item.ServiceCode), item.ServiceNameEn, item.ServiceNameAr, test.Code, null, last.OrderedAtUtc, last.Accession, days));
        }
    }

    /// <summary>What a frequency alert names (BR-12): the repeated test, with the ordered service when the test came through it.</summary>
    public static string RepeatedLabel(CatalogueTestInfo test, string? serviceCode)
    {
        ArgumentNullException.ThrowIfNull(test);
        return string.IsNullOrEmpty(serviceCode) || string.Equals(test.Code, serviceCode, StringComparison.OrdinalIgnoreCase) ? test.Code : $"{test.Code} ({serviceCode})";
    }

    private static OrderWarning Warning(string code, CatalogueServiceInfo service, string? detail, string? detailAr) =>
        new(code, service.Id, service.Code, service.NameEn, service.NameAr, detail, detailAr, null, null, null);

    private static bool Allows(GenderRestriction restriction, GenderRestriction patient) =>
        restriction == GenderRestriction.Any || patient == GenderRestriction.Any || restriction == patient;

    private static GenderRestriction GenderOf(string? gender) => gender switch
    {
        "Male" => GenderRestriction.Male,
        "Female" => GenderRestriction.Female,
        _ => GenderRestriction.Any,
    };

    private static PreparationGender? PreparationGenderOf(string? gender) => gender switch
    {
        "Male" => PreparationGender.Male,
        "Female" => PreparationGender.Female,
        _ => null,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
