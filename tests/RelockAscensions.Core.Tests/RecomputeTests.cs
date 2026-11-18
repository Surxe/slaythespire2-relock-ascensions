using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class RecomputeTests
{
    [Fact]
    public void Lowers_scope_to_evidence()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10));
        var result = Planner.Recompute(snap, new[] { Sp(Ironclad, 5, win: true) }, AllCaps);

        Assert.Equal(new Change(Scope.Character(Ironclad), 10, 6, 10, 6), Assert.Single(result.Plan.Changes));
    }

    [Fact]
    public void Scope_with_no_evidence_is_unchanged_and_reported_as_unknown()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), Blitz(5, 5));
        var result = Planner.Recompute(snap, new[] { Sp(Ironclad, 10) }, AllCaps);

        Assert.True(result.Plan.IsEmpty);
        var blitz = result.Evidence.Single(e => e.Scope == Scope.Blitz);
        Assert.Null(blitz.Evidence);
        Assert.Equal(0, blitz.Runs);
    }

    [Fact]
    public void Never_raises()
    {
        var snap = Snapshot(Char(Ironclad, 2, 2), Mp(0, 0));
        var runs = new[] { Sp(Ironclad, 9, win: true), MpRun(8, win: true) };
        var result = Planner.Recompute(snap, runs, AllCaps);

        Assert.True(result.Plan.IsEmpty);
        Assert.Empty(result.Plan.Rejections);
    }

    [Fact]
    public void Evidence_equal_to_max_is_no_change()
    {
        var snap = Snapshot(Char(Defect, 9, 9));
        var result = Planner.Recompute(snap, new[] { Sp(Defect, 8, win: true) }, AllCaps);

        Assert.True(result.Plan.IsEmpty);
    }

    [Fact]
    public void History_for_characters_not_in_save_is_ignored()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10));
        var result = Planner.Recompute(snap, new[] { Sp("CHARACTER.WATCHER", 1), Sp(Ironclad, 10) }, AllCaps);

        Assert.True(result.Plan.IsEmpty);
        Assert.Single(result.Evidence);
    }

    [Fact]
    public void Clamps_random_preferred_after_recompute()
    {
        var snap = Snapshot(new RandomState(RandomId, 10, 10), Char(Ironclad, 10, 10), Char(Silent, 10, 10));
        var runs = new[] { Sp(Ironclad, 4), Sp(Silent, 6) };
        var result = Planner.Recompute(snap, runs, AllCaps);

        Assert.Equal(6, result.Plan.Changes.Single(c => c.Scope == Scope.Character(RandomId)).NewPreferred);
    }
}
