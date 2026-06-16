using System.Text;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>A single keybinding entry (≈ <c>components.HelpBinding</c>).</summary>
public readonly record struct HelpBinding(string Key, string Description);

/// <summary>A group of related keybindings (≈ <c>components.HelpSection</c>).</summary>
public sealed class HelpSection
{
    public string Title { get; set; } = "";
    public List<HelpBinding> Bindings { get; set; } = new();
}

/// <summary>
/// An overlay that displays available keybindings (≈ <c>components.HelpModal</c>).
/// Toggle/close on "?", "q" or "esc".
/// </summary>
public sealed class HelpModal
{
    private const int MinModalWidth = 80; // shared with the pickers
    private const int ModalChromeRows = 8;
    private const string FooterHintBase = "Press esc, q, or ? to close";
    private const string FooterHintScrollSuffix = " • ↑↓ scroll";

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private readonly List<HelpSection> _sections;
    private string _configPath = "";
    private string _versionInfo = "";
    private int _scrollOffset;

    public HelpModal(StyleSet styles)
    {
        _styles = styles;
        _sections = new List<HelpSection>
        {
            new()
            {
                Title = "Navigation",
                Bindings = new()
                {
                    new("↑/k", "Move up"),
                    new("↓/j", "Move down"),
                    new("pgup/pgdn", "Page up / down"),
                    new("enter", "View details / expand"),
                    new("esc", "Go back"),
                },
            },
            new()
            {
                Title = "Tabs",
                Bindings = new()
                {
                    new("1/2/3", "PR / Work Items / Pipelines"),
                    new("←/→", "Previous / next tab"),
                },
            },
            new()
            {
                Title = "Actions",
                Bindings = new()
                {
                    new("f", "Search / filter"),
                    new("m", "Toggle my items (PRs / work items)"),
                    new("A", "Toggle as reviewer (PRs)"),
                    new("T", "Filter by tag (work items)"),
                    new("s", "Filter by state (work items)"),
                    new("S", "Filter by status (pipelines)"),
                    new("r", "Refresh data"),
                    new("v", "Vote on PR (detail view)"),
                    new("w", "Change work item state (detail view)"),
                    new("c", "Add comment (work item detail)"),
                    new("o", "Open in browser (PR / work item detail)"),
                    new("t", "Select theme"),
                    new("?", "Toggle help"),
                    new("q", "Quit application"),
                },
            },
            new()
            {
                Title = "Code Review (PR diff)",
                Bindings = new()
                {
                    new("c", "Create new comment"),
                    new("p", "Reply to nearest thread"),
                    new("x", "Resolve nearest thread"),
                    new("n", "Jump to next comment"),
                    new("N", "Jump to previous comment"),
                },
            },
            new()
            {
                Title = "Log Viewer (pipelines)",
                Bindings = new()
                {
                    new("g", "Go to top"),
                    new("G", "Go to bottom"),
                },
            },
        };
    }

    public void Show() { _visible = true; _scrollOffset = 0; }
    public void Hide() { _visible = false; _scrollOffset = 0; }
    public void Toggle() { _visible = !_visible; if (!_visible) _scrollOffset = 0; }
    public bool IsVisible => _visible;

    public void SetSize(int width, int height) { _width = width; _height = height; }
    public void SetConfigPath(string path) => _configPath = path;
    public void SetVersionInfo(string info) => _versionInfo = info;

    public void AddSection(string title, IEnumerable<HelpBinding> bindings)
        => _sections.Add(new HelpSection { Title = title, Bindings = bindings.ToList() });

    public void RemoveSection(string title)
        => _sections.RemoveAll(s => s.Title == title);

    /// <summary>Removes bindings from the Actions section whose description contains the substring.</summary>
    public void RemoveBindingsByDescription(string substr)
    {
        foreach (var section in _sections)
            if (section.Title == "Actions")
                section.Bindings.RemoveAll(b => ContainsSubstring(b.Description, substr));
    }

    private static bool ContainsSubstring(string s, string substr)
        => s.ToLowerInvariant().Contains(substr.ToLowerInvariant());

