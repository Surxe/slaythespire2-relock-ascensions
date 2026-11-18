using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using RelockAscensions.Core;

namespace RelockAscensions;

/// <summary>
/// The only layer that touches the game (spec 5). Thin mapping between the game's save objects
/// and the core model, plus saving. Never builds file paths to decide where to write; whatever
/// the game has loaded is what gets read and written.
/// </summary>
internal static class GameAdapter
{
    /// <summary>Current relockable state from <c>SaveManager.Instance.Progress</c>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ProgressSnapshot ReadSnapshot()
    {
        var progress = SaveManager.Instance.Progress;
        var cap = Compat.StandardCap;
        var randomId = ModelDb.GetId<RandomCharacter>().ToString();
        var rotatingId = ModelDb.GetId<RotatingCharacter>().ToString();

        var scopes = new List<ScopeState>();
        RandomState? random = null;

        // Enumerated from the save, so modded characters are included. Ordered like the game lists
        // characters (ModelDb.AllCharacters, the character select order); others follow by id.
        var gameOrder = ModelDb.AllCharacters.Select(c => c.Id.ToString()).ToList();
        int Rank(string? id) => gameOrder.IndexOf(id ?? "") is var i and >= 0 ? i : int.MaxValue;
        foreach (var stats in progress.CharacterStats.Values
                     .OrderBy(s => Rank(s.Id?.ToString()))
                     .ThenBy(s => s.Id?.ToString(), StringComparer.Ordinal))
        {
            var id = stats.Id?.ToString();
            if (id is null || id == rotatingId)
            {
                continue;
            }
            if (id == randomId)
            {
                random = new RandomState(id, stats.MaxAscension, stats.PreferredAscension);
                continue;
            }
            scopes.Add(new ScopeState(Scope.Character(id), stats.MaxAscension, stats.PreferredAscension, cap));
        }

        scopes.Add(new ScopeState(Scope.Multiplayer,
            progress.MaxMultiplayerAscension, progress.PreferredMultiplayerAscension, cap));

        if (Compat.HasBlitz)
        {
            scopes.Add(new ScopeState(Scope.Blitz,
                (int)Compat.MaxBlitzGetter!.Invoke(progress, null)!,
                (int)Compat.PreferredBlitzGetter!.Invoke(progress, null)!,
                Compat.BlitzCap!.Value));
        }

        return new ProgressSnapshot(scopes, random);
    }

    /// <summary>Every playable character the game knows (pseudo-characters excluded).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IReadOnlyList<string> KnownCharacterIds()
    {
        var randomId = ModelDb.GetId<RandomCharacter>().ToString();
        var rotatingId = ModelDb.GetId<RotatingCharacter>().ToString();
        return ModelDb.AllCharacters
            .Select(c => c.Id.ToString())
            .Where(id => id != randomId && id != rotatingId)
            .ToList();
    }

    /// <summary>
    /// The game's localized title for a character id (Random included), or null when the id is
    /// not a known model, e.g. a character from a mod that is not loaded.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string? CharacterTitle(string id)
    {
        var model = ModelDb.AllCharacters
            .Append(ModelDb.Character<RandomCharacter>())
            .FirstOrDefault(c => c.Id.ToString() == id);
        return model?.Title.GetFormattedText();
    }

    public static Caps ReadCaps() =>
        new(Compat.StandardCap, Compat.StandardCap, Compat.HasBlitz ? Compat.BlitzCap : null);

