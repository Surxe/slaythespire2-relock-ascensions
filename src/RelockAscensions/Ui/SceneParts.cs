using Godot;
using MegaCrit.Sts2.Core.Assets;

namespace RelockAscensions.Ui;

/// <summary>
/// The game scenes the relock screen is built from, and the one trick that makes reuse work:
/// instantiate the game's scene as is (layout, fonts, textures, child scripts) and swap only its
/// root script for the mod's own class. Scene paths and node names are the only coupling to the
/// game's UI; if one moves in an update, building the screen fails and the button says so.
/// </summary>
internal static class SceneParts
{
    /// <summary>The Mods screen: back button, bordered list with scrollbar, info panel, two buttons.</summary>
    public const string ModdingScreen = "res://scenes/screens/modding/modding_screen.tscn";

    /// <summary>One row of the Mods list: highlight panel, title, platform icon, tickbox.</summary>
    public const string ModdingRow = "res://scenes/screens/modding/modding_screen_row.tscn";

    /// <summary>The settings screen's "&lt; value &gt;" control (screenshake, MSAA, ...).</summary>
    public const string Paginator = "res://scenes/screens/paginator.tscn";

    private static readonly Dictionary<Type, Script> Scripts = new();

    /// <summary>Instantiates a game scene and makes its root a <typeparamref name="T"/>.</summary>
    public static T InstantiateAs<T>(string scenePath) where T : Node, new()
    {
        var node = PreloadManager.Cache.GetScene(scenePath).Instantiate(PackedScene.GenEditState.Disabled);
        var id = node.GetInstanceId();
        // Replacing the script disposes the old managed wrapper; look the node up again by id.
        node.SetScript(ScriptOf<T>());
        return GodotObject.InstanceFromId(id) as T
            ?? throw new InvalidOperationException($"Could not attach {typeof(T).Name} to {scenePath}");
    }

    private static Script ScriptOf<T>() where T : Node, new()
    {
        if (!Scripts.TryGetValue(typeof(T), out var script))
        {
            var probe = new T();
            script = probe.GetScript().As<Script>();
            probe.Free();
            Scripts[typeof(T)] = script;
        }
        return script;
    }
}
