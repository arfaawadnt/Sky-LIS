using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Compatible sample conditions (LIS-CR-015 M4): <see cref="SampleType.SetCompatibleConditions"/> and its rules CAT-280…285.</summary>
public sealed class CompatibleConditionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static SampleType Serum() =>
        SampleType.Create(
            "SER",
            "Serum",
            new LocalizedText("Serum", "مصل"),
            [
                new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true),
                new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true),
                new SampleConditionSpec(null, "PP", new LocalizedText("Post-prandial", "بعد الأكل"), true),
                new SampleConditionSpec(null, "OLD", new LocalizedText("Old", "قديم"), false),
            ],
            Now);

    private static Guid Id(SampleType type, string code) => type.Conditions.Single(condition => condition.Code == code).Id;

    private static string Refusal(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void A_condition_merges_into_an_active_condition_of_its_type_and_the_change_is_recorded()
    {
        var type = Serum();
        type.ClearDomainEvents();

        var changed = type.SetCompatibleConditions([new SampleConditionPair(Id(type, "RAND"), Id(type, "FAST")), new SampleConditionPair(Id(type, "PP"), Id(type, "FAST"))], Now);

        Assert.True(changed);
        Assert.Equal(Id(type, "FAST"), type.Conditions.Single(condition => condition.Code == "RAND").MergesIntoConditionId);
        Assert.Equal(Id(type, "FAST"), type.Conditions.Single(condition => condition.Code == "PP").MergesIntoConditionId);
        Assert.Null(type.Conditions.Single(condition => condition.Code == "FAST").MergesIntoConditionId);
        Assert.Equal(1, type.Revision);
        Assert.Equal(2, Assert.IsType<SampleConditionCompatibilityChanged>(Assert.Single(type.DomainEvents)).MergingConditionCount);
    }

    [Fact]
    public void The_map_is_complete_unlisted_conditions_keep_their_own_sample_and_an_unchanged_map_changes_nothing()
    {
        var type = Serum();
        type.SetCompatibleConditions([new SampleConditionPair(Id(type, "RAND"), Id(type, "FAST"))], Now);
        type.ClearDomainEvents();

        Assert.False(type.SetCompatibleConditions([new SampleConditionPair(Id(type, "RAND"), Id(type, "FAST"))], Now));
        Assert.Empty(type.DomainEvents);

        Assert.True(type.SetCompatibleConditions([], Now));
        Assert.All(type.Conditions, condition => Assert.Null(condition.MergesIntoConditionId));
    }

    [Fact]
    public void The_rules_refuse_unknown_self_inactive_chained_and_repeated_entries()
    {
        var type = Serum();
        var other = Serum();
        var fasting = Id(type, "FAST");
        var random = Id(type, "RAND");
        var pp = Id(type, "PP");

        Assert.Equal(SampleTypeCodes.CompatibleConditionUnknown, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(Guid.CreateVersion7(), fasting)], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleConditionUnknown, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(random, Id(other, "FAST"))], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleConditionSelf, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(random, random)], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleTargetInactive, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(random, Id(type, "OLD"))], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleTargetMerges, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(pp, random), new SampleConditionPair(random, fasting)], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleSourceIsTarget, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(random, fasting), new SampleConditionPair(pp, random)], Now)));
        Assert.Equal(SampleTypeCodes.CompatibleConditionListedTwice, Refusal(() => type.SetCompatibleConditions([new SampleConditionPair(random, fasting), new SampleConditionPair(random, null)], Now)));
        Assert.All(type.Conditions, condition => Assert.Null(condition.MergesIntoConditionId));
    }

    [Fact]
    public void Deactivating_the_target_releases_the_conditions_that_merged_into_it()
    {
        var type = Serum();
        type.SetCompatibleConditions([new SampleConditionPair(Id(type, "RAND"), Id(type, "FAST"))], Now);
        type.ClearDomainEvents();

        type.ReplaceConditions(
            [.. type.Conditions.Select(condition => new SampleConditionSpec(condition.Id, condition.Code, condition.Name, condition.Code != "FAST" && condition.IsActive))],
            Now);

        Assert.Null(type.Conditions.Single(condition => condition.Code == "RAND").MergesIntoConditionId);
        Assert.Contains(type.DomainEvents, raised => raised is SampleConditionCompatibilityChanged { MergingConditionCount: 0 });
    }
}
