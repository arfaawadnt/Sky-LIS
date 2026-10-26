using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Compatibility groups of samples (LIS-CR-015 M4): members, priorities and the rules CAT-280…283, 287.</summary>
public sealed class SampleCompatibilityGroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly SampleType Serum = SampleType.Create(
        "SER",
        "Serum",
        new LocalizedText("Serum", "مصل"),
        [
            new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true),
            new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true),
            new SampleConditionSpec(null, "OLD", new LocalizedText("Old", "قديم"), false),
        ],
        Now);

    private static readonly SampleType Plasma = SampleType.Create("PL", "Plasma", new LocalizedText("Plasma", "بلازما"), [new SampleConditionSpec(null, "FLU", new LocalizedText("Fluoride", "فلورايد"), true)], Now);

    private static CompatibleSamplePair Pair(SampleType type, string? condition) =>
        new(type.Id, condition is null ? null : type.Conditions.Single(candidate => candidate.Code == condition).Id);

    private static SampleCompatibilityGroup Group(params CompatibleSamplePair[] members) =>
        SampleCompatibilityGroup.Create("GLU", new LocalizedText("Glucose samples", "عينات السكر"), members, [Serum, Plasma], Now);

    private static string Refusal(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void A_group_keeps_its_members_in_the_order_given_and_may_mix_sample_types_and_no_condition()
    {
        var group = Group(Pair(Serum, "FAST"), Pair(Serum, "RAND"), Pair(Plasma, "FLU"), Pair(Serum, null));

        Assert.Equal("GLU", group.Code);
        Assert.True(group.IsActive);
        Assert.Equal([1, 2, 3, 4], group.Members.Select(member => member.Priority));
        Assert.Equal([Pair(Serum, "FAST"), Pair(Serum, "RAND"), Pair(Plasma, "FLU"), Pair(Serum, null)], group.Pairs);
        Assert.IsType<SampleCompatibilityGroupCreated>(Assert.Single(group.DomainEvents));
    }

    [Fact]
    public void The_rules_refuse_too_few_unavailable_and_repeated_members()
    {
        Assert.Equal(SampleCompatibilityCodes.MembersTooFew, Refusal(() => Group(Pair(Serum, "FAST"))));
        Assert.Equal(SampleCompatibilityCodes.MemberSampleTypeUnavailable, Refusal(() => Group(Pair(Serum, "FAST"), new CompatibleSamplePair(Guid.CreateVersion7(), null))));
        Assert.Equal(SampleCompatibilityCodes.MemberConditionUnavailable, Refusal(() => Group(Pair(Serum, "FAST"), Pair(Serum, "OLD"))));
        Assert.Equal(SampleCompatibilityCodes.MemberConditionUnavailable, Refusal(() => Group(Pair(Serum, "FAST"), new CompatibleSamplePair(Serum.Id, Pair(Plasma, "FLU").SampleConditionId))));
        Assert.Equal(SampleCompatibilityCodes.MemberDuplicate, Refusal(() => Group(Pair(Serum, "FAST"), Pair(Serum, "FAST"))));
        Assert.Equal(SampleCompatibilityCodes.MembersTooMany, Refusal(() => Group([.. Enumerable.Range(0, SampleCompatibilityGroup.MaxMembers + 1).Select(_ => Pair(Serum, "FAST"))])));
        Assert.Equal(CatalogueACodes.CodeInvalid, Refusal(() => SampleCompatibilityGroup.Create("bad code!", new LocalizedText("x", "x"), [Pair(Serum, "FAST"), Pair(Serum, "RAND")], [Serum], Now)));
    }

    [Fact]
    public void An_update_reorders_in_place_adds_and_removes_and_keeps_a_member_deactivated_since()
    {
        var group = Group(Pair(Serum, "FAST"), Pair(Serum, "RAND"));
        var randomMember = group.Members.Single(member => member.Pair == Pair(Serum, "RAND")).Id;
        group.ClearDomainEvents();

        // RAND is listed first now, PL·FLU is new, FAST goes
        group.Update(new LocalizedText("Sugar", "السكر"), [Pair(Serum, "RAND"), Pair(Plasma, "FLU")], [Serum, Plasma], Now);

        Assert.Equal([Pair(Serum, "RAND"), Pair(Plasma, "FLU")], group.Pairs);
        Assert.Equal(randomMember, group.Members[0].Id);
        Assert.Equal(1, group.Revision);
        Assert.IsType<SampleCompatibilityGroupChanged>(Assert.Single(group.DomainEvents));

        // an unchanged member whose type became inactive stays; a new one must be active
        var name = new LocalizedText("Urine", "بول");
        var urine = SampleType.Create("UR", "Urine", name, [], Now);
        var withUrine = SampleCompatibilityGroup.Create("U", name, [new CompatibleSamplePair(urine.Id, null), Pair(Plasma, "FLU")], [urine, Plasma], Now);
        urine.Deactivate(Now);
        withUrine.Update(name, [Pair(Plasma, "FLU"), new CompatibleSamplePair(urine.Id, null)], [urine, Plasma], Now);
        Assert.Equal(2, withUrine.Members.Count);

        var stool = SampleType.Create("ST", "Stool", new LocalizedText("Stool", "براز"), [], Now);
        stool.Deactivate(Now);
        Assert.Equal(
            SampleCompatibilityCodes.MemberSampleTypeUnavailable,
            Refusal(() => withUrine.Update(name, [Pair(Plasma, "FLU"), new CompatibleSamplePair(urine.Id, null), new CompatibleSamplePair(stool.Id, null)], [urine, Plasma, stool], Now)));
    }

    [Fact]
    public void Deactivation_and_reactivation_are_idempotent_facts()
    {
        var group = Group(Pair(Serum, "FAST"), Pair(Serum, "RAND"));
        group.ClearDomainEvents();

        group.Deactivate(Now);
        group.Deactivate(Now);
        Assert.False(group.IsActive);
        group.Reactivate(Now);
        Assert.True(group.IsActive);
        Assert.Equal(2, group.DomainEvents.Count);
    }
}
