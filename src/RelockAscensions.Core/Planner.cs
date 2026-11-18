using System;
using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>
/// Builds plans from requests (manual relock, spec 4.3) or from history (recompute, spec 4.4).
/// Pure: no game types, no I/O. Plans only ever lower a max.
/// </summary>
public static class Planner
{
    /// <summary>Manual relock: validate each request and produce the resulting changes.</summary>
    public static Plan Plan(ProgressSnapshot snapshot, IEnumerable<RelockRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requests);

        var states = snapshot.Scopes.ToDictionary(s => s.Scope);
        var accepted = new Dictionary<Scope, int>();
        var rejections = new List<Rejection>();

        foreach (var request in requests)
        {
            var rejection = Validate(request, states, accepted);
            if (rejection is not null)
            {
                rejections.Add(rejection);
                continue;
            }
            accepted[request.Scope] = request.NewMax;
        }

        var changes = new List<Change>();
        // Keep the snapshot's scope order so previews are stable.
        foreach (var state in snapshot.Scopes)
        {
            if (!accepted.TryGetValue(state.Scope, out var newMax))
            {
                continue;
            }
            var newPreferred = Math.Min(state.Preferred, newMax);
            if (newMax == state.Max && newPreferred == state.Preferred)
            {
                continue; // no-op request
            }
            changes.Add(new Change(state.Scope, state.Max, newMax, state.Preferred, newPreferred));
        }

        var randomChange = ClampRandom(snapshot, changes);
        if (randomChange is not null)
        {
            changes.Add(randomChange);
        }

        return new Plan(changes, rejections);
    }

    /// <summary>
    /// Recompute from history: replay, then request <c>NewMax = evidence</c> for every scope whose
    /// evidence is below its current max. Scopes with no evidence are left alone.
    /// </summary>
    public static RecomputeResult Recompute(ProgressSnapshot snapshot, IEnumerable<RunRecord> runs, Caps caps)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(caps);

        var replay = Replay.Run(runs, caps);

        var evidence = new List<ScopeEvidence>();
        var requests = new List<RelockRequest>();
        foreach (var state in snapshot.Scopes)
        {
            replay.TryGetValue(state.Scope, out var result);
            evidence.Add(new ScopeEvidence(state.Scope, result?.Evidence, result?.Runs ?? 0, state.Max));

            if (result is null)
            {
                continue; // no evidence: never report or apply 0
            }
            if (result.Evidence < state.Max)
            {
                requests.Add(new RelockRequest(state.Scope, result.Evidence));
            }
        }

        return new RecomputeResult(Plan(snapshot, requests), evidence);
    }

    /// <summary>
    /// Changes whose old values no longer match <paramref name="current"/> (spec 5.2). A plan with
    /// any stale change must be discarded, not partially applied.
    /// </summary>
    public static IReadOnlyList<string> StaleChanges(ProgressSnapshot current, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(plan);

        var byScope = current.Scopes.ToDictionary(s => s.Scope);
        var stale = new List<string>();
        foreach (var c in plan.Changes)
        {
            int max, preferred;
            if (byScope.TryGetValue(c.Scope, out var s))
            {
                (max, preferred) = (s.Max, s.Preferred);
            }
            else if (current.Random is { } r && c.Scope == Scope.Character(r.CharacterId))
            {
                (max, preferred) = (r.Max, r.Preferred);
            }
            else
            {
                stale.Add($"{c.Scope}: no longer in the save");
                continue;
            }
            if (max != c.OldMax || preferred != c.OldPreferred)
            {
                stale.Add($"{c.Scope}: now {max}/{preferred}, preview had {c.OldMax}/{c.OldPreferred}");
            }
        }
        return stale;
    }

    private static Rejection? Validate(
        RelockRequest request,
        IReadOnlyDictionary<Scope, ScopeState> states,
        IReadOnlyDictionary<Scope, int> accepted)
    {
        if (!states.TryGetValue(request.Scope, out var state))
        {
            return new Rejection(request, RejectionReason.UnknownScope,
                $"{request.Scope}: not a relockable scope in the loaded save");
        }
        if (accepted.ContainsKey(request.Scope))
        {
            return new Rejection(request, RejectionReason.Duplicate,
                $"{request.Scope}: requested more than once");
        }
        if (request.NewMax < 0)
        {
            return new Rejection(request, RejectionReason.Negative,
                $"{request.Scope}: {request.NewMax} is below 0");
        }
        if (request.NewMax > state.Max)
        {
            return new Rejection(request, RejectionReason.AboveCurrentMax,
                $"{request.Scope}: {request.NewMax} is above the current max {state.Max}; raising is not supported");
        }
        return null;
    }

    /// <summary>
    /// Random's preferred is clamped to the highest Character max after the plan (spec 4.5).
    /// Its own max is not touched.
    /// </summary>
    private static Change? ClampRandom(ProgressSnapshot snapshot, IReadOnlyList<Change> changes)
    {
        if (snapshot.Random is not { } random)
        {
            return null;
        }

        var newMaxByScope = changes.ToDictionary(c => c.Scope, c => c.NewMax);
        var characterMaxes = snapshot.Scopes
            .Where(s => s.Scope.Kind == ScopeKind.Character)
            .Select(s => newMaxByScope.TryGetValue(s.Scope, out var m) ? m : s.Max)
            .ToList();
        var highest = characterMaxes.Count == 0 ? 0 : characterMaxes.Max();

        if (random.Preferred <= highest)
        {
            return null;
        }

        // Max is reported as unchanged; the adapter never writes Random's max.
        return new Change(Scope.Character(random.CharacterId), random.Max, random.Max, random.Preferred, highest);
    }
}
