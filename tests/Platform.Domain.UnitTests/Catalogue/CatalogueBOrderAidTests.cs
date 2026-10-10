using Platform.Domain.Catalogue;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenarios of intake LIS-INT-CTB-001 §16 for the order aids: preparations, diagnoses, shortcut sets, test rules (one test per transition, refused precondition and invariant).</summary>
public sealed class CatalogueBOrderAidTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cbc = Guid.CreateVersion7();
    private static readonly Guid Glucose = Guid.CreateVersion7();
    private static readonly Guid Lipids = Guid.CreateVersion7();

    private static LocalizedText Text(string en) => new(en, "نص " + en);

    [Fact]
    public void S_CTB_T1_ok_preparation_is_created_active_with_an_upper_case_code_and_announced()
    {
        var preparation = Preparation.Create(" fast ", Text("Fasting hours?"), PreparationAnswerType.OptionList, ["8 h", " 12 h ", ""], PreparationGender.Any, [Cbc, Glucose, Cbc, Guid.Empty], Now);

        Assert.True(preparation.IsActive);
        Assert.Equal("FAST", preparation.Code);
        Assert.Equal(["8 h", "12 h"], preparation.Options);
        Assert.Equal([Cbc, Glucose], preparation.ServiceIds);
        var raised = Assert.IsType<PreparationCreated>(Assert.Single(preparation.DomainEvents));
        Assert.Equal((preparation.Id, "FAST", "Fasting hours?"), (raised.PreparationId, raised.Code, raised.NameEn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-AB")]
    [InlineData("A B")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void INV_CTB_001_an_invalid_code_is_refused_CAT_200(string code)
    {
        var refusal = Assert.Throws<DomainException>(() => Preparation.Create(code, Text("Q"), PreparationAnswerType.Text, null, PreparationGender.Any, null, Now));

        Assert.Equal(CatalogueBCodes.CodeInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void S_CTB_T1_options_an_option_list_needs_two_different_options_CAT_210_211()
    {
        Assert.Equal(CatalogueBCodes.PreparationOptionsRequired, Assert.Throws<DomainException>(() => Preparation.Create("P1", Text("Q"), PreparationAnswerType.OptionList, ["only"], PreparationGender.Any, null, Now)).Code);
        Assert.Equal(CatalogueBCodes.PreparationOptionInvalid, Assert.Throws<DomainException>(() => Preparation.Create("P1", Text("Q"), PreparationAnswerType.OptionList, ["Yes", "yes"], PreparationGender.Any, null, Now)).Code);
        Assert.Equal(CatalogueBCodes.PreparationOptionInvalid, Assert.Throws<DomainException>(() => Preparation.Create("P1", Text("Q"), PreparationAnswerType.OptionList, ["a", new string('x', 101)], PreparationGender.Any, null, Now)).Code);
    }

    [Fact]
    public void INV_CTB_004_other_answer_types_carry_no_options()
    {
        var preparation = Preparation.Create("P2", Text("Medication?"), PreparationAnswerType.OptionList, ["a", "b"], PreparationGender.Any, null, Now);

        preparation.Update(Text("Medication?"), PreparationAnswerType.Text, ["a", "b"], PreparationGender.Female, [Lipids]);

        Assert.Empty(preparation.Options);
        Assert.Equal(PreparationGender.Female, preparation.Gender);
        Assert.Equal("P2", preparation.Code);
    }

    [Fact]
    public void A_preparation_applies_to_its_services_or_to_all_and_to_the_patients_gender()
    {
        var forAll = Preparation.Create("ALL", Text("Q"), PreparationAnswerType.YesNo, null, PreparationGender.Any, null, Now);
        var forGlucose = Preparation.Create("GLU", Text("Q"), PreparationAnswerType.YesNo, null, PreparationGender.Any, [Glucose], Now);
        var forWomen = Preparation.Create("PREG", Text("Pregnant?"), PreparationAnswerType.YesNo, null, PreparationGender.Female, null, Now);

        Assert.True(forAll.AppliesTo([Cbc], PreparationGender.Male));
        Assert.False(forGlucose.AppliesTo([Cbc], null));
        Assert.True(forGlucose.AppliesTo([Cbc, Glucose], null));
        Assert.False(forWomen.AppliesTo([Cbc], PreparationGender.Male));
        Assert.True(forWomen.AppliesTo([Cbc], PreparationGender.Female));
        Assert.True(forWomen.AppliesTo([Cbc], null));
    }

    [Fact]
    public void S_CTB_T3_T4_deactivate_and_reactivate_are_idempotent_and_raise_once()
    {
        var preparation = Preparation.Create("P3", Text("Q"), PreparationAnswerType.Text, null, PreparationGender.Any, null, Now);
        preparation.ClearDomainEvents();

        preparation.Deactivate(Now);
        preparation.Deactivate(Now);
        Assert.False(preparation.IsActive);
        Assert.IsType<PreparationDeactivated>(Assert.Single(preparation.DomainEvents));

        preparation.ClearDomainEvents();
        preparation.Reactivate(Now);
        preparation.Reactivate(Now);
        Assert.True(preparation.IsActive);
        Assert.IsType<PreparationReactivated>(Assert.Single(preparation.DomainEvents));
    }

    [Fact]
    public void INV_CTB_010_more_than_200_linked_services_are_refused_CAT_209()
    {
        var many = Enumerable.Range(0, CatalogueBGuard.MaxLinks + 1).Select(_ => Guid.CreateVersion7()).ToList();

        Assert.Equal(CatalogueBCodes.TooManyLinks, Assert.Throws<DomainException>(() => Diagnosis.Create("E11.9", Text("Diabetes"), many, Now)).Code);
    }

    [Fact]
    public void A_diagnosis_keeps_its_icd_code_and_service_order()
    {
        var diagnosis = Diagnosis.Create("e11.9", Text("Type 2 diabetes"), [Glucose, Lipids], Now);
        diagnosis.Update(Text("Type 2 diabetes mellitus"), [Lipids, Glucose]);

        Assert.Equal("E11.9", diagnosis.IcdCode);
        Assert.Equal([Lipids, Glucose], diagnosis.ServiceIds);
        Assert.IsType<DiagnosisCreated>(Assert.Single(diagnosis.DomainEvents));
    }

    [Fact]
    public void S_CTB_T1_set_a_shortcut_set_needs_a_service_CAT_230()
    {
        Assert.Equal(CatalogueBCodes.ShortcutSetServicesRequired, Assert.Throws<DomainException>(() => ShortcutSet.Create("ICU", Text("ICU panel"), [], Now)).Code);

        var set = ShortcutSet.Create("icu", Text("ICU panel"), [Cbc, Glucose], Now);
        Assert.Equal(CatalogueBCodes.ShortcutSetServicesRequired, Assert.Throws<DomainException>(() => set.Update(Text("ICU"), [Guid.Empty])).Code);
        Assert.Equal([Cbc, Glucose], set.ServiceIds);
    }

    [Fact]
    public void S_CTB_T1_rule_a_service_on_both_sides_is_refused_CAT_242()
    {
        var refusal = Assert.Throws<DomainException>(() => TestRule.Create(Text("CBC adds film"), null, [Cbc], [Cbc, Glucose], Now));

        Assert.Equal(CatalogueBCodes.TestRuleServiceOnBothSides, refusal.Code);
        Assert.Equal(CatalogueBCodes.TestRuleTriggersRequired, Assert.Throws<DomainException>(() => TestRule.Create(Text("R"), null, [], [Cbc], Now)).Code);
        Assert.Equal(CatalogueBCodes.TestRuleAddedRequired, Assert.Throws<DomainException>(() => TestRule.Create(Text("R"), null, [Cbc], [], Now)).Code);
    }

    [Fact]
    public void A_rule_is_triggered_only_when_every_trigger_is_present()
    {
        var rule = TestRule.Create(Text("Glucose and lipids add HbA1c"), Guid.Empty, [Glucose, Lipids], [Cbc], Now);

        Assert.Null(rule.ClinicalGroupId);
        Assert.False(rule.IsTriggeredBy(new HashSet<Guid> { Glucose }));
        Assert.True(rule.IsTriggeredBy(new HashSet<Guid> { Glucose, Lipids, Cbc }));
    }

    [Fact]
    public void S_CTB_PORT_rules_chain_add_each_service_once_and_skip_inactive_rules()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var d = Guid.CreateVersion7();
        var e = Guid.CreateVersion7();
        var first = TestRule.Create(Text("A+B>>C"), null, [a, b], [c], Now);
        var second = TestRule.Create(Text("C>>D"), null, [c], [d, a], Now);
        var inactive = TestRule.Create(Text("A>>E"), null, [a], [e], Now);
        inactive.Deactivate(Now);

        var additions = TestRuleEngine.Expand([second, inactive, first], [a, b]);

        Assert.Equal([c, d], additions.Select(addition => addition.ServiceId).ToArray());
        Assert.Equal(first.Id, additions[0].RuleId);
        Assert.Equal(second.Id, additions[1].RuleId);
        Assert.Empty(TestRuleEngine.Expand([first], [a]));
    }

    [Fact]
    public void S_CTB_T5_validation_reports_errors_and_warnings_without_changing_anything()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var existing = TestRule.Create(Text("A>>B"), null, [a], [b], Now);
        var feedback = TestRule.Create(Text("C>>A"), null, [c], [a], Now);

        var findings = TestRuleEngine.Analyze(null, [a], [c], [existing, feedback]);

        Assert.DoesNotContain(findings, finding => finding.IsError);
        Assert.Contains(TestRuleEngine.Analyze(null, [a], [a], []), finding => finding.Code == CatalogueBCodes.TestRuleServiceOnBothSides && finding.IsError && finding.ServiceId == a);
        Assert.Contains(findings, finding => finding.Code == CatalogueBCodes.TestRuleDuplicateTriggers && !finding.IsError && finding.OtherRuleId == existing.Id);
        Assert.Contains(findings, finding => finding.Code == CatalogueBCodes.TestRuleChainLoop && !finding.IsError && finding.OtherRuleId == feedback.Id);
        Assert.DoesNotContain(TestRuleEngine.Analyze(existing.Id, [a], [b], [existing]), finding => finding.Code == CatalogueBCodes.TestRuleDuplicateTriggers);
        Assert.Equal([a], existing.TriggerServiceIds);
    }
}
