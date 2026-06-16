using System.Text;
using System.Text.RegularExpressions;
using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.Pipelines;

/// <summary>Carries fetched log content or an error (≈ <c>logContentMsg</c>).</summary>
public sealed record LogContentMsg(string Content, Exception? Err) : IMsg;

/// <summary>
/// A scrollable per-task log viewer (≈ <c>pipelines.LogViewerModel</c>). Fetches
/// the log via <see cref="IAzdoClient.GetBuildLogContentAsync"/>, strips Azure
/// DevOps timestamps, and supports up/down/pgup/pgdown/g/G scrolling plus an
/// inline search ('f') that filters/highlights matching lines.
/// </summary>
public sealed class LogViewerModel
{
    private const int HeaderLines = 2; // title + separator

    // Matches Azure DevOps log timestamps like "2024-02-06T10:00:00.000Z ".
    private static readonly Regex TimestampRegex =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s*", RegexOptions.Compiled);

    private readonly IAzdoClient? _client;
    private readonly int _buildId;
    private readonly int _logId;
    private readonly string _title;
    private readonly StyleSet _styles;
    private readonly LoadingIndicator _spinner;
    private readonly TextInput _searchInput = new() { Prompt = "/ ", CharLimit = 100 };

    private string _content = "";
    private List<string> _lines = new();   // formatted (timestamp-stripped) lines
    private List<string> _visible = new();  // currently displayed lines (filtered when searching)
    private bool _loading;
    private Exception? _err;
    private int _width;
    private int _height;
    private int _viewportHeight = 1;
    private int _yOffset;
    private bool _ready;
    private bool _searching;
    private string _searchQuery = "";

    public LogViewerModel(IAzdoClient? client, int buildId, int logId, string title, StyleSet styles)
    {
        _client = client;
        _buildId = buildId;
        _logId = logId;
        _title = title;
        _styles = styles;
        _loading = true;
        _spinner = new LoadingIndicator(styles);
        _spinner.SetMessage($"Loading log for {title}...");
    }

