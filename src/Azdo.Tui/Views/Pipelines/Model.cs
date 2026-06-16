using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Polling;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.Pipelines;

/// <summary>The pipeline-specific view mode (list / detail timeline / log viewer).</summary>
public enum PipelinesViewMode { List, Detail, Logs }

/// <summary>Directly sets the pipeline runs, e.g. from polling (≈ <c>pipelines.SetRunsMsg</c>).</summary>
public sealed record SetRunsMsg(List<PipelineRun> Runs) : IMsg;

/// <summary>
/// The Pipelines tab (≈ <c>pipelines.Model</c>): a recent-runs list with a status
/// filter picker, an expandable timeline detail view, and a nested log viewer.
/// Implements <see cref="ITabView"/>; data is driven by the app poller via
/// <see cref="SetRunsMsg"/> / <see cref="PipelineRunsUpdated"/>.
/// </summary>
public sealed class Model : ITabView
{
    private readonly ListView<PipelineRun> _list;
    private readonly MultiClient? _client;
    private readonly StyleSet _styles;
    private readonly ListPicker _statusPicker;

    private LogViewerModel? _logViewer;
    private PipelinesViewMode _viewMode = PipelinesViewMode.List;
    private int _width = 80;
    private int _height = 24;
    private string _activeStatus = "";
    private List<PipelineRun> _allRuns = new();

    public Model(MultiClient? client) : this(client, StyleSet.Default()) { }

    public Model(MultiClient? client, StyleSet styles)
    {
        _client = client;
        _styles = styles;
        _statusPicker = new ListPicker(styles);

        bool isMulti = client is not null && client.IsMultiProject();

        var baseColumns = new List<ColumnSpec>
        {
            new("Status", 10, 10),
            new("Pipeline", 12, 15),
            new("Branch", 20, 10),
            new("Build", 24, 8),
            new("Timestamp", 15, 16),
            new("Duration", 10, 8),
        };

        if (isMulti)
            baseColumns.Insert(0, new ColumnSpec("Project", 12, 10));

        var columns = ListView<PipelineRun>.NormalizeWidths(baseColumns);

        var config = new ListConfig<PipelineRun>
        {
            Columns = columns,
            LoadingMessage = "Loading pipeline runs...",
            EntityName = "pipeline runs",
            MinWidth = 50,
            ToRows = isMulti ? RunsToRowsMulti : RunsToRows,
            Fetch = FetchPipelineRuns,
            EnterDetail = (item, st, w, h) =>
            {
                IAzdoClient? projectClient = client?.ClientFor(item.ProjectName);
                var d = new DetailModel(projectClient, item, st);
                d.SetSize(w, h);
                return (d, d.Init());
            },
            HasContextBar = mode => mode == ViewMode.Detail,
            FilterFunc = isMulti ? FilterPipelineRunMulti : FilterPipelineRun,
        };

        _list = new ListView<PipelineRun>(config, styles);
    }

    // NOTE: the poller drives data via SetRunsMsg/PipelineRunsUpdated; the list's
    // own Fetch is a no-op command (matches list.go's nil-returning Fetch).
    private Cmd FetchPipelineRuns() => Commands.FromFunc(() => null);

    public Cmd? Init() => _list.Init();

    public Cmd? Update(IMsg msg)
    {
        if (msg is WindowSizeMsg w) { _width = w.Width; _height = w.Height; _statusPicker.SetSize(w.Width, w.Height); }

        // Domain-specific messages.
        switch (msg)
        {
            case PipelineRunsUpdated u:
                var critical = ErrorClassifier.NewCriticalErrorCmd(u.Err);
                if (critical is not null)
                {
                    _list.HandleFetchResult(null, null);
                    return critical;
                }
                if (u.Err is PartialException)
                {
                    _allRuns = u.Runs;
                    _list.HandleFetchResult(ApplyStatusFilter(u.Runs), null);
                    return null;
                }
                _allRuns = u.Runs;
                _list.HandleFetchResult(ApplyStatusFilter(u.Runs), u.Err);
                return null;

            case SetRunsMsg sr:
                _allRuns = sr.Runs;
                _list.SetItems(ApplyStatusFilter(sr.Runs));
                return null;

            case ListPickerSelectedMsg sel:
                _activeStatus = sel.Value;
                _statusPicker.Hide();
                _list.SetItems(ApplyStatusFilter(_allRuns));
                return null;
        }

        // Route all input to the status picker while it is visible.
        if (_statusPicker.IsVisible)
        {
            if (msg is KeyMsg) return _statusPicker.Update(msg);
            return null;
        }

        // Route by pipeline-specific view mode.
        switch (_viewMode)
        {
            case PipelinesViewMode.Logs:
                return UpdateLogViewer(msg);
            case PipelinesViewMode.Detail:
                return UpdateDetail(msg);
            default:
                if (msg is KeyMsg { Key: "S" } && !_list.IsSearching && _viewMode == PipelinesViewMode.List)
                {
                    var options = GetPipelineStatuses()
                        .Select(s => new ListPickerOption { Name = s.Name, Icon = s.Icon });
                    _statusPicker.SetConfig("Filter by Status", options, _activeStatus, true);
                    _statusPicker.Show();
                    return null;
                }
                var cmd = _list.Update(msg);
                _viewMode = _list.GetViewMode() == ViewMode.Detail
                    ? PipelinesViewMode.Detail
                    : PipelinesViewMode.List;
                return cmd;
        }
    }

