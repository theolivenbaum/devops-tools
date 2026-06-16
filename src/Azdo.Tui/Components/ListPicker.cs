using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>A single option in a <see cref="ListPicker"/> (≈ <c>components.ListPickerOption</c>).</summary>
public sealed class ListPickerOption
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsCurrent { get; set; }
}

/// <summary>Emitted when a list picker option is chosen. An empty value means "clear filter".</summary>
public sealed record ListPickerSelectedMsg(string Value) : IMsg;

/// <summary>
/// A generic single-select modal list picker (≈ <c>components.ListPicker</c>).
/// Used directly and as the model for the more specialized pickers.
/// </summary>
public sealed class ListPicker
{
    internal const int MinModalWidth = 80;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private string _title = "";
    private List<ListPickerOption> _options = new();
    private int _cursor;
    private bool _allowClear;

    public ListPicker(StyleSet styles) => _styles = styles;

    /// <summary>
    /// Configures the picker. When <paramref name="activeValue"/> is set and
    /// <paramref name="allowClear"/> is true, a "Clear filter" option is prepended
    /// and the cursor is positioned on the active value.
    /// </summary>
    public void SetConfig(string title, IEnumerable<ListPickerOption> options, string activeValue, bool allowClear)
    {
        _title = title;
        _allowClear = allowClear;
        _options = new List<ListPickerOption>();
        _cursor = 0;

        bool hasActive = activeValue != "" && allowClear;

        if (hasActive)
            _options.Add(new ListPickerOption { Name = "Clear filter", Icon = "✕", IsCurrent = false });

        int offset = hasActive ? 1 : 0;

        var src = options.ToList();
        for (int i = 0; i < src.Count; i++)
        {
            var opt = src[i];
            opt.IsCurrent = opt.Name == activeValue;
            _options.Add(opt);
            if (opt.Name == activeValue)
                _cursor = i + offset;
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
                    string value = selected.Name == "Clear filter" ? "" : selected.Name;
                    return Commands.Of(new ListPickerSelectedMsg(value));
            }
        }
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;
        const string helpTextStr = "↑/↓: navigate • enter: select • esc/q: cancel";

        int maxWidth = MinModalWidth;
        if (_title.Length > maxWidth) maxWidth = _title.Length;
        if (helpTextStr.Length > maxWidth) maxWidth = helpTextStr.Length;

        foreach (var opt in _options)
        {
            string label = opt.IsCurrent ? opt.Name + " (current)" : opt.Name;
            string icon = opt.Icon == "" ? "●" : opt.Icon;
            int lineLen = $"> {icon} {label}".Length;
            if (lineLen > maxWidth) maxWidth = lineLen;
        }

        var optionList = new System.Text.StringBuilder();
        for (int i = 0; i < _options.Count; i++)
        {
            var opt = _options[i];
            string cursor = i == _cursor ? ">" : " ";
            string label = opt.IsCurrent ? opt.Name + " (current)" : opt.Name;
            string icon = opt.Icon == "" ? "●" : opt.Icon;
            string line = $"{cursor} {icon} {label}";

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
            .Render(_title);

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
}
