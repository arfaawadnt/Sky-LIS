using Platform.Domain.Registration;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>
/// Compatible sample conditions in tube planning (LIS-CR-015 M4): a condition that merges into P joins the (group, type, P)
/// tube when that tube is live on the visit or needed by the same request; alone it keeps its own tube; never the reverse.
/// </summary>
public sealed class CompatibleTubeTests
{
    private static readonly Guid Random = Guid.CreateVersion7();

    private static TubeSpec SerumFasting() => ChemistrySerum(Fasting);

    private static TubeSpec SerumRandom() =>
        new(Chemistry, Serum, Random, "C", "Chemistry", "الكيمياء", "Serum", "مصل", "Random", "عشوائي", MergesIntoConditionId: Fasting);

    [Fact]
    public void Planner_merges_a_need_into_its_target_needed_in_the_same_request_whatever_the_order()
    {
        var fasting = SerumFasting().Key;
        var plan = TubePlanner.Assign([], [SerumRandom().Need, SerumFasting().Need, SerumRandom().Need]);

        Assert.Equal([fasting], plan.NewTubes);
        Assert.All(plan.Assignments, assignment => Assert.Equal(0, assignment!.NewTubeIndex));
        Assert.All(plan.Assignments, assignment => Assert.Equal(fasting, assignment!.Key));
    }

    [Fact]
    public void Planner_merges_into_a_live_target_tube_but_not_into_a_dead_one_and_alone_keeps_its_own_key()
    {
        var fasting = SerumFasting().Key;
        var random = SerumRandom().Key;
        var live = Guid.CreateVersion7();

        Assert.Equal(live, TubePlanner.Assign([new TubePlanner.ExistingTube(live, fasting, true)], [SerumRandom().Need]).Assignments[0]!.ExistingTubeId);
        Assert.Equal([random], TubePlanner.Assign([new TubePlanner.ExistingTube(live, fasting, false)], [SerumRandom().Need]).NewTubes);
        Assert.Equal([random], TubePlanner.Assign([], [SerumRandom().Need]).NewTubes);
    }

    [Fact]
    public void Planner_never_merges_the_other_way_nor_across_units()
    {
        var random = SerumRandom().Key;
        var fasting = SerumFasting().Key;
        var otherUnit = SerumRandom() with { ClinicalGroupId = Haematology };

        var reverse = TubePlanner.Assign([new TubePlanner.ExistingTube(Guid.CreateVersion7(), random, true)], [SerumFasting().Need]);
        Assert.Equal([fasting], reverse.NewTubes);

        var across = TubePlanner.Assign([], [otherUnit.Need, SerumFasting().Need]);
        Assert.Equal(2, across.NewTubes.Count);
    }

    [Fact]
    public void A_visit_with_a_Fasting_and_a_Random_service_has_one_Fasting_tube_carrying_both_lines()
    {
        var visit = Register(Test("RND", SerumRandom()), Test("FBS", SerumFasting()));

        var tube = Assert.Single(visit.Tubes);
        Assert.Equal(Fasting, tube.SampleConditionId);
        Assert.Equal("Fasting", tube.SampleConditionNameEn);
        Assert.All(visit.Lines, line => Assert.Equal(tube.Id, line.TubeId));
    }

    [Fact]
    public void A_Random_only_visit_keeps_Random_and_a_later_Random_joins_a_live_Fasting_tube()
    {
        var randomOnly = Register(Test("RND", SerumRandom()));
        Assert.Equal("Random", Assert.Single(randomOnly.Tubes).SampleConditionNameEn);

        var visit = Register(Test("FBS", SerumFasting()));
        var added = Test("RND", SerumRandom());
        Assert.Equal(0, visit.CountNewTubes(Visit.TubeNeedsOf([added])));
        visit.AddLines([added], [], SampleNumberSource.Empty, Actor, Now);
        Assert.Equal(Assert.Single(visit.Tubes).Id, visit.Lines.Single(line => line.ServiceCode == "RND").TubeId);
    }

    [Fact]
    public void Fasting_added_after_Random_opens_its_own_tube_and_the_Random_tube_is_not_renamed()
    {
        var visit = Register(Test("RND", SerumRandom()));
        var added = Test("FBS", SerumFasting());

        Assert.Equal(1, visit.CountNewTubes(Visit.TubeNeedsOf([added])));
        visit.AddLines([added], [], Numbers(1, start: 20), Actor, Now);

        Assert.Equal(2, visit.Tubes.Count);
        Assert.Contains(visit.Tubes, tube => tube.SampleConditionNameEn == "Random");
        Assert.Contains(visit.Tubes, tube => tube.SampleConditionNameEn == "Fasting");
    }
}
