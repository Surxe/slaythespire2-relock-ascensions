using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class PlanFormatterTests
{
    [Fact]
    public void Formats_changes_and_rejections()
    {
        var snap = Snapshot(new RandomState(RandomId, 0, 10), Char(Ironclad, 10, 10), Mp(10, 4));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Ironclad), 5),
            new RelockRequest(Scope.Multiplayer, 6),
            new RelockRequest(Scope.Blitz, 0),
        });

        Assert.Equal(
            "CHARACTER.IRONCLAD: max 10 -> 5 (selected level 10 -> 5)\n" +
            "Multiplayer: max 10 -> 6\n" +
            "CHARACTER.RANDOM_CHARACTER: selected level 10 -> 5\n" +
            "rejected Blitz: not a relockable scope in the loaded save",
            PlanFormatter.Format(plan));
    }

    [Fact]
    public void Raise_that_reveals_an_epoch_says_so()
    {
        var snap = Snapshot(HiddenChar(Fool, 0, 0), Char(Ironclad, 2, 2));
        var plan = Planner.Plan(snap, new[]
        {
            new RelockRequest(Scope.Character(Fool), 3),
            new RelockRequest(Scope.Character(Ironclad), 4),
        });

        Assert.Equal(new[]
        {
            "CHARACTER.FOOL: max 0 -> 3, reveals FOOL4_EPOCH (ascension + potions)",
            "CHARACTER.IRONCLAD: max 2 -> 4",
        }, PlanFormatter.FormatChanges(plan));
    }

    [Fact]
    public void Empty_plan()
    {
        Assert.Equal("No changes.", PlanFormatter.Format(Plan.Empty));
    }

    [Fact]
    public void Evidence_lines()
    {
        var lines = PlanFormatter.FormatEvidence(new[]
        {
            new ScopeEvidence(Scope.Character(Ironclad), 7, 1, 10),
            new ScopeEvidence(Scope.Blitz, null, 0, 5),
        });
        Assert.Equal(new[] { "CHARACTER.IRONCLAD: evidence 7 (1 run)", "Blitz: no evidence, left unchanged" }, lines);
    }
}
