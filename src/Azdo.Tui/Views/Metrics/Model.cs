using System.Text;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Configuration;
using Azdo.Core.Metrics;
using Azdo.Core.Util;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using CoreMetrics = Azdo.Core.Metrics;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.Metrics;

/// <summary>Which sub-pane (flags vs users) owns the cursor.</summary>
public enum FocusedPane { Flags, Users }

/// <summary>Toggles between Live, Trends table, and Trends chart.</summary>
public enum ViewMode { Live, Trends, TrendsChart }

/// <summary>The f-key flag-filter cycle position.</summary>
public enum FlagFilter { All, ActiveStale, RFTStale }

/// <summary>
/// The metrics dashboard model: a per-developer Live dashboard plus a
/// sprint-on-sprint Trends table and chart. Mutable; implements
/// <see cref="ITabView"/>.
/// </summary>
public sealed partial class Model : ITabView
{
    /// <summary>Seam so tests can intercept browser launches.</summary>
    public static Func<string, Exception?> OpenUrl { get; set; } = url =>
    {
        try { Browser.Open(url); return null; }
        catch (Exception e) { return e; }
    };

    private readonly MultiClient? _client;
    private readonly Config? _config;
    private StyleSet? _styles;

    // Clock seam for deterministic tests.
    private Func<DateTime> _now = () => DateTime.UtcNow;

    private List<WorkItem> _allItems = new();
    private List<UserMetrics> _userRows = new();
    private List<ItemFlag> _flags = new();

    private string _activeTag = "";
    private FlagFilter _flagFilter = FlagFilter.All;
    private FocusedPane _focusedPane = FocusedPane.Flags;
    private int _userCursor;
    private int _flagCursor;

    private ViewMode _mode = ViewMode.Live;
    private List<Snapshot> _snapshots = new();
    private List<string>? _selectedSprints;
    private List<SprintWindow> _sprintWindows = new();
    private List<TrendRow> _trendRows = new();
    private List<string> _availableSprints = new();

    // Trends chart state.
    private MetricKind _chartMetric = MetricKind.Points;
    private int _sprintCursor;
    private int _focusedUser = -1; // -1 = no focus

    private bool _loading;
    private DateTime _lastUpdated;
    private string _statusMessage = "";

    private int _width, _height;
    private Viewport? _viewport;
    private bool _ready;

    private TagPicker? _tagPicker;

    /// <summary>Constructs a metrics model with default styles.</summary>
    public Model(MultiClient? client, Config? cfg) : this(client, cfg, StyleSet.Default()) { }

    /// <summary>
    /// Constructs a metrics model with the provided styles. Pass null styles to
    /// skip picker creation (used by tests).
    /// </summary>
    public Model(MultiClient? client, Config? cfg, StyleSet? styles)
    {
        _client = client;
        _config = cfg;
        _styles = styles;
        if (styles is not null)
            _tagPicker = new TagPicker(styles);
    }

    // --- Test seams / accessors ---

    /// <summary>Replaces the clock for deterministic tests.</summary>
    public void SetNow(Func<DateTime> now) => _now = now;

    public IReadOnlyList<UserMetrics> UserRows => _userRows;
    public IReadOnlyList<ItemFlag> Flags => _flags;
    public IReadOnlyList<string> SelectedSprints => _selectedSprints ?? new List<string>();
    public IReadOnlyList<SprintWindow> SprintWindows => _sprintWindows;
    public IReadOnlyList<TrendRow> TrendRows => _trendRows;
    public string ActiveTagValue => _activeTag;
    public FlagFilter CurrentFlagFilter => _flagFilter;
    public FocusedPane CurrentFocusedPane => _focusedPane;
    public ViewMode CurrentMode => _mode;
    public int UserCursor => _userCursor;
    public int ViewportYOffset => _viewport?.YOffset ?? 0;
    public bool IsLoading => _loading;
    public DateTime LastUpdated => _lastUpdated;

