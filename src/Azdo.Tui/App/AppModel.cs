using Azdo.Core.AzureDevOps;
using Azdo.Core.Configuration;
using Azdo.Core.State;
using Azdo.Core.Version;
using Azdo.Tui.Components;
using Azdo.Tui.Polling;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using Azdo.Tui.Styles;
using Azdo.Tui.Views;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.App;

public enum AppTab { PullRequests, WorkItems, Pipelines, Metrics }

/// <summary>Sent when the background version check completes.</summary>
public sealed record UpdateCheckMsg(UpdateInfo? Info) : IMsg;

/// <summary>
/// Root application model (≈ <c>app.Model</c>): owns tab navigation, modal
/// overlays (help / error / theme), the status bar, background polling, theme
/// switching, and navigation-state persistence. Routes messages with
/// modal-priority dispatch, then delegates to the active tab.
/// </summary>
public sealed class AppModel : IModel
{
    private const int BorderWidth = 2;
    private const int BoxBorderRows = 2;
    private const int TabBarRows = 5;

    private readonly MultiClient _client;
    private readonly Config _config;
    private StyleSet _styles;
    private AppTab _activeTab = AppTab.PullRequests;
    private readonly List<AppTab> _enabledTabs;

    private Views.PullRequests.Model _prView;
    private Views.WorkItems.Model _wiView;
    private Views.Pipelines.Model _pipeView;
    private Views.Metrics.Model _metricsView;

    private Logo _logo;
    private StatusBar _statusBar;
    private HelpModal _helpModal;
    private ErrorModal _errorModal;
    private ThemePicker _themePicker;
    private readonly Poller _poller;
    private readonly ErrorHandler _errorHandler;
    private readonly string _version;
    private readonly string _commit;
    private int _width, _height, _footerRows;
    private Store? _stateStore;

    public AppModel(MultiClient client, Config config, string version, string commit)
    {
        _client = client;
        _config = config;
        _version = version;
        _commit = commit;
        _errorHandler = new ErrorHandler();

        // Load custom themes (best effort), then resolve the configured theme.
        try { Themes.LoadCustomThemesFromDirectory(Themes.GetThemesDirectoryPath()); } catch { }
        var requested = config.GetTheme();
        Theme theme = Themes.TryGetByName(requested, out var t) ? t : Themes.Default;
        bool themeMissing = !Themes.TryGetByName(requested, out _);
        _styles = new StyleSet(theme);

        _logo = new Logo(_styles);
        _statusBar = new StatusBar(_styles);
        _statusBar.SetOrganization(config.Organization);
        _statusBar.SetProject(config.IsMultiProject()
            ? $"{config.Projects.Count} projects"
            : config.DisplayNameFor(config.Projects[0]));

        _helpModal = BuildHelpModal(_styles, config, version, commit);
        _errorModal = new ErrorModal(_styles);
        _themePicker = new ThemePicker(_styles, Themes.ListAvailable(), config.GetTheme());

        var interval = TimeSpan.FromSeconds(config.PollingInterval > 0 ? config.PollingInterval : (int)Poller.DefaultInterval.TotalSeconds);
        _poller = new Poller(client, interval);

        if (themeMissing)
            _errorHandler.SetError(new Exception($"Theme '{requested}' not found, using '{Themes.Default.Name}'. Press 't' to select a theme."));

        _prView = new Views.PullRequests.Model(client, _styles);
        _wiView = new Views.WorkItems.Model(client, _styles);
        _pipeView = new Views.Pipelines.Model(client, _styles);
        _metricsView = new Views.Metrics.Model(client, config, _styles);

        _enabledTabs = BuildEnabledTabs(config);
    }

    public void SetStateStore(Store store) => _stateStore = store;

    private static List<AppTab> BuildEnabledTabs(Config cfg)
    {
        var tabs = new List<AppTab> { AppTab.PullRequests };
        if (cfg.IsPaneEnabled("workitems")) tabs.Add(AppTab.WorkItems);
        if (cfg.IsPaneEnabled("pipelines")) tabs.Add(AppTab.Pipelines);
        if (cfg.Metrics.Enabled) tabs.Add(AppTab.Metrics);
        return tabs;
    }

