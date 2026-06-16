using System.Globalization;
using System.Text;
using Azdo.Core.Metrics;
using Azdo.Tui.Rendering;

namespace Azdo.Tui.Views.Metrics;

public sealed partial class Model
{
    // NOTE: The Go reference uses the ntcharts canvas/graph package to render a
    // grouped bar chart with per-cell colours. This C# port approximates that
    // with a simple block-based bar chart drawn into a char grid using unicode
    // full-block runes. The geometry/legend/readout logic is faithful; the
    // visual fidelity (anti-aliased partial-cell columns, per-rune colouring) is
    // simplified to whole-cell blocks. Bar colours are applied per sprint cluster
    // line rather than per individual cell to keep the renderer self-contained.

    private const int MinChartWidth = 24;
    private const int MinChartHeight = 8;
    private const int ChartChromeRows = 11;

    private static readonly string[] BarPalette =
    {
        "#88c0d0", "#a3be8c", "#ebcb8b", "#bf616a",
        "#b48ead", "#81a1c1", "#d08770", "#8fbcbb",
    };

    internal static string UserColor(int i) => BarPalette[((i % BarPalette.Length) + BarPalette.Length) % BarPalette.Length];

    private int FocusedIndex(int nUsers)
    {
        if (_focusedUser < 0 || _focusedUser >= nUsers) return -1;
        return _focusedUser;
    }

    private string RenderTrendsChart()
    {
        var (preMsg, ok) = TrendsPreamble();
        if (!ok) return preMsg;
        if (_sprintWindows.Count < 2)
            return MutedOr("Pick at least 2 sprints (press T) to see a trend.");

        var metric = _chartMetric;
        var series = ChartData.BuildSeries(_trendRows, metric);
        double yMax = ChartData.NiceCeil(ChartData.SeriesMax(series));

        var (w, h) = ChartCanvasSize();
        if (w < MinChartWidth || h < MinChartHeight)
            return MutedOr("Window too small for the chart — widen the terminal or press v for the table.");

        var chart = RenderBarCanvas(w, h, _sprintWindows.Count, yMax, series);

        var b = new StringBuilder();
        b.Append(ChartHeader(metric)).Append("\n\n");
        b.Append(chart).Append('\n');
        b.Append(SprintLegend()).Append('\n');
        b.Append(UserLegend(series)).Append("\n\n");
        b.Append(ChartReadout(metric, series)).Append("\n\n");
        b.Append(MetricsGlossary()).Append('\n');
        b.Append(ChartHints());
        return b.ToString().TrimEnd('\n');
    }

    private (string Msg, bool Ok) TrendsPreamble()
    {
        int snapDays = DistinctSnapshotDays(_snapshots);
        if (snapDays < MinSnapshotDaysForTrends)
            return (MutedOr($"Insufficient snapshot history ({snapDays}/{MinSnapshotDaysForTrends} days) — Trends becomes useful after ~2 sprints."), false);
        if (_sprintWindows.Count == 0)
            return (MutedOr("No sprints picked. Press T to choose."), false);
        if (_trendRows.Count == 0)
            return (MutedOr("No data for the selected sprints in the snapshot file."), false);
        return ("", true);
    }

    private (int W, int H) ChartCanvasSize()
    {
        int w = _viewport?.Width ?? 0;
        if (w <= 0) w = _width;
        int h = _viewport?.Height ?? 0;
        if (h <= 0) h = _height;
        h -= ChartChromeRows;
        if (h > 22) h = 22;
        return (w, h);
    }

