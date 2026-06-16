using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.WorkItems;

/// <summary>
/// The Work Items tab (≈ <c>workitems.Model</c> in list.go). Wraps a generic
/// <see cref="ListView{T}"/> and layers on the my-items / tag / state filters and
/// detail-view restoration. Mutable, matching the convention of the other tabs.
/// </summary>
public sealed class Model : ITabView, IRestorableTab
{
    private const int Top = 50;

    private readonly ListView<WorkItem> _list;
    private readonly MultiClient? _client;
    private readonly StyleSet _styles;

    private bool _myItemsOnly;
    private List<WorkItem> _allItems = new();
    private List<WorkItem> _myItems = new(); // base my-items set (before tag/state filter)
    private string _activeTag = "";
    private string _activeState = "";
    private readonly TagPicker _tagPicker;
    private readonly ListPicker _statePicker;

    private int _width = 80;
    private int _height = 24;

    // pendingDetailId is the work-item ID requested by startup state restore.
    // Cleared on first populate so polling can't re-trigger it.
    private int _pendingDetailId;
    private bool _pendingRestoreHandled;

    public Model(MultiClient? client) : this(client, StyleSet.Default()) { }

    public Model(MultiClient? client, StyleSet styles)
    {
        _client = client;
        _styles = styles;
        bool isMulti = client is not null && client.IsMultiProject();

        var columns = new List<ColumnSpec>
        {
            new("Type", 10, 8),
            new("ID", 8, 6),
            new("Title", 40, 25),
            new("State", 10, 10),
            new("Prio", 6, 4),
            new("Assigned", 26, 10),
        };

        if (isMulti)
            columns.Insert(0, new ColumnSpec("Project", 10, 8));

        columns = ListView<WorkItem>.NormalizeWidths(columns);

        Func<IReadOnlyList<WorkItem>, StyleSet, List<string[]>> toRows =
            isMulti ? Format.WorkItemsToRowsMulti : Format.WorkItemsToRows;

        Func<WorkItem, string, bool> filterFunc =
            isMulti ? Format.FilterWorkItemMulti : Format.FilterWorkItem;

        var cfg = new ListConfig<WorkItem>
        {
            Columns = columns,
            LoadingMessage = "Loading work items...",
            EntityName = "work items",
            MinWidth = 50,
            ToRows = toRows,
            Fetch = () => FetchWorkItems(),
            EnterDetail = (item, st, w, h) =>
            {
                Client? projectClient = client?.ClientFor(item.ProjectName);
                var d = new DetailModel(projectClient, item, st);
                d.SetSize(w, h);
                return (d, d.Init());
            },
            HasContextBar = mode => mode == ViewMode.Detail,
            FilterFunc = filterFunc,
        };

        _list = new ListView<WorkItem>(cfg, styles);
        _tagPicker = new TagPicker(styles);
        _statePicker = new ListPicker(styles);
    }

    public Cmd? Init() => _list.Init();

