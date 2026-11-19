using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using RelockAscensions.Core;

namespace RelockAscensions.Ui;

/// <summary>
/// The game's own confirmation popup (<see cref="NGenericPopup"/>, the one used for quitting or
/// deleting a profile), shown one at a time.
/// </summary>
internal static class Popups
{
    private const int MaxFramesToWaitForModal = 30;

    public static Task<bool> Confirm(LocString header, LocString body, LocString yes) =>
        Show(header, body, Strings.GameCancel, yes);

    public static Task Notice(LocString header, LocString body) =>
        Show(header, body, null, Strings.GameOk);

    /// <summary>
    /// The changes grouped under "Up" and "Down", one line each (<c>Defect: 10 -> 9</c>), then one
    /// line naming every character whose epoch a raise reveals.
    /// </summary>
    public static string ChangeRows(PreviewSummary summary)
    {
        var changes = summary.Rows.Where(r => r.Change is not null).Select(r => r.Change!).ToList();
        var lines = new List<string>();
        void Group(string header, IReadOnlyCollection<Change> group)
        {
            if (group.Count == 0)
            {
                return;
            }
            if (lines.Count > 0)
            {
                lines.Add("");
            }
            lines.Add(Strings.Text(header));
            lines.AddRange(group.Select(Line));
        }
        // The Random entry only lowers its selected level, so it goes with the lowered ones.
        Group("POPUP.raised", changes.Where(c => c.Raises).ToList());
        Group("POPUP.lowered", changes.Where(c => !c.Raises).ToList());

        // One line for every character whose epoch the save reveals.
        var reveals = changes
            .Where(c => c.Raises && c.RevealEpoch is not null)
            .Select(c => Strings.ScopeName(c.Scope))
            .ToList();
        if (reveals.Count > 0)
        {
            lines.Add("");
            lines.Add(Strings.Text("ROW.revealsEpoch", ("characters", JoinNames(reveals))));
        }
        return string.Join("\n", lines);
    }

    /// <summary><c>Ironclad</c>, <c>Ironclad and Silent</c>, <c>Ironclad, Silent and Defect</c>.</summary>
    private static string JoinNames(IReadOnlyList<string> names) => names.Count == 1
        ? names[0]
        : Strings.Text("LIST.and", ("first", string.Join(", ", names.Take(names.Count - 1))), ("last", names[^1]));

    private static string Line(Change c)
    {
        // The Random entry only moves its selected level; its max is not a real track.
        var (from, to) = c.ChangesMax ? (c.OldMax, c.NewMax) : (c.OldPreferred, c.NewPreferred);
        return Strings.Text("ROW.change", ("scope", Strings.ScopeName(c.Scope)), ("from", from), ("to", to));
    }

    /// <summary>
    /// Shows one popup and waits for its answer. The popup's buttons clear the modal container
    /// after our continuation runs, so wait for that before the next popup: the container ignores
    /// <c>Add</c> while a modal is open.
    /// </summary>
    private static async Task<bool> Show(LocString header, LocString body, LocString? no, LocString yes)
    {
        if (NModalContainer.Instance is not { } modal)
        {
            return false;
        }
        for (var i = 0; modal.OpenModal is not null && i < MaxFramesToWaitForModal; i++)
        {
            await modal.AwaitProcessFrame();
        }
        if (modal.OpenModal is not null || NGenericPopup.Create() is not { } popup)
        {
            Log.Warn("Another popup is open; relock popup not shown");
            return false;
        }
        modal.Add(popup);
        var answer = await popup.WaitForConfirmation(body, header, no, yes);
        await modal.AwaitProcessFrame();
        return answer;
    }
}