    private Cmd? UpdateDetail(IMsg msg)
    {
        if (msg is KeyMsg key)
        {
            var detail = _list.Detail as DetailModel;

            // While the detail view is searching, let it handle all keys except enter.
            if (detail is not null && detail.IsSearching() && key.Key != "enter")
                return _list.Update(msg);

            switch (key.Key)
            {
                case "enter":
                    if (detail is not null)
                    {
                        var selected = detail.SelectedItem();
                        if (selected is not null && selected.HasChildren())
                        {
                            detail.ToggleExpand();
                            return null;
                        }
                        return EnterLogView(detail);
                    }
                    return null;
                case "esc":
                    var cmd = _list.Update(msg);
                    _viewMode = PipelinesViewMode.List;
                    return cmd;
            }
        }
        return _list.Update(msg);
    }

    private Cmd? UpdateLogViewer(IMsg msg)
    {
        if (_logViewer is null) { _viewMode = PipelinesViewMode.Detail; return null; }

        if (msg is KeyMsg { Key: "esc" })
        {
            _viewMode = PipelinesViewMode.Detail;
            _logViewer = null;
            return null;
        }
        return _logViewer.Update(msg);
    }

    private Cmd? EnterLogView(DetailModel detail)
    {
        var selected = detail.SelectedItem();
        if (selected is null || selected.Record.Log is null) return null;

        var run = detail.GetRun();
        IAzdoClient? projectClient = _client?.ClientFor(run.ProjectName);
        _logViewer = new LogViewerModel(projectClient, run.Id, selected.Record.Log.Id, selected.Record.Name, _styles);
        _logViewer.SetSize(_width, _height);
        _viewMode = PipelinesViewMode.Logs;
        return _logViewer.Init();
    }

    public string View()
        => _viewMode == PipelinesViewMode.Logs && _logViewer is not null ? _logViewer.View() : _list.View();

    // ---- ITabView surface ----

    public bool IsSearching()
    {
        if (_list.IsSearching) return true;
        if (_viewMode == PipelinesViewMode.Detail && _list.Detail is DetailModel d) return d.IsSearching();
        if (_viewMode == PipelinesViewMode.Logs && _logViewer is not null) return _logViewer.IsSearching();
        return false;
    }

    public bool IsCapturingInput() => _statusPicker.IsVisible;

    public bool HasContextBar()
    {
        if (_viewMode == PipelinesViewMode.Logs) return true;
        return _list.HasContextBar();
    }

    public IReadOnlyList<ContextItem> GetContextItems()
    {
        if (_viewMode == PipelinesViewMode.Logs && _logViewer is not null) return _logViewer.GetContextItems();
        return _list.GetContextItems();
    }

    public double GetScrollPercent()
    {
        if (_viewMode == PipelinesViewMode.Logs && _logViewer is not null) return _logViewer.GetScrollPercent();
        return _list.GetScrollPercent();
    }

    public string GetStatusMessage() => _list.GetStatusMessage();

    public string FilterLabel()
        => IsStatusFilterActive() ? $"status: {_activeStatus}" : "";

    public string DefaultKeybindings()
    {
        string Sep() => _styles.Border.Render(" • ");
        string Bind(string key, string desc) => _styles.Key.Render(key) + _styles.Description.Render(" " + desc);
        return string.Join(Sep(), new[]
        {
            Bind("r", "refresh"),
            Bind("↑↓", "navigate"),
            Bind("enter", "details"),
            Bind("f", "search"),
            Bind("S", "status"),
            Bind("esc", "back"),
            Bind("?", "help"),
            Bind("q", "quit"),
        });
    }

    // ---- pipelines-specific accessors ----

    public PipelinesViewMode GetViewMode() => _viewMode;
    public bool IsStatusFilterActive() => _activeStatus != "";
    public string ActiveStatus() => _activeStatus;
    public bool IsStatusPickerVisible() => _statusPicker.IsVisible;
    public string StatusPickerView() => _statusPicker.View();
    public void SetStatusPickerSize(int width, int height) => _statusPicker.SetSize(width, height);

