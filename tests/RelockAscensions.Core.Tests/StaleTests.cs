using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class StaleTests
{
    [Fact]
    public void Unchanged_save_is_not_stale()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 10), Char(Ironclad, 10, 10));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 3) });

        Assert.Equal(2, plan.Changes.Count);
        Assert.Empty(Planner.StaleChanges(snap, plan));
    }

    [Fact]
    public void Max_moved_since_preview_is_stale()
    {
        var before = Snapshot(Char(Ironclad, 10, 10));
        var plan = Planner.Plan(before, new[] { new RelockRequest(Scope.Character(Ironclad), 3) });
        var after = Snapshot(Char(Ironclad, 9, 10));

        Assert.Single(Planner.StaleChanges(after, plan));
    }

    [Fact]
    public void Preferred_moved_since_preview_is_stale()
    {
        var before = Snapshot(Mp(10, 10));
        var plan = Planner.Plan(before, new[] { new RelockRequest(Scope.Multiplayer, 3) });

        Assert.Single(Planner.StaleChanges(Snapshot(Mp(10, 8)), plan));
    }

    [Fact]
    public void Random_preferred_moved_is_stale()
    {
        var before = Snapshot(new RandomState(RandomId, 0, 10), Char(Ironclad, 10, 10));
        var plan = Planner.Plan(before, new[] { new RelockRequest(Scope.Character(Ironclad), 3) });
        var after = Snapshot(new RandomState(RandomId, 0, 9), Char(Ironclad, 10, 10));

        Assert.Single(Planner.StaleChanges(after, plan));
    }

    [Fact]
    public void Scope_gone_after_profile_switch_is_stale()
    {
        var before = Snapshot(Char(Ironclad, 10, 10), Blitz(5, 5));
        var plan = Planner.Plan(before, new[] { new RelockRequest(Scope.Blitz, 0) });

        Assert.Single(Planner.StaleChanges(Snapshot(Char(Ironclad, 10, 10)), plan));
    }
}
