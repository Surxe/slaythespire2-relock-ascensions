using RelockAscensions.Core;

namespace RelockAscensions.Core.Tests;

internal static class Fx
{
    public const string Ironclad = "CHARACTER.IRONCLAD";
    public const string Silent = "CHARACTER.SILENT";
    public const string Defect = "CHARACTER.DEFECT";
    public const string RandomId = "CHARACTER.RANDOM_CHARACTER";

    public static readonly Caps AllCaps = new(10, 10, 5);
    public static readonly Caps CapsNoBlitz = new(10, 10, null);

    public static ScopeState Char(string id, int max, int preferred) =>
        new(Scope.Character(id), max, preferred, 10);

    public static ScopeState Mp(int max, int preferred) => new(Scope.Multiplayer, max, preferred, 10);

    public static ScopeState Blitz(int max, int preferred) => new(Scope.Blitz, max, preferred, 5);

    public static ProgressSnapshot Snapshot(params ScopeState[] scopes) => new(scopes, null);

    public static ProgressSnapshot Snapshot(RandomState random, params ScopeState[] scopes) => new(scopes, random);

    private static long _t;

    /// <summary>Single-player Standard run; start times increase in call order.</summary>
    public static RunRecord Sp(string character, int asc, bool win = false) =>
        new(++_t, RunMode.Standard, false, win, asc, new[] { character });

    public static RunRecord MpRun(int asc, bool win = false, params string[] characters) =>
        new(++_t, RunMode.Standard, false, win, asc, characters.Length == 0 ? new[] { Ironclad, Silent } : characters);

    public static RunRecord Run(RunMode mode, int asc, bool win = false, bool custom = false, params string[] characters) =>
        new(++_t, mode, custom, win, asc, characters.Length == 0 ? new[] { Ironclad } : characters);
}
