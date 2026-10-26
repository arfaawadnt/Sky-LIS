using Platform.Application.Catalogue;
using Platform.Application.Registration;
using Platform.Domain.Catalogue;

namespace Platform.Application.UnitTests.Registration;

/// <summary>
/// LIS-CR-015 M4: <see cref="OrderPlanner.TubeSpecOf"/> carries the active compatibility group of the service's (sample
/// type, condition) pair, with the pair's priority and the group's available members; a pair in no group carries none.
/// </summary>
public sealed class CompatibleTubeSpecTests
{
    private static readonly Guid Group = Guid.CreateVersion7();
    private static readonly Guid Type = Guid.CreateVersion7();
    private static readonly Guid Fasting = Guid.CreateVersion7();
    private static readonly Guid Random = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();
    private static readonly Guid Compatibility = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, CatalogueClinicalGroupInfo> Groups = new()
    {
        [Group] = new CatalogueClinicalGroupInfo(Group, "CHEM", "C", "Chemistry", "الكيمياء", null, null, 1, true),
    };

    private static CatalogueServiceInfo Service(Guid? condition) =>
        new(Guid.CreateVersion7(), "RBS", "RBS", null, "Random sugar", "سكر عشوائي", null, ServiceType.Test, Group, Type, condition, GenderRestriction.Any, 0, 1, null, false, true, true, null, 1, true, []);

    private static Dictionary<Guid, CatalogueSampleTypeInfo> Types(IReadOnlyList<CatalogueCompatibilityGroupInfo>? groups) => new()
    {
        [Type] = new CatalogueSampleTypeInfo(Type, "SER", "Serum", "Serum", "مصل", true,
        [
            new CatalogueSampleConditionInfo(Fasting, "FAST", "Fasting", "صائم", true),
            new CatalogueSampleConditionInfo(Random, "RAND", "Random", "عشوائي", true),
            new CatalogueSampleConditionInfo(Other, "OTH", "Other", "أخرى", true),
        ], groups),
    };

    private static readonly IReadOnlyList<CatalogueCompatibilityGroupInfo> SugarGroup =
    [
        new(Compatibility, "SUGAR", [new(Type, Fasting, 1), new(Type, Random, 2)]),
    ];

    [Fact]
    public void A_pair_in_an_active_group_carries_the_group_its_priority_and_the_members()
    {
        var spec = OrderPlanner.TubeSpecOf(Service(Random), Groups, Types(SugarGroup))!;

        Assert.Equal(("Random", Random), (spec.SampleConditionNameEn, spec.SampleConditionId));
        Assert.Equal((Compatibility, 2), (spec.Compatibility!.GroupId, spec.Compatibility.Priority));
        Assert.Equal(1, spec.Compatibility.PriorityOf(spec.Key with { SampleConditionId = Fasting }));
    }

    [Fact]
    public void A_pair_in_no_group_or_without_a_group_read_carries_none()
    {
        Assert.Null(OrderPlanner.TubeSpecOf(Service(Other), Groups, Types(SugarGroup))!.Compatibility);
        Assert.Null(OrderPlanner.TubeSpecOf(Service(null), Groups, Types(SugarGroup))!.Compatibility);
        Assert.Null(OrderPlanner.TubeSpecOf(Service(Random), Groups, Types(null))!.Compatibility);
    }
}