    /// <summary>
    /// Swaps the active styles in place, preserving all loaded data and view
    /// state. The tag picker is rebuilt with the new styles.
    /// </summary>
    public void SetStyles(StyleSet? styles)
    {
        _styles = styles;
        if (styles is not null)
        {
            _tagPicker = new TagPicker(styles);
            if (_width > 0 && _height > 0)
                _tagPicker.SetSize(_width, _height);
        }
        if (_ready)
            UpdateViewportContent();
    }

    /// <summary>
    /// The configured workflow state names; falls back to defaults if the
    /// config block hasn't been populated.
    /// </summary>
    private StateConfig StateConfigOf()
    {
        if (_config is null)
            return StateConfig.DefaultStates();
        var st = _config.Metrics.States;
        if (string.IsNullOrEmpty(st.Active) || string.IsNullOrEmpty(st.ReadyForTest) || string.IsNullOrEmpty(st.Closed))
            return StateConfig.DefaultStates();
        return new StateConfig(st.Active, st.ReadyForTest, st.Closed);
    }

    private Thresholds ThresholdsOf() => new()
    {
        ActiveStaleDays = _config?.Metrics.ActiveStaleDays ?? Config.DefaultMetricsActiveStaleDays,
        RFTStaleDays = _config?.Metrics.RFTStaleDays ?? Config.DefaultMetricsRFTStaleDays,
        WIPLimit = _config?.Metrics.WIPLimit ?? Config.DefaultMetricsWipLimit,
        States = StateConfigOf(),
    };

    private int IntervalDays()
    {
        int d = _config?.Metrics.IntervalDays ?? Config.DefaultMetricsIntervalDays;
        return d <= 0 ? Config.DefaultMetricsIntervalDays : d;
    }

    // --- Lifecycle ---

    public Cmd? Init()
    {
        var cmds = new List<Cmd?> { Fetch(), LoadSnapshotsCmd() };
        if (_config is not null && _config.Metrics.RunOneShotBackfill)
            cmds.Add(RunBackfillCmd(_client, _now(), StateConfigOf()));
        return Commands.Batch(cmds);
    }

    public Cmd? Update(IMsg msg)
    {
        switch (msg)
        {
            case WindowSizeMsg ws:
                _width = ws.Width;
                _height = ws.Height;
                ResizeViewport();
                UpdateViewportContent();
                return null;

            case MetricsLoadedMsg loaded:
                return HandleLoaded(loaded);

            case SnapshotSavedMsg saved:
                return HandleSnapshotSaved(saved);

            case BackfillDoneMsg done:
                return HandleBackfillDone(done);

            case SnapshotsLoadedMsg snapsLoaded:
                return HandleSnapshotsLoaded(snapsLoaded);

            case TagsSelectedMsg tags:
                _selectedSprints = tags.Tags.ToList();
                _tagPicker?.Hide();
                RecomputeTrends();
                if (IsTrendsLike(_mode)) UpdateViewportContent();
                return SaveSelectionCmd(_selectedSprints);

            case OpenUrlResultMsg ou:
                if (ou.Err is not null)
                    _statusMessage = "Open in browser failed: " + ou.Err.Message;
                return null;

            case TagSelectedMsg tag:
                _activeTag = tag.Tag;
                if (_tagPicker?.IsVisible == true) _tagPicker.Hide();
                Recompute();
                return null;
        }

        // Tag picker swallows key events while visible.
        if (_tagPicker?.IsVisible == true)
        {
            if (msg is KeyMsg km)
                return _tagPicker.Update(km);
            return null;
        }

        if (msg is not KeyMsg key)
            return null;

        return HandleKey(key);
    }

    private Cmd? HandleLoaded(MetricsLoadedMsg msg)
    {
        _loading = false;
        _lastUpdated = msg.FetchedAt;
        if (msg.Err is not null)
        {
            if (msg.Err is PartialException pe)
            {
                _allItems = msg.Items ?? new List<WorkItem>();
                Recompute();
                _statusMessage = $"{pe.Failed} of {pe.Total} projects failed — partial data shown";
                return SaveSnapshotCmd(_client, _allItems, _now(), StateConfigOf());
            }
            _allItems = new List<WorkItem>();
            _userRows = new List<UserMetrics>();
            _flags = new List<ItemFlag>();
            _statusMessage = "Failed to load metrics: " + msg.Err.Message;
            return null;
        }
        _allItems = msg.Items ?? new List<WorkItem>();
        _statusMessage = "";
        Recompute();
        return SaveSnapshotCmd(_client, _allItems, _now(), StateConfigOf());
    }