    private static HelpModal BuildHelpModal(StyleSet styles, Config cfg, string version, string commit)
    {
        var help = new HelpModal(styles);
        if (!cfg.IsPaneEnabled("workitems"))
        {
            help.RemoveBindingsByDescription("work items");
            help.RemoveBindingsByDescription("work item");
        }
        if (!cfg.IsPaneEnabled("pipelines"))
        {
            help.RemoveSection("Log Viewer (pipelines)");
            help.RemoveBindingsByDescription("pipelines");
        }
        var tabNames = new List<string> { "PR" };
        if (cfg.IsPaneEnabled("workitems")) tabNames.Add("Work Items");
        if (cfg.IsPaneEnabled("pipelines")) tabNames.Add("Pipelines");
        if (cfg.Metrics.Enabled) tabNames.Add("Metrics");
        if (!tabNames.SequenceEqual(new[] { "PR", "Work Items", "Pipelines" }))
            help.UpdateTabsBinding(string.Join("/", Enumerable.Range(1, tabNames.Count)), string.Join(" / ", tabNames));
        if (cfg.Metrics.Enabled)
            help.AddSection("Metrics tab", new HelpBinding[]
            {
                new("v", "Toggle Live ↔ Trends sub-view"),
                new("Tab", "Switch focus (Live)"),
                new("↑/↓", "Live: move cursor; Trends: scroll"),
                new("f", "Cycle flag filter (Live)"),
                new("T", "Live: filter by tag; Trends: sprint picker"),
                new("esc", "Clear tag filter (Live)"),
                new("o", "Open focused item in browser (Live)"),
                new("r", "Refresh metrics + reload snapshot"),
            });
        help.SetVersionInfo(FormatVersion(version, commit));
        try { help.SetConfigPath(Config.GetPath()); } catch { }
        return help;
    }

    private static string FormatVersion(string version, string commit)
        => commit is not "" and not "none" ? $"{version} ({commit})" : version;

    // ----- ApplyState -----

    public void ApplyState(AppState s)
    {
        var tab = TabFromId(s.ActiveTab);
        if (tab is { } tt && _enabledTabs.Contains(tt)) _activeTab = tt;
        if (s.Tabs.PullRequests.LastDetailId != 0)
            ((IRestorableTab)_prView).SetPendingDetailRestore(s.Tabs.PullRequests.LastDetailId);
        if (s.Tabs.WorkItems.LastDetailId != 0)
            ((IRestorableTab)_wiView).SetPendingDetailRestore(s.Tabs.WorkItems.LastDetailId);
    }

    private static AppTab? TabFromId(string id) => id switch
    {
        TabId.PullRequests => AppTab.PullRequests,
        TabId.WorkItems => AppTab.WorkItems,
        TabId.Pipelines => AppTab.Pipelines,
        _ => null,
    };

    private static string TabIdFor(AppTab t) => t switch
    {
        AppTab.PullRequests => TabId.PullRequests,
        AppTab.WorkItems => TabId.WorkItems,
        AppTab.Pipelines => TabId.Pipelines,
        _ => "",
    };

    private void RecordActiveTab()
    {
        if (_stateStore is null) return;
        var id = TabIdFor(_activeTab);
        _stateStore.Apply(s => { s.Version = AppState.CurrentVersion; s.ActiveTab = id; });
    }

    private void RecordDetailState()
    {
        if (_stateStore is null) return;
        switch (_activeTab)
        {
            case AppTab.PullRequests:
                var prId = ((IRestorableTab)_prView).DetailItemId();
                _stateStore.Apply(s => { s.Version = AppState.CurrentVersion; s.Tabs.PullRequests.LastDetailId = prId; });
                break;
            case AppTab.WorkItems:
                var wiId = ((IRestorableTab)_wiView).DetailItemId();
                _stateStore.Apply(s => { s.Version = AppState.CurrentVersion; s.Tabs.WorkItems.LastDetailId = wiId; });
                break;
        }
    }

    // ----- view access -----

    private ITabView ActiveView() => _activeTab switch
    {
        AppTab.PullRequests => _prView,
        AppTab.WorkItems => _wiView,
        AppTab.Pipelines => _pipeView,
        _ => _metricsView,
    };

    private Cmd? InitTabCmd(AppTab tab) => tab switch
    {
        AppTab.PullRequests => _prView.Init(),
        AppTab.WorkItems => _wiView.Init(),
        AppTab.Metrics => _metricsView.Init(),
        _ => null, // pipelines populated by poller
    };