    /// <summary>The detail view, when in detail/logs mode (for tests/inspection).</summary>
    public DetailModel? Detail() => _list.Detail as DetailModel;
    /// <summary>The active log viewer, when in logs mode (for tests/inspection).</summary>
    public LogViewerModel? LogViewer() => _logViewer;

    private List<PipelineRun> ApplyStatusFilter(List<PipelineRun> runs)
    {
        if (_activeStatus == "") return runs;
        return runs.Where(r => GetStatusKey(r.Status, r.Result) == _activeStatus).ToList();
    }

    // ---- row builders / filters / status helpers ----

    /// <summary>Colored status icon from status/result (≈ <c>statusIconWithStyles</c>).</summary>
    public static string StatusIcon(string status, string result, StyleSet s)
    {
        string st = status.ToLowerInvariant();
        string r = result.ToLowerInvariant();
        if (st == "inprogress") return s.Info.Render("● Running");
        if (st == "notstarted") return s.Info.Render("○ Queued");
        if (st == "canceling") return s.Warning.Render("⊘ Cancel");
        if (r == "succeeded") return s.Success.Render("✓ Success");
        if (r == "failed") return s.Error.Render("✗ Failed");
        if (r == "canceled") return s.Muted.Render("○ Cancel");
        if (r == "partiallysucceeded") return s.Warning.Render("◐ Partial");
        return s.Muted.Render($"{status}/{result}");
    }

    public static List<string[]> RunsToRows(IReadOnlyList<PipelineRun> items, StyleSet s)
        => items.Select(run => new[]
        {
            StatusIcon(run.Status, run.Result, s),
            run.Definition.Name,
            run.BranchShortName(),
            run.BuildNumber,
            run.Timestamp(),
            run.Duration(),
        }).ToList();

    public static List<string[]> RunsToRowsMulti(IReadOnlyList<PipelineRun> items, StyleSet s)
        => items.Select(run => new[]
        {
            run.ProjectDisplayName,
            StatusIcon(run.Status, run.Result, s),
            run.Definition.Name,
            run.BranchShortName(),
            run.BuildNumber,
            run.Timestamp(),
            run.Duration(),
        }).ToList();

    public static bool FilterPipelineRun(PipelineRun run, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        return run.Definition.Name.ToLowerInvariant().Contains(q)
            || run.SourceBranch.ToLowerInvariant().Contains(q)
            || run.BuildNumber.ToLowerInvariant().Contains(q);
    }

    public static bool FilterPipelineRunMulti(PipelineRun run, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        return run.ProjectDisplayName.ToLowerInvariant().Contains(q)
            || run.Project.Name.ToLowerInvariant().Contains(q)
            || run.Definition.Name.ToLowerInvariant().Contains(q)
            || run.SourceBranch.ToLowerInvariant().Contains(q)
            || run.BuildNumber.ToLowerInvariant().Contains(q);
    }

    public readonly record struct PipelineStatus(string Name, string Icon);

    public static IReadOnlyList<PipelineStatus> GetPipelineStatuses() => new[]
    {
        new PipelineStatus("Running", "●"),
        new PipelineStatus("Queued", "○"),
        new PipelineStatus("Success", "✓"),
        new PipelineStatus("Failed", "✗"),
        new PipelineStatus("Cancel", "⊘"),
        new PipelineStatus("Partial", "◐"),
    };

    public static string GetStatusKey(string status, string result)
    {
        string st = status.ToLowerInvariant();
        string r = result.ToLowerInvariant();
        if (st == "inprogress") return "Running";
        if (st == "notstarted") return "Queued";
        if (r == "succeeded") return "Success";
        if (r == "failed") return "Failed";
        if (r == "canceled") return "Cancel";
        if (r == "partiallysucceeded") return "Partial";
        return "";
    }

    // ---- test helpers for driving the list directly (≈ model.list.SetItems) ----

    /// <summary>Sets runs directly on the underlying list (test/poller convenience).</summary>
    public void SetItems(IEnumerable<PipelineRun> runs)
    {
        _allRuns = runs.ToList();
        _list.SetItems(ApplyStatusFilter(_allRuns));
    }

    /// <summary>Opens the detail view for the currently selected run (test convenience).</summary>
    public Cmd? OpenSelectedDetail()
    {
        var cmd = _list.OpenSelectedDetail();
        if (_list.GetViewMode() == ViewMode.Detail) _viewMode = PipelinesViewMode.Detail;
        return cmd;
    }
}