    /// <summary>
    /// Draws axes, Y labels and grouped per-user block bars into a char grid.
    /// Approximation of the ntcharts canvas — whole-cell blocks, no partials.
    /// </summary>
    private string RenderBarCanvas(int w, int h, int nSprints, double yMax, List<Series> series)
    {
        var g = ChartGeom.New(w, h, nSprints, yMax);
        int gutterW = g.GutterW;
        int axisX = g.AxisX;
        int plotLeft = g.PlotLeft;
        int plotRight = g.PlotRight;
        int plotTop = g.PlotTop;
        int plotBottom = g.PlotBottom;

        var grid = new char[h][];
        for (int y = 0; y < h; y++)
        {
            grid[y] = new char[w];
            for (int x = 0; x < w; x++) grid[y][x] = ' ';
        }

        // Axes.
        for (int y = plotTop; y <= plotBottom; y++)
            if (axisX < w) grid[y][axisX] = '│';
        for (int x = axisX; x <= plotRight && x < w; x++)
            grid[plotBottom][x] = '─';
        if (axisX < w) grid[plotBottom][axisX] = '└';

        // Y labels (right-aligned in gutter).
        DrawYLabel(grid, gutterW, plotTop, plotTop, plotBottom, yMax);
        DrawYLabel(grid, gutterW, (plotTop + plotBottom) / 2, plotTop, plotBottom, yMax / 2);
        DrawYLabel(grid, gutterW, plotBottom, plotTop, plotBottom, 0);

        // Bars.
        int nUsers = series.Count;
        var spans = BarLayout(plotLeft, plotRight, nSprints, nUsers);
        double rows = plotBottom; // cells above axis
        int focused = FocusedIndex(nUsers);

        // Track which user owns each plotted column so we can colour by user.
        var colOwner = new int[w];
        for (int i = 0; i < w; i++) colOwner[i] = -1;

        if (spans is not null)
        {
            for (int s = 0; s < nSprints; s++)
            {
                for (int u = 0; u < nUsers; u++)
                {
                    if (s >= series[u].Points.Count) continue;
                    var p = series[u].Points[s];
                    if (!p.Present) continue;
                    double frac = yMax > 0 ? p.Value / yMax : 0;
                    frac = Math.Clamp(frac, 0, 1);
                    double hCells = frac * rows;
                    if (hCells <= 0) continue;
                    int cells = (int)Math.Round(hCells);
                    if (cells < 1 && p.Value > 0) cells = 1;
                    var span = spans[s][u];
                    for (int x = span.X0; x <= span.X1 && x <= plotRight && x < w; x++)
                    {
                        colOwner[x] = u;
                        for (int c = 0; c < cells; c++)
                        {
                            int y = plotBottom - 1 - c;
                            if (y >= plotTop && y < h) grid[y][x] = '█';
                        }
                    }
                }
            }
        }

        // Render rows to strings, colouring each char column by its owning user.
        var sb = new StringBuilder();
        var axisStyle = _styles is not null ? Style.New().Foreground(_styles.Theme.ForegroundMuted) : Style.New();
        for (int y = 0; y < h; y++)
        {
            var line = new StringBuilder();
            for (int x = 0; x < w; x++)
            {
                char ch = grid[y][x];
                if (ch == '█' && colOwner[x] >= 0)
                {
                    line.Append(UserStyle(colOwner[x], focused).Render("█"));
                }
                else if ((ch == '│' || ch == '─' || ch == '└') && _styles is not null)
                {
                    line.Append(axisStyle.Render(ch.ToString()));
                }
                else
                {
                    line.Append(ch);
                }
            }
            sb.Append(line.ToString().TrimEnd());
            if (y < h - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private void DrawYLabel(char[][] grid, int gutterW, int row, int plotTop, int plotBottom, double v)
    {
        if (row < plotTop || row > plotBottom) return;
        var s = FmtAxisVal(v);
        if (s.Length > gutterW) s = s[..gutterW];
        int start = gutterW - s.Length;
        for (int i = 0; i < s.Length && start + i < grid[row].Length; i++)
            grid[row][start + i] = s[i];
    }

    /// <summary>
    /// Lays out grouped bars: for each sprint, one bar per user, clustered and
    /// centred within an evenly divided slot. Returns spans indexed [sprint][user].
    /// </summary>
    public static BarSpan[][]? BarLayout(int plotLeft, int plotRight, int nSprints, int nUsers)
    {
        if (nSprints < 1 || nUsers < 1) return null;
        int plotW = plotRight - plotLeft + 1;
        int slotW = Math.Max(1, plotW / nSprints);
        int inner = Math.Max(slotW - 2, nUsers);
        int barW = Math.Max(1, inner / nUsers);
        int groupW = barW * nUsers;

        var outArr = new BarSpan[nSprints][];
        for (int s = 0; s < nSprints; s++)
        {
            int slotStart = plotLeft + s * slotW;
            int pad = Math.Max(0, (slotW - groupW) / 2);
            int gx = slotStart + pad;
            var group = new BarSpan[nUsers];
            for (int u = 0; u < nUsers; u++)
            {
                int x0 = gx + u * barW;
                group[u] = new BarSpan { X0 = x0, X1 = x0 + barW - 1 };
            }
            outArr[s] = group;
        }
        return outArr;
    }

    private Style UserStyle(int u, int focused)
    {
        if (focused >= 0 && u != focused)
        {
            var s = Style.New();
            if (_styles is not null) s = s.Foreground(_styles.Theme.ForegroundMuted);
            return s;
        }
        return Style.New().Foreground(UserColor(u));
    }

    private string ChartHeader(MetricKind metric)
    {
        var left = $"Trends · chart · {metric.Label()}";
        return _styles is not null ? _styles.Header.Render(left) : left;
    }

    private string UserLegend(List<Series> series)
    {
        int focused = FocusedIndex(series.Count);
        var parts = new List<string>();
        for (int i = 0; i < series.Count; i++)
        {
            var swatch = UserStyle(i, focused).Render("█");
            var entry = swatch + " " + series[i].User;
            if (i == focused)
            {
                entry = "▸" + entry;
                if (_styles is not null) entry = _styles.Selected.Render(entry);
            }
            else if (focused >= 0 && _styles is not null)
            {
                entry = _styles.Muted.Render(entry);
            }
            parts.Add(entry);
        }
        return string.Join("   ", parts);
    }

    private string SprintLegend()
    {
        var parts = new List<string>();
        for (int i = 0; i < _sprintWindows.Count; i++)
        {
            var label = $"{i + 1} {_sprintWindows[i].Tag}";
            if (i == ClampSprintCursor())
            {
                label = "▸" + label + "◂";
                if (_styles is not null) label = _styles.Selected.Render(label);
            }
            else if (_styles is not null)
            {
                label = _styles.Muted.Render(label);
            }
            parts.Add(label);
        }
        return string.Join("  ", parts);
    }

    private string ChartReadout(MetricKind metric, List<Series> series)
    {
        int idx = ClampSprintCursor();
        var w = _sprintWindows[idx];
        var head = $"{w.Tag} ({w.Start.ToString("MMM d", CultureInfo.InvariantCulture)}–{w.End.ToString("MMM d", CultureInfo.InvariantCulture)})";
        if (_styles is not null) head = _styles.Value.Render(head);

        int focused = FocusedIndex(series.Count);
        var parts = new List<string> { head };
        for (int i = 0; i < series.Count; i++)
        {
            if (idx >= series[i].Points.Count) continue;
            var chunk = $"{series[i].User} {ReadoutVal(metric, series[i].Points[idx])}";
            if (_styles is not null) chunk = UserStyle(i, focused).Render(chunk);
            parts.Add(chunk);
        }
        return string.Join("   ", parts);
    }

    private string ChartHints()
    {
        var hint = "h/l metric · ,/. sprint · f focus user · v back to table";
        return _styles is not null ? _styles.Muted.Render(hint) : hint;
    }

    private int ClampSprintCursor()
    {
        int idx = _sprintCursor;
        if (idx < 0) return 0;
        if (idx >= _sprintWindows.Count) return _sprintWindows.Count - 1;
        return idx;
    }

    private static string ReadoutVal(MetricKind metric, SeriesPoint p)
    {
        if (!p.Present) return metric.Short() + ":—";
        return metric switch
        {
            MetricKind.Stuck => $"{metric.Short()}:{(int)(p.Value + 0.5)}",
            MetricKind.Cycle => $"{metric.Short()}:{FmtAxisVal(p.Value)}d",
            _ => $"{metric.Short()}:{FmtAxisVal(p.Value)}",
        };
    }

    internal static string FmtAxisVal(double v)
    {
        if (v == Math.Floor(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("F1", CultureInfo.InvariantCulture);
    }
}