    private AppTab NextTab()
    {
        var i = _enabledTabs.IndexOf(_activeTab);
        return _enabledTabs[(i + 1) % _enabledTabs.Count];
    }

    private AppTab PrevTab()
    {
        var i = _enabledTabs.IndexOf(_activeTab);
        return _enabledTabs[(i - 1 + _enabledTabs.Count) % _enabledTabs.Count];
    }

    // ----- IModel -----

    public Cmd? Init()
    {
        if (_errorHandler.ShouldShowError())
            _statusBar.SetWarningMessage(_errorHandler.ErrorMessage());

        var cmds = new List<Cmd?>
        {
            _poller.FetchPipelineRuns(),
            _poller.StartPolling(),
            CheckForUpdate(),
            InitTabCmd(_activeTab),
        };
        if (_activeTab != AppTab.PullRequests) cmds.Add(_prView.Init());
        return Commands.Batch(cmds);
    }

    private Cmd CheckForUpdate() => Commands.FromAsync(async () =>
    {
        try
        {
            var info = await new VersionChecker(_version).CheckForUpdateAsync().ConfigureAwait(false);
            return new UpdateCheckMsg(info);
        }
        catch { return null; }
    });

    public (IModel, Cmd?) Update(IMsg msg)
    {
        // Modal priority: error → help → theme.
        if (_errorModal.IsVisible)
        {
            if (msg is KeyMsg) { _errorModal.Update(msg); return (this, null); }
            if (msg is WindowSizeMsg w) { ResizeChrome(w); }
            return (this, null);
        }
        if (_helpModal.IsVisible)
        {
            if (msg is KeyMsg) { _helpModal.Update(msg); return (this, null); }
            if (msg is WindowSizeMsg w) { ResizeChrome(w); }
            return (this, null);
        }
        if (_themePicker.IsVisible)
        {
            if (msg is KeyMsg) { var c = _themePicker.Update(msg); return (this, c); }
            if (msg is WindowSizeMsg w) { ResizeChrome(w); }
            // fall through for ThemeSelectedMsg below
            if (msg is not ThemeSelectedMsg) return (this, null);
        }

        switch (msg)
        {
            case KeyMsg key when !IsActiveViewCapturingInput():
                switch (key.Key)
                {
                    case "q": case "ctrl+c":
                        _poller.Stop();
                        return (this, Commands.Quit);
                    case "?":
                        _helpModal.SetSize(_width, _height); _helpModal.Show();
                        return (this, null);
                    case "t":
                        _themePicker.SetSize(_width, _height); _themePicker.Show();
                        return (this, null);
                    case "1": case "2": case "3": case "4":
                        return SwitchTabByIndex(key.Key[0] - '1');
                    case "left":
                        return SwitchTab(PrevTab());
                    case "right":
                        return SwitchTab(NextTab());
                }
                break;

            case ThemeSelectedMsg theme:
                return ApplyTheme(theme.ThemeName);

            case WindowSizeMsg ws:
                _width = ws.Width; _height = ws.Height;
                _statusBar.SetWidth(ws.Width);
                _errorModal.SetSize(ws.Width, ws.Height);
                _helpModal.SetSize(ws.Width, ws.Height);
                _themePicker.SetSize(ws.Width, ws.Height);
                _footerRows = MeasureFooterHeight();
                var size = ContentViewSize();
                _prView.Update(size); _wiView.Update(size); _pipeView.Update(size); _metricsView.Update(size);
                return (this, null);

            case UpdateCheckMsg uc:
                if (uc.Info is { UpdateAvailable: true } info)
                    _statusBar.SetUpdateMessage($"Update available: {info.CurrentVersion} → {info.LatestVersion}");
                return (this, null);

            case PollTickMsg:
                return (this, _poller.OnTick());

            case CriticalErrorMsg ce:
                _errorModal.SetSize(_width, _height);
                _errorModal.Show(ce.Title, ce.Message, ce.Hint);
                return (this, null);

            case PipelineRunsUpdated pru:
                return HandlePipelineUpdate(pru);
        }

        // Delegate to the active view.
        var cmd = ActiveView().Update(msg);
        ResizeActiveViewIfNeeded();
        RecordDetailState();
        return (this, cmd);
    }

