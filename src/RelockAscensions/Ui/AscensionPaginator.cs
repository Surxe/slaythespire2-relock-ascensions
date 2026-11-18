using System.Globalization;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace RelockAscensions.Ui;

/// <summary>
/// The settings screen's paginator ("&lt; 9 &gt;"), offering Ascension levels 0 to a max. Arrows,
/// keyboard and controller paging, the slide animation and the focus reticle all come from the
/// game's <see cref="NPaginator"/>; this class only supplies the options and reports changes.
/// </summary>
public partial class AscensionPaginator : NPaginator
{
    public event Action<int>? ValueChanged;

    public int Value => _currentIndex;

    public override void _Ready() => ConnectSignals();

    /// <summary>Call after the paginator is in the tree.</summary>
    public void Setup(int max, int value)
    {
        _options.Clear();
        for (var i = 0; i <= max; i++)
        {
            _options.Add(i.ToString(CultureInfo.CurrentCulture));
        }
        _currentIndex = Math.Clamp(value, 0, max);
        _label.SetTextAutoSize(_options[_currentIndex]);
    }

    protected override void OnIndexChanged(int index)
    {
        _label.SetTextAutoSize(_options[index]);
        ValueChanged?.Invoke(index);
    }
}