    public Cmd? Init()
    {
        _spinner.SetVisible(true);
        return Commands.Batch(FetchLogContent(), _spinner.Init());
    }

    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
        int vh = height - HeaderLines;
        _viewportHeight = vh < 1 ? 1 : vh;
        _ready = true;
        ClampOffset();
    }

    /// <summary>Sets the log content and resets scroll to the top.</summary>
    public void SetContent(string content)
    {
        _content = content;
        _loading = false;
        _lines = FormatLogLines(content);
        _visible = _lines;
        _yOffset = 0;
    }

    public void SetError(string message)
    {
        _err = new Exception(message);
        _loading = false;
    }

    public string GetContent() => _content;
    public string GetTitle() => _title;
    public int GetBuildId() => _buildId;
    public int GetLogId() => _logId;
    public bool IsLoading() => _loading;
    public Exception? GetError() => _err;

    public Cmd? Update(IMsg msg)
    {
        switch (msg)
        {
            case WindowSizeMsg ws:
                SetSize(ws.Width, ws.Height);
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case LogContentMsg lm:
                _loading = false;
                _spinner.SetVisible(false);
                if (lm.Err is not null) { _err = lm.Err; return null; }
                SetContent(lm.Content);
                return null;

            case KeyMsg key:
                if (_searching) return UpdateSearch(key);
                switch (key.Key)
                {
                    case "r":
                        _loading = true;
                        _spinner.SetVisible(true);
                        _err = null;
                        return Commands.Batch(FetchLogContent(), _spinner.Tick());
                    case "g": _yOffset = 0; break;
                    case "G": GotoBottom(); break;
                    case "up": case "k": ScrollBy(-1); break;
                    case "down": case "j": ScrollBy(1); break;
                    case "pgup": ScrollBy(-_viewportHeight); break;
                    case "pgdown": ScrollBy(_viewportHeight); break;
                    case "f":
                        EnterSearch();
                        return null;
                }
                return null;
        }
        return null;
    }

    // NOTE: search is simplified versus a full incremental highlighter: it filters
    // the visible lines to those containing the query (case-insensitive) and
    // highlights matches via the Selected style. 'esc' restores the full log.
    private Cmd? UpdateSearch(KeyMsg key)
    {
        switch (key.Key)
        {
            case "esc": ExitSearch(); return null;
            case "enter": return null;
            case "up": case "k": ScrollBy(-1); return null;
            case "down": case "j": ScrollBy(1); return null;
            case "pgup": ScrollBy(-_viewportHeight); return null;
            case "pgdown": ScrollBy(_viewportHeight); return null;
        }
        _searchInput.HandleKey(key);
        var newQuery = _searchInput.Value;
        if (newQuery != _searchQuery)
        {
            _searchQuery = newQuery;
            ApplySearchFilter();
        }
        return null;
    }

    public bool IsSearching() => _searching;

    private void EnterSearch()
    {
        _searching = true;
        _searchInput.Value = "";
        _searchQuery = "";
        _searchInput.Focus();
        _yOffset = 0;
    }

    private void ExitSearch()
    {
        _searching = false;
        _searchQuery = "";
        _searchInput.Blur();
        _visible = _lines;
        _yOffset = 0;
    }

    private void ApplySearchFilter()
    {
        if (_searchQuery == "")
        {
            _visible = _lines;
        }
        else
        {
            string q = _searchQuery.ToLowerInvariant();
            _visible = _lines
                .Where(l => l.ToLowerInvariant().Contains(q))
                .Select(l => _styles.Selected.Render(l))
                .ToList();
        }
        _yOffset = 0;
    }

    public string View()
    {
        string Wrap(string content) => Style.New().Width(_width).Render(content);

        if (_err is not null)
            return Wrap($"Error loading log: {_err.Message}\n\nPress r to retry, Esc to go back");
        if (_loading)
            return Wrap(_spinner.View());
        if (_content == "")
            return Wrap($"Log: {_title}\n\nNo log content available.\n\nPress Esc to go back");

        var sb = new StringBuilder();
        sb.Append(_styles.Header.Render($"Log: {_title}"));
        sb.Append('\n');
        sb.Append(new string('─', Math.Min(Math.Max(_width - 2, 0), 60)));
        sb.Append('\n');
        sb.Append(ViewportView());

        if (_searching)
        {
            sb.Append('\n');
            sb.Append($"{_searchInput.View()} {_visible.Count}/{_lines.Count}");
        }

        return Wrap(sb.ToString());
    }

    private string ViewportView()
    {
        ClampOffset();
        if (_visible.Count == 0) return string.Empty;
        int end = Math.Min(_visible.Count, _yOffset + _viewportHeight);
        var lines = new List<string>();
        for (int i = _yOffset; i < end; i++) lines.Add(_visible[i]);
        // Pad to full viewport height so View() output matches the requested height.
        while (lines.Count < _viewportHeight) lines.Add("");
        return string.Join("\n", lines);
    }

    private void ScrollBy(int delta)
    {
        _yOffset += delta;
        ClampOffset();
    }

    private void GotoBottom()
    {
        _yOffset = Math.Max(0, _visible.Count - _viewportHeight);
    }

    private void ClampOffset()
    {
        int maxOffset = Math.Max(0, _visible.Count - _viewportHeight);
        if (_yOffset > maxOffset) _yOffset = maxOffset;
        if (_yOffset < 0) _yOffset = 0;
    }

    public IReadOnlyList<ContextItem> GetContextItems() => new[]
    {
        new ContextItem("↑↓/pgup/pgdn", "scroll"),
        new ContextItem("g/G", "top/bottom"),
    };

    public double GetScrollPercent()
    {
        if (!_ready) return 0;
        int maxOffset = Math.Max(0, _visible.Count - _viewportHeight);
        if (maxOffset == 0) return 0;
        return (double)_yOffset / maxOffset * 100;
    }

    private Cmd FetchLogContent() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new LogContentMsg("", null);
        try
        {
            var content = await _client.GetBuildLogContentAsync(_buildId, _logId).ConfigureAwait(false);
            return new LogContentMsg(content, null);
        }
        catch (Exception e)
        {
            return new LogContentMsg("", e);
        }
    });

    // ---- static helpers ----

    /// <summary>Splits content into lines and strips timestamps (≈ <c>formatLogLines</c>).</summary>
    public static List<string> FormatLogLines(string content)
    {
        if (content == "") return new();
        var raw = content.Split('\n');
        var lines = raw.Select(StripTimestamp).ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Removes the Azure DevOps timestamp prefix from a log line.</summary>
    public static string StripTimestamp(string line) => TimestampRegex.Replace(line, "");
}
