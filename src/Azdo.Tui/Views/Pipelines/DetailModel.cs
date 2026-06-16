using System.Text;
using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.Pipelines;

/// <summary>Carries a fetched build timeline or an error (≈ <c>timelineMsg</c>).</summary>
public sealed record TimelineMsg(Timeline? Timeline, Exception? Err) : IMsg;

/// <summary>
/// A node in the timeline tree with its children (≈ <c>pipelines.TimelineNode</c>).
/// </summary>
public sealed class TimelineNode
{
    public required TimelineRecord Record { get; init; }
    public List<TimelineNode> Children { get; } = new();
    /// <summary>Depth in the displayed tree (skips filtered types).</summary>
    public int VisualDepth { get; set; }
    public bool Expanded { get; set; }

    /// <summary>True if the node has visible (non-filtered) descendants.</summary>
    public bool HasChildren() => HasVisibleChildren(Children);

    private static bool HasVisibleChildren(IReadOnlyList<TimelineNode> nodes)
    {
        foreach (var child in nodes)
        {
            if (!DetailModel.IsFilteredRecordType(child.Record.Type)) return true;
            if (HasVisibleChildren(child.Children)) return true;
        }
        return false;
    }
}

/// <summary>
/// The pipeline detail view showing an expandable timeline tree of
/// stages → jobs → tasks (≈ <c>pipelines.DetailModel</c>). Implements
/// <see cref="IDetailView"/> so it plugs into <see cref="ListView{T}"/>.
/// </summary>
public sealed class DetailModel : IDetailView
{
    private const int HeaderLines = 2; // title + separator

    private readonly IAzdoClient? _client;
    private readonly PipelineRun _run;
    private readonly StyleSet _styles;
    private readonly LoadingIndicator _spinner;
    private readonly TextInput _searchInput = new() { Prompt = "/ ", CharLimit = 100 };

    private Timeline? _timeline;
    private List<TimelineNode> _tree = new();
    private List<TimelineNode> _flatItems = new();
    private List<TimelineNode>? _allFlatItems; // unfiltered, set while searching
    private int _selectedIndex;
    private bool _searching;
    private string _searchQuery = "";
    private bool _loading;
    private Exception? _err;
    private int _width;
    private int _height;
    private int _viewportHeight = 1;
    private int _yOffset;
    private bool _ready;

    public DetailModel(IAzdoClient? client, PipelineRun run, StyleSet styles)
    {
        _client = client;
        _run = run;
        _styles = styles;
        _spinner = new LoadingIndicator(styles);
        _spinner.SetMessage($"Loading timeline for {run.Definition.Name} #{run.BuildNumber}...");
    }

    /// <summary>Initializes the model and fetches the timeline.</summary>
    public Cmd? Init()
    {
        _loading = true;
        _spinner.SetVisible(true);
        return Commands.Batch(FetchTimeline(), _spinner.Init());
    }

