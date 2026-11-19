using System.Collections.Generic;

namespace RelockAscensions.Core;

/// <summary>
/// Which timeline epoch gates each character's ascension selector (spec 4.3, research
/// "Ascension epochs"). Mirrors the game's private <c>StartRunLobby.IsAscensionEpochRevealed</c>:
/// a hard-coded map to each vanilla character's "4" epoch. Any other character (modded) has no
/// gate.
/// </summary>
public static class AscensionEpochs
{
    /// <summary>Gates the Blitz mode buttons. The mod never reveals it.</summary>
    public const string Blitz = "BLITZ_EPOCH";

    private static readonly IReadOnlyDictionary<string, string> ByCharacter = new Dictionary<string, string>
    {
        ["CHARACTER.IRONCLAD"] = "IRONCLAD4_EPOCH",
        ["CHARACTER.SILENT"] = "SILENT4_EPOCH",
        ["CHARACTER.REGENT"] = "REGENT4_EPOCH",
        ["CHARACTER.DEFECT"] = "DEFECT4_EPOCH",
        ["CHARACTER.NECROBINDER"] = "NECROBINDER4_EPOCH",
        ["CHARACTER.FOOL"] = "FOOL4_EPOCH",
    };

    /// <summary>The epoch that gates <paramref name="characterId"/>'s ascension, or null when none does.</summary>
    public static string? For(string characterId) =>
        ByCharacter.TryGetValue(characterId, out var epoch) ? epoch : null;
}
