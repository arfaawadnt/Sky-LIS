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
/// <paramref name="ForQuote"/> plans a price quote (no patient, no duplicates against a visit, no frequency check).
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
    bool ForQuote = false);

/// <summary>A planned line: the domain item plus what the screen shows about it.</summary>
public sealed record PlannedLine(
    VisitOrderItem Item,
    bool Priced,
    string? Precaution,
    string? AlertCondition,
    string? RuleNameEn,
    string? RuleNameAr);

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
                $"'{alert.ServiceCode}' was ordered for this patient within {alert.WindowDays} days ({alert.LastAccession}); confirm the repeat order to continue.",
                DomainFault.Conflict);
        }
    }

    public static DomainFault FaultOf(string code) => code switch
    {
        RegistrationCodes.DuplicateService or RegistrationCodes.FrequencyPrevented or RegistrationCodes.FrequencyConfirmationRequired => DomainFault.Conflict,
        _ => DomainFault.Validation,
    };
}

/// <summary>
/// The order planner shared by every entry path (walk-in, saved visit, booking conversion, batch members) and by the dry
/// run and the price quote: catalogue resolution, diagnosis sets, test rules, contract prices (BR-06), gender restriction,
/// duplicates (INV-REG-002), frequency check (BR-12), contract alerts, precautions, expected result dates (BR-13), tube
/// specs (BR-02) and preparation questions. Reads only (ports); constructed by the handlers.
/// </summary>
public sealed class OrderPlanner(ICatalogueLookup catalogue, IOrderRulesLookup rules, IPriceLookup prices, IVisitRepository visits)
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
        foreach (var entry in entries)
        {
            var refusal = Check(entry, services, tests, quote, context, plannedIds, out var service, out var parts);
            if (refusal is not null)
            {
                if (entry.Origin == Origin.Manual)
                {
                    issues.Add(refusal);
                }
                else if (service is not null && refusal.Code != RegistrationCodes.DuplicateService)
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

            if (entry.Origin == Origin.Rule)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningAddedByRule, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, entry.RuleNameEn, entry.RuleNameAr, null, null, null));
            }
            else if (entry.Origin == Origin.Diagnosis)
            {
                warnings.Add(new OrderWarning(RegistrationCodes.WarningAddedByDiagnosis, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, null, null, null, null, null));
            }
        }

        // 8. preparation questions for every planned service (members included)
        var preparationIds = kept.SelectMany(entry => entry.Line.Item.AllServiceIds).Distinct().ToList();
        var preparations = preparationIds.Count == 0
            ? []
            : await rules.PreparationsForAsync(preparationIds, PreparationGenderOf(context.Patient?.Gender), cancellationToken);

        return new OrderPlan([.. kept.Select(entry => entry.Line)], issues, warnings, preparations);
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
            condition?.NameAr);
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

            warnings.Add(new OrderWarning(RegistrationCodes.WarningFrequency, item.ServiceId, item.ServiceCode, item.ServiceNameEn, item.ServiceNameAr, test.Code, null, last.OrderedAtUtc, last.Accession, days));
        }
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