    /// <summary>
    /// Every run in the active profile's history, through the game's own loader (migrations
    /// included). Files that fail to load are counted, not fatal.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (List<RunRecord> Runs, int Failed) ReadHistory()
    {
        var save = SaveManager.Instance;
        var runs = new List<RunRecord>();
        var failed = 0;
        foreach (var name in save.GetAllRunHistoryNames())
        {
            if (name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
            {
                continue; // crash-recovery copies are never read
            }
            var result = save.LoadRunHistory(name);
            if (!result.Success || result.SaveData is null)
            {
                failed++;
                Log.Warn($"Skipping run history '{name}': {result.Status} {result.ErrorMessage}");
                continue;
            }
            runs.Add(ToRecord(result.SaveData));
        }
        return (runs, failed);
    }

    private static RunRecord ToRecord(RunHistory h) => new(
        h.StartTime,
        MapMode(h.GameMode),
        h.IsCustomGame,
        h.Win,
        h.Ascension,
        h.Players.Select(p => p.Character.ToString()).ToList());

    // Mapped by name so the mod does not bake in enum values that a build may lack.
    private static RunMode MapMode(GameMode mode) => mode.ToString() switch
    {
        "Standard" => RunMode.Standard,
        "Daily" => RunMode.Daily,
        "Dread" => RunMode.Dread,
        "Blitz" => RunMode.Blitz,
        _ => RunMode.Unknown,
    };

    public static bool IsRunInProgress() => RunManager.Instance.IsInProgress;

    /// <summary>
    /// Applies the plan to the loaded progress and saves it (spec 5.2). Callers must have
    /// checked staleness first. On failure, in-memory values are restored and nothing is saved.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Apply(Plan plan)
    {
        var save = SaveManager.Instance;
        var progress = save.Progress;
        var undo = new List<Action>();
        try
        {
            foreach (var change in plan.Changes)
            {
                ApplyChange(progress, change, undo);
            }
            save.SaveProgressFile();
        }
        catch
        {
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                undo[i]();
            }
            throw;
        }
    }

    private static void ApplyChange(ProgressState progress, Change change, List<Action> undo)
    {
        switch (change.Scope.Kind)
        {
            case ScopeKind.Character:
                var stats = FindCharacter(progress, change.Scope.CharacterId!)
                    ?? throw new InvalidOperationException($"{change.Scope} is not in the loaded save");
                int oldMax = stats.MaxAscension, oldPref = stats.PreferredAscension;
                undo.Add(() => { stats.MaxAscension = oldMax; stats.PreferredAscension = oldPref; });
                if (change.ChangesMax)
                {
                    stats.MaxAscension = change.NewMax; // never true for Random
                }
                stats.PreferredAscension = change.NewPreferred;
                break;

            case ScopeKind.Multiplayer:
                int mpMax = progress.MaxMultiplayerAscension, mpPref = progress.PreferredMultiplayerAscension;
                undo.Add(() => { progress.MaxMultiplayerAscension = mpMax; progress.PreferredMultiplayerAscension = mpPref; });
                progress.MaxMultiplayerAscension = change.NewMax;
                progress.PreferredMultiplayerAscension = change.NewPreferred;
                break;

            case ScopeKind.Blitz:
                if (!Compat.HasBlitz)
                {
                    throw new InvalidOperationException("This build has no Blitz ascension");
                }
                var bMax = Compat.MaxBlitzGetter!.Invoke(progress, null);
                var bPref = Compat.PreferredBlitzGetter!.Invoke(progress, null);
                undo.Add(() =>
                {
                    Compat.MaxBlitzSetter!.Invoke(progress, new[] { bMax });
                    Compat.PreferredBlitzSetter!.Invoke(progress, new[] { bPref });
                });
                Compat.MaxBlitzSetter!.Invoke(progress, new object[] { change.NewMax });
                Compat.PreferredBlitzSetter!.Invoke(progress, new object[] { change.NewPreferred });
                break;

            default:
                throw new InvalidOperationException($"Unknown scope kind {change.Scope.Kind}");
        }
    }

    private static CharacterStats? FindCharacter(ProgressState progress, string id) =>
        progress.CharacterStats.Values.FirstOrDefault(s => s.Id?.ToString() == id);

    /// <summary>
    /// Display-only description of what the game has loaded (spec 6, "Shows its target").
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TargetInfo DescribeTarget()
    {
        var save = SaveManager.Instance;
        if (!save.IsProfileInitialized)
        {
            return new TargetInfo(null, "no profile loaded", null);
        }
        var id = save.CurrentProfileId;
        string label, path;
        try
        {
            label = UserDataPathProvider.IsRunningModded ? "modded saves" : "shared vanilla saves";
            path = UserDataPathProvider.GetProfileScopedBasePath(id);
        }
        catch (Exception e)
        {
            label = "save tree unknown";
            path = $"<{e.GetType().Name}>";
        }
        return new TargetInfo(id, $"profile{id} ({label})", path);
    }
}
