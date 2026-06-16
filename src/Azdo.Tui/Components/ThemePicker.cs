using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>Emitted when a theme is selected (≈ <c>components.ThemeSelectedMsg</c>).</summary>
public sealed record ThemeSelectedMsg(string ThemeName) : IMsg;

/// <summary>A modal for selecting the application theme (≈ <c>components.ThemePicker</c>).</summary>
public sealed class ThemePicker
{
    private const int MinModalWidth = 80;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private readonly IReadOnlyList<string> _availableThemes;
    private readonly string _currentTheme;
    private int _cursor;

    public ThemePicker(StyleSet styles, IReadOnlyList<string> available, string current)
    {
        _styles = styles;
        _availableThemes = available;
        _currentTheme = current;

        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] == current) { _cursor = i; break; }
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
                    if (_cursor < _availableThemes.Count - 1) _cursor++;
                    return null;
                case "enter":
                    var selected = _availableThemes[_cursor];
                    _visible = false;
                    return Commands.Of(new ThemeSelectedMsg(selected));
            }
        }
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;

        const string titleText = "Select Theme";
        const string helpTextStr = "↑/↓: navigate • enter: select • esc/q: cancel";

        int maxWidth = MinModalWidth;
        if (titleText.Length > maxWidth) maxWidth = titleText.Length;
        if (helpTextStr.Length > maxWidth) maxWidth = helpTextStr.Length;

        foreach (var themeName in _availableThemes)
        {
            int lineLen = ("> " + themeName + " (current)").Length;
            if (lineLen > maxWidth) maxWidth = lineLen;
        }

        var themeList = new System.Text.StringBuilder();
        for (int i = 0; i < _availableThemes.Count; i++)
        {
            var themeName = _availableThemes[i];
            string cursor = i == _cursor ? ">" : " ";
            string isCurrent = themeName == _currentTheme ? " (current)" : "";
            string line = $"{cursor} {themeName}{isCurrent}";

            line = i == _cursor
                ? Style.New().Foreground(theme.SelectForeground).Background(theme.SelectBackground).Width(maxWidth).Render(line)
                : Style.New().Foreground(theme.Foreground).Background(theme.Background).Width(maxWidth).Render(line);

            themeList.Append(line).Append('\n');
        }

        string title = Style.New()
            .Foreground(theme.Primary)
            .Background(theme.Background)
            .Bold()
            .Width(maxWidth)
            .Render("Select Theme");

        string helpText = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Background(theme.Background)
            .Width(maxWidth)
            .Render(helpTextStr);

        string content = Layout.JoinVertical(HAlign.Left, title, "", themeList.ToString(), helpText);

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
