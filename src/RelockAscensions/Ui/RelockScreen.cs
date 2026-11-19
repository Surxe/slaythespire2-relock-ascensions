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
/// row holding the settings paginator. It opens at what is unlocked (<see cref="RelockEditor"/>),
/// the player moves any of them up to the cap or takes the run-history values, and Save shows the
/// game's confirmation popup before anything is written. Talks to <see cref="RelockApi"/> only (spec 7).
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
        Control Node,
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
    private NButton _historyButton = null!;

    private EditorState? _state;
    private Row? _focused;
    private Row? _hovered; // the mouse is over this row; its text wins over the focused row's
    private bool _busy;
    private bool _historyHelp; // the pointer or focus is on Use Run History

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

        _saveButton = GetNode<NButton>("%GetModsButton");
        _resetButton = GetNode<NButton>("%MakeModsButton");
        _historyButton = CopyOf(_resetButton, "UseRunHistoryButton");
        AddButtonBar(_resetButton, _historyButton, _saveButton);
        _saveButton.GetNode<MegaLabel>("Visuals/Label").SetTextAutoSize(Strings.Text("SCREEN.save"));
        _resetButton.GetNode<MegaLabel>("Visuals/Label").SetTextAutoSize(Strings.Text("SCREEN.reset"));
        _historyButton.GetNode<MegaLabel>("Visuals/Label").SetTextAutoSize(Strings.Text("SCREEN.history"));
        _saveButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnSavePressed()));
        _resetButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => SetAll(r => r.Unlocked, "Reset")));
        _historyButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => SetAll(r => r.FromHistory, "Use Run History")));
        _historyButton.Connect(Control.SignalName.MouseEntered, Callable.From(() => ShowHistoryHelp(true)));
        _historyButton.Connect(Control.SignalName.MouseExited, Callable.From(() => ShowHistoryHelp(false)));
        _historyButton.Connect(Control.SignalName.FocusEntered, Callable.From(() => ShowHistoryHelp(true)));
        _historyButton.Connect(Control.SignalName.FocusExited, Callable.From(() => ShowHistoryHelp(false)));
        LinkFocus(_resetButton, _historyButton, _saveButton);

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
        _hovered = null;
    }

    /// <summary>Reads the save and history again and rebuilds every row at what is unlocked.</summary>
    private void Reload()
    {
        RemoveAllChildren(_rowList);
        _rows.Clear();
        _focused = null;
        _hovered = null;

        _state = RelockApi.ReadEditor();
        _infoTitle.Text = Strings.ProfileName(_state.Target);
        if (!_state.Ok)
        {
            _infoBody.Text = Strings.Text("ERROR.body", ("message", _state.Error));
            _pending.Visible = false;
            _saveButton.Disable();
            _resetButton.Disable();
            _historyButton.Disable();
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
        // Pass: the row sees the pointer (for the info panel) but takes no clicks; the paginator
        // inside it still gets them.
        node.MouseFilter = MouseFilterEnum.Pass;
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
        paginator.Setup(model.Max, model.Unlocked);

        var row = new Row(model, node, highlight, title, detail, paginator);
        paginator.ValueChanged += _ => Refresh();
        paginator.Connect(Control.SignalName.FocusEntered, Callable.From(() => OnRowFocus(row, focused: true)));
        paginator.Connect(Control.SignalName.FocusExited, Callable.From(() => OnRowFocus(row, focused: false)));
        title.MouseFilter = MouseFilterEnum.Ignore;
        detail.MouseFilter = MouseFilterEnum.Ignore;
        // Every control in the row reports the pointer: the paginator's arrows stop mouse events,
        // so the row alone would not see them on every Godot version.
        foreach (var control in Controls(node))
        {
            control.Connect(Control.SignalName.MouseEntered, Callable.From(() => OnRowHover(row, hovered: true)));
            control.Connect(Control.SignalName.MouseExited, Callable.From(() => OnRowHover(row, hovered: false)));
        }
        return row;
    }

    private static IEnumerable<Control> Controls(Node node)
    {
        if (node is Control control)
        {
            yield return control;
        }
        foreach (var child in node.GetChildren())
        {
            foreach (var c in Controls(child))
            {
                yield return c;
            }
        }
    }

    private void OnRowFocus(Row row, bool focused)
    {
        if (focused)
        {
            _focused = row;
        }
        else if (_focused == row)
        {
            _focused = null;
        }
        UpdateHighlight(row);
        RefreshInfo();
    }

    private void OnRowHover(Row row, bool hovered)
    {
        // Moving between controls of one row fires exit then enter; only leaving the row's
        // rect counts as leaving it.
        if (!hovered && row.Node.GetGlobalRect().HasPoint(row.Node.GetGlobalMousePosition()))
        {
            return;
        }
        var previous = _hovered;
        if (hovered)
        {
            _hovered = row;
        }
        else if (_hovered == row)
        {
            _hovered = null;
        }
        if (previous == _hovered)
        {
            return;
        }
        if (previous is not null)
        {
            UpdateHighlight(previous);
        }
        UpdateHighlight(row);
        RefreshInfo();
    }

    /// <summary>Same highlight the Mods list uses, for the focused and the hovered row.</summary>
    private void UpdateHighlight(Row row)
    {
        var color = StsColors.darkBlue;
        color.A = RowFocusAlpha;
        row.Highlight.Modulate = row == _focused || row == _hovered ? color : Colors.Transparent;
    }

    private IReadOnlyList<RelockRequest> Requests() =>
        RelockEditor.Requests(_rows.Select(r => (r.Model, r.Paginator.Value)));

    private void Refresh()
    {
        foreach (var row in _rows)
        {
            var value = row.Paginator.Value;
            row.Title.Modulate = value > row.Model.Unlocked ? StsColors.green
                : value < row.Model.Unlocked ? StsColors.gold
                : Colors.White;
        }
        var count = Requests().Count;
        _pending.Visible = count > 0;
        if (count > 0)
        {
            _pending.SetTextAutoSize(Strings.Text("SCREEN.pending", ("count", count)));
        }
        SetEnabled(_saveButton, count > 0);
        SetEnabled(_resetButton, _rows.Any(r => r.Paginator.Value != r.Model.Unlocked));
        SetEnabled(_historyButton, _rows.Any(r => r.Paginator.Value != r.Model.FromHistory));
        RefreshInfo();
    }

    private void RefreshInfo()
    {
        if (_state is not { Ok: true } state)
        {
            return;
        }
        var text = Strings.Text("SCREEN.intro", ("profile", Strings.ProfileName(state.Target)));
        if (_historyHelp)
        {
            text += "\n\n" + Strings.Text("SCREEN.historyHelp");
        }
        else if ((_hovered ?? _focused) is { } row)
        {
            var m = row.Model;
            var scope = Strings.ScopeName(m.Scope);
            text += "\n\n" + (m.Earned is { } earned
                ? Strings.Text("SCREEN.focus", ("scope", scope),
                    ("unlocked", m.Unlocked), ("earned", earned), ("runs", m.Runs))
                : Strings.Text("SCREEN.focusNoRuns", ("scope", scope), ("unlocked", m.Unlocked)));
            if (m.RevealsEpoch)
            {
                text += "\n" + Strings.Text("SCREEN.focusRevealsEpoch", ("scope", scope));
            }
            // What Save would do to this row, so the panel adds to the row instead of repeating it.
            var value = row.Paginator.Value;
            if (value != m.Unlocked)
            {
                text += "\n\n" + Strings.Text("SCREEN.focusChange", ("from", m.Unlocked), ("to", value));
                if (m.PreferredAfter(value) != m.Preferred)
                {
                    text += " " + Strings.Text("SCREEN.focusPreferred", ("from", m.Preferred), ("to", m.PreferredAfter(value)));
                }
            }
        }
        if (state.HistoryFilesFailed > 0)
        {
            text += "\n\n" + Strings.Text("NOTE.historyFailed", ("count", state.HistoryFilesFailed));
        }
        _infoBody.Text = text;
    }

    /// <summary>Sets every row to <paramref name="value"/> (Reset: unlocked; Use Run History: earned).</summary>
    private void SetAll(Func<EditorRow, int> value, string button)
    {
        Log.Info($"{button} pressed");
        foreach (var row in _rows)
        {
            row.Paginator.SetIndex(value(row.Model));
        }
        Refresh();
    }

    private void ShowHistoryHelp(bool show)
    {
        // Leaving by mouse while the button keeps controller focus still shows the row text;
        // the next focus or hover brings the help back.
        _historyHelp = show;
        RefreshInfo();
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
    /// one their own unfocus animation already picks (gray label when disabled), and fade the
    /// whole button too: a disabled button ignores hover, and a gray label alone reads as merely
    /// unhovered. The button script tweens the label's modulate and the visuals' position, never
    /// the visuals' modulate, so this sticks.
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
        button.GetNode<Control>("Visuals").Modulate = enabled ? Colors.White : DisabledButtonTint;
    }

    private static readonly Color DisabledButtonTint = new(1, 1, 1, 0.45f);

    /// <summary>
    /// Moves the buttons into the bordered list, centered in one line pinned to its bottom,
    /// so they stay put whatever the number of rows; the scroll area ends above them. In the Mods
    /// scene they sit above the list, where the title needs the room (and they get no hover or
    /// clicks there in game). Use Run History is wider for its longer label.
    /// </summary>
    private void AddButtonBar(params NButton[] buttons)
    {
        const int gap = 12;
        const float wideFactor = 2f;
        var size = _resetButton.Size;
        var height = size.Y + 2 * gap;

        var margin = new MarginContainer { Name = "RelockButtons" };
        margin.AddThemeConstantOverride("margin_top", gap);
        margin.AddThemeConstantOverride("margin_bottom", gap);
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", gap);
        margin.AddChild(bar);
        foreach (var button in buttons)
        {
            button.GetParent()?.RemoveChild(button);
            var width = button == _historyButton ? size.X * wideFactor : size.X;
            if (width > size.X)
            {
                Widen(button, size, width);
            }
            button.CustomMinimumSize = new Vector2(width, size.Y);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            bar.AddChild(button);
        }

        var panel = _scroll.GetParent<Control>();
        if (panel is Container)
        {
            // A layout container places children itself: the bar goes right after the list.
            margin.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            panel.AddChild(margin);
            panel.MoveChild(margin, _scroll.GetIndex() + 1);
            return;
        }
        panel.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        margin.GrowVertical = GrowDirection.Begin;
        margin.OffsetTop = -height;
        _scroll.OffsetBottom -= height; // rows scroll above the bar, never under it
    }

    /// <summary>
    /// Share of the button art kept unstretched at each end when a button is widened. The
    /// middle of the art stretches; the ends (rounded corners, trim) keep their shape.
    /// </summary>
    private const float ArtCapShare = 0.3f;

    /// <summary>
    /// Widens a Mods-screen button (<c>NModMenuButton</c>). Its art, <c>Visuals/Image</c>, keeps
    /// its aspect ratio and stays centered, so a wider button shows the same art; its label is a
    /// fixed box. The script needs that image to stay a <c>TextureRect</c> (and drives the hover
    /// glow through its shader material), so it is kept, without a texture, and the art is drawn
    /// as three slices beside it that share the material: ends at the original scale, middle
    /// stretched. The label box spans the middle.
    /// </summary>
    private static void Widen(NButton button, Vector2 original, float width)
    {
        var visuals = button.GetNode<Control>("Visuals");
        var image = button.GetNode<TextureRect>("Visuals/Image");
        var label = button.GetNode<Control>("Visuals/Label");
        if (image.Texture is not { } texture)
        {
            Log.Warn($"{button.Name}: no button art to widen");
            return;
        }

        // Its own glow material: a copy of a scene button may share the original's.
        var material = image.Material?.Duplicate() as Material;
        image.Material = material;

        // Where the original art sits: scaled to fit, centered (stretch_mode keep aspect centered).
        var art = texture.GetSize();
        var scale = Mathf.Min(original.X / art.X, original.Y / art.Y);
        var drawn = art * scale;
        var top = (original.Y - drawn.Y) / 2;
        var side = (original.X - drawn.X) / 2; // empty margin left and right of the art
        var cap = art.X * ArtCapShare;
        var capWidth = cap * scale;
        var middleWidth = width - 2 * side - 2 * capWidth;

        var slices = new[]
        {
            (Region: new Rect2(0, 0, cap, art.Y), X: side, Width: capWidth),
            (Region: new Rect2(cap, 0, art.X - 2 * cap, art.Y), X: side + capWidth, Width: middleWidth),
            (Region: new Rect2(art.X - cap, 0, cap, art.Y), X: width - side - capWidth, Width: capWidth),
        };
        var index = image.GetIndex();
        foreach (var (region, x, w) in slices)
        {
            var slice = new TextureRect
            {
                Name = "WideImage",
                Texture = new AtlasTexture { Atlas = texture, Region = region },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Material = material,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(x, top),
                Size = new Vector2(w, drawn.Y),
            };
            visuals.AddChild(slice);
            visuals.MoveChild(slice, index++);
        }
        image.Texture = null; // kept for the script; the slices draw the art

        // The label box (anchored to the center) spans the stretched middle.
        var half = middleWidth / 2;
        label.OffsetLeft = -half;
        label.OffsetRight = half;
        Log.Info($"{button.Name}: art {art} at scale {scale:0.###}, widened {original.X} -> {width}");
    }

    /// <summary>
    /// A copy of a scene button, without signal connections: the game's button script connects
    /// its own in <c>_Ready</c>, and ours are added by the caller.
    /// </summary>
    private static NButton CopyOf(NButton button, string name)
    {
        var flags = (int)(DuplicateFlags.Groups | DuplicateFlags.Scripts | DuplicateFlags.UseInstantiation);
        var copy = (NButton)button.Duplicate(flags);
        copy.Name = name;
        return copy;
    }

    /// <summary>Left/right controller focus between the buttons, in the order given (left to right).</summary>
    private static void LinkFocus(params NButton[] buttons)
    {
        var ordered = buttons;
        for (var i = 0; i < ordered.Length; i++)
        {
            var left = ordered[i == 0 ? ordered.Length - 1 : i - 1];
            var right = ordered[(i + 1) % ordered.Length];
            ordered[i].FocusNeighborLeft = ordered[i].GetPathTo(left);
            ordered[i].FocusNeighborRight = ordered[i].GetPathTo(right);
        }
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