    private (IModel, Cmd?) HandlePipelineUpdate(PipelineRunsUpdated msg)
    {
        // A 403 means the PAT lacks the Build (Read) scope. This is a per-feature
        // limitation, not an app-wide failure: surface it inline in the Pipelines
        // tab and keep the other tabs (PRs / Work Items) fully usable. Bypass the
        // error handler so a missing scope is not mistaken for a flaky connection
        // and escalated to a blocking modal / "check your network" retry loop.
        if (ErrorClassifier.IsPermissionError(msg.Err))
        {
            _errorHandler.ClearError();
            _statusBar.ClearErrorMessage();
            _statusBar.ClearWarningMessage();
            return (this, _pipeView.Update(msg));
        }

        var (runs, hasError) = _errorHandler.ProcessUpdate(msg);
        if (hasError)
        {
            _statusBar.SetState(ConnectionState.Error);
            var info = ErrorClassifier.Classify(msg.Err);
            if (info is not null) { _errorModal.SetSize(_width, _height); _errorModal.Show(info.Title, info.Message, info.Hint); }
            if (_errorHandler.ShouldShowError()) _statusBar.SetErrorMessage(_errorHandler.RecoveryMessage());
        }
        else
        {
            _statusBar.SetState(ConnectionState.Connected);
            _statusBar.ClearErrorMessage();
            var warning = _errorHandler.PartialWarning();
            if (warning != "") _statusBar.SetWarningMessage(warning); else _statusBar.ClearWarningMessage();
        }
        Cmd? cmd = runs is not null ? _pipeView.Update(new Views.Pipelines.SetRunsMsg(runs)) : null;
        return (this, cmd);
    }

    private (IModel, Cmd?) SwitchTabByIndex(int idx)
    {
        if (idx < 0 || idx >= _enabledTabs.Count) return (this, null);
        return SwitchTab(_enabledTabs[idx]);
    }

    private (IModel, Cmd?) SwitchTab(AppTab target)
    {
        if (target == _activeTab) return (this, null);
        _activeTab = target;
        ResizeActiveViewIfNeeded();
        RecordActiveTab();
        return (this, InitTabCmd(target));
    }

    private (IModel, Cmd?) ApplyTheme(string themeName)
    {
        _themePicker.Hide();
        try { _config.UpdateTheme(themeName); }
        catch (Exception e)
        {
            _errorHandler.SetError(e);
            _statusBar.SetState(ConnectionState.Error);
            _statusBar.SetErrorMessage($"Failed to save theme setting: {e.Message}");
            return (this, null);
        }

        var theme = Themes.GetByNameWithFallback(themeName);
        _styles = new StyleSet(theme);

        var prevState = _statusBar.GetState();
        var prevWarning = _statusBar.GetWarningMessage();
        _statusBar = new StatusBar(_styles);
        _statusBar.SetState(prevState);
        if (prevWarning != "") _statusBar.SetWarningMessage(prevWarning);
        _statusBar.SetOrganization(_config.Organization);
        _statusBar.SetProject(_config.IsMultiProject() ? $"{_config.Projects.Count} projects" : _config.DisplayNameFor(_config.Projects[0]));
        _statusBar.SetWidth(_width);

        _logo = new Logo(_styles);
        _helpModal = BuildHelpModal(_styles, _config, _version, _commit);
        _helpModal.SetSize(_width, _height);
        _errorModal = new ErrorModal(_styles);
        _errorModal.SetSize(_width, _height);
        _themePicker = new ThemePicker(_styles, Themes.ListAvailable(), themeName);

        _prView = new Views.PullRequests.Model(_client, _styles);
        _wiView = new Views.WorkItems.Model(_client, _styles);
        _pipeView = new Views.Pipelines.Model(_client, _styles);
        _metricsView.SetStyles(_styles); // re-style in place to keep loaded data

        var cmds = new List<Cmd?>();
        if (_width > 0 && _height > 0)
        {
            _footerRows = MeasureFooterHeight();
            var size = ContentViewSize();
            _prView.Update(size); _wiView.Update(size); _pipeView.Update(size); _metricsView.Update(size);
        }
        cmds.Add(_pipeView.Init());
        if (_activeTab == AppTab.PullRequests) cmds.Add(_prView.Init());
        if (_activeTab == AppTab.WorkItems) cmds.Add(_wiView.Init());
        return (this, Commands.Batch(cmds));
    }

