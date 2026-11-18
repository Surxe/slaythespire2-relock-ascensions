namespace RelockAscensions.Core;

/// <summary>
/// Resolves user-typed scope names against the loaded scopes, for text front-ends.
/// Accepts <c>multiplayer</c>/<c>mp</c>, <c>blitz</c>, a full character id
/// (<c>CHARACTER.IRONCLAD</c>) or its entry alone (<c>ironclad</c>), case-insensitively.
/// </summary>
public static class ScopeNames
{
    public static Scope? Resolve(string input, IEnumerable<ScopeState> scopes)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input.Trim();
        var available = scopes.Select(s => s.Scope).ToList();

        Scope? fixedScope = name.ToLowerInvariant() switch
        {
            "multiplayer" or "mp" => Scope.Multiplayer,
            "blitz" => Scope.Blitz,
            _ => null,
        };
        if (fixedScope is not null)
        {
            return available.Contains(fixedScope) ? fixedScope : null;
        }

        var id = MatchCharacterId(name, available.Where(s => s.Kind == ScopeKind.Character).Select(s => s.CharacterId!));
        return id is null ? null : Scope.Character(id);
    }

    /// <summary>
    /// Matches a typed name against character ids by full id or entry alone, case-insensitively.
    /// Used both for scopes in the save and for every character the game knows, so a character
    /// with no entry in the save yet (never played on this profile) can be told apart from a typo.
    /// </summary>
    public static string? MatchCharacterId(string input, IEnumerable<string> characterIds)
    {
        var name = input.Trim();
        var ids = characterIds.ToList();
        return ids.FirstOrDefault(id => string.Equals(id, name, StringComparison.OrdinalIgnoreCase))
            ?? ids.FirstOrDefault(id => string.Equals(Entry(id), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Short names for completion: <c>ironclad</c>, ..., <c>multiplayer</c>, <c>blitz</c>.</summary>
    public static IReadOnlyList<string> ShortNames(IEnumerable<ScopeState> scopes) =>
        scopes.Select(s => s.Scope.Kind switch
        {
            ScopeKind.Character => Entry(s.Scope.CharacterId!).ToLowerInvariant(),
            ScopeKind.Multiplayer => "multiplayer",
            _ => "blitz",
        }).ToList();

    private static string Entry(string characterId)
    {
        var dot = characterId.IndexOf('.');
        return dot < 0 ? characterId : characterId[(dot + 1)..];
    }
}
