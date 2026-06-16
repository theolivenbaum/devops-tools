using Azdo.Core.AzureDevOps;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.Pipelines;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.Pipelines;

public class DetailTests
{
    private static StyleSet S => StyleSet.Default();

    private static DetailModel NewDetail(Timeline timeline)
    {
        var run = new PipelineRun { Id = 1, BuildNumber = "1", Definition = new PipelineDefinition { Name = "P" } };
        var m = new DetailModel(null, run, S);
        m.SetSize(80, 24);
        m.SetTimeline(timeline);
        return m;
    }

    [Fact]
    public void BuildTimelineTree_LinksParentsAndSortsByOrder()
    {
        var timeline = new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "j2", ParentId = "s1", Type = "Job", Name = "Job2", Order = 2 },
                new TimelineRecord { Id = "s1", ParentId = null, Type = "Stage", Name = "Stage", Order = 1 },
                new TimelineRecord { Id = "j1", ParentId = "s1", Type = "Job", Name = "Job1", Order = 1 },
            },
        };

        var tree = DetailModel.BuildTimelineTree(timeline);
        var root = Assert.Single(tree);
        Assert.Equal("s1", root.Record.Id);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("j1", root.Children[0].Record.Id);
        Assert.Equal("j2", root.Children[1].Record.Id);
    }

    [Fact]
    public void FlattenTree_SkipsFilteredPhaseAndCheckpoint()
    {
        var timeline = new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "s1", ParentId = null, Type = "Stage", Name = "Stage", Order = 1 },
                new TimelineRecord { Id = "p1", ParentId = "s1", Type = "Phase", Name = "Phase", Order = 1 },
                new TimelineRecord { Id = "j1", ParentId = "p1", Type = "Job", Name = "Job", Order = 1 },
            },
        };

        var detail = NewDetail(timeline);
        // Stage collapsed: only the stage shows.
        Assert.Single(detail.FlatItems);
        Assert.Equal("Stage", detail.FlatItems[0].Record.Name);

        // Expanding the stage should reveal the Job at the same visual depth (Phase skipped).
        detail.ToggleExpand();
        Assert.Equal(2, detail.FlatItems.Count);
        Assert.Equal("Job", detail.FlatItems[1].Record.Name);
        Assert.Equal(1, detail.FlatItems[1].VisualDepth);
    }

    [Fact]
    public void HasChildren_LooksThroughFilteredTypes()
    {
        var tree = DetailModel.BuildTimelineTree(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "s1", ParentId = null, Type = "Stage", Name = "Stage", Order = 1 },
                new TimelineRecord { Id = "p1", ParentId = "s1", Type = "Phase", Name = "Phase", Order = 1 },
                new TimelineRecord { Id = "j1", ParentId = "p1", Type = "Job", Name = "Job", Order = 1 },
            },
        });
        Assert.True(tree[0].HasChildren());
    }

    [Fact]
    public void Navigation_MoveDownMoveUp()
    {
        var detail = NewDetail(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "t1", ParentId = null, Type = "Task", Name = "A", Order = 1 },
                new TimelineRecord { Id = "t2", ParentId = null, Type = "Task", Name = "B", Order = 2 },
            },
        });

        Assert.Equal(0, detail.SelectedIndex());
        detail.MoveDown();
        Assert.Equal(1, detail.SelectedIndex());
        detail.MoveUp();
        Assert.Equal(0, detail.SelectedIndex());
    }

    [Fact]
    public void CanViewLogs_TrueOnlyForLeafWithLog()
    {
        var detail = NewDetail(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "t1", ParentId = null, Type = "Task", Name = "withLog", Order = 1,
                    Log = new LogReference { Id = 5 } },
                new TimelineRecord { Id = "t2", ParentId = null, Type = "Task", Name = "noLog", Order = 2 },
            },
        });

        Assert.True(detail.CanViewLogs());
        detail.MoveDown();
        Assert.False(detail.CanViewLogs());
        Assert.Contains("no logs", detail.GetStatusMessage());
    }

    [Fact]
    public void GetContextItems_IncludesNavigateAndEnter()
    {
        var detail = NewDetail(new Timeline { Records = new() });
        var items = detail.GetContextItems();
        Assert.Contains(items, i => i.Description.Contains("navigate"));
        Assert.Contains(items, i => i.Key == "enter");
    }

    [Fact]
    public void Search_FiltersByName()
    {
        var detail = NewDetail(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "t1", ParentId = null, Type = "Task", Name = "npm install", Order = 1 },
                new TimelineRecord { Id = "t2", ParentId = null, Type = "Task", Name = "dotnet build", Order = 2 },
            },
        });

        detail.EnterSearch();
        Assert.True(detail.IsSearching());
        detail.SetSearchQuery("npm");
        Assert.Single(detail.FlatItems);
        Assert.Equal("npm install", detail.FlatItems[0].Record.Name);

        detail.ExitSearch();
        Assert.False(detail.IsSearching());
        Assert.Equal(2, detail.FlatItems.Count);
    }

    [Fact]
    public void Search_FindsCollapsedDescendants()
    {
        var detail = NewDetail(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "s1", ParentId = null, Type = "Stage", Name = "Build", Order = 1 },
                new TimelineRecord { Id = "j1", ParentId = "s1", Type = "Job", Name = "hidden job", Order = 1 },
            },
        });

        // Stage collapsed; search should still find the hidden job.
        detail.EnterSearch();
        detail.SetSearchQuery("hidden");
        Assert.Single(detail.FlatItems);
        Assert.Equal("hidden job", detail.FlatItems[0].Record.Name);
    }

    [Fact]
    public void GetScrollPercent_TracksSelection()
    {
        var records = new List<TimelineRecord>();
        for (int i = 0; i < 5; i++)
            records.Add(new TimelineRecord { Id = $"t{i}", ParentId = null, Type = "Task", Name = $"T{i}", Order = i });
        var detail = NewDetail(new Timeline { Records = records });

        Assert.Equal(0, detail.GetScrollPercent());
        detail.MoveDown();
        detail.MoveDown();
        detail.MoveDown();
        detail.MoveDown();
        Assert.Equal(100, detail.GetScrollPercent());
    }

    [Theory]
    [InlineData("inProgress", "", "●")]
    [InlineData("pending", "", "○")]
    [InlineData("completed", "succeeded", "✓")]
    [InlineData("completed", "failed", "✗")]
    [InlineData("completed", "succeededWithIssues", "◐")]
    public void RecordIcon_ReflectsStateAndResult(string state, string result, string want)
    {
        Assert.Contains(want, DetailModel.RecordIcon(state, result, S));
    }

    [Fact]
    public void FormatRecordDuration_NullTimesReturnDash()
    {
        Assert.Equal("-", DetailModel.FormatRecordDuration(null, null));
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("-", DetailModel.FormatRecordDuration(start, null));
        Assert.Equal("5m0s", DetailModel.FormatRecordDuration(start, start.AddMinutes(5)));
    }

    [Fact]
    public void View_RendersHeaderAndRecords()
    {
        var detail = NewDetail(new Timeline
        {
            Records = new()
            {
                new TimelineRecord { Id = "t1", ParentId = null, Type = "Task", Name = "npm install", Order = 1 },
            },
        });
        var view = detail.View();
        Assert.Contains("npm install", view);
    }
}
