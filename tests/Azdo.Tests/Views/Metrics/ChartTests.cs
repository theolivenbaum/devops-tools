using System.Globalization;
using Azdo.Core.Configuration;
using Azdo.Core.Metrics;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.Metrics;
using ViewMode = Azdo.Tui.Views.Metrics.ViewMode;
using StyleSet = Azdo.Tui.Styles.Styles;
using Xunit;

namespace Azdo.Tests.Views.Metrics;

public class ChartTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Config MakeConfig() => new()
    {
        Metrics = new MetricsConfig { Enabled = true, IntervalDays = 14, ActiveStaleDays = 3, RFTStaleDays = 2, WIPLimit = 4 },
    };

    private static DateTime Day(string s) => DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static (List<Snapshot> Snaps, List<SprintWindow> Windows, List<TrendRow> Rows) Fixture()
    {
        var snaps = new List<Snapshot>();
        for (int i = 0; i < 10; i++)
            snaps.Add(new Snapshot { TS = $"2026-05-{i + 1:D2}" });
        var windows = new List<SprintWindow>
        {
            new("sprint-41", Day("2026-05-01"), Day("2026-05-07")),
            new("sprint-42", Day("2026-05-08"), Day("2026-05-14")),
            new("sprint-43", Day("2026-05-15"), Day("2026-05-21")),
        };
        var rows = new List<TrendRow>
        {
            new() { User = "alice", Cells = new()
            {
                new TrendCell { Points = 8, AvgWIP = 2, CycleTime = TimeSpan.FromHours(48) },
                new TrendCell(),
                new TrendCell { Points = 12, AvgWIP = 3, StuckCount = 1, CycleTime = TimeSpan.FromHours(72) },
            }},
            new() { User = "bob", Cells = new()
            {
                new TrendCell { Points = 5, AvgWIP = 1 },
                new TrendCell { Points = 0, AvgWIP = 1 },
                new TrendCell { Points = 7, AvgWIP = 2, CycleTime = TimeSpan.FromHours(24) },
            }},
        };
        return (snaps, windows, rows);
    }

    private static Model ChartModel(bool styled)
    {
        var m = new Model(null, MakeConfig(), styled ? StyleSet.Default() : null);
        m.SetNow(() => FixedNow);
        var (snaps, windows, rows) = Fixture();
        m.SeedTrendsForTest(snaps, windows, rows, ViewMode.TrendsChart);
        return m;
    }

    private static KeyMsg Rune(char c) => KeyMsg.Rune_(c);

    [Fact]
    public void RenderTrendsChart_DoesNotPanicAndShowsMetric()
    {
        var m = ChartModel(true);
        m.Update(new WindowSizeMsg(120, 30));
        var outStr = m.View();
        Assert.Contains(MetricKind.Points.Label(), outStr);
        foreach (var tag in new[] { "sprint-41", "sprint-42", "sprint-43" })
            Assert.Contains(tag, outStr);
    }

    [Fact]
    public void RenderTrendsChart_EmptyAndSmallWindow()
    {
        var m = new Model(null, MakeConfig(), null);
        m.SetNow(() => FixedNow);
        m.SetModeForTest(ViewMode.TrendsChart);
        m.Update(new WindowSizeMsg(120, 30));
        Assert.NotEqual("", m.View());

        var m2 = ChartModel(false);
        var (snaps, windows, rows) = Fixture();
        m2.SeedTrendsForTest(snaps, windows.Take(1),
            new[] { new TrendRow { User = rows[0].User, Cells = rows[0].Cells.Take(1).ToList() },
                    new TrendRow { User = rows[1].User, Cells = rows[1].Cells.Take(1).ToList() } },
            ViewMode.TrendsChart);
        m2.Update(new WindowSizeMsg(120, 30));
        Assert.Contains("at least 2 sprints", m2.RenderTrendsChartForTest());
    }

    [Fact]
    public void RenderTrendsChart_Bars()
    {
        var m = ChartModel(true);
        m.Update(new WindowSizeMsg(120, 30));
        var outStr = m.RenderTrendsChartForTest();
        Assert.Contains('█', outStr);
        Assert.Contains("alice", outStr);
        Assert.Contains("bob", outStr);
        Assert.Contains("1 sprint-41", outStr);
        Assert.DoesNotContain("0 sprint-41", outStr);
    }

    [Fact]
    public void ChartFocus_CycleKey()
    {
        var m = ChartModel(false);
        m.Update(new WindowSizeMsg(120, 30));
        Assert.Equal(-1, m.FocusedUser);
        m.Update(Rune('f'));
        Assert.Equal(0, m.FocusedUser);
        m.Update(Rune('f'));
        Assert.Equal(1, m.FocusedUser);
        m.Update(Rune('f'));
        Assert.Equal(-1, m.FocusedUser);

        var live = new Model(null, MakeConfig(), null);
        live.SetNow(() => FixedNow);
        live.SetModeForTest(ViewMode.Live);
        live.Update(new WindowSizeMsg(120, 30));
        live.Update(Rune('f'));
        Assert.Equal(-1, live.FocusedUser);
    }

    [Fact]
    public void UserLegend_MarksFocusedUser()
    {
        var m = ChartModel(true);
        m.Update(new WindowSizeMsg(120, 30));
        Assert.DoesNotContain("▸", m.UserLegendForTest());

        // Focus bob (index 1): two f presses (all → 0 → 1).
        m.Update(Rune('f'));
        m.Update(Rune('f'));
        var leg = m.UserLegendForTest();
        Assert.Contains("▸", leg);
        Assert.True(leg.IndexOf("▸", StringComparison.Ordinal) > leg.IndexOf("alice", StringComparison.Ordinal));
    }

    [Fact]
    public void ChartHints_IncludeFocus()
    {
        var m = ChartModel(true);
        Assert.Contains("focus", m.ChartHintsForTest());
    }

    [Fact]
    public void MetricsGlossary_InBothViews()
    {
        var m = ChartModel(true);
        m.Update(new WindowSizeMsg(120, 30));
        var chart = m.RenderTrendsChartForTest();
        m.SetModeForTest(ViewMode.Trends);
        var table = m.RenderTrendsForTest();
        foreach (var outStr in new[] { chart, table })
        {
            Assert.Contains("Legend:", outStr);
            Assert.Contains("Cycle time", outStr);
        }
    }

    [Fact]
    public void SetStyles_PreservesState()
    {
        var m = ChartModel(true);
        m.Update(new WindowSizeMsg(120, 30));
        m.SetSelectedSprintsForTest(new[] { "sprint-41", "sprint-42", "sprint-43" });
        m.SetSprintCursorForTest(2);

        int snaps = m.SnapshotCount, sprints = m.SprintWindows.Count, rows = m.TrendRows.Count;
        var mode = m.CurrentMode;
        int cursor = m.SprintCursor, sel = m.SelectedSprints.Count;

        m.SetStyles(StyleSet.Default());
        Assert.True(m.HasStyles);
        Assert.Equal(snaps, m.SnapshotCount);
        Assert.Equal(sprints, m.SprintWindows.Count);
        Assert.Equal(rows, m.TrendRows.Count);
        Assert.Equal(mode, m.CurrentMode);
        Assert.Equal(cursor, m.SprintCursor);
        Assert.Equal(sel, m.SelectedSprints.Count);
        Assert.Contains("sprint-41", m.View());
    }

    [Fact]
    public void ChartGeom_DiscreteAndMonotonic()
    {
        var g = ChartGeom.New(60, 12, 3, 20);
        Assert.Equal(g.PlotLeft, g.XFor(0));
        Assert.Equal(g.PlotRight, g.XFor(2));
        Assert.NotEqual(g.XFor(0), g.XFor(1));
        Assert.NotEqual(g.XFor(1), g.XFor(2));

        Assert.Equal(g.PlotBottom, g.YFor(0));
        Assert.Equal(g.PlotTop, g.YFor(20));
        Assert.True(g.YFor(20) < g.YFor(10) && g.YFor(10) < g.YFor(0));
    }

    [Fact]
    public void BarLayout_GroupedNonOverlapping()
    {
        var spans = Model.BarLayout(3, 62, 3, 2);
        Assert.NotNull(spans);
        Assert.Equal(3, spans!.Length);
        int prevX1 = -1;
        foreach (var group in spans)
        {
            Assert.Equal(2, group.Length);
            foreach (var span in group)
            {
                Assert.True(span.X0 >= 3 && span.X1 <= 62);
                Assert.True(span.X1 >= span.X0);
                Assert.True(span.X0 > prevX1);
                prevX1 = span.X1;
            }
        }
    }

    [Fact]
    public void ChartKeys_OnlyActInChartMode()
    {
        var m = ChartModel(false);
        m.Update(new WindowSizeMsg(120, 30));
        Assert.Equal(MetricKind.Points, m.ChartMetric);
        m.Update(Rune('l'));
        Assert.Equal(MetricKind.AvgWIP, m.ChartMetric);
        m.Update(Rune('h'));
        Assert.Equal(MetricKind.Points, m.ChartMetric);

        m.Update(Rune(','));
        Assert.Equal(0, m.SprintCursor);
        m.Update(Rune('.'));
        m.Update(Rune('.'));
        m.Update(Rune('.'));
        Assert.Equal(2, m.SprintCursor);

        var live = new Model(null, MakeConfig(), null);
        live.SetNow(() => FixedNow);
        live.SetModeForTest(ViewMode.Live);
        live.Update(new WindowSizeMsg(120, 30));
        live.Update(Rune('l'));
        Assert.Equal(MetricKind.Points, live.ChartMetric);
    }
}
