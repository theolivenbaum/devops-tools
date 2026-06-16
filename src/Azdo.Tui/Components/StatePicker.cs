using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>Emitted when a state option is selected (≈ <c>components.StateSelectedMsg</c>).</summary>
public sealed record StateSelectedMsg(string State) : IMsg;

/// <summary>
/// An available work item state passed to <see cref="StatePicker.SetStates"/>. Kept
/// local to avoid a dependency on Core's <c>WorkItemTypeState</c>.
/// </summary>
public readonly record struct WorkItemStateOption(string Name, string Category);

/// <summary>A single state choice in the picker.</summary>
internal sealed class StateOption
{
    public string Name = "";
    public string Icon = "";
    public bool IsCurrent;
}

/// <summary>A modal for selecting a work item state (≈ <c>components.StatePicker</c>).</summary>
public sealed class StatePicker
{
    private const int MinModalWidth = 80;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private List<StateOption> _options = new();
    private int _cursor;
    private string _currentState = "";

    public StatePicker(StyleSet styles) => _styles = styles;

    /// <summary>Sets the available states and positions the cursor on the current state.</summary>
    public void SetStates(IEnumerable<WorkItemStateOption> states, string currentState)
    {
        _currentState = currentState;
        _cursor = 0;
        var src = states.ToList();
        _options = new List<StateOption>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            var s = src[i];
            _options.Add(new StateOption
            {
                Name = s.Name,
                Icon = StateIcon(s.Category),
                IsCurrent = s.Name == currentState,
            });
            if (s.Name == currentState) _cursor = i;
        }
    }

    public void Show() => _visible = true;
    public void Hide() => _visible = false;
    public bool IsVisible => _visible;
    public void SetSize(int width, int height) { _width = width; _height = height; }
    public int GetCursor() => _cursor;

    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;
        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "esc":
                case "q":
                    _visible = false;
                    return null;
                case "up":
                case "k":
                    if (_cursor > 0) _cursor--;
                    return null;
                case "down":
                case "j":
                    if (_cursor < _options.Count - 1) _cursor++;
                    return null;
                case "enter":
                    if (_options.Count == 0) return null;
                    var selected = _options[_cursor];
                    _visible = false;
                    return Commands.Of(new StateSelectedMsg(selected.Name));
            }
        }
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;
        const string titleText = "Change Work Item State";
        const string helpTextStr = "↑/↓: navigate • enter: select • esc/q: cancel";

        int maxWidth = MinModalWidth;
        if (titleText.Length > maxWidth) maxWidth = titleText.Length;
        if (helpTextStr.Length > maxWidth) maxWidth = helpTextStr.Length;

        foreach (var opt in _options)
        {
            string label = opt.IsCurrent ? opt.Name + " (current)" : opt.Name;
            int lineLen = $"> {opt.Icon} {label}".Length;
            if (lineLen > maxWidth) maxWidth = lineLen;
        }

        var optionList = new System.Text.StringBuilder();
        for (int i = 0; i < _options.Count; i++)
        {
            var opt = _options[i];
            string cursor = i == _cursor ? ">" : " ";
            string label = opt.IsCurrent ? opt.Name + " (current)" : opt.Name;
            string line = $"{cursor} {opt.Icon} {label}";

            line = i == _cursor
                ? Style.New().Foreground(theme.SelectForeground).Background(theme.SelectBackground).Width(maxWidth).Render(line)
                : Style.New().Foreground(theme.Foreground).Background(theme.Background).Width(maxWidth).Render(line);

            optionList.Append(line).Append('\n');
        }

        string title = Style.New()
            .Foreground(theme.Primary)
            .Background(theme.Background)
            .Bold()
            .Width(maxWidth)
            .Render(titleText);

        string helpText = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Background(theme.Background)
            .Width(maxWidth)
            .Render(helpTextStr);

        string content = Layout.JoinVertical(HAlign.Left, title, "", optionList.ToString(), helpText);

        var modalStyle = Style.New()
            .WithBorder(Border.Rounded)
            .BorderForeground(theme.Border)
            .Padding(1, 2)
            .Background(theme.Background);

        var modal = modalStyle.Render(content);

        if (_width > 0 && _height > 0)
            modal = Layout.Place(_width, _height, HAlign.Center, VAlign.Center, modal);

        return modal;
    }

    /// <summary>Returns an icon for the work item state category.</summary>
    private static string StateIcon(string category) => category switch
    {
        "Proposed" => "○",
        "InProgress" => "◐",
        "Resolved" => "●",
        "Completed" => "✓",
        _ => "○",
    };
}
