using RelockAscensions.Core;

namespace RelockAscensions;

/// <summary>What the mod would act on, for display before any preview.</summary>
/// <param name="ProfileId">Active profile slot, or null when no profile is loaded.</param>
/// <param name="Label">e.g. <c>profile1 (shared vanilla saves)</c>.</param>
/// <param name="Path">The game's own path for the profile; display only.</param>
public sealed record TargetInfo(int? ProfileId, string Label, string? Path);

public sealed record Preview(
    TargetInfo Target,
    Plan Plan,
    IReadOnlyList<ScopeEvidence>? Evidence,
    int HistoryFilesFailed,
    string? Error)
{
    public bool Ok => Error is null;

    /// <summary>Plain-text preview: target, evidence (recompute only), changes, rejections.</summary>
    public string Format()
    {
        var lines = new List<string> { $"Target: {Target.Label}" };
        if (Error is not null)
        {
            lines.Add($"Error: {Error}");
            return string.Join("\n", lines);
        }
        if (Evidence is not null)
        {
            lines.AddRange(PlanFormatter.FormatEvidence(Evidence));
            if (HistoryFilesFailed > 0)
            {
                lines.Add($"Warning: {HistoryFilesFailed} history file(s) could not be loaded");
            }
            lines.Add("");
        }
        lines.Add(PlanFormatter.Format(Plan));
        return string.Join("\n", lines);
    }
}

/// <summary>Everything a relock editor needs: one row per scope, starting at what is unlocked.</summary>
public sealed record EditorState(
    TargetInfo Target,
    IReadOnlyList<EditorRow> Rows,
    int HistoryFilesFailed,
    string? Error)
{
    public bool Ok => Error is null;
}

public enum ApplyStatus
{
    Applied,
    NothingToDo,
    RunInProgress,
    Stale,
    Incompatible,
    Failed,
}

public sealed record ApplyResult(ApplyStatus Status, string Message);

/// <summary>
/// The seam for front-ends (spec 7). A front-end builds requests or asks for a recompute, shows
/// the returned <see cref="Preview"/>, and calls <see cref="Apply"/> only after the player
/// confirms. Nothing here runs at load, and only <see cref="Apply"/> writes.
/// </summary>
public static class RelockApi
{
    public static TargetInfo DescribeTarget() => Guard(GameAdapter.DescribeTarget, new TargetInfo(null, "unavailable", null));

    /// <summary>Current scopes, for building a UI that changes ascension maxes.</summary>
    public static ProgressSnapshot? ReadState() => Compat.CanWrite ? Guard(GameAdapter.ReadSnapshot, null) : null;

    /// <summary>Every playable character the game knows, including ones not yet in the save.</summary>
    public static IReadOnlyList<string> KnownCharacterIds() => Guard(GameAdapter.KnownCharacterIds, Array.Empty<string>());

    /// <summary>
    /// Player-facing name of a character scope, from the game's localization. Null for
    /// non-character scopes and unknown ids; the front-end picks its own label then.
    /// </summary>
    public static string? CharacterTitle(Scope scope) =>
        scope.Kind == ScopeKind.Character && scope.CharacterId is { } id
            ? Guard(() => GameAdapter.CharacterTitle(id), null)
            : null;

    /// <summary>
    /// Rows for a relock editor: every scope with what is unlocked, what the run history earned,
    /// and how high it may go. Reads the save and history; writes nothing. Changes made in the
    /// editor, lower or higher, go back through <see cref="PreviewManual"/>.
    /// </summary>
    public static EditorState ReadEditor()
    {
        var target = DescribeTarget();
        if (Unavailable() is { } why)
        {
            return new EditorState(target, Array.Empty<EditorRow>(), 0, why);
        }
        try
        {
            var snapshot = GameAdapter.ReadSnapshot();
            var (runs, failed) = GameAdapter.ReadHistory();
            var evidence = Planner.Recompute(snapshot, runs, GameAdapter.ReadCaps()).Evidence;
            return new EditorState(target, RelockEditor.Rows(snapshot, evidence), failed, null);
        }
        catch (Exception e)
        {
            Log.Error($"Reading the editor state failed: {e}");
            return new EditorState(target, Array.Empty<EditorRow>(), 0, e.Message);
        }
    }

