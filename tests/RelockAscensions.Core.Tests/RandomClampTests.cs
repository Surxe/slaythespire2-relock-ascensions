using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class RandomClampTests
{
    [Fact]
    public void Random_preferred_clamps_to_highest_new_character_max()
    {
        var snap = Snapshot(new RandomState(RandomId, 10, 10), Char(Ironclad, 10, 10), Char(Silent, 6, 6));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 3) });

        Assert.Equal(2, plan.Changes.Count);
        var random = plan.Changes.Single(c => c.Scope == Scope.Character(RandomId));
        Assert.Equal(new Change(Scope.Character(RandomId), 10, 10, 10, 6), random);
        Assert.False(random.ChangesMax);
    }

    [Fact]
    public void Random_untouched_when_still_within_highest_max()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 8), Char(Ironclad, 10, 10), Char(Silent, 9, 9));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 2) });

        Assert.DoesNotContain(plan.Changes, c => c.Scope == Scope.Character(RandomId));
    }

    [Fact]
    public void Multiplayer_and_blitz_do_not_count_toward_random()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 10), Char(Ironclad, 10, 10), Mp(10, 10), Blitz(5, 5));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Character(Ironclad), 1) });

        Assert.Equal(1, plan.Changes.Single(c => c.Scope == Scope.Character(RandomId)).NewPreferred);
    }

    [Fact]
    public void Random_with_no_characters_clamps_to_zero()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 4), Mp(10, 10));
        var plan = Planner.Plan(snap, new[] { new RelockRequest(Scope.Multiplayer, 5) });

        Assert.Equal(0, plan.Changes.Single(c => c.Scope == Scope.Character(RandomId)).NewPreferred);
    }
}