    public Cmd? Update(IMsg msg)
    {
        if (msg is WindowSizeMsg ws) { _width = ws.Width; _height = ws.Height; }

        switch (msg)
        {
            case WorkItemsMsg wim:
                if (wim.Err is not null)
                {
                    if (wim.Err is PartialException pe)
                    {
                        _allItems = (List<WorkItem>)pe.PartialData!;
                        if (_myItemsOnly)
                            return FetchMyWorkItems();
                        _list.HandleFetchResult(_allItems, null);
                        return TryRestoreDetail(null);
                    }

                    var criticalCmd = ErrorClassifier.NewCriticalErrorCmd(wim.Err);
                    if (criticalCmd is not null)
                    {
                        _list.HandleFetchResult(null, null);
                        return criticalCmd;
                    }
                    _list.HandleFetchResult(wim.WorkItems, wim.Err);
                    return TryRestoreDetail(null);
                }
                _allItems = wim.WorkItems.ToList();
                if (_myItemsOnly)
                    return FetchMyWorkItems();
                _list.HandleFetchResult(_allItems, null);
                return TryRestoreDetail(null);

            case MyWorkItemsMsg mim:
                if (mim.Err is not null)
                {
                    if (mim.Err is PartialException mpe)
                    {
                        _myItems = (List<WorkItem>)mpe.PartialData!;
                        _list.SetItems(ApplyAllFilters(_myItems));
                        return TryRestoreDetail(null);
                    }
                    // On error, fall back to all items and clear loading.
                    _myItemsOnly = false;
                    _myItems = new();
                    _list.SetItems(ApplyAllFilters(_allItems));
                    return TryRestoreDetail(null);
                }
                _myItems = mim.WorkItems.ToList();
                _list.SetItems(ApplyAllFilters(_myItems));
                return TryRestoreDetail(null);

            case WorkItemStateChangedMsg:
                return FetchWorkItems();

            case SetWorkItemsMsg swm:
                _allItems = swm.WorkItems.ToList();
                if (!_myItemsOnly)
                {
                    _list.SetItems(ApplyAllFilters(_allItems));
                    return TryRestoreDetail(null);
                }
                return null;

            case TagSelectedMsg ts:
                _activeTag = ts.Tag;
                _tagPicker.Hide();
                _list.SetItems(ApplyAllFilters(GetBaseItems()));
                return null;

            case ListPickerSelectedMsg ps:
                _activeState = ps.Value;
                _statePicker.Hide();
                _list.SetItems(ApplyAllFilters(GetBaseItems()));
                return null;

            case KeyMsg key:
                bool pickerOpen = _tagPicker.IsVisible || _statePicker.IsVisible;
                if (!pickerOpen)
                {
                    bool onList = GetViewMode() == ViewMode.List && !_list.IsSearching;
                    if (key.Key == "T" && onList)
                    {
                        var tags = Format.CollectUniqueTags(_allItems);
                        _tagPicker.SetTags(tags, _activeTag);
                        _tagPicker.SetSize(_width, _height);
                        _tagPicker.Show();
                        return null;
                    }
                    if (key.Key == "m" && onList)
                    {
                        _myItemsOnly = !_myItemsOnly;
                        if (_myItemsOnly)
                            return FetchMyWorkItems();
                        _myItems = new();
                        _list.SetItems(ApplyAllFilters(_allItems));
                        return null;
                    }
                    if (key.Key == "s" && onList)
                    {
                        var states = Format.CollectUniqueStates(_allItems);
                        var options = states
                            .Select(state => new ListPickerOption { Name = state, Icon = "●" })
                            .ToList();
                        _statePicker.SetConfig("Filter by State", options, _activeState, true);
                        _statePicker.SetSize(_width, _height);
                        _statePicker.Show();
                        return null;
                    }
                }
                break;
        }

        // When the tag picker is visible, route all input to it.
        if (_tagPicker.IsVisible)
            return msg is KeyMsg ? _tagPicker.Update(msg) : null;

        // When the state picker is visible, route all input to it.
        if (_statePicker.IsVisible)
            return msg is KeyMsg ? _statePicker.Update(msg) : null;

        // When in detail view, intercept esc to check for modals first. If the
        // detail view has a modal/form open, route esc directly to it so the
        // listview doesn't close the whole detail view.
        if (GetViewMode() == ViewMode.Detail && msg is KeyMsg { Key: "esc" })
        {
            if (_list.Detail is DetailModel dm && (dm.IsStatePickerVisible || dm.IsCommentFormVisible))
                return dm.Update(msg);
        }

        return _list.Update(msg);
    }

    public string View()
    {
        if (_tagPicker.IsVisible)
            return _tagPicker.View();
        if (_statePicker.IsVisible)
            return _statePicker.View();
        return _list.View();
    }

    // --- ITabView surface ---

    public ViewMode GetViewMode() => _list.GetViewMode();
    public IReadOnlyList<ContextItem> GetContextItems() => _list.GetContextItems();
    public double GetScrollPercent() => _list.GetScrollPercent();
    public string GetStatusMessage() => _list.GetStatusMessage();
    public bool HasContextBar() => _list.HasContextBar();
    public bool IsSearching() => _list.IsSearching;

    public bool IsCapturingInput()
    {
        if (_tagPicker.IsVisible || _statePicker.IsVisible) return true;
        return IsCommentFormVisible();
    }

    public string FilterLabel()
    {
        var parts = new List<string>();
        if (_myItemsOnly) parts.Add("My Items");
        if (_activeTag != "") parts.Add($"Tag: {_activeTag}");
        if (_activeState != "") parts.Add($"State: {_activeState}");
        return string.Join(" + ", parts);
    }

