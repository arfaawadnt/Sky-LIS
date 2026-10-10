using Platform.Domain.Catalogue;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenarios of intake LIS-INT-CTB-001 §16 for the set-up lists and the microbiology masters.</summary>
public sealed class CatalogueBSetupTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static LocalizedText Text(string en) => new(en, "نص " + en);

    private static Doctor NewDoctor(string code = "D1") => Doctor.Create(code, Text("Dr. Amal"), "Internal medicine", null, null, 10m, null, Now);

    [Fact]
    public void A_comment_template_short_name_is_a_code_and_an_empty_group_means_all_groups()
    {
        var template = CommentTemplate.Create(" hemolysed ", Text("The sample is hemolysed."), Guid.Empty, Now);

        Assert.Equal("HEMOLYSED", template.ShortName);
        Assert.Null(template.ClinicalGroupId);
        Assert.IsType<CommentTemplateCreated>(Assert.Single(template.DomainEvents));
        Assert.Equal(CatalogueBCodes.CodeInvalid, Assert.Throws<DomainException>(() => CommentTemplate.Create(new string('A', 33), Text("x"), null, Now)).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(12.345)]
    public void S_CTB_T1_share_a_share_outside_0_100_or_with_three_decimals_is_refused_CAT_220(double share)
    {
        var refusal = Assert.Throws<DomainException>(() => Doctor.Create("D9", Text("Dr. X"), null, null, null, (decimal)share, null, Now));

        Assert.Equal(CatalogueBCodes.SharePercentInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("+20 100 123 4567", "+20 100 123 4567")]
    [InlineData("  01001234567 ", "01001234567")]
    public void INV_CTB_012_phone_numbers_are_normalised(string raw, string expected)
    {
        var doctor = Doctor.Create("D2", Text("Dr. Y"), null, null, null, 0m, raw, Now);

        Assert.Equal(expected, doctor.Phone);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+ 2010012345")]
    [InlineData("0100-123-4567")]
    public void INV_CTB_012_an_invalid_phone_is_refused_CAT_202(string raw)
    {
        Assert.Equal(CatalogueBCodes.PhoneInvalid, Assert.Throws<DomainException>(() => Doctor.Create("D3", Text("Dr. Z"), null, null, null, 0m, raw, Now)).Code);
    }

    [Fact]
    public void A_doctor_without_branches_is_offered_everywhere_and_an_inactive_doctor_nowhere()
    {
        var branch = Guid.CreateVersion7();
        var everywhere = NewDoctor("D4");
        var restricted = Doctor.Create("D5", Text("Dr. R"), " Pathology ", " Consultant ", [branch], 12.5m, null, Now);

        Assert.True(everywhere.IsAvailableIn(branch));
        Assert.True(restricted.IsAvailableIn(branch));
        Assert.False(restricted.IsAvailableIn(Guid.CreateVersion7()));
        Assert.Equal(("Pathology", "Consultant"), (restricted.Specialty, restricted.Occupation));

        restricted.Deactivate(Now);
        Assert.False(restricted.IsAvailableIn(branch));
    }

    [Fact]
    public void A_sending_lab_refuses_a_newly_linked_inactive_doctor_CAT_310_but_keeps_an_unchanged_link()
    {
        var doctor = NewDoctor();
        var lab = SendingLab.Create(Text("Delta Lab"), Guid.CreateVersion7(), Guid.CreateVersion7(), Text("Ahmed"), "01001234567", doctor, Now);
        Assert.Equal(doctor.Id, lab.DoctorId);

        doctor.Deactivate(Now);
        lab.Update(Text("Delta Lab 2"), lab.PayerId, lab.ContractTierId, Text("Ahmed"), null, doctor);
        Assert.Equal(doctor.Id, lab.DoctorId);

        var other = NewDoctor("D6");
        other.Deactivate(Now);
        var refusal = Assert.Throws<DomainException>(() => lab.Update(Text("Delta Lab"), lab.PayerId, lab.ContractTierId, Text("Ahmed"), null, other));
        Assert.Equal((CatalogueBCodes.ReferenceInactive, DomainFault.Conflict), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void A_sending_lab_needs_a_payer_and_a_tier()
    {
        Assert.Equal(CatalogueBCodes.PayerUnknown, Assert.Throws<DomainException>(() => SendingLab.Create(Text("L"), Guid.Empty, Guid.CreateVersion7(), Text("C"), null, null, Now)).Code);
        Assert.Equal(CatalogueBCodes.ContractTierUnknown, Assert.Throws<DomainException>(() => SendingLab.Create(Text("L"), Guid.CreateVersion7(), Guid.Empty, Text("C"), null, null, Now)).Code);
    }

    [Fact]
    public void A_reason_keeps_its_kind_and_reports_it_on_its_events()
    {
        var reason = Reason.Create(ReasonKind.Rejection, Text("Hemolysis"), Now);
        reason.Rename(Text("Hemolysed sample"));
        reason.Deactivate(Now);

        Assert.Equal(ReasonKind.Rejection, reason.Kind);
        Assert.Equal("Hemolysed sample", reason.Name.En);
        Assert.Equal(ReasonKind.Rejection, Assert.IsType<ReasonDeactivated>(reason.DomainEvents.Last()).Kind);
        Assert.Equal(CatalogueBCodes.TextInvalid, Assert.Throws<DomainException>(() => Reason.Create((ReasonKind)42, Text("x"), Now)).Code);
    }

    [Fact]
    public void A_report_template_clears_empty_file_ids_and_keeps_optional_texts()
    {
        var template = ReportTemplate.Create(Text("Results A4"), ReportTemplateType.Results, Guid.Empty, Guid.CreateVersion7(), true, ReportTemplateLanguage.Both, Text("Header"), null, Now);

        Assert.Null(template.LogoFileId);
        Assert.NotNull(template.SignatureFileId);
        Assert.Equal("Header", template.HeaderText!.En);
        Assert.Null(template.FooterText);
        Assert.Equal(ReportTemplateType.Results, Assert.IsType<ReportTemplateCreated>(Assert.Single(template.DomainEvents)).Type);
    }

    [Fact]
    public void A_commercial_name_needs_an_active_drug_type_when_it_is_linked_CAT_310()
    {
        var penicillins = DrugType.Create(Text("Penicillin"), Now);
        var name = CommercialName.Create(penicillins, Text("Augmentin"), Now);
        Assert.Equal(penicillins.Id, name.DrugTypeId);

        var macrolides = DrugType.Create(Text("Macrolide"), Now);
        macrolides.Deactivate(Now);
        Assert.Equal(CatalogueBCodes.ReferenceInactive, Assert.Throws<DomainException>(() => name.Update(macrolides, Text("Augmentin"))).Code);
        Assert.Equal(CatalogueBCodes.ReferenceInactive, Assert.Throws<DomainException>(() => CommercialName.Create(macrolides, Text("Zithromax"), Now)).Code);
    }

    [Fact]
    public void An_antibiotic_upper_cases_code_and_abbreviation_and_links_an_active_commercial_name()
    {
        var name = CommercialName.Create(DrugType.Create(Text("Penicillin"), Now), Text("Augmentin"), Now);
        var antibiotic = Antibiotic.Create("amc", Text("Amoxicillin-clavulanate"), "amc", name, Now);

        Assert.Equal(("AMC", "AMC", (Guid?)name.Id), (antibiotic.Code, antibiotic.Abbreviation, antibiotic.CommercialNameId));
        Assert.Equal(CatalogueBCodes.CodeInvalid, Assert.Throws<DomainException>(() => antibiotic.Update(Text("x"), "TOO-LONG-ABBR", null)).Code);

        antibiotic.Update(Text("Amoxicillin-clavulanic acid"), "AMC", null);
        Assert.Null(antibiotic.CommercialNameId);
    }

    [Fact]
    public void Organisms_and_sensitivity_values_follow_the_code_and_order_rules()
    {
        var organism = Organism.Create("ecoli", Text("Escherichia coli"), Now);
        var susceptible = SensitivityValue.Create("s", Text("Susceptible"), 1, Now);

        Assert.Equal("ECOLI", organism.Code);
        Assert.Equal(("S", 1), (susceptible.Code, susceptible.DisplayOrder));
        Assert.Equal(CatalogueBCodes.DisplayOrderInvalid, Assert.Throws<DomainException>(() => susceptible.Update(Text("Susceptible"), 1000)).Code);
        Assert.Equal(CatalogueBCodes.CodeInvalid, Assert.Throws<DomainException>(() => SensitivityValue.Create("ABCDEFGHIJK", Text("x"), 0, Now)).Code);
    }
}
