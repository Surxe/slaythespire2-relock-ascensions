using System.Text.Json;
using RelockAscensions.Core;

namespace RelockAscensions.Core.Tests;

/// <summary>
/// Runs only when <c>RELOCK_GOLDEN_PROFILE</c> points at a profile's <c>saves</c> directory
/// (containing <c>progress.save</c> and <c>history/</c>). Real saves are never committed
/// (spec section 8 / open decision 4); see README.md.
/// </summary>
public sealed class GoldenFactAttribute : FactAttribute
{
    public const string EnvVar = "RELOCK_GOLDEN_PROFILE";

    public GoldenFactAttribute()
    {
        var dir = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Skip = $"{EnvVar} is not set to a profile saves directory";
        }
    }
}

public class GoldenReplayTests
{
    private const string Random = "CHARACTER.RANDOM_CHARACTER";

    // Validated profile1 result (research, "History replay"). Scopes not listed must have no
    // evidence or evidence 0.
    private static readonly Dictionary<Scope, int> Golden = new()
    {
        [Scope.Character("CHARACTER.IRONCLAD")] = 10,
        [Scope.Character("CHARACTER.SILENT")] = 10,
        [Scope.Character("CHARACTER.REGENT")] = 10,
        [Scope.Character("CHARACTER.NECROBINDER")] = 10,
        [Scope.Character("CHARACTER.DEFECT")] = 9,
        [Scope.Multiplayer] = 10,
    };

    private static string Dir => Environment.GetEnvironmentVariable(GoldenFactAttribute.EnvVar)!;

    [GoldenFact]
    public void Replay_matches_saved_values()
    {
        var snapshot = ReadSnapshot(Path.Combine(Dir, "progress.save"));
        var runs = HistoryJson.ReadDirectory(Path.Combine(Dir, "history"));

        var result = Planner.Recompute(snapshot, runs, new Caps(10, 10, 5));

        // Recompute moves every scope with runs to its evidence, either way; scopes without runs
        // keep their saved max.
        var after = snapshot.Scopes.ToDictionary(s => s.Scope, s => s.Max);
        foreach (var c in result.Plan.Changes.Where(c => c.Scope.CharacterId != Random))
        {
            after[c.Scope] = c.NewMax;
        }
        foreach (var e in result.Evidence)
        {
            var expected = e.Evidence ?? snapshot.Scopes.Single(s => s.Scope == e.Scope).Max;
            Assert.Equal((e.Scope, expected), (e.Scope, after[e.Scope]));
        }
        AssertGolden(result.Evidence);
    }

    [GoldenFact]
    public void Recompute_on_unlocked_fork_reproduces_golden_table()
    {
        // Simulates acceptance 3: the fork after Unlock the Spire (every scope at its cap).
        var saved = ReadSnapshot(Path.Combine(Dir, "progress.save"));
        var unlocked = new ProgressSnapshot(
            saved.Scopes.Select(s => s with { Max = s.Cap, Preferred = s.Cap }).ToList(),
            new RandomState(Random, 10, 10));
        var runs = HistoryJson.ReadDirectory(Path.Combine(Dir, "history"));

        var result = Planner.Recompute(unlocked, runs, new Caps(10, 10, 5));

        var after = unlocked.Scopes.ToDictionary(s => s.Scope, s => s.Max);
        foreach (var c in result.Plan.Changes.Where(c => c.Scope.CharacterId != Random))
        {
            after[c.Scope] = c.NewMax;
        }
        foreach (var (scope, expected) in Golden)
        {
            Assert.Equal((scope, expected), (scope, after[scope]));
        }
        foreach (var e in result.Evidence.Where(e => !Golden.ContainsKey(e.Scope)))
        {
            if (e.Evidence is null)
            {
                // No eligible runs: left at the unlocked value, not reported as 0 (spec 4.4).
                Assert.Equal(unlocked.Scopes.Single(s => s.Scope == e.Scope).Cap, after[e.Scope]);
            }
            else
            {
                Assert.Equal((e.Scope, 0), (e.Scope, after[e.Scope]));
            }
        }
        AssertGolden(result.Evidence);
    }

    private static void AssertGolden(IEnumerable<ScopeEvidence> evidence)
    {
        var byScope = evidence.ToDictionary(e => e.Scope);
        foreach (var (scope, expected) in Golden)
        {
            Assert.Equal((scope, (int?)expected), (scope, byScope[scope].Evidence));
        }
        foreach (var e in byScope.Values.Where(e => !Golden.ContainsKey(e.Scope)))
        {
            Assert.True(e.Evidence is null or 0, $"{e.Scope}: expected no evidence or 0, got {e.Evidence}");
        }
    }

    private static ProgressSnapshot ReadSnapshot(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var scopes = new List<ScopeState>();
        RandomState? random = null;
        foreach (var c in root.GetProperty("character_stats").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            var max = c.GetProperty("max_ascension").GetInt32();
            var pref = c.GetProperty("preferred_ascension").GetInt32();
            if (id == Random)
            {
                random = new RandomState(id, max, pref);
            }
            else if (id != "CHARACTER.ROTATING_CHARACTER")
            {
                scopes.Add(new ScopeState(Scope.Character(id), max, pref, 10));
            }
        }
        scopes.Add(new ScopeState(Scope.Multiplayer,
            root.GetProperty("max_multiplayer_ascension").GetInt32(),
            root.GetProperty("preferred_multiplayer_ascension").GetInt32(), 10));
        if (root.TryGetProperty("max_blitz_ascension", out var bm))
        {
            scopes.Add(new ScopeState(Scope.Blitz, bm.GetInt32(),
                root.GetProperty("preferred_blitz_ascension").GetInt32(), 5));
        }
        return new ProgressSnapshot(scopes, random);
    }
}

/// <summary>
/// Test-only reader for raw <c>.run</c> files. In the game, the adapter goes through the game's
/// own history loader (with migrations) instead.
/// </summary>
internal static class HistoryJson
{
    public static List<RunRecord> ReadDirectory(string dir) =>
        Directory.EnumerateFiles(dir, "*.run") // never *.run.backup
            .Select(f => Read(File.ReadAllText(f)))
            .ToList();

    public static RunRecord Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var mode = r.TryGetProperty("game_mode", out var gm)
            ? gm.GetString() switch
            {
                "standard" => RunMode.Standard,
                "daily" => RunMode.Daily,
                "dread" => RunMode.Dread,
                "blitz" => RunMode.Blitz,
                _ => RunMode.Unknown,
            }
            : RunMode.Standard; // the game's default for files without the field
        return new RunRecord(
            r.GetProperty("start_time").GetInt64(),
            mode,
            r.TryGetProperty("is_custom_game", out var custom) && custom.GetBoolean(),
            r.GetProperty("win").GetBoolean(),
            r.GetProperty("ascension").GetInt32(),
            r.GetProperty("players").EnumerateArray()
                .Select(p => p.GetProperty("character").GetString()!)
                .ToList());
    }
}