    public string DefaultKeybindings()
    {
        string K(string k) => _styles.Key.Render(k);
        string D(string d) => _styles.Description.Render(d);
        var items = new[]
        {
            $"{K("r")} {D("refresh")}",
            $"{K("↑↓")} {D("navigate")}",
            $"{K("enter")} {D("details")}",
            $"{K("f")} {D("search")}",
            $"{K("m")} {D("my items")}",
            $"{K("T")} {D("tags")}",
            $"{K("s")} {D("state")}",
            $"{K("esc")} {D("back")}",
            $"{K("?")} {D("help")}",
            $"{K("q")} {D("quit")}",
        };
        return string.Join(" • ", items);
    }

    // --- Filter / picker accessors (for the app + tests) ---

    public bool IsMyItemsActive() => _myItemsOnly;
    public bool IsTagFilterActive() => _activeTag != "";
    public string ActiveTag() => _activeTag;
    public bool IsStateFilterActive() => _activeState != "";
    public string ActiveState() => _activeState;
    public bool IsTagPickerVisible() => _tagPicker.IsVisible;
    public bool IsStatePickerVisible() => _statePicker.IsVisible;
    public string TagPickerSearchQuery() => _tagPicker.SearchQuery();

    /// <summary>
    /// Whether the detail view's comment form is open. Used by the app to suppress
    /// global shortcuts so keystrokes reach the form.
    /// </summary>
    public bool IsCommentFormVisible()
    {
        if (GetViewMode() != ViewMode.Detail) return false;
        return _list.Detail is DetailModel dm && dm.IsCommentFormVisible;
    }

    // --- IRestorableTab ---

    public int DetailItemId()
    {
        if (GetViewMode() != ViewMode.Detail) return 0;
        return _list.Detail is DetailModel dm ? dm.GetWorkItem().Id : 0;
    }

    public void SetPendingDetailRestore(int id)
    {
        _pendingDetailId = id;
        _pendingRestoreHandled = false;
    }

    /// <summary>Attempts to open detail for the pending ID, if any (≈ <c>tryRestoreDetail</c>).</summary>
    private Cmd? TryRestore()
    {
        if (_pendingRestoreHandled || _pendingDetailId == 0) return null;
        int target = _pendingDetailId;
        _pendingDetailId = 0;
        _pendingRestoreHandled = true;

        int idx = _list.FindIndex(wi => wi.Id == target);
        if (idx < 0) return null;
        _list.SetCursor(idx);
        return _list.OpenSelectedDetail();
    }

    /// <summary>Combines <see cref="TryRestore"/> with a caller-supplied cmd (≈ <c>withRestore</c>).</summary>
    private Cmd? TryRestoreDetail(Cmd? prev)
    {
        var restoreCmd = TryRestore();
        return Commands.Batch(prev, restoreCmd);
    }

    // --- Internal helpers ---

    private List<WorkItem> GetBaseItems() => _myItemsOnly ? _myItems : _allItems;

    private List<WorkItem> ApplyAllFilters(IReadOnlyList<WorkItem> items)
    {
        var result = Format.ApplyTagFilter(items, _activeTag);
        result = Format.ApplyStateFilter(result, _activeState);
        return result;
    }

    private Cmd FetchWorkItems()
    {
        var client = _client;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new WorkItemsMsg(Array.Empty<WorkItem>(), null);
            try
            {
                var items = await client.ListWorkItemsAsync(Top).ConfigureAwait(false);
                return new WorkItemsMsg(items, null);
            }
            catch (PartialException pe)
            {
                return new WorkItemsMsg((List<WorkItem>)pe.PartialData!, pe);
            }
            catch (Exception e)
            {
                return new WorkItemsMsg(Array.Empty<WorkItem>(), e);
            }
        });
    }

    private Cmd FetchMyWorkItems()
    {
        var client = _client;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new MyWorkItemsMsg(Array.Empty<WorkItem>(), null);
            try
            {
                var items = await client.ListMyWorkItemsAsync(Top).ConfigureAwait(false);
                return new MyWorkItemsMsg(items, null);
            }
            catch (PartialException pe)
            {
                return new MyWorkItemsMsg((List<WorkItem>)pe.PartialData!, pe);
            }
            catch (Exception e)
            {
                return new MyWorkItemsMsg(Array.Empty<WorkItem>(), e);
            }
        });
    }

    // --- Test seams: expose the inner list for parity with the Go tests ---

    internal IReadOnlyList<WorkItem> ListItems => _list.Items;
    internal IReadOnlyList<WorkItem> AllItems => _allItems;
    internal void SetListItemsForTest(IEnumerable<WorkItem> items) => _list.SetItems(items);
    internal Cmd? UpdateListForTest(IMsg msg) => _list.Update(msg);
    internal IDetailView? Detail => _list.Detail;
}
