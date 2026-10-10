using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Catalogue.CatalogueAFixtures;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenarios S-T9…S-T12 of intake LIS-INT-CAT-001: the service and its member rules (INV-CAT-003).</summary>
public sealed class ServiceTests
{
    [Fact]
    public void T9_Create_a_test_service_with_exactly_one_test()
    {
        var test = TestMember(1);

        var service = Service.Create(" glu ", ServiceType.Test, ServiceDefinition(), 7, [test], Now);

        Assert.Equal(("GLU", ServiceType.Test, 7, true), (service.Code, service.Type, service.Sequence, service.IsActive));
        var member = Assert.Single(service.Members);
        Assert.Equal((ServiceMemberKind.Test, test.MemberId, 1, service.Id), (member.Kind, member.MemberId, member.Order, member.ServiceId));
        Assert.True(service.HasMember(ServiceMemberKind.Test, test.MemberId));
        var raised = Assert.IsType<ServiceCreated>(Assert.Single(service.DomainEvents));
        Assert.Equal((ServiceType.Test, 7), (raised.Type, raised.Sequence));
    }

    [Fact]
    public void T9_Members_are_kept_in_order()
    {
        var service = Service.Create("LIPID", ServiceType.Profile, ServiceDefinition(), 1, [TestMember(3), TestMember(1), TestMember(2)], Now);

        Assert.Equal([1, 2, 3], service.Members.Select(member => member.Order).ToArray());
    }

    [Fact]
    public void T9_A_test_service_with_two_tests_is_refused_CAT_021()
    {
        Assert.Equal(ServiceCodes.MembersInvalidForType, Refusal(ServiceType.Test, [TestMember(1), TestMember(2)]));
        Assert.Equal(ServiceCodes.MembersInvalidForType, Refusal(ServiceType.Profile, []));
        Assert.Equal(ServiceCodes.MembersInvalidForType, Refusal(ServiceType.Package, [], sample: false));
    }

    [Fact]
    public void T9_A_profile_with_a_service_and_a_package_with_a_test_are_refused_CAT_022()
    {
        Assert.Equal(ServiceCodes.MemberKindNotAllowed, Refusal(ServiceType.Profile, [TestMember(1), ServiceMember(2)]));
        Assert.Equal(ServiceCodes.MemberKindNotAllowed, Refusal(ServiceType.Package, [TestMember(1)], sample: false));
    }

    [Fact]
    public void T9_A_duplicate_member_is_refused_CAT_023_and_duplicate_orders_CAT_024()
    {
        var id = Guid.CreateVersion7();
        Assert.Equal(ServiceCodes.DuplicateMember, Refusal(ServiceType.Profile, [TestMember(1, id: id), TestMember(2, id: id)]));
        Assert.Equal(ServiceCodes.MemberOrderInvalid, Refusal(ServiceType.Profile, [TestMember(1), TestMember(1)]));
        Assert.Equal(ServiceCodes.MemberOrderInvalid, Refusal(ServiceType.Profile, [TestMember(0)]));
    }

    [Fact]
    public void T11_A_package_cannot_include_itself_CAT_025_nor_a_package_or_an_additional_service_CAT_026()
    {
        var package = Service.Create("PANEL", ServiceType.Package, PackageDefinition(), 1, [ServiceMember(1)], Now);

        Assert.Equal(ServiceCodes.SelfInclusion, Assert.Throws<DomainException>(() => package.ReplaceMembers([ServiceMember(1, ServiceType.Package, package.Id)], Now)).Code);
        Assert.Equal(ServiceCodes.NestedPackage, Assert.Throws<DomainException>(() => package.ReplaceMembers([ServiceMember(1, ServiceType.Package)], Now)).Code);
        Assert.Equal(ServiceCodes.NestedPackage, Assert.Throws<DomainException>(() => package.ReplaceMembers([ServiceMember(1, ServiceType.Additional)], Now)).Code);
    }

    [Fact]
    public void T11_Replace_members_bumps_the_revision_and_raises_the_event()
    {
        var service = Service.Create("LIPID", ServiceType.Profile, ServiceDefinition(), 1, [TestMember(1)], Now);
        service.ClearDomainEvents();

        service.ReplaceMembers([TestMember(1), TestMember(2)], Now);

        Assert.Equal((2, 1), (service.Members.Count, service.Revision));
        Assert.Equal(2, Assert.IsType<ServiceMembersChanged>(Assert.Single(service.DomainEvents)).MemberCount);
    }

    [Fact]
    public void T9_Culture_needs_exactly_one_culture_test_and_a_text_report_one_formatted_text_test_CAT_036()
    {
        Assert.Equal(ServiceCodes.MemberTestTypeMismatch, Refusal(ServiceType.Culture, [TestMember(1)]));
        Assert.Equal(ServiceCodes.MemberTestTypeMismatch, Refusal(ServiceType.Culture, [TestMember(1, LabTestType.Culture), TestMember(2, LabTestType.Culture)]));
        Assert.Equal(ServiceCodes.MemberTestTypeMismatch, Refusal(ServiceType.TextReport, [TestMember(1)]));

        var culture = Service.Create("URC", ServiceType.Culture, ServiceDefinition(), 1, [TestMember(1, LabTestType.Culture), TestMember(2)], Now);
        Assert.Equal(2, culture.Members.Count);
    }

