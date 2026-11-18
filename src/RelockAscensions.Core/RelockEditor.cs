using System;
using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>
/// One editable scope in a front-end's relock editor.
/// </summary>
/// <param name="Unlocked">The scope's max in the loaded save.</param>
/// <param name="Earned">The level the run history proves (spec 4.4), or null with no eligible runs.</param>
/// <param name="Runs">Eligible runs behind <paramref name="Earned"/>.</param>
/// <param name="Max">Highest value the editor offers. Equal to <paramref name="Unlocked"/>: the
/// planner never raises (spec 4.2).</param>
public sealed record EditorRow(Scope Scope, int Unlocked, int? Earned, int Runs, int Max)
{
    /// <summary>
    /// Where the editor starts: what the runs earned, never above what is unlocked; the unlocked
    /// value when there are no runs (spec 4.4, "no evidence: no change").
    /// </summary>
    public int Suggested => Earned is { } earned ? Math.Min(earned, Unlocked) : Unlocked;
}

/// <summary>
/// Pure model behind the relock editor: rows start at the recompute suggestion, the player moves
/// any of them, and the edited values become ordinary manual-relock requests.
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
                return new EditorRow(s.Scope, s.Max, e?.Evidence, e?.Runs ?? 0, s.Max);
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
