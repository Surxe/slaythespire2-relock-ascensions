using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;
using RelockAscensions.Core;

namespace RelockAscensions.Ui;

/// <summary>
/// Root of a relock-screen row: the Mods list row scene with its click handling replaced by
/// nothing. Input goes to the paginator inside it.
/// </summary>
public partial class RelockRow : Control
{
}

/// <summary>
/// The relock editor, a main-menu submenu built on the game's Mods screen. Each scope is a Mods
/// row holding the settings paginator. It opens at the levels your run history earned
/// (<see cref="RelockEditor"/>), the player moves any of them, and Save shows the game's
/// confirmation popup before anything is written. Talks to <see cref="RelockApi"/> only (spec 7).
/// </summary>
public partial class RelockScreen : NSubmenu
{
    public const string NodeName = "RelockAscensionsScreen";

    private const float RowFocusAlpha = 0.25f;

    private static readonly StringName ThemeColorDefault = "default_color";

    // Row layout inside the Mods list (about 720 px wide, 72 px tall): the name over a small
    // detail line on the left, the paginator (its minimum width) on the right.
    private const float PaginatorWidth = 316;
    private const float RowMargin = 12;
    private const float NameBottom = 0.62f;
    private const float DetailTop = 0.56f;

    private sealed record Row(
        EditorRow Model,
        Panel Highlight,
        MegaRichTextLabel Title,
        MegaRichTextLabel Detail,
        AscensionPaginator Paginator);

    private readonly List<Row> _rows = new();

    private NScrollableContainer _scroll = null!;
    private Control _rowList = null!;
    private MegaRichTextLabel _infoTitle = null!;
    private MegaRichTextLabel _infoBody = null!;
    private MegaRichTextLabel _pending = null!;
    private NButton _saveButton = null!;
    private NButton _resetButton = null!;

    private EditorState? _state;
    private Row? _focused;
    private bool _busy;

    protected override Control? InitialFocusedControl => _rows.FirstOrDefault()?.Paginator;

    /// <summary>Pushes the screen on the main menu's submenu stack, building it on first use.</summary>
    public static void Open(NMainMenu menu)
    {
        var stack = menu.SubmenuStack;
        var screen = stack.GetNodeOrNull<RelockScreen>(NodeName);
        if (screen is null)
        {
            screen = SceneParts.InstantiateAs<RelockScreen>(SceneParts.ModdingScreen);
            screen.Name = NodeName;
            screen.Visible = false;
            stack.AddChild(screen); // runs _Ready now, before Push opens it
        }
        stack.Push(screen);
    }

