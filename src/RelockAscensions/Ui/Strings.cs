using MegaCrit.Sts2.Core.Localization;
using RelockAscensions.Core;

namespace RelockAscensions.Ui;

/// <summary>
/// Localized text for the UI. The mod's keys live in its PCK at
/// <c>RelockAscensions/localization/eng/main_menu_ui.json</c>, which the game merges into its own
/// <c>main_menu_ui</c> table at startup (other languages fall back to English). Buttons reuse the
/// game's own keys where one fits.
/// </summary>
internal static class Strings
{
    private const string Table = "main_menu_ui";
    private const string Prefix = "RELOCK_ASCENSIONS.";

    public const string MainMenuButtonKey = Prefix + "MAIN_MENU_BUTTON";

    /// <summary>One of the mod's strings, with SmartFormat variables (ints become numbers so
    /// <c>{n:plural:...}</c> works).</summary>
    public static LocString Mod(string key, params (string Name, object? Value)[] vars)
    {
        var loc = new LocString(Table, Prefix + key);
        foreach (var (name, value) in vars)
        {
            if (value is int i)
            {
                loc.Add(name, (decimal)i);
            }
            else
            {
                loc.Add(name, value?.ToString() ?? "");
            }
        }
        return loc;
    }

    public static string Text(string key, params (string Name, object? Value)[] vars) =>
        Mod(key, vars).GetFormattedText();

    /// <summary>The game's generic popup buttons ("Ok", "No").</summary>
    public static LocString GameOk => new(Table, "GENERIC_POPUP.ok");

    public static LocString GameCancel => new(Table, "GENERIC_POPUP.cancel");

    /// <summary>The game's own profile name, "Save Profile 1"; the console's label otherwise.</summary>
    public static string ProfileName(TargetInfo target)
    {
        if (target.ProfileId is not { } id)
        {
            return target.Label;
        }
        var loc = new LocString(Table, "PROFILE_SCREEN.BUTTON.title");
        loc.Add("Id", (decimal)id);
        return loc.GetFormattedText();
    }

    /// <summary>Character title from the game, else the game's Multiplayer label, else the mod's.</summary>
    public static string ScopeName(Scope scope) => scope.Kind switch
    {
        ScopeKind.Character => RelockApi.CharacterTitle(scope) ?? scope.CharacterId ?? "?",
        ScopeKind.Multiplayer => new LocString(Table, "MULTIPLAYER").GetFormattedText(),
        ScopeKind.Blitz => Mod("SCOPE.blitz").GetFormattedText(),
        _ => scope.ToString(),
    };
}