    private Cmd? HandleSnapshotSaved(SnapshotSavedMsg msg)
    {
        if (msg.Err is not null)
        {
            _statusMessage = "Snapshot save failed: " + msg.Err.Message;
            return null;
        }
        if (msg.AlreadyToday)
        {
            if (_statusMessage == "")
                _statusMessage = "Snapshot skipped (already saved today)";
            return null;
        }
        _statusMessage = msg.Skipped > 0
            ? $"Snapshot saved · {msg.Saved} rows, {msg.Skipped} items couldn't be backfilled"
            : $"Snapshot saved · {msg.Saved} rows appended";
        return LoadSnapshotsCmd();
    }

    private Cmd? HandleBackfillDone(BackfillDoneMsg msg)
    {
        if (msg.Err is not null)
        {
            _statusMessage = "Backfill failed: " + msg.Err.Message;
            return null;
        }
        if (msg.AlreadyDone)
            return null;
        _statusMessage =
            $"Backfill complete · {msg.Saved} rows from {msg.Total} items, {msg.Skipped} skipped — " +
            "set run_one_shot_backfill: false to stop seeing this";
        return LoadSnapshotsCmd();
    }

    private Cmd? HandleSnapshotsLoaded(SnapshotsLoadedMsg msg)
    {
        if (msg.Err is not null)
            return null;
        _snapshots = msg.Snaps ?? new List<Snapshot>();
        _availableSprints = CollectUniqueTagsFromSnaps(_snapshots);
        if (_selectedSprints is null)
            _selectedSprints = SelectionStore.FilterAvailable(msg.Selected, _availableSprints);
        else
            _selectedSprints = SelectionStore.FilterAvailable(_selectedSprints, _availableSprints);
        RecomputeTrends();
        if (IsTrendsLike(_mode)) UpdateViewportContent();
        return null;
    }

    private Cmd? HandleKey(KeyMsg key)
    {
        switch (key.Key)
        {
            case "tab":
            case "shift+tab":
                if (IsTrendsLike(_mode)) return null;
                _focusedPane = _focusedPane == FocusedPane.Flags ? FocusedPane.Users : FocusedPane.Flags;
                UpdateViewportContent();
                ScrollCursorIntoView();
                return null;
            case "up":
                if (IsTrendsLike(_mode)) { _viewport?.LineUp(1); return null; }
                MoveCursor(-1);
                UpdateViewportContent();
                ScrollCursorIntoView();
                return null;
            case "down":
                if (IsTrendsLike(_mode)) { _viewport?.LineDown(1); return null; }
                MoveCursor(1);
                UpdateViewportContent();
                ScrollCursorIntoView();
                return null;
            case "pgup":
                if (_viewport is not null) _viewport.LineUp(_viewport.Height);
                return null;
            case "pgdown":
                if (_viewport is not null) _viewport.LineDown(_viewport.Height);
                return null;
            case "esc":
                if (_mode == ViewMode.Live && _activeTag != "")
                {
                    _activeTag = "";
                    Recompute();
                }
                return null;
        }

        switch (key.Key)
        {
            case "r":
                _loading = true;
                _statusMessage = "";
                return Commands.Batch(Fetch(), LoadSnapshotsCmd());
            case "v":
                _mode = _mode switch
                {
                    ViewMode.Live => ViewMode.Trends,
                    ViewMode.Trends => ViewMode.TrendsChart,
                    _ => ViewMode.Live,
                };
                UpdateViewportContent();
                _viewport?.SetYOffset(0);
                return null;
            case "f":
                if (_mode == ViewMode.TrendsChart)
                {
                    HandleChartKey("f");
                    UpdateViewportContent();
                    return null;
                }
                if (IsTrendsLike(_mode)) return null;
                _flagFilter = (FlagFilter)(((int)_flagFilter + 1) % 3);
                _flagCursor = 0;
                UpdateViewportContent();
                ScrollCursorIntoView();
                return null;
            case "T":
                if (_styles is null || _tagPicker is null) return null;
                if (IsTrendsLike(_mode))
                    _tagPicker.SetTagsMulti(_availableSprints, _selectedSprints ?? new List<string>());
                else
                    _tagPicker.SetTags(CollectUniqueTags(_allItems), _activeTag);
                _tagPicker.Show();
                return null;
            case "o":
                if (IsTrendsLike(_mode)) return null;
                return OpenFocused();
            case "h":
            case "l":
            case ",":
            case ".":
            case "n":
            case "p":
            case "a":
                if (_mode != ViewMode.TrendsChart) return null;
                HandleChartKey(key.Key);
                UpdateViewportContent();
                return null;
        }
        return null;
    }