    public override void _Ready()
    {
        _scroll = GetNode<NScrollableContainer>("%ModsScrollContainer");
        _rowList = _scroll.GetNode<Control>("Mask/Content");
        RemoveAllChildren(_rowList); // the scene's placeholder row

        var info = GetNode<Control>("%ModInfoContainer");
        _infoTitle = info.GetNode<MegaRichTextLabel>("ModTitle");
        _infoBody = info.GetNode<MegaRichTextLabel>("ModDescription");
        var image = info.GetNode<TextureRect>("ModImage");
        image.Visible = false;
        _infoBody.OffsetTop = image.OffsetTop; // the description takes the image's space

        // The Mods scene's warning line: plain text, red by default. Ours is a summary, in gold.
        _pending = GetNode<MegaRichTextLabel>("%PendingChangesLabel");
        _pending.AddThemeColorOverride(ThemeColorDefault, StsColors.gold);
        GetNode<MegaRichTextLabel>("%InstalledModsTitle").SetTextAutoSize(Strings.Text("SCREEN.title"));

        _saveButton = LiftToScreen(GetNode<NButton>("%GetModsButton"));
        _resetButton = LiftToScreen(GetNode<NButton>("%MakeModsButton"));
        _saveButton.GetNode<MegaLabel>("Visuals/Label").SetTextAutoSize(Strings.Text("SCREEN.save"));
        _resetButton.GetNode<MegaLabel>("Visuals/Label").SetTextAutoSize(Strings.Text("SCREEN.reset"));
        _saveButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnSavePressed()));
        _resetButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ResetToSuggested()));

        ConnectSignals(); // NSubmenu: back button pops the stack
    }

    public override void OnSubmenuOpened()
    {
        base.OnSubmenuOpened();
        Reload();
    }

    public override void OnSubmenuClosed()
    {
        base.OnSubmenuClosed();
        RemoveAllChildren(_rowList);
        _rows.Clear();
        _focused = null;
    }

    /// <summary>Reads the save and history again and rebuilds every row at its suggestion.</summary>
    private void Reload()
    {
        RemoveAllChildren(_rowList);
        _rows.Clear();
        _focused = null;

        _state = RelockApi.ReadEditor();
        _infoTitle.Text = Strings.ProfileName(_state.Target);
        if (!_state.Ok)
        {
            _infoBody.Text = Strings.Text("ERROR.body", ("message", _state.Error));
            _pending.Visible = false;
            _saveButton.Disable();
            _resetButton.Disable();
            return;
        }

        foreach (var model in _state.Rows)
        {
            _rows.Add(AddRow(model));
        }
        _scroll.DisableScrollingIfContentFits();
        Refresh();
    }

    private Row AddRow(EditorRow model)
    {
        var node = SceneParts.InstantiateAs<RelockRow>(SceneParts.ModdingRow);
        node.MouseFilter = MouseFilterEnum.Ignore;
        node.GetNode<Control>("PlatformIcon").Visible = false;
        node.GetNode<Control>("Tickbox").Visible = false;

        var highlight = node.GetNode<Panel>("SelectionHighlight");
        highlight.Modulate = Colors.Transparent;

        var title = node.GetNode<MegaRichTextLabel>("Title");
        title.SetAnchorsPreset(LayoutPreset.FullRect);
        title.AnchorBottom = NameBottom;
        title.OffsetLeft = 0;
        title.OffsetTop = 0;
        title.OffsetRight = -(PaginatorWidth + 2 * RowMargin);
        title.OffsetBottom = 0;

        // Same label, smaller and dimmer, under the name.
        var detail = (MegaRichTextLabel)title.Duplicate();
        detail.Name = "Detail";
        detail.MaxFontSize = 20;
        detail.Modulate = StsColors.lightGray;
        detail.AnchorTop = DetailTop;
        detail.AnchorBottom = 1;
        node.AddChild(detail);

        var paginator = SceneParts.InstantiateAs<AscensionPaginator>(SceneParts.Paginator);
        paginator.SetAnchorsPreset(LayoutPreset.CenterRight);
        paginator.OffsetLeft = -(PaginatorWidth + RowMargin);
        paginator.OffsetRight = -RowMargin;
        paginator.OffsetTop = -32;
        paginator.OffsetBottom = 32;
        node.AddChild(paginator);

        _rowList.AddChild(node); // in the tree: labels and paginator are ready
        title.SetTextAutoSize(Strings.ScopeName(model.Scope));
        detail.SetTextAutoSize(model.Earned is { } earned
            ? Strings.Text("SCREEN.detail", ("unlocked", model.Unlocked), ("earned", earned))
            : Strings.Text("SCREEN.detailNoRuns", ("unlocked", model.Unlocked)));
        paginator.Setup(model.Max, model.Suggested);

        var row = new Row(model, highlight, title, detail, paginator);
        paginator.ValueChanged += _ => Refresh();
        paginator.Connect(Control.SignalName.FocusEntered, Callable.From(() => OnRowFocus(row, focused: true)));
        paginator.Connect(Control.SignalName.FocusExited, Callable.From(() => OnRowFocus(row, focused: false)));
        return row;
    }

    private void OnRowFocus(Row row, bool focused)
    {
        // Same highlight the Mods list uses for a focused row.
        var color = StsColors.darkBlue;
        color.A = RowFocusAlpha;
        row.Highlight.Modulate = focused ? color : Colors.Transparent;
        if (focused)
        {
            _focused = row;
            RefreshInfo();
        }
    }

    private IReadOnlyList<RelockRequest> Requests() =>
        RelockEditor.Requests(_rows.Select(r => (r.Model, r.Paginator.Value)));

    private void Refresh()
    {
        foreach (var row in _rows)
        {
            row.Title.Modulate = row.Paginator.Value != row.Model.Unlocked ? StsColors.gold : Colors.White;
        }
        var count = Requests().Count;
        _pending.Visible = count > 0;
        if (count > 0)
        {
            _pending.SetTextAutoSize(Strings.Text("SCREEN.pending", ("count", count)));
        }
        SetEnabled(_saveButton, count > 0);
        SetEnabled(_resetButton, _rows.Any(r => r.Paginator.Value != r.Model.Suggested));
        RefreshInfo();
    }

    private void RefreshInfo()
    {
        if (_state is not { Ok: true } state)
        {
            return;
        }
        var text = Strings.Text("SCREEN.intro", ("profile", Strings.ProfileName(state.Target)));
        if (_focused is { } row)
        {
            var m = row.Model;
            text += "\n\n" + (m.Earned is { } earned
                ? Strings.Text("SCREEN.focus", ("scope", Strings.ScopeName(m.Scope)),
                    ("unlocked", m.Unlocked), ("earned", earned), ("runs", m.Runs))
                : Strings.Text("SCREEN.focusNoRuns", ("scope", Strings.ScopeName(m.Scope)),
                    ("unlocked", m.Unlocked)));
        }
        if (state.HistoryFilesFailed > 0)
        {
            text += "\n\n" + Strings.Text("NOTE.historyFailed", ("count", state.HistoryFilesFailed));
        }
        _infoBody.Text = text;
    }

    private void ResetToSuggested()
    {
        Log.Info("Reset pressed");
        foreach (var row in _rows)
        {
            row.Paginator.SetIndex(row.Model.Suggested);
        }
        Refresh();
    }

    private void OnSavePressed()
    {
        Log.Info($"Save pressed ({Requests().Count} change(s){(_busy ? ", already busy" : "")})");
        if (_busy)
        {
            return;
        }
        _busy = true;
        TaskHelper.RunSafely(ConfirmAndApply());
    }

    private async Task ConfirmAndApply()
    {
        try
        {
            var preview = RelockApi.PreviewManual(Requests());
            if (!preview.Ok)
            {
                await Popups.Notice(Strings.Mod("NOT_SAVED.header"), Strings.Mod("ERROR.body", ("message", preview.Error)));
                return;
            }
            if (preview.Plan.IsEmpty)
            {
                return;
            }

            var profile = Strings.ProfileName(preview.Target);
            var rows = Popups.ChangeRows(PreviewSummary.Build(preview.Plan, null));
            if (!await Popups.Confirm(Strings.Mod("POPUP.header"),
                    Strings.Mod("POPUP.body", ("profile", profile), ("rows", rows)), Strings.Mod("SCREEN.save")))
            {
                Log.Info("Save cancelled from the relock screen; nothing written");
                return;
            }

            var result = RelockApi.Apply(preview.Plan);
            if (result.Status == ApplyStatus.Applied)
            {
                await Popups.Notice(Strings.Mod("DONE.header"), Strings.Mod("DONE.body", ("profile", profile)));
            }
            else
            {
                await Popups.Notice(Strings.Mod("NOT_SAVED.header"), Strings.Mod("ERROR.body", ("message", result.Message)));
            }
            if (IsInstanceValid(this) && Visible)
            {
                Reload(); // show what is now saved
                ActiveScreenContext.Instance.Update(); // the focused row was rebuilt; refocus like the game does
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The Mods screen's buttons are never disabled there, so they have no disabled look. Use the
    /// one their own unfocus animation already picks (gray label when disabled).
    /// </summary>
    private static void SetEnabled(NButton button, bool enabled)
    {
        if (enabled)
        {
            button.Enable();
        }
        else
        {
            button.Disable();
        }
        button.GetNode<MegaLabel>("Visuals/Label").Modulate = enabled ? StsColors.cream : StsColors.gray;
    }

    /// <summary>
    /// In the Mods scene the two buttons sit above their bordered panel, outside its rect, and get
    /// no hover or clicks there in game. Move them to the screen root at the same spot.
    /// </summary>
    private NButton LiftToScreen(NButton button)
    {
        var position = button.GlobalPosition;
        var size = button.Size;
        button.GetParent().RemoveChild(button);
        AddChild(button);
        button.SetAnchorsPreset(LayoutPreset.TopLeft);
        button.GlobalPosition = position;
        button.Size = size;
        return button;
    }

    private static void RemoveAllChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
