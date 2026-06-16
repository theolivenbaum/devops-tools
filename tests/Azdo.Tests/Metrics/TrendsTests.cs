using System.Globalization;
using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class TrendsTests
{
    private static Snapshot Snap(string ts, int id, string state, string user, double points, params string[] tags)
        => new()
        {
            TS = ts,
            Id = id,
            State = state,
            AssignedTo = user,
            Points = points,
            Tags = tags.ToList(),
            Source = Snapshots.SourceSnapshot,
        };

    private static DateTime MustDate(string s) =>
        DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    [Fact]
    public void DeriveSprintWindow_StartEnd()
    {
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-11", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-12", 1, "Ready for Test", "Alice", 3, "sprint-42"),
            Snap("2026-05-13", 1, "Closed", "Alice", 3, "sprint-42"),
            Snap("2026-06-01", 2, "Active", "Bob", 3, "sprint-43"),
        };
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var (w, ok) = Trends.DeriveSprintWindow(snaps, "sprint-42", now, StateConfig.DefaultStates());
        Assert.True(ok);
        Assert.Equal("2026-05-10", w.Start.ToString("yyyy-MM-dd"));
        Assert.Equal("2026-05-12", w.End.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void DeriveSprintWindow_OngoingExtendsToNow()
    {
        var snaps = new[]
        {
            Snap("2026-05-25", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-31", 1, "Active", "Alice", 3, "sprint-42"),
        };
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var (w, ok) = Trends.DeriveSprintWindow(snaps, "sprint-42", now, StateConfig.DefaultStates());
        Assert.True(ok);
        Assert.Equal(now, w.End);
    }

    [Fact]
    public void DeriveSprintWindow_RespectsConfiguredClosedName()
    {
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-11", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-12", 1, "Done", "Alice", 3, "sprint-42"),
        };
        var custom = new StateConfig("Active", "RFT", "Done");
        var (w, ok) = Trends.DeriveSprintWindow(snaps, "sprint-42",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), custom);
        Assert.True(ok);
        Assert.Equal(MustDate("2026-05-11"), w.End);
    }

    [Fact]
    public void DeriveSprintWindow_TagNeverSeen()
    {
        var snaps = new[] { Snap("2026-05-10", 1, "Active", "Alice", 3, "sprint-42") };
        var (_, ok) = Trends.DeriveSprintWindow(snaps, "sprint-99", DateTime.UtcNow, StateConfig.DefaultStates());
        Assert.False(ok);
    }

    private static Thresholds Th(int active = 0, int rft = 0, int wip = 0) => new()
    {
        ActiveStaleDays = active,
        RFTStaleDays = rft,
        WIPLimit = wip,
        States = StateConfig.DefaultStates(),
    };

    [Fact]
    public void TrendAggregate_PointsClosed()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-12", 1, "Closed", "Alice", 3, "sprint-42"),
            Snap("2026-05-13", 2, "Closed", "Alice", 5, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-14"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(3, 2, 4), now);
        Assert.Single(rows);
        Assert.Equal(8.0, rows[0].Cells[0].Points);
    }

    [Fact]
    public void TrendAggregate_SkipsUnassigned()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Closed", "Alice", 3, "sprint-42"),
            Snap("2026-05-11", 2, "Closed", "-", 5, "sprint-42"),
            Snap("2026-05-12", 3, "Closed", "", 7, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-13"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(3, 2, 4), now);
        Assert.Single(rows);
        Assert.Equal("Alice", rows[0].User);
    }

    [Fact]
    public void TrendAggregate_PointsClosed_NotDoubleCountedAcrossDays()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Closed", "Alice", 5, "sprint-42"),
            Snap("2026-05-11", 1, "Closed", "Alice", 5, "sprint-42"),
            Snap("2026-05-12", 1, "Closed", "Alice", 5, "sprint-42"),
            Snap("2026-05-13", 1, "Closed", "Alice", 5, "sprint-42"),
            Snap("2026-05-14", 1, "Closed", "Alice", 5, "sprint-42"),
            Snap("2026-05-13", 2, "Closed", "Alice", 8, "sprint-42"),
            Snap("2026-05-14", 2, "Closed", "Alice", 8, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-15"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(3, 2, 4), now);
        Assert.Equal(13.0, rows[0].Cells[0].Points);
    }

    [Fact]
    public void TrendAggregate_AvgWIPAcrossDays()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 1, "sprint-42"),
            Snap("2026-05-11", 1, "Active", "Alice", 1, "sprint-42"),
            Snap("2026-05-11", 2, "Active", "Alice", 1, "sprint-42"),
            Snap("2026-05-11", 3, "Ready for Test", "Alice", 1, "sprint-42"),
            Snap("2026-05-12", 1, "Active", "Alice", 1, "sprint-42"),
            Snap("2026-05-12", 2, "Ready for Test", "Alice", 1, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-12"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(wip: 4), now);
        Assert.Single(rows);
        Assert.Equal(2.0, rows[0].Cells[0].AvgWIP);
    }

    [Fact]
    public void TrendAggregate_StuckCount_DedupedPerItem()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-11", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-12", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-13", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-14", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-10", 2, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-11", 2, "Active", "Alice", 0, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-14"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(active: 3, wip: 4), now);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Cells[0].StuckCount);
    }

    [Fact]
    public void TrendAggregate_CycleTime()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 3, "sprint-42"),
            Snap("2026-05-14", 1, "Closed", "Alice", 3, "sprint-42"),
            Snap("2026-05-11", 2, "Active", "Alice", 5, "sprint-42"),
            Snap("2026-05-13", 2, "Closed", "Alice", 5, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-14"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(), now);
        Assert.Equal(TimeSpan.FromDays(3), rows[0].Cells[0].CycleTime);
    }

    [Fact]
    public void TrendAggregate_OverloadedAnyDay()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-10", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-11", 1, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-11", 2, "Active", "Alice", 0, "sprint-42"),
            Snap("2026-05-11", 3, "Ready for Test", "Alice", 0, "sprint-42"),
            Snap("2026-05-12", 1, "Active", "Alice", 0, "sprint-42"),
        };
        var w = new SprintWindow("sprint-42", MustDate("2026-05-10"), MustDate("2026-05-12"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(wip: 2), now);
        Assert.True(rows[0].Cells[0].OverloadedAnyDay);
    }

    [Fact]
    public void TrendAggregate_MultiSprintMultiUser()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-01", 1, "Closed", "Alice", 3, "sprint-40"),
            Snap("2026-05-08", 2, "Closed", "Bob", 5, "sprint-40"),
            Snap("2026-05-15", 3, "Closed", "Alice", 8, "sprint-41"),
        };
        var windows = new[]
        {
            new SprintWindow("sprint-40", MustDate("2026-05-01"), MustDate("2026-05-10")),
            new SprintWindow("sprint-41", MustDate("2026-05-11"), MustDate("2026-05-20")),
        };
        var rows = Trends.TrendAggregate(snaps, windows, Th(), now);
        var byUser = rows.ToDictionary(r => r.User, r => r.Cells);
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, byUser["Alice"][0].Points);
        Assert.Equal(8, byUser["Alice"][1].Points);
        Assert.Equal(5, byUser["Bob"][0].Points);
        Assert.Equal(0, byUser["Bob"][1].Points);
    }

    [Fact]
    public void TrendAggregate_RowsSortedByName()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var snaps = new[]
        {
            Snap("2026-05-01", 1, "Active", "Zach", 0, "sprint-40"),
            Snap("2026-05-01", 2, "Active", "Alice", 0, "sprint-40"),
            Snap("2026-05-01", 3, "Active", "Mara", 0, "sprint-40"),
        };
        var w = new SprintWindow("sprint-40", MustDate("2026-05-01"), MustDate("2026-05-01"));
        var rows = Trends.TrendAggregate(snaps, new[] { w }, Th(wip: 4), now);
        var names = rows.Select(r => r.User).ToList();
        var sorted = names.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, names);
    }
}
