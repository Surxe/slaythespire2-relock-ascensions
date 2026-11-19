using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class PreviewSummaryTests
{
    [Fact]
    public void Recompute_lists_every_scope_with_runs_then_the_random_clamp()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 10),
            Char(Ironclad, 10, 10), Char(Silent, 10, 3), Mp(10, 10), Blitz(5, 5));
        var runs = new[] { Sp(Ironclad, 4), Sp(Ironclad, 4), Sp(Silent, 2), MpRun(10) };

        var result = Planner.Recompute(snap, runs, AllCaps);
        var summary = PreviewSummary.Build(result.Plan, result.Evidence);

        Assert.Collection(summary.Rows,
            r => { Assert.Equal(Scope.Character(Ironclad), r.Scope); Assert.Equal(4, r.Change!.NewMax); Assert.Equal(2, r.Runs); },
            r => { Assert.Equal(Scope.Character(Silent), r.Scope); Assert.Equal(2, r.Change!.NewMax); Assert.Equal(1, r.Runs); },
            r => { Assert.Equal(Scope.Multiplayer, r.Scope); Assert.Null(r.Change); Assert.Equal(10, r.CurrentMax); Assert.Equal(1, r.Runs); },
            r => { Assert.Equal(Scope.Blitz, r.Scope); Assert.Null(r.Change); Assert.Equal(5, r.CurrentMax); Assert.Equal(0, r.Runs); },
            r => { Assert.Equal(RandomId, r.Scope.CharacterId); Assert.Equal(4, r.Change!.NewPreferred); Assert.Null(r.Runs); });
        Assert.True(summary.HasChanges);
        Assert.Equal(1, summary.NoRunsCount);
    }

    [Fact]
    public void Recompute_with_nothing_to_change_still_lists_every_scope()
    {
        var snap = Snapshot(Char(Ironclad, 3, 3), Mp(0, 0));
        var result = Planner.Recompute(snap, new[] { Sp(Ironclad, 3) }, AllCaps);

        var summary = PreviewSummary.Build(result.Plan, result.Evidence);

        Assert.False(summary.HasChanges);
        Assert.Equal(2, summary.Rows.Count);
        Assert.Equal(1, summary.NoRunsCount);
    }

    [Fact]
    public void Rows_carry_the_epoch_to_reveal()
    {
        var snap = Snapshot(HiddenChar(Defect, 1, 1));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Defect), 6) });

        var row = Assert.Single(PreviewSummary.Build(plan, null).Rows);
        Assert.Equal("DEFECT4_EPOCH", row.Change!.RevealEpoch);
    }

    [Fact]
    public void Manual_preview_lists_only_changes_without_run_counts()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Ironclad), 3),
            new RelockRequest(Scope.Multiplayer, 0),
        });

        var summary = PreviewSummary.Build(plan, null);

        var row = Assert.Single(summary.Rows);
        Assert.Null(row.Runs);
        Assert.Equal(0, summary.NoRunsCount);
        Assert.Single(summary.Rejections);
    }
}