    internal static bool IsTrendsLike(ViewMode v) => v == ViewMode.Trends || v == ViewMode.TrendsChart;

    private void HandleChartKey(string k)
    {
        switch (k)
        {
            case "l":
                _chartMetric = NextMetric(_chartMetric, 1);
                break;
            case "h":
                _chartMetric = NextMetric(_chartMetric, -1);
                break;
            case ".":
                if (_sprintWindows.Count > 0)
                    _sprintCursor = Clamp(_sprintCursor + 1, 0, _sprintWindows.Count - 1);
                break;
            case ",":
                if (_sprintWindows.Count > 0)
                    _sprintCursor = Clamp(_sprintCursor - 1, 0, _sprintWindows.Count - 1);
                break;
            case "f":
                if (_trendRows.Count > 0)
                {
                    _focusedUser++;
                    if (_focusedUser >= _trendRows.Count) _focusedUser = -1;
                }
                break;
        }
    }

    private static MetricKind NextMetric(MetricKind cur, int delta)
    {
        int n = ChartData.AllMetricKinds.Length;
        int idx = ((int)cur + delta + n) % n;
        return ChartData.AllMetricKinds[idx];
    }

    private Cmd? Fetch()
    {
        if (_client is null) return null;
        var since = _now().AddDays(-IntervalDays());
        var client = _client;
        var now = _now;
        var states = ToMetricsStateNames(StateConfigOf());
        return Commands.FromAsync(async () =>
        {
            try
            {
                var items = await client.MetricsWorkItemsAsync(since, states).ConfigureAwait(false);
                return new MetricsLoadedMsg(items, null, now());
            }
            catch (Exception e)
            {
                return new MetricsLoadedMsg(null, e, now());
            }
        });
    }

    internal static MetricsStateNames ToMetricsStateNames(StateConfig sc) => new()
    {
        Active = sc.Active,
        ReadyForTest = sc.ReadyForTest,
        Closed = sc.Closed,
    };

    private void RecomputeTrends()
    {
        if (_selectedSprints is null || _selectedSprints.Count == 0)
        {
            _sprintWindows = new List<SprintWindow>();
            _trendRows = new List<TrendRow>();
            _sprintCursor = 0;
            return;
        }
        var windows = new List<SprintWindow>();
        foreach (var tag in _selectedSprints)
        {
            var (w, ok) = Trends.DeriveSprintWindow(_snapshots, tag, _now(), StateConfigOf());
            if (ok) windows.Add(w);
        }
        _sprintWindows = windows;
        _trendRows = Trends.TrendAggregate(_snapshots, windows, ThresholdsOf(), _now());
        if (_sprintCursor >= _sprintWindows.Count) _sprintCursor = 0;
    }

    private void Recompute()
    {
        var now = _now();
        var intervalStart = now.AddDays(-IntervalDays());
        var filtered = ApplyTagFilter(_allItems, _activeTag);
        var (rows, flags) = Aggregator.Aggregate(filtered, intervalStart, now, ThresholdsOf());
        _userRows = rows;
        _flags = flags;
        if (_userCursor >= rows.Count) _userCursor = 0;
        if (_flagCursor >= flags.Count) _flagCursor = 0;
        UpdateViewportContent();
    }

