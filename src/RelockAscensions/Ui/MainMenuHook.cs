using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace RelockAscensions.Ui;

/// <summary>
/// Adds a "Relock Ascensions" entry to the main menu's text-button column without Harmony
/// (spec 5.3, 10.7). Every main menu the game builds is caught from a scene-tree signal while it
/// enters the tree, which is before its own <c>_Ready</c>. The button is a copy of the game's own
/// Compendium button placed in the same VBoxContainer, so:
/// <list type="bullet">
/// <item>it looks and animates like the other entries (same scene, script, font, hover tween);</item>
/// <item>the menu's <c>_Ready</c> wires it into the gold focus reticle like its siblings;</item>
/// <item>it hides with the column whenever a submenu opens (the menu hides the whole container);</item>
/// <item>other mods that add entries to the same container stack instead of overlapping.</item>
/// </list>
/// Installing only connects a signal; nothing is read or written until the player presses it.
/// </summary>
internal static class MainMenuHook
{
    public const string ButtonName = "RelockAscensionsButton";

    private const string ContainerPath = "MainMenuTextButtons";
    private const string TemplateName = "CompendiumButton";
    private const string InsertBefore = "QuitButton";

    private static readonly Callable OnNodeAddedCallable = Callable.From<Node>(OnNodeAdded);
    private static readonly Callable OnSceneEnteredCallable = Callable.From<Node>(OnSceneEntered);

    private static SceneTree? _tree;

    /// <summary>
    /// Starts with <c>SceneTree.node_added</c> (the root scene container may not exist yet at mod
    /// load) and narrows to the root scene container's <c>child_entered_tree</c> once it does, so
    /// the handler runs once per screen change instead of once per node.
    /// </summary>
    public static void Install()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Log.Warn("No scene tree at load; main-menu button disabled (the console command still works)");
            return;
        }
        _tree = tree;
        if (!TryNarrow())
        {
            tree.Connect(SceneTree.SignalName.NodeAdded, OnNodeAddedCallable);
        }
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is NMainMenu menu)
        {
            Inject(menu);
        }
        if (TryNarrow() && _tree!.IsConnected(SceneTree.SignalName.NodeAdded, OnNodeAddedCallable))
        {
            _tree.Disconnect(SceneTree.SignalName.NodeAdded, OnNodeAddedCallable);
        }
    }

    private static bool TryNarrow()
    {
        if (NGame.Instance?.RootSceneContainer is not { } container || !GodotObject.IsInstanceValid(container))
        {
            return false;
        }
        if (!container.IsConnected(Node.SignalName.ChildEnteredTree, OnSceneEnteredCallable))
        {
            container.Connect(Node.SignalName.ChildEnteredTree, OnSceneEnteredCallable);
        }
        return true;
    }

    private static void OnSceneEntered(Node node)
    {
        if (node is NMainMenu menu)
        {
            Inject(menu);
        }
    }

    private static void OpenScreen(NMainMenu menu)
    {
        try
        {
            RelockScreen.Open(menu);
        }
        catch (Exception e)
        {
            // A game update moved a scene or node the screen is built from.
            Log.Error($"Opening the relock screen failed: {e}");
            TaskHelper.RunSafely(Popups.Notice(Strings.Mod("NOT_SAVED.header"),
                Strings.Mod("ERROR.body", ("message", "The relock screen could not be opened on this game version. The relock console command still works."))));
        }
    }

    private static void Inject(NMainMenu menu)
    {
        try
        {
            var column = menu.GetNodeOrNull<Control>(ContainerPath);
            if (column is null)
            {
                Log.Warn($"Main menu has no {ContainerPath}; button not added");
                return;
            }
            if (column.HasNode(ButtonName))
            {
                return;
            }
            var template = column.GetNodeOrNull<NMainMenuTextButton>(TemplateName)
                ?? column.GetChildren().OfType<NMainMenuTextButton>().LastOrDefault();
            if (template is null)
            {
                Log.Warn("Main menu has no text button to copy; button not added");
                return;
            }

            // The menu has not run _Ready yet, so the copy carries no signal connections.
            var button = (NMainMenuTextButton)template.Duplicate();
            button.Name = ButtonName;
            button.Visible = true;
            column.AddChild(button);
            if (column.GetNodeOrNull(InsertBefore) is { } quit)
            {
                column.MoveChild(button, quit.GetIndex());
            }

            // The label exists only after the button's own _Ready; SetLocalization then keeps it
            // in sync with language changes like the stock entries.
            button.Connect(Node.SignalName.Ready,
                Callable.From(() => button.SetLocalization(Strings.MainMenuButtonKey)),
                (uint)GodotObject.ConnectFlags.OneShot);
            button.Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => OpenScreen(menu)));
        }
        catch (Exception e)
        {
            Log.Error($"Adding the main-menu button failed (the console command still works): {e}");
        }
    }
}