    private bool IsActiveViewCapturingInput()
    {
        var v = ActiveView();
        return v.IsSearching() || v.IsCapturingInput();
    }

    private void ResizeChrome(WindowSizeMsg w)
    {
        _width = w.Width; _height = w.Height;
        _errorModal.SetSize(w.Width, w.Height);
        _helpModal.SetSize(w.Width, w.Height);
        _themePicker.SetSize(w.Width, w.Height);
        _statusBar.SetWidth(w.Width);
    }

    private void ResizeActiveViewIfNeeded()
    {
        SyncStatusBarContext();
        var newFooter = MeasureFooterHeight();
        if (newFooter == _footerRows) return;
        _footerRows = newFooter;
        ActiveView().Update(ContentViewSize());
    }

    private void SyncStatusBarContext()
    {
        var v = ActiveView();
        if (v.HasContextBar()) _statusBar.SetContextItems(v.GetContextItems());
        else _statusBar.ClearContextItems();
    }

    private WindowSizeMsg ContentViewSize()
    {
        const int minW = 20, minH = 5;
        int width = Math.Max(minW, _width - BorderWidth);
        int height = Math.Max(minH, _height - TabBarRows - BoxBorderRows - _footerRows);
        return new WindowSizeMsg(width, height);
    }

    private int MeasureFooterHeight() => _statusBar.View().Count(c => c == '\n') + 1;

    // ----- View -----

    public string View()
    {
        // Full-screen modal overlays take precedence over the tab content.
        if (_errorModal.IsVisible) return _errorModal.View();
        if (_helpModal.IsVisible) return _helpModal.View();
        if (_themePicker.IsVisible) return _themePicker.View();

        var size = ContentViewSize();
        var tabBar = RenderTabBar(size.Width);

        var v = ActiveView();
        var content = v.View();
        bool hasContextBar = v.HasContextBar();

        // Footer keybindings: context items, or per-tab defaults.
        if (!hasContextBar) _statusBar.SetKeybindings(v.DefaultKeybindings());
        else _statusBar.SetKeybindings("");

        var filter = v.FilterLabel();
        if (filter != "") _statusBar.SetFilterLabel(filter); else _statusBar.ClearFilterLabel();

        if (hasContextBar)
        {
            _statusBar.SetContextItems(v.GetContextItems());
            var status = v.GetStatusMessage();
            if (status != "") _statusBar.SetContextStatus(status);
        }
        else _statusBar.ClearContextItems();

        var scroll = v.GetScrollPercent();
        if (scroll > 0) { _statusBar.ShowScrollPercent(true); _statusBar.SetScrollPercent(scroll); }
        else _statusBar.ShowScrollPercent(false);

        var footer = _statusBar.View();
        var contentBox = _styles.ContentBox.Width(size.Width).Height(size.Height).Render(content);
        return tabBar + "\n" + contentBox + "\n" + footer;
    }

    private string RenderTabBar(int innerWidth)
    {
        var labels = new Dictionary<AppTab, string>
        {
            [AppTab.PullRequests] = "Pull Requests",
            [AppTab.WorkItems] = "Work Items",
            [AppTab.Pipelines] = "Pipelines",
            [AppTab.Metrics] = "Metrics",
        };
        var rendered = new List<string>();
        for (int i = 0; i < _enabledTabs.Count; i++)
        {
            var tab = _enabledTabs[i];
            var label = $"{i + 1}: {labels[tab]}";
            rendered.Add(tab == _activeTab ? _styles.TabActive.Render(label) : _styles.TabInactive.Render(label));
        }
        var tabs = string.Join(" ", rendered);
        var logo = _logo.View();
        int logoWidth = Ansi.MaxLineWidth(logo);
        int tabsWidth = Math.Max(0, innerWidth - logoWidth);
        var left = Style.New().Width(tabsWidth).Height(_logo.Height).AlignVertical(VAlign.Center).Render(tabs);
        var combined = Layout.JoinHorizontal(VAlign.Top, left, logo);
        return _styles.TabBar.Width(innerWidth).Render(combined);
    }
}
