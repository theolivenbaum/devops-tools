namespace Azdo.Core.Metrics;

/// <summary>One of the four Trends metrics rendered on the Y axis of the chart view.</summary>
public enum MetricKind
{
    Points = 0,
    AvgWIP,
    Stuck,
    Cycle,
}

/// <summary>One (sprint, value) sample for a user's line.</summary>
public sealed class SeriesPoint
{
    public int SprintIndex { get; set; }
    public double Value { get; set; }

    /// <summary>false marks a gap that callers must not plot (and must not treat as zero).</summary>
    public bool Present { get; set; }
}

/// <summary>One user's line across the selected sprints, in sprint order.</summary>
public sealed class Series
{
    public string User { get; set; } = "";
    public List<SeriesPoint> Points { get; set; } = new();
}

/// <summary>Pure chart-data projection helpers.</summary>
public static class ChartData
{
    /// <summary>The metrics in display order, used to cycle through them.</summary>
    public static readonly MetricKind[] AllMetricKinds =
        { MetricKind.Points, MetricKind.AvgWIP, MetricKind.Stuck, MetricKind.Cycle };

    /// <summary>Human-readable metric name shown in the chart header.</summary>
    public static string Label(this MetricKind k) => k switch
    {
        MetricKind.Points => "Points closed",
        MetricKind.AvgWIP => "Avg WIP",
        MetricKind.Stuck => "Stuck items",
        MetricKind.Cycle => "Cycle time (days)",
        _ => "?",
    };

    /// <summary>Compact metric tag used in legends/readouts.</summary>
    public static string Short(this MetricKind k) => k switch
    {
        MetricKind.Points => "pts",
        MetricKind.AvgWIP => "wip",
        MetricKind.Stuck => "stuck",
        MetricKind.Cycle => "cy",
        _ => "?",
    };

    /// <summary>The numeric value for metric k from a cell. Cycle time is in days.</summary>
    public static double CellValue(TrendCell c, MetricKind k) => k switch
    {
        MetricKind.Points => c.Points,
        MetricKind.AvgWIP => c.AvgWIP,
        MetricKind.Stuck => c.StuckCount,
        MetricKind.Cycle => c.CycleTime.TotalHours / 24,
        _ => 0,
    };

    /// <summary>Whether the user had any signal at all in this sprint.</summary>
    public static bool CellActive(TrendCell c) =>
        c.Points > 0 || c.AvgWIP > 0 || c.StuckCount > 0 || c.CycleTime > TimeSpan.Zero;

    /// <summary>Whether metric k should be plotted for this cell.</summary>
    public static bool CellHasValue(TrendCell c, MetricKind k)
    {
        if (k == MetricKind.Cycle)
            return c.CycleTime > TimeSpan.Zero;
        return CellActive(c);
    }

    /// <summary>Projects per-user trend rows onto a single metric.</summary>
    public static List<Series> BuildSeries(IReadOnlyList<TrendRow> rows, MetricKind k)
    {
        var outList = new List<Series>(rows.Count);
        foreach (var r in rows)
        {
            var s = new Series { User = r.User, Points = new List<SeriesPoint>(r.Cells.Count) };
            for (int i = 0; i < r.Cells.Count; i++)
            {
                var c = r.Cells[i];
                s.Points.Add(new SeriesPoint
                {
                    SprintIndex = i,
                    Value = CellValue(c, k),
                    Present = CellHasValue(c, k),
                });
            }
            outList.Add(s);
        }
        return outList;
    }

    /// <summary>The largest plotted value across all series, ignoring gaps. 0 if nothing.</summary>
    public static double SeriesMax(IReadOnlyList<Series> series)
    {
        double max = 0.0;
        foreach (var s in series)
            foreach (var p in s.Points)
                if (p.Present && p.Value > max)
                    max = p.Value;
        return max;
    }

    /// <summary>
    /// Rounds v up to the nearest "nice" axis bound (1, 2, or 5 times a power of
    /// ten). Values &lt;= 0 return 1.
    /// </summary>
    public static double NiceCeil(double v)
    {
        if (v <= 0)
            return 1;
        double exp = Math.Floor(Math.Log10(v));
        double pow = Math.Pow(10, exp);
        double f = v / pow; // mantissa in [1, 10)
        double nice;
        if (f <= 1) nice = 1;
        else if (f <= 2) nice = 2;
        else if (f <= 5) nice = 5;
        else nice = 10;
        return nice * pow;
    }
}
