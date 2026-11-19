using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace RelockAscensions;

/// <summary>
/// Build compatibility checks (spec 5.3). Every member the adapter touches is resolved by
/// reflection first, so a missing member is reported instead of throwing at JIT time, and the
/// adapter refuses to write.
/// </summary>
internal static class Compat
{
    public const string TargetVersion = "v0.113.1";

    private static readonly (Type Type, string Member)[] Required =
    {
        (typeof(SaveManager), nameof(SaveManager.Instance)),
        (typeof(SaveManager), nameof(SaveManager.Progress)),
        (typeof(SaveManager), nameof(SaveManager.CurrentProfileId)),
        (typeof(SaveManager), nameof(SaveManager.IsProfileInitialized)),
        (typeof(SaveManager), nameof(SaveManager.GetAllRunHistoryNames)),
        (typeof(SaveManager), nameof(SaveManager.LoadRunHistory)),
        (typeof(SaveManager), nameof(SaveManager.SaveProgressFile)),
        (typeof(SaveManager), nameof(SaveManager.IsEpochRevealed)),
        (typeof(SaveManager), nameof(SaveManager.ObtainEpochOverride)),
        (typeof(ProgressState), nameof(ProgressState.Epochs)),
        (typeof(ProgressState), EpochsFieldName),
        (typeof(SerializableEpoch), nameof(SerializableEpoch.Id)),
        (typeof(SerializableEpoch), nameof(SerializableEpoch.State)),
        (typeof(SerializableEpoch), nameof(SerializableEpoch.ObtainDate)),
        (typeof(ProgressState), nameof(ProgressState.CharacterStats)),
        (typeof(ProgressState), nameof(ProgressState.MaxMultiplayerAscension)),
        (typeof(ProgressState), nameof(ProgressState.PreferredMultiplayerAscension)),
        (typeof(CharacterStats), nameof(CharacterStats.Id)),
        (typeof(CharacterStats), nameof(CharacterStats.MaxAscension)),
        (typeof(CharacterStats), nameof(CharacterStats.PreferredAscension)),
        (typeof(RunHistory), nameof(RunHistory.GameMode)),
        (typeof(RunHistory), nameof(RunHistory.IsCustomGame)),
        (typeof(RunHistory), nameof(RunHistory.Win)),
        (typeof(RunHistory), nameof(RunHistory.StartTime)),
        (typeof(RunHistory), nameof(RunHistory.Ascension)),
        (typeof(RunHistory), nameof(RunHistory.Players)),
        (typeof(RunHistoryPlayer), nameof(RunHistoryPlayer.Character)),
        (typeof(RunManager), nameof(RunManager.Instance)),
        (typeof(RunManager), nameof(RunManager.IsInProgress)),
        (typeof(AscensionManager), "maxAscensionAllowed"),
    };

    private const string EpochsFieldName = "_epochs";

    private static readonly Lazy<IReadOnlyList<string>> _missing = new(FindMissing);

    /// <summary>Required members absent from the loaded game build. Empty when compatible.</summary>
    public static IReadOnlyList<string> Missing => _missing.Value;

    public static bool CanWrite => Missing.Count == 0;

    // Blitz is beta-only; accessed through reflection so the mod loads on builds without it.
    public static readonly MethodInfo? MaxBlitzGetter = AccessTools.PropertyGetter(typeof(ProgressState), "MaxBlitzAscension");
    public static readonly MethodInfo? MaxBlitzSetter = AccessTools.PropertySetter(typeof(ProgressState), "MaxBlitzAscension");
    public static readonly MethodInfo? PreferredBlitzGetter = AccessTools.PropertyGetter(typeof(ProgressState), "PreferredBlitzAscension");
    public static readonly MethodInfo? PreferredBlitzSetter = AccessTools.PropertySetter(typeof(ProgressState), "PreferredBlitzAscension");

    /// <summary>
    /// The private epoch list behind <see cref="ProgressState.Epochs"/>. Undo removes an entry the
    /// mod created from it; the game has no public way to delete one.
    /// </summary>
    public static readonly FieldInfo? EpochsField = AccessTools.Field(typeof(ProgressState), EpochsFieldName);

    /// <summary>
    /// <c>EpochState.Revealed</c>, resolved by name so the mod does not bake in the enum value
    /// (spec 5.3). Read lazily: a build without it fails here, not at load.
    /// </summary>
    public static EpochState Revealed => (EpochState)Enum.Parse(typeof(EpochState), "Revealed");

    public static bool HasBlitz =>
        MaxBlitzGetter is not null && MaxBlitzSetter is not null &&
        PreferredBlitzGetter is not null && PreferredBlitzSetter is not null &&
        BlitzCap is not null;

    /// <summary>Character/multiplayer cap from the running game's constant (not the compile-time copy).</summary>
    public static int StandardCap => ReadConst("maxAscensionAllowed") ?? 10;

    public static int? BlitzCap => ReadConst("maxAscensionAllowedBlitz");

    public static string? GameVersion
    {
        get
        {
            try
            {
                return ReleaseInfoManager.Instance.ReleaseInfo?.Version;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public static bool IsTargetVersion => GameVersion == TargetVersion;

    private static int? ReadConst(string name)
    {
        // Constants are inlined at compile time, so read the loaded assembly's value.
        var field = typeof(AscensionManager).GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field?.GetRawConstantValue() is int value ? value : null;
    }

    private static IReadOnlyList<string> FindMissing()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return Required
            .Where(r => r.Type.GetMember(r.Member, all).Length == 0)
            .Select(r => $"{r.Type.Name}.{r.Member}")
            .ToList();
    }
}
