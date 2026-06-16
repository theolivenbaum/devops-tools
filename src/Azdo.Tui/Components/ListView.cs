using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Components;

public enum ViewMode { List, Detail }

/// <summary>A column with percentage-based width and a minimum (≈ <c>listview.ColumnSpec</c>).</summary>
public sealed record ColumnSpec(string Title, int WidthPct, int MinWidth);

/// <summary>Domain-specific callbacks configuring a <see cref="ListView{T}"/> (≈ <c>listview.Config</c>).</summary>
public sealed class ListConfig<T>
{
    public required IReadOnlyList<ColumnSpec> Columns { get; init; }
    public string LoadingMessage { get; init; } = "Loading...";
    public string EntityName { get; init; } = "items";
    public int MinWidth { get; init; } = 70;
    public required Func<IReadOnlyList<T>, Styles.Styles, List<string[]>> ToRows { get; init; }
    public required Func<Cmd> Fetch { get; init; }
    public required Func<T, Styles.Styles, int, int, (IDetailView Detail, Cmd? Cmd)> EnterDetail { get; init; }
    public Func<ViewMode, bool>? HasContextBar { get; init; }
    public Func<T, string, bool>? FilterFunc { get; init; }
}

/// <summary>
/// Generic, callback-driven list view shared by all tabs (≈ <c>listview.Model[T]</c>):
/// scrollable table, inline search/filter, and list↔detail toggling.
/// </summary>
public sealed class ListView<T>
{
    private const int SearchBarHeight = 1;

    private readonly Table _table;
    private List<T> _items = new();
    private List<T>? _filteredItems;
    private bool _searching;
    private readonly TextInput _searchInput = new() { Prompt = "/ ", CharLimit = 100 };
    private string _searchQuery = "";
    private bool _loading;
    private Exception? _err;
    private int _width = 80;
    private int _height = 24;
    private ViewMode _viewMode = ViewMode.List;
    private IDetailView? _detail;
    private readonly LoadingIndicator _spinner;
    private readonly Styles.Styles _styles;
    private readonly ListConfig<T> _config;

    public ListView(ListConfig<T> config, Styles.Styles styles)
    {
        _config = config;
        _styles = styles;
        _table = new Table(styles) { Focused = true };
        _table.SetColumns(MakeColumns(config.Columns, 80, EffectiveMinWidth));
        _spinner = new LoadingIndicator(styles);
        _spinner.SetMessage(config.LoadingMessage);
    }

    private int EffectiveMinWidth => _config.MinWidth == 0 ? 70 : _config.MinWidth;

    public Cmd? Init()
    {
        _loading = true;
        _spinner.SetVisible(true);
        return Commands.Batch(_config.Fetch(), _spinner.Init());
    }

    public Cmd? Update(IMsg msg)
    {
        if (msg is WindowSizeMsg w) { _width = w.Width; _height = w.Height; }
        return _viewMode == ViewMode.Detail ? UpdateDetail(msg) : UpdateList(msg);
    }

    private Cmd? UpdateList(IMsg msg)
    {
        switch (msg)
        {
            case WindowSizeMsg ws:
                int tableHeight = ws.Height - 1;
                if (_searching) tableHeight -= SearchBarHeight;
                _table.SetWidth(ws.Width);
                _table.SetColumns(MakeColumns(_config.Columns, ws.Width, EffectiveMinWidth));
                _table.SetHeight(tableHeight);
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case KeyMsg key:
                if (_searching) return UpdateSearch(key);
                switch (key.Key)
                {
                    case "r":
                        _loading = true;
                        _spinner.SetVisible(true);
                        return Commands.Batch(_config.Fetch(), _spinner.Tick());
                    case "enter":
                        return EnterDetailView();
                    case "f" when _config.FilterFunc is not null:
                        return EnterSearch();
                }
                return _table.Update(msg);
        }
        return _table.Update(msg);
    }

    private Cmd? EnterSearch()
    {
        _searching = true;
        _searchInput.Value = "";
        _searchQuery = "";
        _searchInput.Focus();
        _table.SetHeight(_height - 1 - SearchBarHeight);
        return null;
    }

    private Cmd? ExitSearch()
    {
        _searching = false;
        _searchQuery = "";
        _searchInput.Blur();
        _filteredItems = null;
        _table.SetRows(_config.ToRows(_items, _styles));
        _table.SetHeight(_height - 1);
        return null;
    }

    private Cmd? UpdateSearch(KeyMsg key)
    {
        switch (key.Key)
        {
            case "esc": return ExitSearch();
            case "enter": return EnterDetailView();
            case "up": case "down": case "pgup": case "pgdown":
                return _table.Update(key);
        }
        _searchInput.HandleKey(key);
        var newQuery = _searchInput.Value;
        if (newQuery != _searchQuery)
        {
            _searchQuery = newQuery;
            ApplyFilter();
        }
        return null;
    }

    private void ApplyFilter()
    {
        if (_searchQuery == "")
        {
            _filteredItems = null;
            _table.SetRows(_config.ToRows(_items, _styles));
            return;
        }
        _filteredItems = _items.Where(i => _config.FilterFunc!(i, _searchQuery)).ToList();
        _table.SetRows(_config.ToRows(_filteredItems, _styles));
    }

    private Cmd? UpdateDetail(IMsg msg)
    {
        if (_detail is null) { _viewMode = ViewMode.List; return null; }
        switch (msg)
        {
            case WindowSizeMsg ws:
                _detail.SetSize(ws.Width, ws.Height);
                break;
            case KeyMsg { Key: "esc" }:
                _viewMode = ViewMode.List;
                _detail = null;
                return null;
        }
        return _detail?.Update(msg);
    }

