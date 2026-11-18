using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class RelockEditorTests
{
    [Fact]
    public void Rows_start_at_earned_and_keep_unlocked_without_runs()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), Char(Silent, 3, 3), Mp(10, 10), Blitz(5, 5));
        var runs = new[] { Sp(Ironclad, 4), Sp(Ironclad, 4, win: true), Sp(Silent, 3, win: true), MpRun(10) };
        var evidence = Planner.Recompute(snap, runs, AllCaps).Evidence;

        var rows = RelockEditor.Rows(snap, evidence);

        Assert.Collection(rows,
            r => { Assert.Equal(5, r.Earned); Assert.Equal(5, r.Suggested); Assert.Equal(2, r.Runs); Assert.Equal(10, r.Max); },
            r => { Assert.Equal(4, r.Earned); Assert.Equal(3, r.Suggested); }, // earned above unlocked: capped
            r => { Assert.Equal(10, r.Suggested); Assert.Equal(1, r.Runs); },
            r => { Assert.Null(r.Earned); Assert.Equal(5, r.Suggested); Assert.Equal(0, r.Runs); });
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
