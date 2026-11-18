using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class ReplayTests
{
    [Fact]
    public void Played_ascension_is_evidence()
    {
        var result = Replay.Run(new[] { Sp(Ironclad, 6) }, AllCaps);
        Assert.Equal(new Replay.Result(6, 1), result[Scope.Character(Ironclad)]);
    }

    [Fact]
    public void Win_at_evidence_advances()
    {
        var result = Replay.Run(new[] { Sp(Ironclad, 6, win: true), Sp(Ironclad, 7, win: true) }, AllCaps);
        Assert.Equal(8, result[Scope.Character(Ironclad)].Evidence);
    }

    [Fact]
    public void Win_below_evidence_does_not_advance()
    {
        var result = Replay.Run(new[] { Sp(Ironclad, 6), Sp(Ironclad, 4, win: true) }, AllCaps);
        Assert.Equal(6, result[Scope.Character(Ironclad)].Evidence);
    }

    [Fact]
    public void Win_at_cap_does_not_exceed_cap()
    {
        var result = Replay.Run(new[] { Sp(Ironclad, 10, win: true) }, AllCaps);
        Assert.Equal(10, result[Scope.Character(Ironclad)].Evidence);
    }

    [Fact]
    public void Runs_are_ordered_by_start_time_not_input_order()
    {
        var a = new RunRecord(100, RunMode.Standard, false, true, 3, new[] { Silent });
        var b = new RunRecord(200, RunMode.Standard, false, true, 4, new[] { Silent });
        // In time order: win A3 -> 4, win A4 -> 5. Reversed would give 4.
        var result = Replay.Run(new[] { b, a }, AllCaps);
        Assert.Equal(5, result[Scope.Character(Silent)].Evidence);
    }

    [Fact]
    public void Daily_dread_custom_and_unknown_are_skipped()
    {
        var runs = new[]
        {
            Run(RunMode.Daily, 10, win: true),
            Run(RunMode.Dread, 10, win: true),
            Run(RunMode.Unknown, 10, win: true),
            Run(RunMode.Standard, 10, win: true, custom: true),
        };
        Assert.Empty(Replay.Run(runs, AllCaps));
    }

    [Fact]
    public void Abandoned_run_still_counts_as_evidence()
    {
        // An abandoned run is a loss with an ascension; it is not skipped.
        var result = Replay.Run(new[] { Sp(Defect, 7, win: false) }, AllCaps);
        Assert.Equal(7, result[Scope.Character(Defect)].Evidence);
    }

    [Fact]
    public void Multiple_players_go_to_multiplayer()
    {
        var result = Replay.Run(new[] { MpRun(3, win: true) }, AllCaps);
        Assert.Equal(4, result[Scope.Multiplayer].Evidence);
        Assert.False(result.ContainsKey(Scope.Character(Ironclad)));
    }

    [Fact]
    public void Blitz_any_player_count_goes_to_blitz_with_its_cap()
    {
        var runs = new[]
        {
            Run(RunMode.Blitz, 4, win: true, characters: Ironclad),
            Run(RunMode.Blitz, 5, win: true, characters: new[] { Ironclad, Silent }),
        };
        var result = Replay.Run(runs, AllCaps);
        Assert.Equal(5, result[Scope.Blitz].Evidence);
        Assert.Single(result);
    }

    [Fact]
    public void Old_blitz_values_above_cap_are_clamped()
    {
        var result = Replay.Run(new[] { Run(RunMode.Blitz, 9) }, AllCaps);
        Assert.Equal(5, result[Scope.Blitz].Evidence);
    }

    [Fact]
    public void Blitz_runs_skipped_when_build_has_no_blitz()
    {
        Assert.Empty(Replay.Run(new[] { Run(RunMode.Blitz, 3) }, CapsNoBlitz));
    }

    [Fact]
    public void Run_with_no_players_is_skipped()
    {
        var run = new RunRecord(1, RunMode.Standard, false, true, 3, Array.Empty<string>());
        Assert.Empty(Replay.Run(new[] { run }, AllCaps));
    }

    [Fact]
    public void Replay_is_deterministic()
    {
        var runs = new[] { Sp(Ironclad, 2, true), Sp(Ironclad, 3, true), MpRun(5), Run(RunMode.Blitz, 1, true) };
        var first = Replay.Run(runs, AllCaps);
        var second = Replay.Run(Enumerable.Reverse(runs).ToArray(), AllCaps);
        Assert.Equal(first.OrderBy(k => k.Key.ToString()), second.OrderBy(k => k.Key.ToString()));
    }
}
