namespace RelockAscensions;

/// <summary>
/// The mod's player settings. They live here, not in a config library, so the mod runs the same
/// with or without BaseLib: without it every setting keeps its default. With BaseLib,
/// <c>RelockAscensions.BaseLibConfig</c> forwards its config properties to these, so BaseLib's
/// Mod Configuration screen loads, edits and saves them (see <see cref="BaseLibBridge"/>).
/// Public so that assembly can reach them.
/// </summary>
public static class Settings
{
    private static bool _hideMainMenuButton;

    /// <summary>Raised on the main thread after any setting changes value.</summary>
    public static event Action? Changed;

    /// <summary>Hides the Relock Ascensions main-menu button. The console command still works.</summary>
    public static bool HideMainMenuButton
    {
        get => _hideMainMenuButton;
        set
        {
            if (_hideMainMenuButton == value)
            {
                return;
            }
            _hideMainMenuButton = value;
            Changed?.Invoke();
        }
    }
}
