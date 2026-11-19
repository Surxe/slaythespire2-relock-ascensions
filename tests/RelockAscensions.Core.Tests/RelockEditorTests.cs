using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class RelockEditorTests
{
    [Fact]
    public void Rows_offer_the_ceiling_and_take_history_values()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), HiddenChar(Silent, 3, 3), Mp(10, 10), LockedBlitz(2, 2));
        var runs = new[]
        {
            Sp(Ironclad, 4), Sp(Ironclad, 4, win: true), Sp(Silent, 3, win: true), MpRun(10),
            Run(RunMode.Blitz, 4, win: true),
        };
        var evidence = Planner.Recompute(snap, runs, AllCaps).Evidence;

        var rows = RelockEditor.Rows(snap, evidence);

        Assert.Collection(rows, // locked Blitz gets no row
            r => { Assert.Equal(5, r.Earned); Assert.Equal(5, r.FromHistory); Assert.Equal(2, r.Runs); Assert.Equal(10, r.Max); Assert.False(r.RevealsEpoch); },
            r => { Assert.Equal(4, r.FromHistory); Assert.Equal(3, r.Unlocked); Assert.Equal(10, r.Max); Assert.True(r.RevealsEpoch); }, // above unlocked
            r => { Assert.Equal(10, r.FromHistory); Assert.Equal(1, r.Runs); });
    }

    [Fact]
    public void Unlocked_blitz_gets_a_row()
    {
        var rows = RelockEditor.Rows(Snapshot(Blitz(2, 2)), Array.Empty<ScopeEvidence>());
        Assert.Equal(Scope.Blitz, Assert.Single(rows).Scope);
    }

    [Fact]
    public void Preferred_after_saving_is_clamped_only_when_lowering()
    {
        var row = Assert.Single(RelockEditor.Rows(Snapshot(Char(Ironclad, 8, 6)), Array.Empty<ScopeEvidence>()));

        Assert.Equal(6, row.Preferred);
        Assert.Equal(4, row.PreferredAfter(4));
        Assert.Equal(6, row.PreferredAfter(10));
    }

    [Fact]
    public void Rows_without_runs_keep_unlocked_from_history()
    {
        var snap = Snapshot(Blitz(3, 3));
        var row = Assert.Single(RelockEditor.Rows(snap, Array.Empty<ScopeEvidence>()));

        Assert.Null(row.Earned);
        Assert.Equal(3, row.FromHistory);
        Assert.Equal(5, row.Max);
    }

    [Fact]
    public void Requests_only_for_values_that_differ_from_unlocked()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), Mp(10, 10), Blitz(5, 5));
        var rows = RelockEditor.Rows(snap, Array.Empty<ScopeEvidence>());

        var requests = RelockEditor.Requests(new[] { (rows[0], 7), (rows[1], 10), (rows[2], 0) });

        Assert.Equal(new[]
        {
            new RelockRequest(Scope.Character(Ironclad), 7),
            new RelockRequest(Scope.Blitz, 0),
        }, requests);
    }

    [Fact]
    public void Edited_values_plan_like_a_manual_relock()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 10), Char(Ironclad, 10, 8));
        var rows = RelockEditor.Rows(snap, Array.Empty<ScopeEvidence>());

        var plan = Planner.Plan(snap, RelockEditor.Requests(new[] { (rows[0], 6) }));

        Assert.Equal(new[] { "CHARACTER.IRONCLAD: max 10 -> 6 (selected level 8 -> 6)", "CHARACTER.RANDOM_CHARACTER: selected level 10 -> 6" },
            PlanFormatter.FormatChanges(plan));
    }
}
