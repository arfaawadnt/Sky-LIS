using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Catalogue.CatalogueAFixtures;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenarios S-T1…S-T8 of intake LIS-INT-CAT-001: clinical groups, sample types and conditions, units, patient conditions.</summary>
public sealed class ReferenceListTests
{
    [Fact]
    public void T1_Create_clinical_group_upper_cases_the_codes_and_raises_the_event()
    {
        var group = ClinicalGroup.Create(" chem ", "c", Name("Chemistry", "الكيمياء"), " Clinical chemistry ", null, 10, Now);

        Assert.Equal(("CHEM", "C", "Clinical chemistry", 10, true), (group.Code, group.UnitCode, group.ReportName, group.DisplayOrder, group.IsActive));
        var raised = Assert.IsType<ClinicalGroupCreated>(Assert.Single(group.DomainEvents));
        Assert.Equal((group.Id, "CHEM"), (raised.ClinicalGroupId, raised.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-AB")]
    [InlineData("AB CD")]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    public void T1_Create_clinical_group_refuses_a_malformed_code_CAT_001(string code)
    {
        var refusal = Assert.Throws<DomainException>(() => ClinicalGroup.Create(code, "C", Name(), null, null, 0, Now));

        Assert.Equal((CatalogueACodes.CodeInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDE")]
    [InlineData("C-1")]
    public void T1_Create_clinical_group_refuses_a_malformed_unit_code_CAT_003(string unitCode)
    {
        Assert.Equal(ClinicalGroupCodes.UnitCodeInvalid, Assert.Throws<DomainException>(() => ClinicalGroup.Create("CHEM", unitCode, Name(), null, null, 0, Now)).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void T1_Create_clinical_group_refuses_a_display_order_out_of_range_CAT_008(int order)
    {
        Assert.Equal(CatalogueACodes.NumberOutOfRange, Assert.Throws<DomainException>(() => ClinicalGroup.Create("CHEM", "C", Name(), null, null, order, Now)).Code);
    }

    [Fact]
    public void T2_Edit_clinical_group_raises_an_event_only_when_something_changed()
    {
        var group = ClinicalGroup.Create("CHEM", "C", Name("Chemistry", "الكيمياء"), null, null, 1, Now);
        group.ClearDomainEvents();

        group.Change("C", Name("Chemistry", "الكيمياء"), null, null, 1, Now);
        Assert.Empty(group.DomainEvents);

        var template = Guid.CreateVersion7();
        group.Change("CH", Name("Clinical chemistry", "الكيمياء السريرية"), "Chemistry", template, 2, Now);
        Assert.IsType<ClinicalGroupChanged>(Assert.Single(group.DomainEvents));
        Assert.Equal(("CH", template, 2), (group.UnitCode, group.ReportTemplateId, group.DisplayOrder));
    }

    [Fact]
    public void T3_Deactivate_and_reactivate_are_idempotent_and_raise_one_event_each()
    {
        var group = ClinicalGroup.Create("HEM", "H", Name("Haematology", "أمراض الدم"), null, null, 2, Now);
        group.ClearDomainEvents();

        group.Deactivate(Now);
        group.Deactivate(Now);
        group.Reactivate(Now);
        group.Reactivate(Now);

        Assert.True(group.IsActive);
        Assert.Collection(group.DomainEvents, first => Assert.IsType<ClinicalGroupDeactivated>(first), second => Assert.IsType<ClinicalGroupReactivated>(second));
    }

    [Fact]
    public void T4_Create_sample_type_with_conditions()
    {
        var sampleType = SampleType.Create("serum", "SER", Name("Serum", "مصل"), [new SampleConditionSpec(null, "fast", Name("Fasting", "صائم"), true), new SampleConditionSpec(null, "RND", Name("Random", "عشوائي"), true)], Now);

        Assert.Equal("SERUM", sampleType.Code);
        Assert.Equal(["FAST", "RND"], sampleType.Conditions.Select(condition => condition.Code).ToArray());
        Assert.All(sampleType.Conditions, condition => Assert.Equal(sampleType.Id, condition.SampleTypeId));
        Assert.IsType<SampleTypeCreated>(Assert.Single(sampleType.DomainEvents));
    }

    [Fact]
    public void T4_Create_sample_type_refuses_duplicate_condition_codes_CAT_011_and_a_missing_short_name_CAT_010()
    {
        var duplicate = Assert.Throws<DomainException>(() => SampleType.Create("SERUM", "SER", Name(), [new SampleConditionSpec(null, "FAST", Name(), true), new SampleConditionSpec(null, "fast", Name(), true)], Now));
        Assert.Equal(SampleTypeCodes.ConditionCodeDuplicate, duplicate.Code);

        Assert.Equal(SampleTypeCodes.ShortNameInvalid, Assert.Throws<DomainException>(() => SampleType.Create("SERUM", " ", Name(), [], Now)).Code);
    }

    [Fact]
    public void T6_Save_conditions_keeps_ids_updates_adds_and_deactivates_the_omitted_ones()
    {
        var sampleType = SampleType.Create("SERUM", "SER", Name(), [new SampleConditionSpec(null, "FAST", Name("Fasting", "صائم"), true), new SampleConditionSpec(null, "RND", Name("Random", "عشوائي"), true)], Now);
        var fasting = sampleType.Conditions.Single(condition => condition.Code == "FAST");
        var random = sampleType.Conditions.Single(condition => condition.Code == "RND");
        sampleType.ClearDomainEvents();

        sampleType.ReplaceConditions([new SampleConditionSpec(fasting.Id, "IGNORED", Name("Fasting 12 h", "صائم ١٢ ساعة"), true), new SampleConditionSpec(null, "COLD", Name("Refrigerated", "مبرد"), true)], Now);

        Assert.Equal(3, sampleType.Conditions.Count);
        Assert.Equal(("FAST", "Fasting 12 h", true), (fasting.Code, fasting.Name.En, fasting.IsActive));
        Assert.False(random.IsActive);
        Assert.True(sampleType.HasCondition(fasting.Id, mustBeActive: true));
        Assert.True(sampleType.HasCondition(random.Id, mustBeActive: false));
        Assert.False(sampleType.HasCondition(random.Id, mustBeActive: true));
        Assert.Equal(1, sampleType.Revision);
        Assert.Equal(2, Assert.IsType<SampleConditionsChanged>(Assert.Single(sampleType.DomainEvents)).ActiveConditionCount);
    }

    [Fact]
    public void T6_Save_conditions_refuses_an_unknown_id_CAT_012_and_a_new_code_that_already_exists_CAT_011()
    {
        var sampleType = SampleType.Create("SERUM", "SER", Name(), [new SampleConditionSpec(null, "FAST", Name(), true)], Now);

        Assert.Equal(SampleTypeCodes.ConditionNotFound, Assert.Throws<DomainException>(() => sampleType.ReplaceConditions([new SampleConditionSpec(Guid.CreateVersion7(), "X", Name(), true)], Now)).Code);
        Assert.Equal(SampleTypeCodes.ConditionCodeDuplicate, Assert.Throws<DomainException>(() => sampleType.ReplaceConditions([new SampleConditionSpec(null, "FAST", Name(), true)], Now)).Code);
        Assert.True(sampleType.Conditions.Single().IsActive);
    }

    [Fact]
    public void CR007_A_sample_type_is_no_culture_specimen_unless_created_as_one()
    {
        Assert.False(SampleType.Create("SERUM", "SER", Name(), [], Now).ForCulture);
        Assert.True(SampleType.Create("USWAB", "SWAB", Name("Wound swab", "مسحة جرح"), [], Now, forCulture: true).ForCulture);
    }

    [Fact]
    public void CR007_Edit_sets_or_clears_the_culture_flag_and_keeps_it_when_the_editor_sends_none()
    {
        var sampleType = SampleType.Create("URINE", "UR", Name("Urine", "بول"), [], Now);
        sampleType.ClearDomainEvents();

        sampleType.Change("UR", Name("Urine", "بول"), Now, forCulture: true);
        Assert.True(sampleType.ForCulture);
        Assert.IsType<SampleTypeChanged>(Assert.Single(sampleType.DomainEvents));

        sampleType.ClearDomainEvents();
        sampleType.Change("UR", Name("Urine", "بول"), Now);   // an older client: the flag stays, nothing changed
        Assert.True(sampleType.ForCulture);
        Assert.Empty(sampleType.DomainEvents);

        sampleType.Change("UR", Name("Urine", "بول"), Now, forCulture: false);
        Assert.False(sampleType.ForCulture);
        Assert.IsType<SampleTypeChanged>(Assert.Single(sampleType.DomainEvents));
    }

    [Fact]
    public void T7_Unit_names_are_trimmed_keyed_ignoring_case_and_refused_when_empty_CAT_015()
    {
        var unit = MeasurementUnit.Create(" mg/dL ", Now);
        Assert.Equal(("mg/dL", "MG/DL"), (unit.Name, unit.NameKey));

        unit.ClearDomainEvents();
        unit.Rename("mg/dL", Now);
        Assert.Empty(unit.DomainEvents);
        unit.Rename("mmol/L", Now);
        Assert.Equal("MMOL/L", unit.NameKey);
        Assert.IsType<MeasurementUnitChanged>(Assert.Single(unit.DomainEvents));

        Assert.Equal(MeasurementUnitCodes.NameInvalid, Assert.Throws<DomainException>(() => MeasurementUnit.Create("  ", Now)).Code);
        Assert.Equal(MeasurementUnitCodes.NameInvalid, Assert.Throws<DomainException>(() => MeasurementUnit.Create(new string('x', 51), Now)).Code);
    }

    [Fact]
    public void T8_Patient_conditions_key_their_English_name_and_rename_only_on_change()
    {
        var condition = PatientCondition.Create(new LocalizedText("Pregnant", "حامل"), Now);
        Assert.Equal("PREGNANT", condition.NameKey);
        condition.ClearDomainEvents();

        condition.Rename(new LocalizedText("Pregnant", "حامل"), Now);
        Assert.Empty(condition.DomainEvents);
        condition.Rename(new LocalizedText("Pregnancy", "الحمل"), Now);
        Assert.Equal("PREGNANCY", condition.NameKey);

        condition.Deactivate(Now);
        Assert.False(condition.IsActive);
        Assert.IsType<PatientConditionDeactivated>(condition.DomainEvents.Last());
    }
}
