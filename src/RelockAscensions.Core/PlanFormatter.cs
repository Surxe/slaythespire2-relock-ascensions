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

    /// <summary>
    /// <c>Ironclad: max 10 -> 5 (selected level 10 -> 5)</c>. "Selected level" is the game's
    /// preferred ascension: the level character select starts on.
    /// </summary>
    /// A raise that reveals an epoch ends with <c>, reveals IRONCLAD4_EPOCH (ascension + potions)</c>.
    public static string FormatChange(Change c)
    {
        var selected = $"selected level {c.OldPreferred} -> {c.NewPreferred}";
        if (!c.ChangesMax)
        {
            return $"{c.Scope}: {selected}";
        }
        var max = $"{c.Scope}: max {c.OldMax} -> {c.NewMax}";
        var line = c.ChangesPreferred ? $"{max} ({selected})" : max;
        return c.Raises && c.RevealEpoch is { } epoch ? $"{line}, reveals {epoch} (ascension + potions)" : line;
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
