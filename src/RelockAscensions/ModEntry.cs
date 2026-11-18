using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace RelockAscensions;

/// <summary>
/// Loader entry point. Logs compatibility and hooks the main-menu button (a scene-tree signal, not
/// a patch). The mod never scans, replays or writes at load (spec 6), and it applies no Harmony
/// patches (spec 5.3). Declaring an initializer also stops
/// the loader from calling <c>Harmony.PatchAll</c> on this assembly.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "RelockAscensions";

    public static void Initialize()
    {
        try
        {
            var version = Compat.GameVersion ?? "unknown";
            if (!Compat.IsTargetVersion)
            {
                Log.Warn($"Built for {Compat.TargetVersion}, running on {version}; checking members");
            }
            if (Compat.CanWrite)
            {
                Log.Info($"Loaded on {version}. Blitz: {(Compat.HasBlitz ? "yes" : "no")}. Nothing runs until a relock is requested.");
            }
            else
            {
                Log.Error($"Writes disabled; missing: {string.Join(", ", Compat.Missing)}");
            }
            // Only connects a scene-tree signal; the button reads nothing until pressed.
            Ui.MainMenuHook.Install();
        }
        catch (Exception e)
        {
            Log.Error($"Initialize failed (mod stays inert): {e}");
        }
    }
}

internal static class Log
{
    private static readonly Logger Logger = new(ModEntry.ModId, LogType.Generic);

    public static void Info(string text) => Logger.Info(text, 2);

    public static void Warn(string text) => Logger.Warn(text, 2);

    public static void Error(string text) => Logger.Error(text, 2);
}