    private Cmd? EnterDetailView()
    {
        int idx = _table.Cursor;
        var source = _searching && _filteredItems is not null ? _filteredItems : _items;
        if (idx < 0 || idx >= source.Count) return null;
        var (detail, cmd) = _config.EnterDetail(source[idx], _styles, _width, _height);
        _detail = detail;
        _viewMode = ViewMode.Detail;
        return cmd;
    }

    public string View()
        => _viewMode == ViewMode.Detail && _detail is not null ? _detail.View() : ViewList();

    private string ViewList()
    {
        string content;
        if (_err is not null)
            content = $"Error loading {_config.EntityName}: {_err.Message}\n\nPress r to retry, q to quit";
        else if (_loading)
            content = _spinner.View() + "\n\nPress q to quit";
        else if (_items.Count == 0)
            content = $"No {_config.EntityName} found.\n\nPress r to refresh, q to quit";
        else
        {
            var tableView = _table.View();
            return _searching ? tableView + "\n" + SearchBarView() : tableView;
        }
        return Style.New().Width(_width).Render(content);
    }

    private string SearchBarView()
    {
        int total = _items.Count;
        int matched = _searchQuery != "" && _filteredItems is not null ? _filteredItems.Count : total;
        return _searchInput.View() + $" {matched}/{total}";
    }

    /// <summary>Sets items directly (e.g. from polling), clearing loading/error state.</summary>
    public void SetItems(IEnumerable<T> items)
    {
        _loading = false;
        _spinner.SetVisible(false);
        _err = null;
        _items = items.ToList();
        if (_searching && _searchQuery != "" && _config.FilterFunc is not null) ApplyFilter();
        else _table.SetRows(_config.ToRows(_items, _styles));
    }

    /// <summary>Handles a fetch response (items or error).</summary>
    public void HandleFetchResult(IReadOnlyList<T>? items, Exception? err)
    {
        _loading = false;
        _spinner.SetVisible(false);
        if (err is not null) { _err = err; return; }
        _items = (items ?? Array.Empty<T>()).ToList();
        if (_searching && _searchQuery != "" && _config.FilterFunc is not null) ApplyFilter();
        else _table.SetRows(_config.ToRows(_items, _styles));
    }

    public bool IsSearching => _searching;
    public IReadOnlyList<T> Items => _items;
    public int SelectedIndex => _table.Cursor;
    public ViewMode GetViewMode() => _viewMode;
    public IDetailView? Detail => _detail;

    public IReadOnlyList<ContextItem> GetContextItems()
        => _viewMode == ViewMode.Detail && _detail is not null ? _detail.GetContextItems() : Array.Empty<ContextItem>();

    public double GetScrollPercent()
        => _viewMode == ViewMode.Detail && _detail is not null ? _detail.GetScrollPercent() : 0;

    public string GetStatusMessage()
        => _viewMode == ViewMode.Detail && _detail is not null ? _detail.GetStatusMessage() : "";

    public bool HasContextBar() => _config.HasContextBar?.Invoke(_viewMode) ?? false;

    public int FindIndex(Func<T, bool> pred)
    {
        for (int i = 0; i < _items.Count; i++) if (pred(_items[i])) return i;
        return -1;
    }

    public void SetCursor(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return;
        _table.SetCursor(idx);
    }

    public Cmd? OpenSelectedDetail() => EnterDetailView();

    private const int CellPadding = 2;

    /// <summary>Sizes columns from specs for the given width, clamping to minimums.</summary>
    public static List<TableColumn> MakeColumns(IReadOnlyList<ColumnSpec> specs, int width, int minWidth)
    {
        int available = width - specs.Count * CellPadding;
        if (available < minWidth) available = minWidth;

        var widths = new int[specs.Count];
        var clamped = new bool[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            int w = available * specs[i].WidthPct / 100;
            if (w < specs[i].MinWidth) { w = specs[i].MinWidth; clamped[i] = true; }
            widths[i] = w;
        }

        int total = widths.Sum();
        if (total > available)
        {
            int overflow = total - available;
            int flexTotal = 0;
            for (int i = 0; i < widths.Length; i++) if (!clamped[i]) flexTotal += widths[i];
            if (flexTotal > 0)
            {
                int shrunk = 0, lastFlex = -1;
                for (int i = 0; i < widths.Length; i++) if (!clamped[i]) lastFlex = i;
                for (int i = 0; i < widths.Length; i++)
                {
                    if (clamped[i]) continue;
                    int reduction = i == lastFlex ? overflow - shrunk : overflow * widths[i] / flexTotal;
                    widths[i] -= reduction;
                    if (widths[i] < 1) widths[i] = 1;
                    shrunk += reduction;
                }
            }
        }

        var columns = new List<TableColumn>(specs.Count);
        for (int i = 0; i < specs.Count; i++) columns.Add(new TableColumn(specs[i].Title, widths[i]));
        return columns;
    }

    /// <summary>Normalizes percentage widths to sum to 100 (≈ <c>listview.NormalizeWidths</c>).</summary>
    public static List<ColumnSpec> NormalizeWidths(IReadOnlyList<ColumnSpec> cols)
    {
        int total = cols.Sum(c => c.WidthPct);
        if (total == 0) return cols.ToList();
        var result = new List<ColumnSpec>(cols.Count);
        int assigned = 0;
        for (int i = 0; i < cols.Count; i++)
        {
            int pct = i == cols.Count - 1 ? 100 - assigned : cols[i].WidthPct * 100 / total;
            if (i != cols.Count - 1) assigned += pct;
            result.Add(cols[i] with { WidthPct = pct });
        }
        return result;
    }
}
