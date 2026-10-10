namespace Platform.Domain.Catalogue;

/// <summary>A service a rule adds to an order.</summary>
public sealed record TestRuleAddition(Guid RuleId, Guid ServiceId);

/// <summary>One finding of the rule validation (T5): an error refuses the save, a warning only informs.</summary>
public sealed record TestRuleFinding(string Code, bool IsError, Guid? ServiceId, Guid? OtherRuleId);

/// <summary>
/// The behaviour of a set of test rules (intake LIS-INT-CTB-001 A1): applying them to an order and checking a
/// candidate rule against the others ("Check Rule", LDM §3.8). Pure functions over loaded rules.
/// </summary>
public static class TestRuleEngine
{
    /// <summary>
    /// The services the active rules add to an order, in firing order. A rule fires once, when all its triggers are
    /// present; what it adds can trigger further rules (chaining) until nothing changes; a service already present is
    /// never added again, so the expansion always terminates.
    /// </summary>
    public static IReadOnlyList<TestRuleAddition> Expand(IEnumerable<TestRule> rules, IEnumerable<Guid> orderedServiceIds)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(orderedServiceIds);

        var active = rules.Where(rule => rule.IsActive).OrderBy(rule => rule.Id).ToList();
        var present = new HashSet<Guid>(orderedServiceIds);
        var fired = new HashSet<Guid>();
        var additions = new List<TestRuleAddition>();

        bool changed;
        do
        {
            changed = false;
            foreach (var rule in active)
            {
                if (fired.Contains(rule.Id) || !rule.IsTriggeredBy(present))
                {
                    continue;
                }

                fired.Add(rule.Id);
                changed = true;
                foreach (var serviceId in rule.AddedServiceIds)
                {
                    if (present.Add(serviceId))
                    {
                        additions.Add(new TestRuleAddition(rule.Id, serviceId));
                    }
                }
            }
        }
        while (changed);

        return additions;
    }

    /// <summary>The errors of a rule's own shape: no trigger (CAT-240), no added service (CAT-241), a service on both sides (CAT-242, one per service).</summary>
    public static IReadOnlyList<TestRuleFinding> ShapeFindings(IReadOnlyCollection<Guid> triggerServiceIds, IReadOnlyCollection<Guid> addedServiceIds)
    {
        ArgumentNullException.ThrowIfNull(triggerServiceIds);
        ArgumentNullException.ThrowIfNull(addedServiceIds);

        var findings = new List<TestRuleFinding>();
        if (triggerServiceIds.Count == 0)
        {
            findings.Add(new TestRuleFinding(CatalogueBCodes.TestRuleTriggersRequired, true, null, null));
        }

        if (addedServiceIds.Count == 0)
        {
            findings.Add(new TestRuleFinding(CatalogueBCodes.TestRuleAddedRequired, true, null, null));
        }

        findings.AddRange(triggerServiceIds.Intersect(addedServiceIds).Select(serviceId => new TestRuleFinding(CatalogueBCodes.TestRuleServiceOnBothSides, true, serviceId, null)));
        return findings;
    }

    /// <summary>
    /// Validation of a candidate rule against the other active rules (T5): its shape errors, then warnings for another
    /// rule with exactly the same triggers (CAT-243) and for rules that feed back into one of its triggers (CAT-244).
    /// </summary>
    public static IReadOnlyList<TestRuleFinding> Analyze(Guid? ruleId, IReadOnlyCollection<Guid> triggerServiceIds, IReadOnlyCollection<Guid> addedServiceIds, IEnumerable<TestRule> otherRules)
    {
        ArgumentNullException.ThrowIfNull(otherRules);
        var findings = ShapeFindings(triggerServiceIds, addedServiceIds).ToList();
        var others = otherRules.Where(rule => rule.IsActive && rule.Id != ruleId).ToList();

        findings.AddRange(others
            .Where(rule => CatalogueBGuard.SameSet(rule.TriggerServiceIds, triggerServiceIds))
            .Select(rule => new TestRuleFinding(CatalogueBCodes.TestRuleDuplicateTriggers, false, null, rule.Id)));

        if (addedServiceIds.Count > 0)
        {
            var triggers = triggerServiceIds.ToHashSet();
            foreach (var addition in Expand(others, addedServiceIds).Where(addition => triggers.Contains(addition.ServiceId)))
            {
                findings.Add(new TestRuleFinding(CatalogueBCodes.TestRuleChainLoop, false, addition.ServiceId, addition.RuleId));
            }
        }

        return findings;
    }

    /// <summary>The English message of a finding code (the client translates by code).</summary>
    public static string Describe(string code) => code switch
    {
        CatalogueBCodes.TestRuleTriggersRequired => "A test rule needs at least one trigger service.",
        CatalogueBCodes.TestRuleAddedRequired => "A test rule needs at least one added service.",
        CatalogueBCodes.TestRuleServiceOnBothSides => "A service cannot be both a trigger and an added service.",
        CatalogueBCodes.TestRuleDuplicateTriggers => "Another active rule has exactly the same trigger services.",
        CatalogueBCodes.TestRuleChainLoop => "Other rules add one of this rule's triggers back: the rules feed each other.",
        CatalogueBCodes.TestRuleInactiveService => "A linked service is inactive.",
        CatalogueBCodes.ServiceUnknown => "A linked service does not exist in this laboratory's catalogue.",
        _ => "The rule is not valid.",
    };
}
