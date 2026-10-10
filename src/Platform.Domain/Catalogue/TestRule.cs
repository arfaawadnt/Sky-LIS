using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// An auto-add rule <c>{A} + {B} &gt;&gt; {C} + {D}</c> (SRS CP-52; LDM §3.8 "Test Rules"): when every trigger
/// service is ordered, registration adds the added services, each at its own price. A rule is neither a price (that
/// is a package) nor a computation (that is a formula). No service is both a trigger and an added service
/// (INV-CTB-006).
/// </summary>
public sealed class TestRule : CatalogueBRecord
{
    private TestRule()
    {
        Name = null!;
        TriggerServiceIds = [];
        AddedServiceIds = [];
    }

    private TestRule(Guid id)
        : base(id)
    {
        Name = null!;
        TriggerServiceIds = [];
        AddedServiceIds = [];
    }

    /// <summary>English name unique per tenant (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    /// <summary>The clinical group (Catalogue A) the rule is filed under; optional.</summary>
    public Guid? ClinicalGroupId { get; private set; }

    public Guid[] TriggerServiceIds { get; private set; }

    public Guid[] AddedServiceIds { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static TestRule Create(LocalizedText name, Guid? clinicalGroupId, IEnumerable<Guid>? triggerServiceIds, IEnumerable<Guid>? addedServiceIds, DateTimeOffset nowUtc)
    {
        var rule = new TestRule(Guid.CreateVersion7());
        rule.Update(name, clinicalGroupId, triggerServiceIds, addedServiceIds);
        rule.Raise(new TestRuleCreated(rule.Id, rule.Name.En, nowUtc));
        return rule;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText name, Guid? clinicalGroupId, IEnumerable<Guid>? triggerServiceIds, IEnumerable<Guid>? addedServiceIds)
    {
        ArgumentNullException.ThrowIfNull(name);
        var triggers = CatalogueBGuard.Ids(triggerServiceIds);
        var added = CatalogueBGuard.Ids(addedServiceIds);
        var findings = TestRuleEngine.ShapeFindings(triggers, added);
        if (findings.Count > 0)
        {
            var first = findings[0];
            throw new DomainException(first.Code, TestRuleEngine.Describe(first.Code), DomainFault.Validation);
        }

        Name = name;
        ClinicalGroupId = clinicalGroupId == Guid.Empty ? null : clinicalGroupId;
        TriggerServiceIds = triggers;
        AddedServiceIds = added;
    }

    /// <summary>Every trigger is among the services present in the order.</summary>
    public bool IsTriggeredBy(IReadOnlySet<Guid> presentServiceIds)
    {
        ArgumentNullException.ThrowIfNull(presentServiceIds);
        return TriggerServiceIds.Length > 0 && TriggerServiceIds.All(presentServiceIds.Contains);
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new TestRuleDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new TestRuleReactivated(Id, nowUtc);
}