    [Fact]
    public void T9_Additional_services_carry_no_members_no_sample_and_no_labels_CAT_021_CAT_028()
    {
        var additional = Service.Create("HOME", ServiceType.Additional, AdditionalDefinition(), 1, [], Now);
        Assert.Equal((0, false, 0, (Guid?)null), (additional.LabelCount, additional.PrintAlone, additional.TurnaroundHours, additional.ClinicalGroupId));

        Assert.Equal(ServiceCodes.MembersInvalidForType, Assert.Throws<DomainException>(() => Service.Create("HOME2", ServiceType.Additional, AdditionalDefinition(), 1, [TestMember(1)], Now)).Code);
        Assert.Equal(ServiceCodes.SampleNotAllowed, Assert.Throws<DomainException>(() => Service.Create("HOME3", ServiceType.Additional, AdditionalDefinition() with { SampleTypeId = Guid.CreateVersion7() }, 1, [], Now)).Code);
    }

    [Fact]
    public void T9_Sample_rules_CAT_028_CAT_029_and_group_rule_CAT_041()
    {
        Assert.Equal(ServiceCodes.SampleRequired, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Profile, ServiceDefinition() with { SampleTypeId = null }, 1, [TestMember(1)], Now)).Code);
        Assert.Equal(ServiceCodes.SampleNotAllowed, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Package, PackageDefinition() with { SampleConditionId = Guid.CreateVersion7() }, 1, [ServiceMember(1)], Now)).Code);
        Assert.Equal(ServiceCodes.ClinicalGroupRequired, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Test, ServiceDefinition() with { ClinicalGroupId = null }, 1, [TestMember(1)], Now)).Code);
    }

    [Fact]
    public void T9_Shortcut_CAT_032_aliases_CAT_034_and_numbers_CAT_008_are_checked()
    {
        Assert.Equal(ServiceCodes.ShortcutInvalid, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Test, ServiceDefinition() with { ShortcutNo = 0 }, 1, [TestMember(1)], Now)).Code);
        Assert.Equal(ServiceCodes.AliasesInvalid, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Test, ServiceDefinition() with { Aliases = ["Sugar", "SUGAR"] }, 1, [TestMember(1)], Now)).Code);
        Assert.Equal(CatalogueACodes.NumberOutOfRange, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Test, ServiceDefinition() with { TurnaroundHours = -1 }, 1, [TestMember(1)], Now)).Code);
    }

    [Fact]
    public void T9_A_profile_sum_needs_an_in_sum_member_and_only_profiles_carry_one_CAT_037()
    {
        Assert.Equal(ServiceCodes.ProfileSumInvalid, Assert.Throws<DomainException>(() => Service.Create("DIFF", ServiceType.Profile, ServiceDefinition(profileSum: 100m), 1, [TestMember(1)], Now)).Code);
        Assert.Equal(ServiceCodes.ProfileSumInvalid, Assert.Throws<DomainException>(() => Service.Create("GLU", ServiceType.Test, ServiceDefinition(profileSum: 100m), 1, [TestMember(1, inSum: true)], Now)).Code);

        var differential = Service.Create("DIFF", ServiceType.Profile, ServiceDefinition(profileSum: 100m), 1, [TestMember(1, inSum: true), TestMember(2, inSum: true)], Now);
        Assert.Equal(100m, differential.ProfileSum);
        Assert.All(differential.Members, member => Assert.True(member.InSum));
    }

    [Fact]
    public void T12_Resequence_refuses_non_positive_values_CAT_031_and_raises_only_on_change()
    {
        var service = Service.Create("GLU", ServiceType.Test, ServiceDefinition(), 5, [TestMember(1)], Now);
        service.ClearDomainEvents();

        service.Resequence(5, Now);
        Assert.Empty(service.DomainEvents);
        service.Resequence(9, Now);
        Assert.Equal(9, Assert.IsType<ServiceResequenced>(Assert.Single(service.DomainEvents)).Sequence);

        Assert.Equal(ServiceCodes.SequenceInvalid, Assert.Throws<DomainException>(() => service.Resequence(0, Now)).Code);
        Assert.Equal(ServiceCodes.SequenceInvalid, Assert.Throws<DomainException>(() => Service.Create("X", ServiceType.Test, ServiceDefinition(), -1, [TestMember(1)], Now)).Code);
    }

    [Fact]
    public void T10_Edit_changes_the_definition_but_never_the_code_or_type()
    {
        var service = Service.Create("GLU", ServiceType.Test, ServiceDefinition(), 1, [TestMember(1)], Now);

        service.Change(ServiceDefinition() with { ShortName = "FBS", Gender = GenderRestriction.Female, Precaution = "Fasting 8 h" }, Now);

        Assert.Equal(("GLU", ServiceType.Test, "FBS", GenderRestriction.Female, "Fasting 8 h"), (service.Code, service.Type, service.ShortName, service.Gender, service.Precaution));
        Assert.IsType<ServiceChanged>(service.DomainEvents.Last());
    }

    private static string Refusal(ServiceType type, IReadOnlyList<ResolvedServiceMember> members, bool sample = true)
    {
        var definition = sample ? ServiceDefinition() : PackageDefinition();
        return Assert.Throws<DomainException>(() => Service.Create("X", type, definition, 1, members, Now)).Code;
    }

    private static ServiceDefinition PackageDefinition() => ServiceDefinition() with { SampleTypeId = null };

    private static ServiceDefinition AdditionalDefinition() => ServiceDefinition() with { ClinicalGroupId = null, SampleTypeId = null, PrintAlone = true, LabelCount = 3 };
}
