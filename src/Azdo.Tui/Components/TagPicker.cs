using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>
/// Emitted when a single tag is chosen in single-select mode. An empty
/// <see cref="Tag"/> means "clear filter" (≈ <c>components.TagSelectedMsg</c>).
/// </summary>
public sealed record TagSelectedMsg(string Tag) : IMsg;

/// <summary>
/// Emitted when a multi-select picker is confirmed with enter. An empty list
/// means "clear selection" (≈ <c>components.TagsSelectedMsg</c>).
/// </summary>
public sealed record TagsSelectedMsg(IReadOnlyList<string> Tags) : IMsg;

/// <summary>A single tag choice in the picker.</summary>
internal sealed class TagOption
{
    public string Name = "";
    public bool IsClear;
    public bool Selected;
}

/// <summary>
/// A modal for selecting tag(s) to filter by (≈ <c>components.TagPicker</c>).
/// Defaults to single-select; call <see cref="SetTagsMulti"/> for multi-select.
/// Supports type-to-search filtering.
/// </summary>
public sealed class TagPicker
{
    private const int MinModalWidth = 80;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private List<TagOption> _options = new();
    private int _cursor;
    private string _activeTag = "";
    private bool _multiSelect;
    private string _title = "";
    private readonly TextInput _searchInput;

    public TagPicker(StyleSet styles)
    {
        _styles = styles;
        _searchInput = new TextInput
        {
            Prompt = "🔍 ",
            Placeholder = "search tags...",
            CharLimit = 100,
        };
    }

    /// <summary>Single-select mode. Prepends a "Clear filter" option when activeTag is set.</summary>
    public void SetTags(IEnumerable<string> tags, string activeTag)
    {
        _multiSelect = false;
        _title = "Filter by Tag";
        _activeTag = activeTag;
        _cursor = 0;
        _searchInput.Value = "";

        _options = new List<TagOption>();

        if (activeTag != "")
            _options.Add(new TagOption { Name = "Clear filter", IsClear = true });

        var src = tags.ToList();
        for (int i = 0; i < src.Count; i++)
        {
            var tag = src[i];
            _options.Add(new TagOption { Name = tag });
            if (tag == activeTag)
            {
                int offset = activeTag != "" ? 1 : 0;
                _cursor = i + offset;
            }
        }
    }

    /// <summary>Multi-select mode. Confirm with enter, which emits <see cref="TagsSelectedMsg"/>.</summary>
    public void SetTagsMulti(IEnumerable<string> tags, IEnumerable<string> selected)
    {
        _multiSelect = true;
        _title = "Pick sprints";
        _activeTag = "";
        _cursor = 0;
        _searchInput.Value = "";

        var sel = new HashSet<string>(selected);
        _options = tags.Select(tag => new TagOption { Name = tag, Selected = sel.Contains(tag) }).ToList();
    }

    public void Show() { _visible = true; _searchInput.Focus(); }
    public void Hide() { _visible = false; _searchInput.Blur(); }
    public bool IsVisible => _visible;
    public void SetSize(int width, int height) { _width = width; _height = height; }
    public int GetCursor() => _cursor;
    public string SearchQuery() => _searchInput.Value;

    /// <summary>Options filtered by the current search query. "Clear filter" is always retained.</summary>
    private List<TagOption> VisibleOptions()
    {
        string query = _searchInput.Value.Trim().ToLowerInvariant();
        if (query == "") return _options;
        return _options
            .Where(o => o.IsClear || o.Name.ToLowerInvariant().Contains(query))
            .ToList();
    }

    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;
        if (msg is not KeyMsg key) return null;

        switch (key.Key)
        {
            case "esc":
                _visible = false;
                _searchInput.Blur();
                return null;

            case "up":
                if (_cursor > 0) _cursor--;
                return null;

            case "down":
            {
                var opts = VisibleOptions();
                if (_cursor < opts.Count - 1) _cursor++;
                return null;
            }

            case " ":
            {
                // Space toggles selection in multi-select mode; otherwise fall through to search.
                if (!_multiSelect) break;
                var opts = VisibleOptions();
                if (opts.Count == 0 || _cursor >= opts.Count) return null;
                string name = opts[_cursor].Name;
                foreach (var o in _options)
                {
                    if (o.Name == name) { o.Selected = !o.Selected; break; }
                }
                return null;
            }

            case "enter":
            {
                var opts = VisibleOptions();
                if (_multiSelect)
                {
                    _visible = false;
                    _searchInput.Blur();
                    var chosen = _options.Where(o => o.Selected).Select(o => o.Name).ToList();
                    return Commands.Of(new TagsSelectedMsg(chosen));
                }
                if (opts.Count == 0 || _cursor >= opts.Count) return null;
                var selected = opts[_cursor];
                _visible = false;
                _searchInput.Blur();
                string tag = selected.IsClear ? "" : selected.Name;
                return Commands.Of(new TagSelectedMsg(tag));
            }
        }

        // Delegate other keys to the search input; reset cursor when the query changes.
        string prev = _searchInput.Value;
        _searchInput.HandleKey(key);
        if (_searchInput.Value != prev) _cursor = 0;
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;

        string titleText = _title == "" ? "Filter by Tag" : _title;
        string helpTextStr = _multiSelect
            ? "type to search • ↑/↓: navigate • space: toggle • enter: confirm • esc: cancel"
            : "type to search • ↑/↓: navigate • enter: select • esc: cancel";

        var opts = VisibleOptions();
        string searchView = _searchInput.View();

        int maxWidth = MinModalWidth;
        if (titleText.Length > maxWidth) maxWidth = titleText.Length;
        if (helpTextStr.Length > maxWidth) maxWidth = helpTextStr.Length;
        if (Ansi.Width(searchView) > maxWidth) maxWidth = Ansi.Width(searchView);

        foreach (var opt in opts)
        {
            int lineLen = $"> ● {opt.Name}".Length;
            if (lineLen > maxWidth) maxWidth = lineLen;
        }

        var optionList = new System.Text.StringBuilder();
        if (opts.Count == 0)
        {
            optionList.Append(Style.New()
                .Foreground(theme.ForegroundMuted)
                .Background(theme.Background)
                .Italic()
                .Width(maxWidth)
                .Render("  no matching tags")).Append('\n');
        }
        for (int i = 0; i < opts.Count; i++)
        {
            var opt = opts[i];
            string cursor = i == _cursor ? ">" : " ";

            string icon = "●";
            if (opt.IsClear) icon = "✕";
            if (_multiSelect) icon = opt.Selected ? "☑" : "☐";

            string line = $"{cursor} {icon} {opt.Name}";

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

        string searchBar = Style.New()
            .Background(theme.Background)
            .Width(maxWidth)
            .Render(searchView);

        string helpText = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Background(theme.Background)
            .Width(maxWidth)
            .Render(helpTextStr);

        string content = Layout.JoinVertical(HAlign.Left, title, "", searchBar, "", optionList.ToString(), helpText);

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
