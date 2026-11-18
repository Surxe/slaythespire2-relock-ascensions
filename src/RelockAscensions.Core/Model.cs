using System.Collections.Generic;

namespace RelockAscensions.Core;

/// <summary>One independently capped ascension track (spec section 3).</summary>
public enum ScopeKind
{
    Character,
    Multiplayer,
    Blitz,
}

/// <summary>
/// Identity of a scope. <see cref="CharacterId"/> is set only for <see cref="ScopeKind.Character"/>
/// and holds the game's model id string, e.g. <c>CHARACTER.IRONCLAD</c>.
/// </summary>
public sealed record Scope(ScopeKind Kind, string? CharacterId)
{
    public static Scope Character(string characterId) => new(ScopeKind.Character, characterId);

    public static readonly Scope Multiplayer = new(ScopeKind.Multiplayer, null);

    public static readonly Scope Blitz = new(ScopeKind.Blitz, null);

    public override string ToString() => Kind switch
    {
        ScopeKind.Character => CharacterId ?? "<character>",
        ScopeKind.Multiplayer => "Multiplayer",
        ScopeKind.Blitz => "Blitz",
        _ => Kind.ToString(),
    };
}

/// <summary>Current values of one scope, as read from the loaded save.</summary>
public sealed record ScopeState(Scope Scope, int Max, int Preferred, int Cap);

/// <summary>
/// State of the Random character pseudo-entry. Its stored max is not a real track and is never
/// written; only its preferred is clamped (spec 4.5). <see cref="Max"/> is carried for display.
/// </summary>
public sealed record RandomState(string CharacterId, int Max, int Preferred);

/// <summary>
/// Everything the core needs from the loaded save. Built by the adapter.
/// </summary>
/// <param name="Scopes">Relockable scopes. Pseudo-characters (Random, Rotating) must not be listed.</param>
/// <param name="Random">The Random character's preferred value, when the save has an entry for it.</param>
public sealed record ProgressSnapshot(IReadOnlyList<ScopeState> Scopes, RandomState? Random);

/// <summary>Game mode of a history run, mapped by the adapter.</summary>
public enum RunMode
{
    Standard,
    Daily,
    Dread,
    Blitz,
    Unknown,
}

/// <summary>Plain copy of the history file fields the replay uses.</summary>
public sealed record RunRecord(
    long StartTime,
    RunMode Mode,
    bool IsCustom,
    bool Win,
    int Ascension,
    IReadOnlyList<string> Characters);

/// <summary>Caps used by the replay. <see cref="Blitz"/> is null on builds without Blitz.</summary>
public sealed record Caps(int Character, int Multiplayer, int? Blitz)
{
    public int? For(Scope scope) => scope.Kind switch
    {
        ScopeKind.Character => Character,
        ScopeKind.Multiplayer => Multiplayer,
        ScopeKind.Blitz => Blitz,
        _ => null,
    };
}

/// <summary>A front-end's request to lower one scope's max.</summary>
public sealed record RelockRequest(Scope Scope, int NewMax);

public enum RejectionReason
{
    /// <summary>The scope is not in the loaded save (or is a pseudo-character).</summary>
    UnknownScope,

    /// <summary>The new max is below 0.</summary>
    Negative,

    /// <summary>The new max is above the current max. Raising is out of scope.</summary>
    AboveCurrentMax,

    /// <summary>The same scope was requested more than once; only the first request is used.</summary>
    Duplicate,
}

public sealed record Rejection(RelockRequest Request, RejectionReason Reason, string Message);

/// <summary>
/// One scope's change. For the Random pseudo-entry, <see cref="OldMax"/> equals <see cref="NewMax"/>
/// (its max is never touched) and only preferred moves.
/// </summary>
public sealed record Change(Scope Scope, int OldMax, int NewMax, int OldPreferred, int NewPreferred)
{
    public bool ChangesMax => OldMax != NewMax;

    public bool ChangesPreferred => OldPreferred != NewPreferred;
}

public sealed record Plan(IReadOnlyList<Change> Changes, IReadOnlyList<Rejection> Rejections)
{
    public static readonly Plan Empty = new(new List<Change>(), new List<Rejection>());

    public bool IsEmpty => Changes.Count == 0;
}

/// <summary>Outcome of replaying history for one scope.</summary>
/// <param name="Evidence">Proven lower bound, or null when the scope has no eligible runs.</param>
/// <param name="Runs">Number of eligible runs counted for this scope.</param>
public sealed record ScopeEvidence(Scope Scope, int? Evidence, int Runs);

public sealed record RecomputeResult(Plan Plan, IReadOnlyList<ScopeEvidence> Evidence);
