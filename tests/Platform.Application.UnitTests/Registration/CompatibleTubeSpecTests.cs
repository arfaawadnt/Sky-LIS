using Platform.Application.Catalogue;
using Platform.Application.Registration;
using Platform.Domain.Catalogue;

namespace Platform.Application.UnitTests.Registration;

/// <summary>
/// LIS-CR-015 M4: <see cref="OrderPlanner.TubeSpecOf"/> carries the compatible target of the service's condition when that
/// target is active and does not merge itself; the planner decides later whether the merge happens.
/// </summary>
public sealed class CompatibleTubeSpecTests
{
    private static readonly Guid Group = Guid.CreateVersion7();
    private static readonly Guid Type = Guid.CreateVersion7();
    private static readonly Guid Fasting = Guid.CreateVersion7();
    private static readonly Guid Random = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, CatalogueClinicalGroupInfo> Groups = new()
    {
        [Group] = new CatalogueClinicalGroupInfo(Group, "CHEM", "C", "Chemistry", "الكيمياء", null, null, 1, true),
    };

    private static CatalogueServiceInfo Service(Guid condition) =>
        new(Guid.CreateVersion7(), "RBS", "RBS", null, "Random sugar", "سكر عشوائي", null, ServiceType.Test, Group, Type, condition, GenderRestriction.Any, 0, 1, null, false, true, true, null, 1, true, []);

    private static Dictionary<Guid, CatalogueSampleTypeInfo> Types(bool fastingActive = true, Guid? fastingMergesInto = null) => new()
    {
        [Type] = new CatalogueSampleTypeInfo(Type, "SER", "Serum", "Serum", "مصل", true,
        [
            new CatalogueSampleConditionInfo(Fasting, "FAST", "Fasting", "صائم", fastingActive, fastingMergesInto),
            new CatalogueSampleConditionInfo(Random, "RAND", "Random", "عشوائي", true, Fasting),
        ]),
    };

    [Fact]
    public void The_spec_of_a_compatible_condition_names_its_own_condition_and_carries_the_target()
    {
        var spec = OrderPlanner.TubeSpecOf(Service(Random), Groups, Types())!;

        Assert.Equal(Random, spec.SampleConditionId);
        Assert.Equal("Random", spec.SampleConditionNameEn);
        Assert.Equal(Fasting, spec.MergesIntoConditionId);
        Assert.Equal(spec.Key with { SampleConditionId = Fasting }, spec.MergeKey);
    }

    [Fact]
    public void An_inactive_or_chained_target_is_ignored_and_the_target_itself_carries_none()
    {
        Assert.Null(OrderPlanner.TubeSpecOf(Service(Random), Groups, Types(fastingActive: false))!.MergesIntoConditionId);
        Assert.Null(OrderPlanner.TubeSpecOf(Service(Random), Groups, Types(fastingMergesInto: Guid.CreateVersion7()))!.MergesIntoConditionId);
        Assert.Null(OrderPlanner.TubeSpecOf(Service(Fasting), Groups, Types())!.MergeKey);
    }
}
