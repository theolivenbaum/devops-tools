using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class AggregateTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Interval = Now.AddDays(-14);

    private static WorkItem Item(int id, string state, string user, int daysInState, int closedDaysAgo, double points)
    {
        var wi = new WorkItem
        {
            Id = id,
            ProjectDisplayName = "proj",
            Fields = new WorkItemFields
            {
                Title = "item",
                State = state,
                StateChangeDate = Now.AddDays(-daysInState),
                StoryPoints = points,
            },
        };
        if (user != "")
            wi.Fields.AssignedTo = new Identity { DisplayName = user };
        if (closedDaysAgo >= 0)
            wi.Fields.ClosedDate = Now.AddDays(-closedDaysAgo);
        return wi;
    }

    private static Thresholds DefaultThresholds() => new()
    {
        ActiveStaleDays = 3,
        RFTStaleDays = 2,
        WIPLimit = 4,
        States = StateConfig.DefaultStates(),
    };

    [Fact]
    public void BucketsByState()
    {
        var items = new[]
        {
            Item(1, "Active", "Alice", 1, -1, 3),
            Item(2, "Active", "Alice", 1, -1, 5),
            Item(3, "Ready for Test", "Alice", 1, -1, 8),
            Item(4, "Closed", "Alice", 1, 5, 13),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal("Alice", r.User);
        Assert.Equal(2, r.ActiveCount);
        Assert.Equal(1, r.RFTCount);
        Assert.Equal(3, r.InFlight);
        Assert.Equal(13, r.PointsClosed);
    }

    [Fact]
    public void PointsClosed_RespectsIntervalWindow()
    {
        var items = new[]
        {
            Item(1, "Closed", "Alice", 5, 5, 3),
            Item(2, "Closed", "Alice", 30, 30, 100),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        Assert.Equal(3, rows[0].PointsClosed);
    }

    [Fact]
    public void FlagsStaleActive()
    {
        var items = new[]
        {
            Item(1, "Active", "Bob", 5, -1, 0),
            Item(2, "Active", "Bob", 1, -1, 0),
        };
        var (rows, flags) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(flags);
        Assert.Equal(1, flags[0].Id);
        Assert.Equal("active-stale", flags[0].Reason);
        Assert.Equal(1, rows[0].Stalled);
        Assert.Equal(TimeSpan.FromDays(5), rows[0].OldestActive);
    }

    [Fact]
    public void FlagsStaleRFT()
    {
        var items = new[]
        {
            Item(1, "Ready for Test", "Carol", 4, -1, 0),
            Item(2, "Ready for Test", "Carol", 1, -1, 0),
        };
        var (rows, flags) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(flags);
        Assert.Equal("rft-stale", flags[0].Reason);
        Assert.Equal(1, rows[0].Stalled);
        Assert.Equal(TimeSpan.FromDays(4), rows[0].OldestRFT);
    }

    [Fact]
    public void OverloadedFlag()
    {
        var items = new List<WorkItem>();
        for (int i = 0; i < 5; i++) items.Add(Item(i + 1, "Active", "Dave", 1, -1, 0));
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        Assert.True(rows[0].Overloaded);
        Assert.Equal(5, rows[0].InFlight);
    }

    [Fact]
    public void NotOverloadedAtLimit()
    {
        var items = new List<WorkItem>();
        for (int i = 0; i < 4; i++) items.Add(Item(i + 1, "Active", "Eve", 1, -1, 0));
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.False(rows[0].Overloaded);
    }

    [Fact]
    public void SortByStalledThenInFlight()
    {
        var items = new[]
        {
            Item(1, "Active", "Alice", 1, -1, 0),
            Item(2, "Active", "Alice", 1, -1, 0),
            Item(3, "Active", "Bob", 5, -1, 0),
            Item(4, "Active", "Carol", 1, -1, 0),
            Item(5, "Active", "Carol", 1, -1, 0),
            Item(6, "Active", "Carol", 1, -1, 0),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Equal(3, rows.Count);
        Assert.Equal("Bob", rows[0].User);
        Assert.Equal("Carol", rows[1].User);
        Assert.Equal("Alice", rows[2].User);
    }

    [Fact]
    public void FlagsSortedWorstFirst()
    {
        var items = new[]
        {
            Item(1, "Active", "Alice", 4, -1, 0),
            Item(2, "Active", "Bob", 10, -1, 0),
            Item(3, "Ready for Test", "Carol", 6, -1, 0),
        };
        var (_, flags) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Equal(3, flags.Count);
        Assert.Equal(2, flags[0].Id);
        Assert.Equal(3, flags[1].Id);
        Assert.Equal(1, flags[2].Id);
    }

    [Fact]
    public void IgnoresNewAndUnknownStates()
    {
        var items = new[]
        {
            Item(1, "New", "Alice", 1, -1, 0),
            Item(2, "Removed", "Alice", 1, -1, 0),
            Item(3, "InReview", "Alice", 1, -1, 0),
        };
        var (rows, flags) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Empty(rows);
        Assert.Empty(flags);
    }

    [Fact]
    public void UnassignedUser()
    {
        var items = new[]
        {
            Item(1, "Active", "", 1, -1, 0),
            Item(2, "Active", "", 1, -1, 0),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        Assert.Equal("-", rows[0].User);
        Assert.Equal(2, rows[0].InFlight);
    }

    [Fact]
    public void CaseInsensitiveStateMatching()
    {
        var items = new[]
        {
            Item(1, "active", "Alice", 1, -1, 0),
            Item(2, "READY FOR TEST", "Alice", 1, -1, 0),
            Item(3, "closed", "Alice", 5, 5, 7),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        Assert.Equal(1, rows[0].ActiveCount);
        Assert.Equal(1, rows[0].RFTCount);
        Assert.Equal(7, rows[0].PointsClosed);
    }

    [Fact]
    public void CustomStateNames()
    {
        var items = new[]
        {
            Item(1, "In Progress", "Alice", 5, -1, 3),
            Item(2, "RFT", "Alice", 1, -1, 2),
            Item(3, "Done", "Bob", 1, 1, 5),
        };
        var th = new Thresholds
        {
            ActiveStaleDays = 3,
            RFTStaleDays = 2,
            WIPLimit = 4,
            States = new StateConfig("In Progress", "RFT", "Done"),
        };
        var (rows, flags) = Aggregator.Aggregate(items, Interval, Now, th);
        Assert.Equal(2, rows.Count);
        var alice = rows.First(r => r.User == "Alice");
        var bob = rows.First(r => r.User == "Bob");
        Assert.Equal(1, alice.ActiveCount);
        Assert.Equal(1, alice.RFTCount);
        Assert.Equal(2, alice.InFlight);
        Assert.Equal(5, bob.PointsClosed);
        Assert.Single(flags);
        Assert.Equal(Aggregator.ReasonActiveStale, flags[0].Reason);
    }

    [Fact]
    public void DualCasingRFT()
    {
        var items = new[]
        {
            Item(1, "Ready For Test", "Alice", 1, -1, 2),
            Item(2, "Ready for test", "Alice", 1, -1, 3),
            Item(3, "READY FOR TEST", "Alice", 1, -1, 1),
        };
        var (rows, _) = Aggregator.Aggregate(items, Interval, Now, DefaultThresholds());
        Assert.Single(rows);
        Assert.Equal(3, rows[0].RFTCount);
    }
}
