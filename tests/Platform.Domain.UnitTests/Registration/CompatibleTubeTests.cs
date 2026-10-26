using Platform.Domain.Registration;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>
/// Compatibility groups in tube planning (LIS-CR-015 M4): needs of one clinical group whose pairs are in the same group share
/// one tube, named after the best-priority member needed; a live compatible tube is joined, never renamed; different units
/// never merge; pairs outside any group behave as before.
/// </summary>
public sealed class CompatibleTubeTests
{
    private static readonly Guid Random = Guid.CreateVersion7();
    private static readonly Guid Plasma = Guid.CreateVersion7();
    private static readonly Guid Fluoride = Guid.CreateVersion7();
    private static readonly Guid GroupId = Guid.CreateVersion7();

    /// <summary>The group: 1 Serum·Fasting, 2 Serum·Random, 3 Plasma·Fluoride.</summary>
    private static readonly IReadOnlyList<CompatibleTubeMember> Members =
    [
        new(Serum, Fasting, 1),
        new(Serum, Random, 2),
        new(Plasma, Fluoride, 3),
    ];

    private static TubeCompatibility In(int priority) => new(GroupId, priority, Members);

    private static TubeSpec SerumFasting(Guid? unit = null) => ChemistrySerum(Fasting) with { ClinicalGroupId = unit ?? Chemistry, Compatibility = In(1) };

    private static TubeSpec SerumRandom(Guid? unit = null) =>
        new(unit ?? Chemistry, Serum, Random, "C", "Chemistry", "الكيمياء", "Serum", "مصل", "Random", "عشوائي", In(2));

    private static TubeSpec PlasmaFluoride() =>
        new(Chemistry, Plasma, Fluoride, "C", "Chemistry", "الكيمياء", "Plasma", "بلازما", "Fluoride", "فلورايد", In(3));

    [Fact]
    public void Two_members_share_one_tube_keyed_after_the_better_priority_whatever_the_order()
    {
        var plan = TubePlanner.Assign([], [SerumRandom().Need, SerumFasting().Need, SerumRandom().Need]);

        Assert.Equal([SerumFasting().Key], plan.NewTubes);
        Assert.All(plan.Assignments, assignment => Assert.Equal((0, SerumFasting().Key), (assignment!.NewTubeIndex!.Value, assignment.Key!)));
    }

    [Fact]
    public void Mixed_sample_types_of_one_group_merge_under_the_best_priority_present()
    {
        var plan = TubePlanner.Assign([], [PlasmaFluoride().Need, SerumRandom().Need]);
        Assert.Equal([SerumRandom().Key], plan.NewTubes);
    }

    [Fact]
    public void A_lone_member_keeps_its_own_key_and_a_pair_outside_any_group_is_untouched()
    {
        Assert.Equal([SerumRandom().Key], TubePlanner.Assign([], [SerumRandom().Need]).NewTubes);
        var plain = HaematologyBlood();
        Assert.Equal([SerumRandom().Key, plain.Key], TubePlanner.Assign([], [SerumRandom().Need, plain.Need]).NewTubes);
    }

    [Fact]
    public void A_live_compatible_tube_is_joined_whatever_its_priority_and_a_dead_one_is_not()
    {
        var randomTube = Guid.CreateVersion7();
        var joined = TubePlanner.Assign([new TubePlanner.ExistingTube(randomTube, SerumRandom().Key, true)], [SerumFasting().Need]);
        Assert.Equal(randomTube, joined.Assignments[0]!.ExistingTubeId);
        Assert.Empty(joined.NewTubes);

        var dead = TubePlanner.Assign([new TubePlanner.ExistingTube(randomTube, SerumRandom().Key, false)], [SerumFasting().Need]);
        Assert.Equal([SerumFasting().Key], dead.NewTubes);
    }

    [Fact]
    public void Different_clinical_groups_never_merge()
    {
        var plan = TubePlanner.Assign([], [SerumFasting().Need, SerumRandom(Haematology).Need]);
        Assert.Equal(2, plan.NewTubes.Count);
        Assert.Contains(SerumRandom(Haematology).Key, plan.NewTubes);

        var existing = TubePlanner.Assign([new TubePlanner.ExistingTube(Guid.CreateVersion7(), SerumFasting(Haematology).Key, true)], [SerumRandom().Need]);
        Assert.Equal([SerumRandom().Key], existing.NewTubes);
    }

    [Fact]
    public void A_visit_with_Fasting_and_Random_has_one_Fasting_tube_carrying_both_lines()
    {
        var visit = Register(Test("RND", SerumRandom()), Test("FBS", SerumFasting()));

        var tube = Assert.Single(visit.Tubes);
        Assert.Equal(("Fasting", Fasting), (tube.SampleConditionNameEn, tube.SampleConditionId));
        Assert.All(visit.Lines, line => Assert.Equal(tube.Id, line.TubeId));
    }

    [Fact]
    public void Random_alone_keeps_Random_and_Fasting_added_later_joins_the_Random_tube_which_is_not_renamed()
    {
        var visit = Register(Test("RND", SerumRandom()));
        Assert.Equal("Random", Assert.Single(visit.Tubes).SampleConditionNameEn);

        var added = Test("FBS", SerumFasting());
        Assert.Equal(0, visit.CountNewTubes(Visit.TubeNeedsOf([added])));
        visit.AddLines([added], [], SampleNumberSource.Empty, Actor, Now);

        var tube = Assert.Single(visit.Tubes);
        Assert.Equal("Random", tube.SampleConditionNameEn);
        Assert.Equal(tube.Id, visit.Lines.Single(line => line.ServiceCode == "FBS").TubeId);
    }

    [Fact]
    public void Without_compatibility_the_needs_split_as_before()
    {
        var visit = Register(Test("RND", SerumRandom() with { Compatibility = null }), Test("FBS", SerumFasting() with { Compatibility = null }));
        Assert.Equal(2, visit.Tubes.Count);
    }
}
