using System;
using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>
/// Evidence replay over run history (spec 4.4), using the current mode and cap rules.
/// The result per scope is a lower bound on what was unlocked.
/// </summary>
public static class Replay
{
    public sealed record Result(int Evidence, int Runs);

    /// <summary>
    /// Which scope a run belongs to, or null when the run is skipped (Daily, Dread, custom,
    /// unknown mode, no players, or Blitz on a build without Blitz).
    /// </summary>
    public static Scope? Classify(RunRecord run, Caps caps)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.IsCustom)
        {
            return null;
        }
        switch (run.Mode)
        {
            case RunMode.Blitz:
                // Blitz is checked first, regardless of player count.
                return caps.Blitz is null ? null : Scope.Blitz;
            case RunMode.Standard:
                if (run.Characters.Count == 0)
                {
                    return null;
                }
                return run.Characters.Count == 1 ? Scope.Character(run.Characters[0]) : Scope.Multiplayer;
            default:
                return null; // Daily, Dread, Unknown
        }
    }

    /// <summary>
    /// Replays eligible runs in <c>start_time</c> order. Abandoned runs are included: starting at A
    /// proves A was unlocked, and they are never wins.
    /// </summary>
    public static IReadOnlyDictionary<Scope, Result> Run(IEnumerable<RunRecord> runs, Caps caps)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(caps);

        var evidence = new Dictionary<Scope, int>();
        var counts = new Dictionary<Scope, int>();

        // OrderBy is stable, so equal start times keep input order.
        foreach (var run in runs.OrderBy(r => r.StartTime))
        {
            var scope = Classify(run, caps);
            if (scope is null || caps.For(scope) is not { } cap)
            {
                continue;
            }

            // History can carry values from older rules (Blitz allowed 10 before it was cut to 5).
            var ascension = Math.Clamp(run.Ascension, 0, cap);

            var current = evidence.TryGetValue(scope, out var e) ? e : 0;
            current = Math.Max(current, ascension);
            if (run.Win && ascension == current && current < cap)
            {
                current++;
            }

            evidence[scope] = current;
            counts[scope] = (counts.TryGetValue(scope, out var c) ? c : 0) + 1;
        }

        return evidence.ToDictionary(kv => kv.Key, kv => new Result(kv.Value, counts[kv.Key]));
    }
}
