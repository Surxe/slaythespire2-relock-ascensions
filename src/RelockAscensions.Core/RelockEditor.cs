using System;
using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>
/// One editable scope in a front-end's relock editor.
/// </summary>
/// <param name="Unlocked">The scope's max in the loaded save. The editor starts here.</param>
/// <param name="Earned">The level the run history proves (spec 4.4), or null with no eligible runs.</param>
/// <param name="Runs">Eligible runs behind <paramref name="Earned"/>.</param>
/// <param name="Max">Highest value the editor offers: the scope's ceiling (spec 4.2), which is
/// the cap unless raising is locked.</param>
/// <param name="RevealsEpoch">Raising this scope also reveals its ascension epoch (and with it the
/// character's potions).</param>
/// <param name="RaiseLocked">The scope cannot be raised until the player unlocks it in the game
/// (Blitz).</param>
public sealed record EditorRow(
    Scope Scope,
    int Unlocked,
    int? Earned,
    int Runs,
    int Max,
    bool RevealsEpoch = false,
    bool RaiseLocked = false)
{
    /// <summary>
    /// What the run history earned, within what the editor offers; the unlocked value when there
    /// are no runs (spec 4.4, "no evidence: no change").
    /// </summary>
    public int FromHistory => Earned is { } earned ? Math.Min(earned, Max) : Unlocked;
}

/// <summary>
/// Pure model behind the relock editor: rows start at what is unlocked, the player moves any of
/// them (or takes the run-history values), and the edited values become ordinary manual-relock
/// requests.
/// </summary>
public static class RelockEditor
{
    public static IReadOnlyList<EditorRow> Rows(ProgressSnapshot snapshot, IEnumerable<ScopeEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evidence);
        var byScope = evidence.ToDictionary(e => e.Scope);
        return snapshot.Scopes
            .Select(s =>
            {
                byScope.TryGetValue(s.Scope, out var e);
                return new EditorRow(s.Scope, s.Max, e?.Evidence, e?.Runs ?? 0, s.Ceiling,
                    s.HiddenEpoch is not null, s.RaiseLocked);
            })
            .ToList();
    }

    /// <summary>Requests for every row whose chosen value differs from what is unlocked.</summary>
    public static IReadOnlyList<RelockRequest> Requests(IEnumerable<(EditorRow Row, int Value)> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return edits
            .Where(e => e.Value != e.Row.Unlocked)
            .Select(e => new RelockRequest(e.Row.Scope, e.Value))
            .ToList();
    }
}
