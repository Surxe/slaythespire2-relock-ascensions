using System.Collections.Generic;
using System.Linq;

namespace RelockAscensions.Core;

/// <summary>
/// Plain-text rendering of plans and evidence, so every front-end shows the same preview
/// (spec 6: <c>scope: old -> new</c> for every change).
/// </summary>
public static class PlanFormatter
{
    public static IReadOnlyList<string> FormatChanges(Plan plan)
    {
        if (plan.IsEmpty)
        {
            return new[] { "No changes." };
        }
        return plan.Changes.Select(FormatChange).ToList();
    }

    public static string FormatChange(Change c)
    {
        var parts = new List<string>();
        if (c.ChangesMax)
        {
            parts.Add($"max {c.OldMax} -> {c.NewMax}");
        }
        if (c.ChangesPreferred)
        {
            parts.Add($"preferred {c.OldPreferred} -> {c.NewPreferred}");
        }
        return $"{c.Scope}: {string.Join(", ", parts)}";
    }

    public static IReadOnlyList<string> FormatRejections(Plan plan) =>
        plan.Rejections.Select(r => $"rejected {r.Message}").ToList();

    public static IReadOnlyList<string> FormatEvidence(IEnumerable<ScopeEvidence> evidence) =>
        evidence
            .Select(e => e.Evidence is { } v
                ? $"{e.Scope}: evidence {v} ({e.Runs} run{(e.Runs == 1 ? "" : "s")})"
                : $"{e.Scope}: no evidence, left unchanged")
            .ToList();

    /// <summary>Full preview: changes, then rejections.</summary>
    public static string Format(Plan plan) =>
        string.Join("\n", FormatChanges(plan).Concat(FormatRejections(plan)));
}