    /// <summary>Manual preview: change scopes to the requested maxes, lower or higher. Writes nothing.</summary>
    public static Preview PreviewManual(IEnumerable<RelockRequest> requests)
    {
        var target = DescribeTarget();
        if (Unavailable() is { } why)
        {
            return new Preview(target, Plan.Empty, null, 0, why);
        }
        try
        {
            var plan = Planner.Plan(GameAdapter.ReadSnapshot(), requests);
            return new Preview(target, plan, null, 0, null);
        }
        catch (Exception e)
        {
            Log.Error($"Manual preview failed: {e}");
            return new Preview(target, Plan.Empty, null, 0, e.Message);
        }
    }

    /// <summary>Recompute-from-history preview for every scope. Writes nothing.</summary>
    public static Preview PreviewRecompute()
    {
        var target = DescribeTarget();
        if (Unavailable() is { } why)
        {
            return new Preview(target, Plan.Empty, null, 0, why);
        }
        try
        {
            var snapshot = GameAdapter.ReadSnapshot();
            var (runs, failed) = GameAdapter.ReadHistory();
            var result = Planner.Recompute(snapshot, runs, GameAdapter.ReadCaps());
            Log.Info($"Recompute over {runs.Count} run(s), {failed} unreadable: {result.Plan.Changes.Count} change(s)");
            return new Preview(target, result.Plan, result.Evidence, failed, null);
        }
        catch (Exception e)
        {
            Log.Error($"Recompute preview failed: {e}");
            return new Preview(target, Plan.Empty, null, 0, e.Message);
        }
    }

    /// <summary>
    /// Writes a previewed plan after the player confirmed it. Re-reads the save first and
    /// discards the plan if anything it would change has moved since the preview.
    /// </summary>
    public static ApplyResult Apply(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            return new ApplyResult(ApplyStatus.NothingToDo, "No changes.");
        }
        if (!Compat.CanWrite)
        {
            return new ApplyResult(ApplyStatus.Incompatible, IncompatibleMessage());
        }
        try
        {
            if (GameAdapter.IsRunInProgress())
            {
                return new ApplyResult(ApplyStatus.RunInProgress, "A run is in progress; relock from the main menu.");
            }

            var stale = Planner.StaleChanges(GameAdapter.ReadSnapshot(), plan);
            if (stale.Count > 0)
            {
                return new ApplyResult(ApplyStatus.Stale,
                    "The save changed since the preview; nothing was written. Preview again. (" +
                    string.Join("; ", stale) + ")");
            }

            var target = GameAdapter.DescribeTarget();
            GameAdapter.Apply(plan);
            var summary = $"Wrote {target.Label}:\n{PlanFormatter.Format(plan with { Rejections = Array.Empty<Rejection>() })}";
            Log.Info(summary);
            return new ApplyResult(ApplyStatus.Applied, summary);
        }
        catch (Exception e)
        {
            Log.Error($"Apply failed: {e}");
            return new ApplyResult(ApplyStatus.Failed, $"Nothing was saved: {e.Message}");
        }
    }

    private static string? Unavailable()
    {
        if (!Compat.CanWrite)
        {
            return IncompatibleMessage();
        }
        return GameAdapter.DescribeTarget().ProfileId is null ? "No profile is loaded." : null;
    }

    private static string IncompatibleMessage() =>
        $"Game build {Compat.GameVersion ?? "unknown"} is missing: {string.Join(", ", Compat.Missing)}";

    private static T Guard<T>(Func<T> f, T fallback)
    {
        try
        {
            return f();
        }
        catch (Exception e)
        {
            Log.Error(e.ToString());
            return fallback;
        }
    }
}
