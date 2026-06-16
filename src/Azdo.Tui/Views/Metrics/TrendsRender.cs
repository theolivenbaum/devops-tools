using System.Globalization;
using System.Text;
using Azdo.Core.Metrics;
using Azdo.Tui.Rendering;

namespace Azdo.Tui.Views.Metrics;

public sealed partial class Model
{
    private const int MinSnapshotDaysForTrends = 7;
    private const int TrendUserColW = 18;
    private const int TrendCellW = 22;
    private const int TrendCellGap = 2;

    private string RenderTrends()
    {
        int snapDays = DistinctSnapshotDays(_snapshots);
        if (snapDays < MinSnapshotDaysForTrends)
        {
            var msg = $"Insufficient snapshot history ({snapDays}/{MinSnapshotDaysForTrends} days) — " +
                "Trends view becomes useful after ~2 sprints. Run backfill by setting configuration " +
                "parameter runOneShotBackfill for immediate history.";
            return MutedOr(msg);
        }
        if (_sprintWindows.Count == 0)
            return MutedOr("No sprints picked. Press T to choose.");
        if (_trendRows.Count == 0)
            return MutedOr("No data for the selected sprints in the snapshot file.");

        var b = new StringBuilder();
        var subhead = $"Trends · {_sprintWindows.Count} sprints · {snapDays} days collected · Updated {LastUpdatedLabel()}";
        if (_styles is not null) subhead = _styles.Muted.Render(subhead);
        b.Append(subhead).Append('\n');
        b.Append(MetricsGlossary()).Append("\n\n");

        var gap = new string(' ', TrendCellGap);

        var tagLine = PadCol("", TrendUserColW);
        foreach (var w in _sprintWindows)
            tagLine += gap + PadCol(w.Tag, TrendCellW);
        var rangeLine = PadCol("", TrendUserColW);
        foreach (var w in _sprintWindows)
        {
            var rng = $"({w.Start.ToString("MMM d", CultureInfo.InvariantCulture)} – {w.End.ToString("MMM d", CultureInfo.InvariantCulture)})";
            rangeLine += gap + PadCol(rng, TrendCellW);
        }
        if (_styles is not null)
        {
            tagLine = _styles.Header.Render(tagLine);
            rangeLine = _styles.Muted.Render(rangeLine);
        }
        b.Append(tagLine).Append('\n');
        b.Append(rangeLine).Append('\n');
        b.Append(new string('─', SafeWidth(rangeLine))).Append('\n');

        for (int i = 0; i < _trendRows.Count; i++)
        {
            if (i > 0) b.Append('\n');
            AppendTrendRow(b, _trendRows[i].User, _trendRows[i].Cells);
        }

        var (total, ok) = ComputeTeamTotal(_trendRows);
        if (ok)
        {
            b.Append(new string('─', SafeWidth(rangeLine))).Append('\n');
            AppendTrendRow(b, total.User, total.Cells);
        }

        return b.ToString().TrimEnd('\n');
    }

    private void AppendTrendRow(StringBuilder b, string user, List<TrendCell> cells)
    {
        var gap = new string(' ', TrendCellGap);
        var userCell = PadCol(user, TrendUserColW);
        if (_styles is not null) userCell = _styles.Header.Render(userCell);
        var indent = PadCol("", TrendUserColW);

        var linePts = userCell;
        var lineWIP = indent;
        var lineStuck = indent;
        var lineCy = indent;

        foreach (var c in cells)
        {
            var wipMark = c.OverloadedAnyDay ? "⚠" : "";
            var ptsText = $"pts:{FmtPoints(c.Points)}";
            var wipText = $"wip:{FmtFloat1(c.AvgWIP)}{wipMark}";
            var stuckText = $"stuck:{c.StuckCount}";
            var cyText = $"cy:{FmtDwell(c.CycleTime)}";

            linePts += gap + PadCol(ColorPoints(ptsText, c.Points), TrendCellW);
            lineWIP += gap + PadCol(ColorWIP(wipText, c), TrendCellW);
            lineStuck += gap + PadCol(ColorStuck(stuckText, c.StuckCount), TrendCellW);
            lineCy += gap + PadCol(ColorCycle(cyText, c.CycleTime), TrendCellW);
        }

        b.Append(linePts).Append('\n');
        b.Append(lineWIP).Append('\n');
        b.Append(lineStuck).Append('\n');
        b.Append(lineCy).Append('\n');
    }

    private string ColorPoints(string s, double pts)
    {
        if (_styles is null) return s;
        return pts > 0 ? _styles.Success.Render(s) : _styles.Muted.Render(s);
    }

    private string ColorWIP(string s, TrendCell c)
    {
        if (_styles is null) return s;
        if (c.OverloadedAnyDay) return _styles.Warning.Render(s);
        return c.AvgWIP > 0 ? _styles.Value.Render(s) : _styles.Muted.Render(s);
    }

    private string ColorStuck(string s, int n)
    {
        if (_styles is null) return s;
        return n > 0 ? _styles.Error.Render(s) : _styles.Muted.Render(s);
    }

    private string ColorCycle(string s, TimeSpan d)
    {
        if (_styles is null) return s;
        return d > TimeSpan.Zero ? _styles.Value.Render(s) : _styles.Muted.Render(s);
    }

    internal static (TrendRow Total, bool Ok) ComputeTeamTotal(List<TrendRow> rows)
    {
        if (rows.Count == 0 || rows[0].Cells.Count == 0)
            return (new TrendRow(), false);
        int nCells = rows[0].Cells.Count;
        var cells = new List<TrendCell>(nCells);
        for (int c = 0; c < nCells; c++)
        {
            double sumPts = 0, sumWIP = 0;
            var sumCy = TimeSpan.Zero;
            int sumStuck = 0, cyN = 0, overloadedN = 0;
            foreach (var r in rows)
            {
                sumPts += r.Cells[c].Points;
                sumWIP += r.Cells[c].AvgWIP;
                sumStuck += r.Cells[c].StuckCount;
                if (r.Cells[c].CycleTime > TimeSpan.Zero)
                {
                    sumCy += r.Cells[c].CycleTime;
                    cyN++;
                }
                if (r.Cells[c].OverloadedAnyDay) overloadedN++;
            }
            var cell = new TrendCell
            {
                Points = sumPts,
                AvgWIP = sumWIP / rows.Count,
                StuckCount = sumStuck,
                OverloadedAnyDay = overloadedN > 0,
            };
            if (cyN > 0) cell.CycleTime = TimeSpan.FromTicks(sumCy.Ticks / cyN);
            cells.Add(cell);
        }
        return (new TrendRow { User = "Team total", Cells = cells }, true);
    }

    private string MetricsGlossary()
    {
        var parts = new List<string>();
        foreach (var k in ChartData.AllMetricKinds)
            parts.Add($"{k.Short()} = {k.Label()}");
        var line = "Legend: " + string.Join(" · ", parts);
        return _styles is not null ? _styles.Muted.Render(line) : line;
    }

    internal static int DistinctSnapshotDays(IReadOnlyList<Snapshot> snaps)
    {
        var set = new HashSet<string>();
        foreach (var s in snaps) set.Add(s.TS);
        return set.Count;
    }

    private static string FmtFloat1(double f) => f.ToString("F1", CultureInfo.InvariantCulture);

    private string MutedOr(string s) => _styles is not null ? _styles.Muted.Render(s) : s;

    private static int SafeWidth(string s)
    {
        int w = Ansi.Width(s);
        return w > 200 ? 200 : w;
    }
}
