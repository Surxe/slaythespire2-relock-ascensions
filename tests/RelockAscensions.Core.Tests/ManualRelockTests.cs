using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class ManualRelockTests
{
    [Fact]
    public void Lowers_max_and_clamps_preferred()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), Char(Silent, 10, 7));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 5) });

        var change = Assert.Single(plan.Changes);
        Assert.Equal(new Change(Scope.Character(Ironclad), 10, 5, 10, 5), change);
        Assert.Empty(plan.Rejections);
    }

    [Fact]
    public void Preferred_below_new_max_is_kept()
    {
        var snap = Snapshot(Char(Silent, 10, 3));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Silent), 6) });

        Assert.Equal(new Change(Scope.Character(Silent), 10, 6, 3, 3), Assert.Single(plan.Changes));
    }

    [Fact]
    public void Full_relock_to_zero_is_allowed()
    {
        var snap = Snapshot(Mp(10, 10));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Multiplayer, 0) });

        Assert.Equal(new Change(Scope.Multiplayer, 10, 0, 10, 0), Assert.Single(plan.Changes));
    }

    [Fact]
    public void Raise_up_to_the_cap_keeps_preferred()
    {
        var snap = Snapshot(Char(Defect, 4, 2));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Defect), 10) });

        var change = Assert.Single(plan.Changes);
        Assert.Equal(new Change(Scope.Character(Defect), 4, 10, 2, 2), change);
        Assert.True(change.Raises);
        Assert.False(change.Lowers);
        Assert.Empty(plan.Rejections);
    }

    [Fact]
    public void Above_the_cap_is_rejected_not_clamped()
    {
        var snap = Snapshot(Char(Defect, 4, 4), Blitz(2, 2));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Defect), 11),
            new RelockRequest(Scope.Blitz, 6),
        });

        Assert.Empty(plan.Changes);
        Assert.All(plan.Rejections, r => Assert.Equal(RejectionReason.AboveCap, r.Reason));
        Assert.Equal(2, plan.Rejections.Count);
    }

    [Fact]
    public void Raising_locked_blitz_is_rejected_but_lowering_it_is_allowed()
    {
        var snap = Snapshot(LockedBlitz(2, 2));

        var raise = Planner.Plan(snap, new[] { new RelockRequest(Scope.Blitz, 3) });
        Assert.Empty(raise.Changes);
        Assert.Equal(RejectionReason.RaiseLocked, Assert.Single(raise.Rejections).Reason);

        var lower = Planner.Plan(snap, new[] { new RelockRequest(Scope.Blitz, 1) });
        Assert.Equal(new Change(Scope.Blitz, 2, 1, 2, 1), Assert.Single(lower.Changes));
    }

    [Fact]
    public void Raise_reveals_the_hidden_epoch()
    {
        var snap = Snapshot(HiddenChar(Ironclad, 0, 0));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 5) });

        Assert.Equal("IRONCLAD4_EPOCH", Assert.Single(plan.Changes).RevealEpoch);
    }

    [Fact]
    public void No_epoch_reveal_when_lowering_revealed_or_ungated()
    {
        var snap = Snapshot(HiddenChar(Ironclad, 5, 5), Char(Silent, 3, 3), Char(Modded, 0, 0), Mp(0, 0));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Ironclad), 2), // lowering
            new RelockRequest(Scope.Character(Silent), 8), // epoch already revealed
            new RelockRequest(Scope.Character(Modded), 4), // no gate
            new RelockRequest(Scope.Multiplayer, 10), // no gate
        });

        Assert.Equal(4, plan.Changes.Count);
        Assert.All(plan.Changes, c => Assert.Null(c.RevealEpoch));
    }

    [Fact]
    public void Negative_is_rejected()
    {
        var snap = Snapshot(Blitz(5, 5));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Blitz, -1) });

        Assert.Empty(plan.Changes);
        Assert.Equal(RejectionReason.Negative, Assert.Single(plan.Rejections).Reason);
    }

    [Fact]
    public void Unknown_scope_is_rejected()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character("CHARACTER.WATCHER"), 0),
            new RelockRequest(Scope.Blitz, 0), // Blitz absent on this build
        });

        Assert.Empty(plan.Changes);
        Assert.All(plan.Rejections, r => Assert.Equal(RejectionReason.UnknownScope, r.Reason));
        Assert.Equal(2, plan.Rejections.Count);
    }

    [Fact]
    public void Random_is_not_relockable_directly()
    {
        var snap = Snapshot(new RandomState(RandomId, 10, 10), Char(Ironclad, 10, 10));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(RandomId), 0) });

        Assert.Empty(plan.Changes);
        Assert.Equal(RejectionReason.UnknownScope, Assert.Single(plan.Rejections).Reason);
    }

    [Fact]
    public void Duplicate_request_keeps_the_first()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Ironclad), 4),
            new RelockRequest(Scope.Character(Ironclad), 2),
        });

        Assert.Equal(4, Assert.Single(plan.Changes).NewMax);
        Assert.Equal(RejectionReason.Duplicate, Assert.Single(plan.Rejections).Reason);
    }

    [Fact]
    public void Same_value_request_is_a_no_op()
    {
        var snap = Snapshot(Char(Ironclad, 7, 7));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 7) });

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Rejections);
    }

    [Fact]
    public void Only_requested_scope_changes()
    {
        var snap = Snapshot(Char(Ironclad, 10, 10), Char(Silent, 10, 10), Mp(10, 10), Blitz(5, 5));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Blitz, 2) });

        Assert.Equal(Scope.Blitz, Assert.Single(plan.Changes).Scope);
    }
}