    private void ResizeViewport()
    {
        const int reservedRows = 2; // header + blank
        int h = Math.Max(1, _height - reservedRows);
        int w = Math.Max(1, _width);
        if (_viewport is null)
        {
            _viewport = new Viewport(w, h);
            _ready = true;
            return;
        }
        _viewport.Width = w;
        _viewport.Height = h;
    }

    private void UpdateViewportContent()
    {
        if (!_ready || _viewport is null) return;
        string body = _mode switch
        {
            ViewMode.Trends => RenderTrends(),
            ViewMode.TrendsChart => RenderTrendsChart(),
            _ => Layout.JoinVertical(HAlign.Left, RenderFlagsPane(), "", RenderUsersPane()),
        };
        _viewport.SetContent(body);
    }

    private void ScrollCursorIntoView()
    {
        if (!_ready || _viewport is null) return;
        int line = CursorLineInBody();
        int top = _viewport.YOffset;
        int bottom = top + _viewport.Height - 1;
        if (line < top) _viewport.SetYOffset(line);
        else if (line > bottom) _viewport.SetYOffset(line - _viewport.Height + 1);
    }

    private int CursorLineInBody()
    {
        int flagsRows = VisibleFlags().Count;
        if (flagsRows == 0) flagsRows = 1;
        switch (_focusedPane)
        {
            case FocusedPane.Flags:
                return 1 + _flagCursor;
            case FocusedPane.Users:
                int usersStart = 1 + flagsRows + 1;
                return usersStart + 2 + _userCursor;
        }
        return 0;
    }

    internal List<ItemFlag> VisibleFlags() => _flagFilter switch
    {
        FlagFilter.ActiveStale => _flags.Where(f => f.Reason == "active-stale").ToList(),
        FlagFilter.RFTStale => _flags.Where(f => f.Reason == "rft-stale").ToList(),
        _ => _flags,
    };

    private void MoveCursor(int delta)
    {
        switch (_focusedPane)
        {
            case FocusedPane.Flags:
                int nf = VisibleFlags().Count;
                _flagCursor = nf == 0 ? 0 : Clamp(_flagCursor + delta, 0, nf - 1);
                break;
            case FocusedPane.Users:
                int nu = _userRows.Count;
                _userCursor = nu == 0 ? 0 : Clamp(_userCursor + delta, 0, nu - 1);
                break;
        }
    }

    internal static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private Cmd? OpenFocused()
    {
        if (_client is null)
        {
            _statusMessage = "Cannot open: no Azure DevOps client";
            return null;
        }
        var org = _client.GetOrg();
        string url = "";
        switch (_focusedPane)
        {
            case FocusedPane.Flags:
                var vis = VisibleFlags();
                if (vis.Count == 0 || _flagCursor >= vis.Count) return null;
                var f = vis[_flagCursor];
                var project = ProjectApiNameFor(_allItems, f.Id, f.Project);
                url = BuildWorkItemUrl(org, project, f.Id);
                break;
            case FocusedPane.Users:
                if (_userRows.Count == 0 || _userCursor >= _userRows.Count) return null;
                var user = _userRows[_userCursor].User;
                var (item, ok) = WorstItemForUser(_allItems, user, _now(), ThresholdsOf());
                if (!ok)
                {
                    _statusMessage = "No openable item for " + user;
                    return null;
                }
                url = BuildWorkItemUrl(org, item!.ProjectName, item.Id);
                break;
        }
        if (url == "")
        {
            _statusMessage = "Cannot open: missing organization or project";
            return null;
        }
        var captured = url;
        return Commands.FromFunc(() => new OpenUrlResultMsg(OpenUrl(captured)));
    }

    internal static string ProjectApiNameFor(IReadOnlyList<WorkItem> items, int id, string fallback)
    {
        foreach (var wi in items)
            if (wi.Id == id) return wi.ProjectName;
        return fallback;
    }

