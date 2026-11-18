using BaseLib.Config;

namespace RelockAscensions.BaseLibConfig;

/// <summary>
/// The mod's page on BaseLib's Mod Configuration screen. Each property forwards to
/// <see cref="Settings"/>, which owns the value, so the main mod never touches BaseLib. BaseLib
/// derives the file name (<c>mod_configs/RelockAscensions.cfg</c>) and the label prefix
/// (<c>RELOCKASCENSIONS-</c>, keys in the PCK's <c>settings_ui.json</c>) from the root namespace,
/// so this class must stay under <c>RelockAscensions</c>. Defaults are read from the getters at
/// construction, before the saved file is loaded.
/// </summary>
public sealed class RelockConfig : SimpleModConfig
{
    [ConfigHoverTip]
    public static bool HideMainMenuButton
    {
        get => Settings.HideMainMenuButton;
        set => Settings.HideMainMenuButton = value;
    }
}
