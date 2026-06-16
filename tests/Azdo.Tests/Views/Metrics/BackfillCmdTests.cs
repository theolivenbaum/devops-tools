using System.Globalization;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Azdo.Tui.Views.Metrics;
using Xunit;

namespace Azdo.Tests.Views.Metrics;

public class BackfillCmdTests
{
    private static DateTime BfNow() => new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WorkItem MakeWorkItem(int id, string project, string state, string assignee)
    {
        var wi = new WorkItem { Id = id, ProjectName = project };
        wi.Fields.State = state;
        wi.Fields.IterationPath = "sprint-1";
        wi.Fields.Tags = "sprint-1";
        wi.Fields.StoryPoints = 3;
        if (assignee != "") wi.Fields.AssignedTo = new Identity { DisplayName = assignee };
        return wi;
    }

    [Fact]
    public async Task HappyPath_SynthesizesRowsTaggedSourceUpdates()
    {
        var now = BfNow();
        var items = new List<WorkItem> { MakeWorkItem(101, "proj-a", "Closed", "Alice") };
        BackfillFetcher fetch = (project, id) =>
        {
            Assert.Equal("proj-a", project);
            Assert.Equal(101, id);
            var txns = new List<WorkItemStateTransition>
            {
                new() { State = "Active", At = now.AddDays(-10) },
                new() { State = "Ready for Test", At = now.AddDays(-5) },
                new() { State = "Closed", At = now.AddDays(-2) },
            };
            return Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((txns, null));
        };

        var (rows, skipped) = await Model.BuildBackfillRowsAsync(items, fetch, now, 2);
        Assert.Equal(0, skipped);
        Assert.NotEmpty(rows);
        foreach (var r in rows)
        {
            Assert.Equal(Snapshots.SourceUpdates, r.Source);
            Assert.Equal(101, r.Id);
            Assert.Equal("proj-a", r.Project);
            Assert.Equal("Alice", r.AssignedTo);
        }
    }

    [Fact]
    public async Task FetchError_IncrementsSkipped()
    {
        var now = BfNow();
        var items = new List<WorkItem>
        {
            MakeWorkItem(1, "proj-a", "Active", "Alice"),
            MakeWorkItem(2, "proj-a", "Closed", "Bob"),
        };
        BackfillFetcher fetch = (project, id) =>
        {
            if (id == 1)
                return Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((null, new Exception("network timeout")));
            var txns = new List<WorkItemStateTransition>
            {
                new() { State = "Active", At = now.AddDays(-10) },
                new() { State = "Closed", At = now.AddDays(-3) },
            };
            return Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((txns, null));
        };

        var (rows, skipped) = await Model.BuildBackfillRowsAsync(items, fetch, now, 2);
        Assert.Equal(1, skipped);
        Assert.DoesNotContain(rows, r => r.Id == 1);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task EmptyItems_ReturnsNothing()
    {
        BackfillFetcher fetch = (_, _) =>
        {
            Assert.Fail("fetch should not be called for empty items");
            return Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((null, null));
        };
        var (rows, skipped) = await Model.BuildBackfillRowsAsync(new List<WorkItem>(), fetch, BfNow(), 4);
        Assert.Empty(rows);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public async Task NoTransitions_ProducesNoRows()
    {
        var items = new List<WorkItem> { MakeWorkItem(7, "proj-a", "New", "Alice") };
        BackfillFetcher fetch = (_, _) =>
            Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((new List<WorkItemStateTransition>(), null));
        var (rows, skipped) = await Model.BuildBackfillRowsAsync(items, fetch, BfNow(), 2);
        Assert.Equal(0, skipped);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task OnlyEmitsWithin90DayWindow()
    {
        var now = BfNow();
        var items = new List<WorkItem> { MakeWorkItem(42, "proj-a", "Closed", "Carol") };
        BackfillFetcher fetch = (_, _) =>
        {
            var txns = new List<WorkItemStateTransition>
            {
                new() { State = "Active", At = now.AddDays(-200) },
                new() { State = "Closed", At = now.AddDays(-100) },
            };
            return Task.FromResult<(List<WorkItemStateTransition>?, Exception?)>((txns, null));
        };
        var (rows, _) = await Model.BuildBackfillRowsAsync(items, fetch, now, 2);
        var cutoff = now.AddDays(-90);
        foreach (var r in rows)
        {
            var ts = DateTime.ParseExact(r.TS, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.False(ts < cutoff, $"row TS {r.TS} before cutoff");
            Assert.True(ts < now, $"row TS {r.TS} not before now");
        }
    }
}
