using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class ChartDataTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.6, 1)]
    [InlineData(1, 1)]
    [InlineData(1.4, 2)]
    [InlineData(2, 2)]
    [InlineData(2.0001, 5)]
    [InlineData(4.9, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 10)]
    [InlineData(10, 10)]
    [InlineData(12, 20)]
    [InlineData(18, 20)]
    [InlineData(21, 50)]
    public void NiceCeil(double input, double want)
        => Assert.True(Math.Abs(ChartData.NiceCeil(input) - want) < 1e-9, $"NiceCeil({input}) = {ChartData.NiceCeil(input)}, want {want}");

    [Fact]
    public void CellValue_CycleInDays()
    {
        var c = new TrendCell { CycleTime = TimeSpan.FromHours(48) };
        Assert.True(Math.Abs(ChartData.CellValue(c, MetricKind.Cycle) - 2) < 1e-9);

        c = new TrendCell { Points = 8, AvgWIP = 2.5, StuckCount = 3 };
        Assert.Equal(8, ChartData.CellValue(c, MetricKind.Points));
        Assert.Equal(2.5, ChartData.CellValue(c, MetricKind.AvgWIP));
        Assert.Equal(3, ChartData.CellValue(c, MetricKind.Stuck));
    }

    [Fact]
    public void CellHasValue_GapVsZero()
    {
        var empty = new TrendCell();
        foreach (var k in ChartData.AllMetricKinds)
            Assert.False(ChartData.CellHasValue(empty, k));

        var active = new TrendCell { AvgWIP = 1.5 };
        Assert.True(ChartData.CellHasValue(active, MetricKind.Points));
        Assert.Equal(0, ChartData.CellValue(active, MetricKind.Points));
        Assert.False(ChartData.CellHasValue(active, MetricKind.Cycle));

        var done = new TrendCell { AvgWIP = 1, CycleTime = TimeSpan.FromHours(72) };
        Assert.True(ChartData.CellHasValue(done, MetricKind.Cycle));
    }

    [Fact]
    public void BuildSeries_AndMax()
    {
        var rows = new List<TrendRow>
        {
            new() { User = "alice", Cells = new()
            {
                new TrendCell { Points = 8, AvgWIP = 2 },
                new TrendCell(),
                new TrendCell { Points = 12, AvgWIP = 3 },
            }},
            new() { User = "bob", Cells = new()
            {
                new TrendCell { Points = 5, AvgWIP = 1 },
                new TrendCell { Points = 0, AvgWIP = 1 },
                new TrendCell { Points = 7, AvgWIP = 2 },
            }},
        };

        var series = ChartData.BuildSeries(rows, MetricKind.Points);
        Assert.Equal(2, series.Count);
        Assert.Equal("alice", series[0].User);
        Assert.Equal(3, series[0].Points.Count);
        Assert.False(series[0].Points[1].Present);
        Assert.True(series[1].Points[1].Present);
        Assert.Equal(0, series[1].Points[1].Value);
        Assert.Equal(12, ChartData.SeriesMax(series));
    }

    [Fact]
    public void Labels_AndShort()
    {
        Assert.Equal("Points closed", MetricKind.Points.Label());
        Assert.Equal("cy", MetricKind.Cycle.Short());
    }
}
