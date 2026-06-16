using Azdo.Tui.Polling;
using Azdo.Tui.Rendering;

namespace Azdo.Tui.Components;

/// <summary>
/// Footer showing context/default keybindings, org/project, connection state,
/// filter labels, warnings and scroll percentage (≈ <c>components.StatusBar</c>).
/// Mutable, matching the Go pointer-based component.
/// </summary>
public sealed class StatusBar(Styles.Styles styles)
{
    private string _organization = "";
    private string _project = "";
    private ConnectionState _state = ConnectionState.Connecting;
    private string _keybindings = "";
    private double _scrollPercent;
    private bool _showScroll;
    private int _width;
    private string _errorMessage = "";
    private string _filterLabel = "";
    private string _updateMessage = "";
    private string _warningMessage = "";
    private IReadOnlyList<ContextItem> _contextItems = Array.Empty<ContextItem>();
    private string _contextStatus = "";

    public void SetOrganization(string org) => _organization = org;
    public void SetProject(string project) => _project = project;
    public ConnectionState GetState() => _state;
    public void SetState(ConnectionState state) => _state = state;
    public string GetWarningMessage() => _warningMessage;
    public void SetKeybindings(string bindings) => _keybindings = bindings;
    public void SetContextItems(IReadOnlyList<ContextItem> items) => _contextItems = items;
    public void ClearContextItems() { _contextItems = Array.Empty<ContextItem>(); _contextStatus = ""; }
    public void SetContextStatus(string status) => _contextStatus = status;
    public void SetWidth(int width) => _width = width;
    public void SetScrollPercent(double percent) => _scrollPercent = percent;
    public void ShowScrollPercent(bool show) => _showScroll = show;
    public void SetErrorMessage(string message) => _errorMessage = message;
    public void ClearErrorMessage() => _errorMessage = "";
    public void SetFilterLabel(string label) => _filterLabel = label;
    public void ClearFilterLabel() => _filterLabel = "";
    public void SetUpdateMessage(string message) => _updateMessage = message;
    public void SetWarningMessage(string message) => _warningMessage = message;
    public void ClearWarningMessage() => _warningMessage = "";

    public string View()
    {
        int width = _width < 40 ? 80 : _width;
        var sepStyle = Style.New().Foreground(styles.Theme.Border);
        var sep = sepStyle.Render(" │ ");

        var parts = new List<string>();

        if (_errorMessage != "" && _state == ConnectionState.Error)
            parts.Add(Style.New().Foreground(styles.Theme.Error).Bold().Render(_errorMessage));
        else
            parts.Add(RenderKeybindings());

        if (_contextStatus != "")
            parts.Add(Style.New().Foreground(styles.Theme.ForegroundMuted).Italic().Render(_contextStatus));

        if (_warningMessage != "")
            parts.Add(Style.New().Foreground(styles.Theme.Warning).Bold().Render("⚠ " + _warningMessage));

        if (_filterLabel != "")
            parts.Add(Style.New().Foreground(styles.Theme.Background).Background(styles.Theme.Accent).Bold().Padding(0, 1).Render(_filterLabel));

        if (_updateMessage != "")
            parts.Add(Style.New().Foreground(styles.Theme.Warning).Bold().Render(_updateMessage));

        var orgProj = RenderOrgProject();
        if (orgProj != "") parts.Add(orgProj);

        var scroll = RenderScrollPercent();
        if (scroll != "") parts.Add(scroll);

        parts.Add(RenderConnectionState());

        var content = string.Join(sep, parts);
        int boxInnerWidth = Math.Max(20, width - 2);
        return styles.BoxRounded.Width(boxInnerWidth).Render(content);
    }

    private string RenderKeybindings()
    {
        if (_contextItems.Count > 0) return RenderContextKeybindings();
        if (_keybindings != "") return _keybindings;

        var sep = Style.New().Foreground(styles.Theme.Border).Render(" • ");
        return styles.Key.Render("r") + styles.Description.Render(" refresh") + sep +
               styles.Key.Render("↑↓") + styles.Description.Render(" navigate") + sep +
               styles.Key.Render("enter") + styles.Description.Render(" details") + sep +
               styles.Key.Render("esc") + styles.Description.Render(" back") + sep +
               styles.Key.Render("?") + styles.Description.Render(" help") + sep +
               styles.Key.Render("q") + styles.Description.Render(" quit");
    }

    private string RenderContextKeybindings()
    {
        var sep = Style.New().Foreground(styles.Theme.Border).Render(" • ");
        var hasKey = _contextItems.Select(i => i.Key).ToHashSet();
        var parts = _contextItems
            .Select(i => styles.Key.Render(i.Key) + " " + styles.Description.Render(i.Description))
            .ToList();

        foreach (var (key, desc) in new[] { ("esc", "back"), ("?", "help"), ("q", "quit") })
            if (!hasKey.Contains(key))
                parts.Add(styles.Key.Render(key) + " " + styles.Description.Render(desc));

        return string.Join(sep, parts);
    }

    private string RenderOrgProject()
    {
        if (_organization == "" && _project == "") return "";
        var sep = Style.New().Foreground(styles.Theme.Border).Render("/");
        var s = Style.New().Foreground(styles.Theme.Secondary).Bold();
        if (_organization != "" && _project != "") return s.Render(_organization) + sep + s.Render(_project);
        return _organization != "" ? s.Render(_organization) : s.Render(_project);
    }

    private string RenderScrollPercent()
        => !_showScroll ? "" : styles.ScrollInfo.Render($"{_scrollPercent:0}%");

    private string RenderConnectionState() => _state switch
    {
        ConnectionState.Connected => styles.Connected.Render("●"),
        ConnectionState.Connecting => styles.Connecting.Render("◐ connecting"),
        ConnectionState.Disconnected => styles.Disconnected.Render("○ disconnected"),
        ConnectionState.Error => styles.ConnError.Render("✗ error"),
        _ => styles.Disconnected.Render($"? {_state}"),
    };
}
