using System;
using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>One row of a UI preview: a scope, its current max, and what would change.</summary>
/// <param name="Change">The planned change, or null when the scope stays as it is.</param>
/// <param name="Runs">Eligible runs behind the scope (0 when it has none), or null for a manual
/// relock and for the Random pseudo-entry, which has no history of its own.</param>
public sealed record PreviewRow(Scope Scope, int CurrentMax, Change? Change, int? Runs)
{
    public bool HasRuns => Runs > 0;
}

/// <summary>
/// Structured preview for graphical front-ends, which lay rows out themselves instead of
/// printing <see cref="PlanFormatter"/> text. Same content as the text preview (spec 6 and
/// decision 2). A recompute lists every scope, changed or not, with the runs behind it, so the
/// player sees the whole picture even when nothing changes. A manual relock lists its changes.
/// </summary>
public sealed record PreviewSummary(IReadOnlyList<PreviewRow> Rows, IReadOnlyList<Rejection> Rejections)
{
    public bool HasChanges => Rows.Any(r => r.Change is not null);

    /// <summary>Scopes left unchanged because they have no eligible runs (recompute only).</summary>
    public int NoRunsCount => Rows.Count(r => r.Runs == 0);

    public static PreviewSummary Build(Plan plan, IReadOnlyList<ScopeEvidence>? evidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var changes = plan.Changes.ToDictionary(c => c.Scope);
        var rows = new List<PreviewRow>();
        foreach (var e in evidence ?? Array.Empty<ScopeEvidence>())
        {
            changes.Remove(e.Scope, out var change);
            rows.Add(new PreviewRow(e.Scope, e.CurrentMax, change, e.Runs));
        }
        // Changes without evidence: every manual change, and the Random preferred clamp.
        rows.AddRange(plan.Changes
            .Where(c => changes.ContainsKey(c.Scope))
            .Select(c => new PreviewRow(c.Scope, c.OldMax, c, null)));
        return new PreviewSummary(rows, plan.Rejections);
    }
}