    internal static (WorkItem? Item, bool Ok) WorstItemForUser(
        IReadOnlyList<WorkItem> items, string user, DateTime now, Thresholds th)
    {
        var states = th.States;
        var activeStale = TimeSpan.FromDays(th.ActiveStaleDays);
        var rftStale = TimeSpan.FromDays(th.RFTStaleDays);
        WorkItem? bestStale = null, bestInFlight = null;
        TimeSpan bestStaleDwell = TimeSpan.Zero, bestInFlightDwell = TimeSpan.Zero;
        foreach (var wi in items)
        {
            if (wi.AssignedToName() != user) continue;
            var dwell = wi.TimeInCurrentState(now);
            bool isActive = states.IsActive(wi.Fields.State);
            bool isRFT = states.IsRFT(wi.Fields.State);
            if (!isActive && !isRFT) continue;
            if (bestInFlight is null || dwell > bestInFlightDwell)
            {
                bestInFlight = wi;
                bestInFlightDwell = dwell;
            }
            bool isStale = (isActive && dwell > activeStale) || (isRFT && dwell > rftStale);
            if (isStale && (bestStale is null || dwell > bestStaleDwell))
            {
                bestStale = wi;
                bestStaleDwell = dwell;
            }
        }
        if (bestStale is not null) return (bestStale, true);
        if (bestInFlight is not null) return (bestInFlight, true);
        return (null, false);
    }

    internal static List<WorkItem> ApplyTagFilter(List<WorkItem> items, string tag)
    {
        if (tag == "") return items;
        var filtered = new List<WorkItem>();
        foreach (var wi in items)
            if (wi.TagList().Contains(tag))
                filtered.Add(wi);
        return filtered;
    }

    internal static List<string> CollectUniqueTags(IReadOnlyList<WorkItem> items)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var wi in items)
            foreach (var t in wi.TagList())
                seen.Add(t);
        return seen.ToList();
    }

    internal static List<string> CollectUniqueTagsFromSnaps(IReadOnlyList<Snapshot> snaps)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in snaps)
            foreach (var t in s.Tags)
                seen.Add(t);
        return seen.ToList();
    }

    internal static string BuildWorkItemUrl(string org, string project, int id)
    {
        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(project)) return "";
        return $"https://dev.azure.com/{org}/{project}/_workitems/edit/{id}";
    }

    // --- ITabView ---

    public string View()
    {
        if (_loading && _userRows.Count == 0 && _statusMessage == "" && _mode == ViewMode.Live)
            return RenderLoading();

        var header = RenderHeader();
        if (!_ready || _viewport is null)
        {
            switch (_mode)
            {
                case ViewMode.Trends:
                    return header + "\n\n" + RenderTrends();
                case ViewMode.TrendsChart:
                    return header + "\n\n" + RenderTrendsChart();
            }
            return Layout.JoinVertical(HAlign.Left, header, "", RenderFlagsPane(), "", RenderUsersPane());
        }
        if (_tagPicker?.IsVisible == true)
            return _tagPicker.View();
        return header + "\n\n" + _viewport.View();
    }

    private string RenderLoading()
    {
        var msg = "Loading metrics…";
        return _styles is not null ? _styles.Muted.Render(msg) : msg;
    }

    public bool IsSearching() => false;

    public bool IsCapturingInput() => _tagPicker?.IsVisible == true;

    public bool HasContextBar() => false;

    public IReadOnlyList<ContextItem> GetContextItems() => Array.Empty<ContextItem>();

    public double GetScrollPercent() => _ready && _viewport is not null ? _viewport.ScrollPercent() * 100 : 0;

    public string GetStatusMessage() => _statusMessage;

    public string FilterLabel() => _activeTag != "" ? "Tag: " + _activeTag : "";

    public string DefaultKeybindings() =>
        "r refresh • v live/trends • Tab focus pane • ↑↓ navigate • T tag • f flag filter • o open • esc clear tag • ? help • q quit";

    // Tag-picker glue.
    public bool IsTagPickerVisible() => _tagPicker?.IsVisible == true;
    public bool IsTagFilterActive() => _activeTag != "";
    public string ActiveTag() => _activeTag;
    public void SetTagPickerSize(int width, int height) => _tagPicker?.SetSize(width, height);
}
