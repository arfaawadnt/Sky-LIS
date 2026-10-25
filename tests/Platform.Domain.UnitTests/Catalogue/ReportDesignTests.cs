using Platform.Domain.Catalogue;
using Platform.Domain.Pricing;
using Platform.Domain.Settings;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Report designs (LIS-CR-010, reviewer point C-14; intake RES §18, CTB §19, PRC §20): the vocabulary, the selection order and the two overrides.</summary>
public sealed class ReportDesignTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static LocalizedText Text(string en) => new(en, "نص " + en);

    private static ContractPricing Pricing() =>
        ContractPricing.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 7, 1), null, [new PricingFormulaTerm(Guid.CreateVersion7(), 0m, 1m)], Now);

    [Fact]
    public void RD_01_the_contract_override_wins_over_the_template_and_the_laboratory()
    {
        Assert.Equal(new ReportDesignChoice(ReportDesign.Compact, ReportDesignSource.Contract), ReportDesigns.Resolve(ReportDesign.Compact, ReportDesign.Modern, ReportDesign.Letterhead));
    }

    [Fact]
    public void RD_02_without_a_contract_override_the_template_decides()
    {
        Assert.Equal(new ReportDesignChoice(ReportDesign.Modern, ReportDesignSource.Template), ReportDesigns.Resolve(null, ReportDesign.Modern, ReportDesign.Letterhead));
    }

    [Fact]
    public void RD_03_without_overrides_the_laboratory_parameter_decides()
    {
        Assert.Equal(new ReportDesignChoice(ReportDesign.Cumulative, ReportDesignSource.Laboratory), ReportDesigns.Resolve(null, null, ReportDesign.Cumulative));
        Assert.Equal(new ReportDesignChoice(ReportDesign.Classic, ReportDesignSource.Laboratory), default(ReportDesignChoice));
    }

    [Theory]
    [InlineData("classic", ReportDesign.Classic)]
    [InlineData("modern", ReportDesign.Modern)]
    [InlineData("Compact", ReportDesign.Compact)]
    [InlineData(" letterhead ", ReportDesign.Letterhead)]
    [InlineData("cumulative", ReportDesign.Cumulative)]
    [InlineData("fancy", ReportDesign.Classic)]
    [InlineData("3", ReportDesign.Classic)]
    [InlineData(null, ReportDesign.Classic)]
    public void RD_04_the_parameter_value_maps_to_a_design_and_anything_unknown_is_classic(string? value, ReportDesign expected)
    {
        Assert.Equal(expected, ReportDesigns.FromSettingValue(value));
    }

    [Fact]
    public void RD_05_the_laboratory_parameter_is_a_printing_select_defaulting_to_classic()
    {
        Assert.Equal("reportDesign", SettingKeys.ReportDesign.Key);
        Assert.Equal("classic", SettingKeys.ReportDesign.Default);
        Assert.Same(SettingKeys.ReportDesign, SettingKeys.Find("reportDesign"));
        Assert.Equal(["classic", "modern", "compact", "letterhead", "cumulative"], ReportDesigns.SettingValues);
        Assert.All(ReportDesigns.SettingValues, value => Assert.True(SettingKeys.ReportDesign.IsValid(value), value));
        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("reportDesign", "Modern")).Code);
        Assert.Equal(SettingCodes.InvalidValue, Assert.Throws<DomainException>(() => SettingKeys.Validate("reportDesign", "fancy")).Code);
    }

    [Fact]
    public void RD_06_a_result_template_carries_a_design_override_and_an_edit_without_one_clears_it()
    {
        var template = ReportTemplate.Create(Text("Results A4"), ReportTemplateType.Results, true, ReportTemplateLanguage.Both, null, null, Now, ReportDesign.Modern);
        Assert.Equal(ReportDesign.Modern, template.Design);

        template.Update(Text("Results A4"), ReportTemplateType.Culture, true, ReportTemplateLanguage.Both, null, null, ReportDesign.Cumulative);
        Assert.Equal(ReportDesign.Cumulative, template.Design);

        template.Update(Text("Results A4"), ReportTemplateType.Text, true, ReportTemplateLanguage.Both, null, null);
        Assert.Null(template.Design);
    }

    [Theory]
    [InlineData(ReportTemplateType.Claim)]
    [InlineData(ReportTemplateType.Receipt)]
    [InlineData(ReportTemplateType.Label)]
    [InlineData(ReportTemplateType.JobOrder)]
    public void RD_07_a_template_of_another_document_refuses_a_design_CAT_273(ReportTemplateType type)
    {
        Assert.Equal(CatalogueBCodes.TemplateDesignNotApplicable, Assert.Throws<DomainException>(() => ReportTemplate.Create(Text("Doc"), type, false, ReportTemplateLanguage.En, null, null, Now, ReportDesign.Compact)).Code);
        Assert.Null(ReportTemplate.Create(Text("Doc"), type, false, ReportTemplateLanguage.En, null, null, Now).Design);
    }

    [Fact]
    public void RD_08_an_unknown_design_is_refused()
    {
        Assert.Equal(CatalogueBCodes.TextInvalid, Assert.Throws<DomainException>(() => ReportTemplate.Create(Text("Results"), ReportTemplateType.Results, false, ReportTemplateLanguage.En, null, null, Now, (ReportDesign)42)).Code);
        Assert.Equal(PricingCodes.ReportDesignInvalid, Assert.Throws<DomainException>(() => Pricing().SetReportDesign((ReportDesign)42, Now)).Code);
    }

    [Fact]
    public void RD_09_a_contract_sets_and_clears_its_design_with_one_event_per_change()
    {
        var pricing = Pricing();
        Assert.Null(pricing.ReportDesign);
        pricing.ClearDomainEvents();

        pricing.SetReportDesign(ReportDesign.Letterhead, Now);
        pricing.SetReportDesign(ReportDesign.Letterhead, Now);
        Assert.Equal(ReportDesign.Letterhead, pricing.ReportDesign);
        var set = Assert.IsType<ContractReportDesignSet>(Assert.Single(pricing.DomainEvents));
        Assert.Equal((pricing.Id, pricing.ContractTierId, (ReportDesign?)ReportDesign.Letterhead), (set.ContractPricingId, set.ContractTierId, set.Design));

        pricing.SetReportDesign(null, Now);
        Assert.Null(pricing.ReportDesign);
        Assert.Null(Assert.IsType<ContractReportDesignSet>(pricing.DomainEvents.Last()).Design);
    }

    [Fact]
    public void RD_10_the_formula_and_a_copy_onto_the_tier_keep_the_design()
    {
        var pricing = Pricing();
        pricing.SetReportDesign(ReportDesign.Compact, Now);
        pricing.SetFormula(new DateOnly(2026, 8, 1), null, [new PricingFormulaTerm(Guid.CreateVersion7(), 0.5m, 0.5m)], Now);
        pricing.CopyFrom(Pricing(), 1m, Now);

        Assert.Equal(ReportDesign.Compact, pricing.ReportDesign);
    }
}
