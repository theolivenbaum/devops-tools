using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Components;

/// <summary>A table column (title + rendered width in cells).</summary>
public sealed record TableColumn(string Title, int Width);

/// <summary>
/// Scrollable table with ANSI-aware cell truncation (≈ the local fork of
/// bubbles <c>table</c>). Keeps a cursor and a viewport offset; cells that
/// contain SGR escapes are truncated by visible width so columns stay aligned.
/// </summary>
public sealed class Table
{
    private List<TableColumn> _cols = new();
    private List<string[]> _rows = new();
    private int _cursor;
    private int _yOffset;
    private int _viewportHeight = 10;
    private int _width;
    public bool Focused { get; set; } = true;

    private Style _header;
    private Style _cell;
    private Style _selected;

    public Table(Styles.Styles styles)
    {
        _header = styles.TableHeader;
        _cell = styles.TableCell;
        _selected = styles.TableSelected;
    }

    public void SetStyles(Style header, Style cell, Style selected)
    {
        _header = header; _cell = cell; _selected = selected;
    }

    public void SetColumns(IEnumerable<TableColumn> cols) => _cols = cols.ToList();
    public IReadOnlyList<TableColumn> Columns => _cols;

    public void SetRows(IEnumerable<string[]> rows)
    {
        _rows = rows.ToList();
        if (_cursor > _rows.Count - 1) _cursor = Math.Max(0, _rows.Count - 1);
        ClampOffset();
    }

    public IReadOnlyList<string[]> Rows => _rows;

    public int Cursor => _cursor;
    public string[]? SelectedRow => _cursor >= 0 && _cursor < _rows.Count ? _rows[_cursor] : null;

    public void SetCursor(int n)
    {
        _cursor = Math.Clamp(n, 0, Math.Max(0, _rows.Count - 1));
        ClampOffset();
    }

    public int Width => _width;
    public void SetWidth(int w) => _width = w;

    /// <summary>Sets total height; visible rows = height − header height.</summary>
    public void SetHeight(int h)
    {
        _viewportHeight = Math.Max(1, h - Ansi.Height(HeadersView()));
        ClampOffset();
    }

    public int Height => _viewportHeight;

    public void MoveUp(int n) { _cursor = Math.Clamp(_cursor - n, 0, Math.Max(0, _rows.Count - 1)); ClampOffset(); }
    public void MoveDown(int n) { _cursor = Math.Clamp(_cursor + n, 0, Math.Max(0, _rows.Count - 1)); ClampOffset(); }
    public void GotoTop() => SetCursor(0);
    public void GotoBottom() => SetCursor(_rows.Count - 1);

    private void ClampOffset()
    {
        if (_cursor < _yOffset) _yOffset = _cursor;
        else if (_cursor >= _yOffset + _viewportHeight) _yOffset = _cursor - _viewportHeight + 1;
        if (_yOffset < 0) _yOffset = 0;
    }

    public Cmd? Update(IMsg msg)
    {
        if (!Focused) return null;
        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "up": case "k": MoveUp(1); break;
                case "down": case "j": MoveDown(1); break;
                case "pgup": MoveUp(_viewportHeight); break;
                case "pgdown": MoveDown(_viewportHeight); break;
            }
        }
        return null;
    }

    public string View() => HeadersView() + "\n" + ViewportView();

    private string HeadersView()
    {
        var cells = new List<string>();
        foreach (var col in _cols)
        {
            if (col.Width <= 0) continue;
            var inner = Style.New().Width(col.Width).MaxWidth(col.Width).Render(Truncate(col.Title, col.Width));
            cells.Add(_header.Render(inner));
        }
        return cells.Count == 0 ? string.Empty : Layout.JoinHorizontal(VAlign.Top, cells.ToArray());
    }

    private string ViewportView()
    {
        if (_rows.Count == 0) return string.Empty;
        int end = Math.Min(_rows.Count, _yOffset + _viewportHeight);
        var lines = new List<string>();
        for (int i = _yOffset; i < end; i++) lines.Add(RenderRow(i));
        return string.Join("\n", lines);
    }

    private string RenderRow(int r)
    {
        bool isSelected = r == _cursor;
        var cells = new List<string>();
        var row = _rows[r];
        for (int i = 0; i < _cols.Count && i < row.Length; i++)
        {
            if (_cols[i].Width <= 0) continue;
            var inner = Style.New().Width(_cols[i].Width).MaxWidth(_cols[i].Width).Render(Truncate(row[i], _cols[i].Width));
            cells.Add(_cell.Render(inner));
        }
        var line = Layout.JoinHorizontal(VAlign.Top, cells.ToArray());
        if (isSelected)
        {
            int w = _width > 0 ? _width : Ansi.Width(line);
            return _selected.Width(w).Render(Ansi.Strip(line));
        }
        return line;
    }

    private static string Truncate(string value, int width)
    {
        if (Ansi.Width(value) <= width) return value;
        if (width <= 1) return Ansi.Truncate(value, width);
        return Ansi.Truncate(value, width - 1) + "…";
    }
}