    /// <summary>Replaces the tab keys/descriptions in the Tabs section.</summary>
    public void UpdateTabsBinding(string keys, string description)
    {
        foreach (var section in _sections)
        {
            if (section.Title != "Tabs") continue;
            for (int j = 0; j < section.Bindings.Count; j++)
            {
                var b = section.Bindings[j];
                if (b.Key.Contains('/') && b.Description.Contains('/'))
                {
                    section.Bindings[j] = new HelpBinding(keys, description);
                    return;
                }
            }
        }
    }

    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;
        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "esc":
                case "q":
                case "?":
                    Hide();
                    return null;
                case "down":
                case "j":
                    ScrollBy(1);
                    return null;
                case "up":
                case "k":
                    ScrollBy(-1);
                    return null;
                case "pgdown":
                    ScrollBy(PageStep());
                    return null;
                case "pgup":
                    ScrollBy(-PageStep());
                    return null;
            }
        }
        return null;
    }

    private void ScrollBy(int delta)
    {
        _scrollOffset += delta;
        int max = MaxScrollOffset();
        if (_scrollOffset > max) _scrollOffset = max;
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    private int PageStep()
    {
        int step = BodyHeight() - 1;
        return step < 1 ? 1 : step;
    }

    private int MaxScrollOffset()
    {
        var lines = BodyLines(ContentWidth());
        int max = lines.Count - BodyHeight();
        return max < 0 ? 0 : max;
    }

    private int ContentWidth()
    {
        const string titleText = "⌨ Keyboard Shortcuts";
        string footerText = FooterHintBase + FooterHintScrollSuffix;

        int contentWidth = MinModalWidth;
        if (titleText.Length > contentWidth) contentWidth = titleText.Length;
        if (footerText.Length > contentWidth) contentWidth = footerText.Length;

        foreach (var section in _sections)
        {
            if (section.Title.Length > contentWidth) contentWidth = section.Title.Length;
            foreach (var binding in section.Bindings)
            {
                int lineLen = 12 + binding.Description.Length;
                if (lineLen > contentWidth) contentWidth = lineLen;
            }
        }
        return contentWidth;
    }

    private int BodyHeight()
    {
        if (_height <= 0) return BodyLines(ContentWidth()).Count;
        int avail = _height - ModalChromeRows;
        if (avail < 1) avail = 1;
        int full = BodyLines(ContentWidth()).Count;
        return avail > full ? full : avail;
    }

    private List<string> BodyLines(int contentWidth)
    {
        var theme = _styles.Theme;

        var helpSectionStyle = Style.New()
            .Foreground(theme.Secondary)
            .Bold()
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var helpKeyStyle = Style.New()
            .Foreground(theme.Accent)
            .Bold()
            .Width(12)
            .Background(theme.BackgroundAlt);

        var helpDescStyle = Style.New()
            .Foreground(theme.Foreground)
            .Background(theme.BackgroundAlt)
            .Width(contentWidth - 12);

        string blankLine = Style.New()
            .Width(contentWidth)
            .Background(theme.BackgroundAlt)
            .Render("");

        var lines = new List<string>();
        for (int i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];
            if (i > 0) lines.Add(blankLine);
            lines.Add(helpSectionStyle.Render(section.Title));
            foreach (var binding in section.Bindings)
                lines.Add(helpKeyStyle.Render(binding.Key) + helpDescStyle.Render(binding.Description));
        }

        if (_versionInfo != "" || _configPath != "")
        {
            var infoValueStyle = Style.New()
                .Foreground(theme.ForegroundMuted)
                .Background(theme.BackgroundAlt)
                .Width(contentWidth);

            lines.Add(blankLine);
            lines.Add(helpSectionStyle.Render("Info"));
            if (_versionInfo != "") lines.Add(infoValueStyle.Render("Version: " + _versionInfo));
            if (_configPath != "") lines.Add(infoValueStyle.Render("Config: " + _configPath));
        }
        return lines;
    }

    private string FooterText()
        => IsScrollable() ? FooterHintBase + FooterHintScrollSuffix : FooterHintBase;

    private bool IsScrollable()
    {
        if (_height <= 0) return false;
        int avail = _height - ModalChromeRows;
        return avail >= 1 && BodyLines(ContentWidth()).Count > avail;
    }

    public string View()
    {
        if (!_visible) return "";

        int contentWidth = ContentWidth();
        var theme = _styles.Theme;

        var helpModalStyle = Style.New()
            .WithBorder(Border.Rounded)
            .BorderForeground(theme.Accent)
            .Padding(1, 2)
            .Background(theme.BackgroundAlt);

        var helpTitleStyle = Style.New()
            .Foreground(theme.Accent)
            .Bold()
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var footerStyle = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var bodyAll = BodyLines(contentWidth);
        int bodyH = BodyHeight();
        if (_scrollOffset > bodyAll.Count - bodyH) _scrollOffset = bodyAll.Count - bodyH;
        if (_scrollOffset < 0) _scrollOffset = 0;
        int end = _scrollOffset + bodyH;
        if (end > bodyAll.Count) end = bodyAll.Count;
        var bodyVisible = bodyAll.GetRange(_scrollOffset, end - _scrollOffset);

        var content = new StringBuilder();
        // MarginBottom(1) on the title is emulated with a blank line.
        content.Append(helpTitleStyle.Render("⌨ Keyboard Shortcuts"));
        content.Append('\n');
        content.Append('\n');
        content.Append(string.Join("\n", bodyVisible));
        content.Append("\n\n");
        content.Append(footerStyle.Render(FooterText()));

        var modal = helpModalStyle.Render(content.ToString());

        if (_width > 0 && _height > 0)
            return Layout.Place(_width, _height, HAlign.Center, VAlign.Center, modal);

        return modal;
    }
}