    /// <summary>Sets the timeline data directly (used by polling/tests).</summary>
    public void SetTimeline(Timeline? timeline)
    {
        _timeline = timeline;
        _tree = BuildTimelineTree(timeline);
        _flatItems = FlattenTree(_tree);
        _selectedIndex = 0;
        _yOffset = 0;
    }

    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
        int vh = height - HeaderLines;
        _viewportHeight = vh < 1 ? 1 : vh;
        _ready = true;
        EnsureSelectedVisible();
    }

    public Cmd? Update(IMsg msg)
    {
        switch (msg)
        {
            case WindowSizeMsg ws:
                SetSize(ws.Width, ws.Height);
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case TimelineMsg tm:
                _loading = false;
                _spinner.SetVisible(false);
                if (tm.Err is not null) { _err = tm.Err; return null; }
                SetTimeline(tm.Timeline);
                return null;

            case KeyMsg key:
                if (_searching) return UpdateSearch(key);
                switch (key.Key)
                {
                    case "up": case "k": MoveUp(); break;
                    case "down": case "j": MoveDown(); break;
                    case "pgup": PageUp(); break;
                    case "pgdown": PageDown(); break;
                    case "f":
                        if (_flatItems.Count > 0) { EnterSearch(); return null; }
                        break;
                    case "r":
                        _loading = true;
                        _spinner.SetVisible(true);
                        return Commands.Batch(FetchTimeline(), _spinner.Tick());
                }
                return null;
        }
        return null;
    }

    private Cmd? UpdateSearch(KeyMsg key)
    {
        switch (key.Key)
        {
            case "esc": ExitSearch(); return null;
            case "enter": return null; // parent handles expand/collapse
            case "up": case "k": MoveUp(); return null;
            case "down": case "j": MoveDown(); return null;
            case "pgup": PageUp(); return null;
            case "pgdown": PageDown(); return null;
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

    public string View()
    {
        string Wrap(string content) => Style.New().Width(_width).Render(content);

        if (_err is not null)
            return Wrap($"Error loading timeline: {_err.Message}\n\nPress r to retry, Esc to go back");
        if (_loading)
            return Wrap(_spinner.View());
        if (_timeline is null || _flatItems.Count == 0)
            return Wrap("No timeline data available.\n\nPress r to refresh, Esc to go back");

        var sb = new StringBuilder();
        sb.Append(_styles.Header.Render($"{_run.Definition.Name} #{_run.BuildNumber}"));
        sb.Append('\n');
        sb.Append(new string('─', Math.Min(Math.Max(_width - 2, 0), 60)));
        sb.Append('\n');
        sb.Append(ViewportView());

        if (_searching)
        {
            int total = _allFlatItems?.Count ?? 0;
            int matched = _flatItems.Count;
            sb.Append('\n');
            sb.Append($"{_searchInput.View()} {matched}/{total}");
        }

        return Wrap(sb.ToString());
    }

    private string ViewportView()
    {
        if (_flatItems.Count == 0) return string.Empty;
        int end = Math.Min(_flatItems.Count, _yOffset + _viewportHeight);
        var lines = new List<string>();
        for (int i = _yOffset; i < end; i++)
            lines.Add(RenderRecord(_flatItems[i], i == _selectedIndex));
        return string.Join("\n", lines);
    }

    private string RenderRecord(TimelineNode node, bool selected)
    {
        string indent = new string(' ', node.VisualDepth * 2);
        string icon = RecordIcon(node.Record.State, node.Record.Result, _styles);
        string duration = FormatRecordDuration(node.Record.StartTime, node.Record.FinishTime);

        string expand = " ";
        if (node.HasChildren()) expand = node.Expanded ? "▼" : "▶";

        string line = $"{indent}{icon} {expand} {node.Record.Name}";
        if (duration != "-") line = $"{line} ({duration})";
        if (node.Record.Log is not null) line = $"{line} 📄";

        return selected ? _styles.Selected.Render(line) : line;
    }

    public int SelectedIndex() => _selectedIndex;

    public TimelineNode? SelectedItem()
        => _flatItems.Count == 0 || _selectedIndex >= _flatItems.Count ? null : _flatItems[_selectedIndex];

    public void MoveUp()
    {
        if (_selectedIndex > 0) { _selectedIndex--; EnsureSelectedVisible(); }
    }

    public void MoveDown()
    {
        if (_selectedIndex < _flatItems.Count - 1) { _selectedIndex++; EnsureSelectedVisible(); }
    }

    /// <summary>Toggles the expanded state of the selected node.</summary>
    public void ToggleExpand()
    {
        var selected = SelectedItem();
        if (selected is null || !selected.HasChildren()) return;

        selected.Expanded = !selected.Expanded;
        if (selected.Expanded) ExpandFilteredChildren(selected.Children);

        _flatItems = FlattenTree(_tree);
        if (_selectedIndex >= _flatItems.Count) _selectedIndex = _flatItems.Count - 1;
        EnsureSelectedVisible();
    }

    public void PageUp()
    {
        if (_flatItems.Count == 0) return;
        int page = Math.Max(1, _viewportHeight);
        _selectedIndex = Math.Max(0, _selectedIndex - page);
        EnsureSelectedVisible();
    }

    public void PageDown()
    {
        if (_flatItems.Count == 0) return;
        int page = Math.Max(1, _viewportHeight);
        _selectedIndex = Math.Min(_flatItems.Count - 1, _selectedIndex + page);
        EnsureSelectedVisible();
    }

    private void EnsureSelectedVisible()
    {
        if (_flatItems.Count == 0) return;
        if (_selectedIndex < _yOffset) _yOffset = _selectedIndex;
        else if (_selectedIndex > _yOffset + _viewportHeight - 1)
            _yOffset = _selectedIndex - _viewportHeight + 1;
        if (_yOffset < 0) _yOffset = 0;
    }

    /// <summary>True if the selected item has logs that can be viewed.</summary>
    public bool CanViewLogs()
    {
        var selected = SelectedItem();
        return selected is not null && selected.Record.Log is not null;
    }

    public string GetStatusMessage()
    {
        var selected = SelectedItem();
        if (selected is null) return "";
        if (selected.Record.Log is null) return $"{selected.Record.Type} has no logs";
        return "";
    }

    public PipelineRun GetRun() => _run;

    public IReadOnlyList<ContextItem> GetContextItems() => new[]
    {
        new ContextItem("↑↓/pgup/pgdn", "navigate"),
        new ContextItem("enter", "expand/collapse or view logs"),
    };

    public double GetScrollPercent()
        => !_ready || _flatItems.Count <= 1 ? 0 : (double)_selectedIndex / (_flatItems.Count - 1) * 100;

    // ---- search ----

    public bool IsSearching() => _searching;

    public void EnterSearch()
    {
        _searching = true;
        _searchInput.Value = "";
        _searchQuery = "";
        _allFlatItems = AllTreeNodes(_tree);
        _searchInput.Focus();
    }

    public void ExitSearch()
    {
        _searching = false;
        _searchQuery = "";
        _searchInput.Blur();
        _flatItems = FlattenTree(_tree);
        _allFlatItems = null;
        _selectedIndex = 0;
        _yOffset = 0;
    }

    public void SetSearchQuery(string query)
    {
        _searchQuery = query;
        _searchInput.Value = query;
        _allFlatItems = AllTreeNodes(_tree);
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        if (_searchQuery == "")
        {
            _flatItems = FlattenTree(_tree);
        }
        else
        {
            var all = AllTreeNodes(_tree);
            string q = _searchQuery.ToLowerInvariant();
            _flatItems = all.Where(n => n.Record.Name.ToLowerInvariant().Contains(q)).ToList();
        }
        _selectedIndex = 0;
        _yOffset = 0;
    }

    // Test/inspection helpers.
    public IReadOnlyList<TimelineNode> FlatItems => _flatItems;

    private Cmd FetchTimeline() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new TimelineMsg(null, null);
        try
        {
            var timeline = await _client.GetBuildTimelineAsync(_run.Id).ConfigureAwait(false);
            return new TimelineMsg(timeline, null);
        }
        catch (Exception e)
        {
            return new TimelineMsg(null, e);
        }
    });

    // ---- static helpers ----

    /// <summary>Icon for a timeline record based on state/result (≈ <c>recordIconWithStyles</c>).</summary>
    public static string RecordIcon(string state, string result, StyleSet s)
    {
        string st = state.ToLowerInvariant();
        string r = result.ToLowerInvariant();
        if (st == "inprogress") return s.Info.Render("●");
        if (st == "pending") return s.Muted.Render("○");
        if (r == "succeeded") return s.Success.Render("✓");
        if (r == "succeededwithissues") return s.Warning.Render("◐");
        if (r == "failed") return s.Error.Render("✗");
        if (r is "canceled" or "skipped" or "abandoned") return s.Muted.Render("○");
        return s.Muted.Render("○");
    }

    /// <summary>Formats a timeline record's duration (≈ <c>formatRecordDuration</c>).</summary>
    public static string FormatRecordDuration(DateTime? startTime, DateTime? finishTime)
    {
        if (startTime is null || finishTime is null) return "-";
        return Format.Duration(finishTime.Value - startTime.Value);
    }

    /// <summary>True for intermediary Azure DevOps types hidden in the UI (Phase, Checkpoint).</summary>
    public static bool IsFilteredRecordType(string recordType)
        => recordType == "Phase" || recordType == "Checkpoint";

    /// <summary>Builds a tree from flat timeline records, sorted by Order.</summary>
    public static List<TimelineNode> BuildTimelineTree(Timeline? timeline)
    {
        if (timeline is null || timeline.Records.Count == 0) return new();

        var nodeMap = new Dictionary<string, TimelineNode>();
        foreach (var record in timeline.Records)
            nodeMap[record.Id] = new TimelineNode { Record = record };

        var roots = new List<TimelineNode>();
        foreach (var node in nodeMap.Values)
        {
            if (node.Record.ParentId is null)
                roots.Add(node);
            else if (nodeMap.TryGetValue(node.Record.ParentId, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node); // orphan → root
        }

        SortNodes(roots);
        foreach (var root in roots) SortNodesRecursive(root);
        return roots;
    }

    private static void SortNodes(List<TimelineNode> nodes)
        => nodes.Sort((a, b) => a.Record.Order.CompareTo(b.Record.Order));

    private static void SortNodesRecursive(TimelineNode node)
    {
        SortNodes(node.Children);
        foreach (var child in node.Children) SortNodesRecursive(child);
    }

    /// <summary>Flattens the tree depth-first, skipping filtered types.</summary>
    public static List<TimelineNode> FlattenTree(IReadOnlyList<TimelineNode> roots)
    {
        var result = new List<TimelineNode>();
        foreach (var root in roots) FlattenNodeAtDepth(root, 0, result);
        return result;
    }

    private static void FlattenNodeAtDepth(TimelineNode node, int visualDepth, List<TimelineNode> result)
    {
        if (IsFilteredRecordType(node.Record.Type))
        {
            foreach (var child in node.Children) FlattenNodeAtDepth(child, visualDepth, result);
            return;
        }
        node.VisualDepth = visualDepth;
        result.Add(node);
        if (node.Expanded)
            foreach (var child in node.Children) FlattenNodeAtDepth(child, visualDepth + 1, result);
    }

    /// <summary>Auto-expands filtered intermediary nodes so visible children become reachable.</summary>
    private static void ExpandFilteredChildren(IReadOnlyList<TimelineNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (IsFilteredRecordType(node.Record.Type))
            {
                node.Expanded = true;
                ExpandFilteredChildren(node.Children);
            }
        }
    }

    /// <summary>Every node regardless of expand state, skipping filtered types.</summary>
    private static List<TimelineNode> AllTreeNodes(IReadOnlyList<TimelineNode> roots)
    {
        var result = new List<TimelineNode>();
        void Walk(IReadOnlyList<TimelineNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                if (IsFilteredRecordType(node.Record.Type)) { Walk(node.Children, depth); continue; }
                node.VisualDepth = depth;
                result.Add(node);
                Walk(node.Children, depth + 1);
            }
        }
        Walk(roots, 0);
        return result;
    }
}
