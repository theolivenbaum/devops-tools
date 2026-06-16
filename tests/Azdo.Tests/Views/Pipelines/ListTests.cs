using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Polling;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views;
using Azdo.Tui.Views.Pipelines;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.Pipelines;

public class ListTests
{
    private static StyleSet S => StyleSet.Default();

    [Theory]
    [InlineData("inProgress", "", "Running")]
    [InlineData("InProgress", "", "Running")]
    [InlineData("notStarted", "", "Queued")]
    [InlineData("NotStarted", "", "Queued")]
    [InlineData("canceling", "", "Cancel")]
    [InlineData("completed", "succeeded", "Success")]
    [InlineData("completed", "failed", "Failed")]
    [InlineData("completed", "canceled", "Cancel")]
    [InlineData("completed", "partiallySucceeded", "Partial")]
    [InlineData("", "", "/")]
    [InlineData("somethingElse", "", "somethingElse")]
    public void StatusIcon_ContainsExpectedLabel(string status, string result, string want)
    {
        var got = Model.StatusIcon(status, result, S);
        Assert.Contains(want, got);
    }

    [Fact]
    public void InitialViewMode_IsList()
    {
        var model = new Model(null);
        Assert.Equal(PipelinesViewMode.List, model.GetViewMode());
    }

    [Fact]
    public void Enter_TransitionsToDetail_ThenEscReturnsToList()
    {
        var model = new Model(null);
        model.SetItems(new[]
        {
            new PipelineRun { Id = 123, BuildNumber = "20240206.1", Status = "completed", Result = "succeeded",
                Definition = new PipelineDefinition { Id = 1, Name = "CI Pipeline" } },
        });

        model.Update(KeyMsg.Named("enter"));
        Assert.Equal(PipelinesViewMode.Detail, model.GetViewMode());
        Assert.NotNull(model.Detail());

        model.Update(KeyMsg.Named("esc"));
        Assert.Equal(PipelinesViewMode.List, model.GetViewMode());
    }

    [Fact]
    public void Enter_OnItemWithLog_OpensLogViewer_AndEscNavigatesBack()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(80, 24));
        model.SetItems(new[]
        {
            new PipelineRun { Id = 456, BuildNumber = "20240206.2",
                Definition = new PipelineDefinition { Id = 1, Name = "Build Pipeline" } },
        });

        model.Update(KeyMsg.Named("enter"));
        var detail = model.Detail();
        Assert.NotNull(detail);
        detail!.SetTimeline(new Timeline
        {
            Id = "test-timeline",
            Records = new()
            {
                new TimelineRecord { Id = "task-1", Type = "Task", Name = "npm install", State = "completed",
                    Log = new LogReference { Id = 10 } },
            },
        });

        model.Update(KeyMsg.Named("enter"));
        Assert.Equal(PipelinesViewMode.Logs, model.GetViewMode());
        Assert.NotNull(model.LogViewer());

        model.Update(KeyMsg.Named("esc"));
        Assert.Equal(PipelinesViewMode.Detail, model.GetViewMode());

        model.Update(KeyMsg.Named("esc"));
        Assert.Equal(PipelinesViewMode.List, model.GetViewMode());
    }

    [Fact]
    public void Enter_OnItemWithoutLog_StaysInDetail()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(80, 24));
        model.SetItems(new[]
        {
            new PipelineRun { Id = 789, BuildNumber = "20240206.3",
                Definition = new PipelineDefinition { Id = 1, Name = "Test Pipeline" } },
        });

        model.Update(KeyMsg.Named("enter"));
        model.Detail()!.SetTimeline(new Timeline
        {
            Id = "test-timeline",
            Records = new()
            {
                new TimelineRecord { Id = "stage-1", Type = "Stage", Name = "Build Stage", State = "completed", Log = null },
            },
        });

        model.Update(KeyMsg.Named("enter"));
        Assert.Equal(PipelinesViewMode.Detail, model.GetViewMode());
    }

    [Fact]
    public void Enter_OnExpandableNode_TogglesExpand_NotLogs()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(80, 24));
        model.SetItems(new[]
        {
            new PipelineRun { Id = 123, BuildNumber = "20240206.1",
                Definition = new PipelineDefinition { Id = 1, Name = "Build Pipeline" } },
        });

        model.Update(KeyMsg.Named("enter"));
        var detail = model.Detail()!;
        detail.SetTimeline(new Timeline
        {
            Id = "test",
            Records = new()
            {
                new TimelineRecord { Id = "stage-1", ParentId = null, Type = "Stage", Name = "Build", Order = 1 },
                new TimelineRecord { Id = "job-1", ParentId = "stage-1", Type = "Job", Name = "Build Job", Order = 1,
                    Log = new LogReference { Id = 10 } },
            },
        });

        Assert.Single(detail.FlatItems); // collapsed

        model.Update(KeyMsg.Named("enter"));
        Assert.Equal(PipelinesViewMode.Detail, model.GetViewMode());
        Assert.Equal(2, detail.FlatItems.Count); // expanded
    }

    [Fact]
    public void RunsToRows_IncludesTimestampAndDuration()
    {
        var queue = new DateTime(2024, 2, 10, 14, 30, 0, DateTimeKind.Utc);
        var start = new DateTime(2024, 2, 10, 14, 31, 0, DateTimeKind.Utc);
        var finish = new DateTime(2024, 2, 10, 14, 36, 0, DateTimeKind.Utc);

        var rows = Model.RunsToRows(new[]
        {
            new PipelineRun { Id = 123, BuildNumber = "20240210.1", Status = "completed", Result = "succeeded",
                SourceBranch = "refs/heads/main", QueueTime = queue, StartTime = start, FinishTime = finish,
                Definition = new PipelineDefinition { Id = 1, Name = "CI Pipeline" } },
        }, S);

        var row = Assert.Single(rows);
        Assert.Equal(6, row.Length);
        Assert.Equal("2024-02-10 14:30", row[4]);
        Assert.Equal("5m0s", row[5]);
    }

    [Fact]
    public void RunsToRowsMulti_IncludesProjectColumn()
    {
        var rows = Model.RunsToRowsMulti(new[]
        {
            new PipelineRun { Id = 1, BuildNumber = "20240210.1", Status = "completed", Result = "succeeded",
                SourceBranch = "refs/heads/main", QueueTime = DateTime.UtcNow,
                Definition = new PipelineDefinition { Name = "CI" },
                Project = new Project { Name = "alpha" }, ProjectDisplayName = "alpha" },
        }, S);

        var row = Assert.Single(rows);
        Assert.Equal(7, row.Length);
        Assert.Equal("alpha", row[0]);
    }

    [Theory]
    [InlineData("CI Pipeline", true)]
    [InlineData("ci pipe", true)]
    [InlineData("deploy", true)]
    [InlineData("20240210", true)]
    [InlineData("nonexistent", false)]
    [InlineData("", true)]
    public void FilterPipelineRun_Matches(string query, bool want)
    {
        var run = new PipelineRun
        {
            BuildNumber = "20240210.1",
            SourceBranch = "refs/heads/feature/deploy",
            Definition = new PipelineDefinition { Name = "CI Pipeline" },
        };
        Assert.Equal(want, Model.FilterPipelineRun(run, query));
    }

    [Fact]
    public void FilterPipelineRunMulti_MatchesProjectName()
    {
        var run = new PipelineRun
        {
            BuildNumber = "20240210.1",
            SourceBranch = "refs/heads/main",
            Definition = new PipelineDefinition { Name = "CI" },
            Project = new Project { Name = "alpha" },
        };
        Assert.True(Model.FilterPipelineRunMulti(run, "alpha"));
        Assert.False(Model.FilterPipelineRunMulti(run, "beta"));
    }

    [Fact]
    public void View_ContainsAllColumnTitles()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(120, 30));
        model.SetItems(new[]
        {
            new PipelineRun { Id = 1, BuildNumber = "1", Definition = new PipelineDefinition { Name = "test" } },
        });

        var view = model.View();
        foreach (var title in new[] { "Status", "Pipeline", "Branch", "Build", "Timestamp", "Duration" })
            Assert.Contains(title, view);
    }

    [Fact]
    public void PipelineRunsUpdated_CriticalError_BubblesCommand_NoInlineError()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(120, 30));

        var criticalErr = new Exception("all projects failed: [HTTP request failed with status 400]");
        var cmd = model.Update(new PipelineRunsUpdated(new List<PipelineRun>(), criticalErr));

        Assert.NotNull(cmd);
        Assert.DoesNotContain("Error loading", model.View());
    }

    [Fact]
    public void PipelineRunsUpdated_PermissionError_ShowsInline_NoCommand()
    {
        // Missing "Build (Read)" scope → 403. The Pipelines tab degrades
        // gracefully (inline error), it must not bubble a critical-modal command.
        var model = new Model(null);
        model.Update(new WindowSizeMsg(120, 30));

        var cmd = model.Update(new PipelineRunsUpdated(new List<PipelineRun>(), Client.FormatHttpError(403)));

        Assert.Null(cmd);
        Assert.Contains("Error loading", model.View());
    }

    [Fact]
    public void PipelineRunsUpdated_TransientError_ShowsInline()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(120, 30));

        var cmd = model.Update(new PipelineRunsUpdated(new List<PipelineRun>(), new Exception("connection timeout")));

        Assert.Null(cmd);
        Assert.Contains("Error loading", model.View());
    }

    [Fact]
    public void PipelineRunsUpdated_Success_NoCommand()
    {
        var model = new Model(null);
        var cmd = model.Update(new PipelineRunsUpdated(new List<PipelineRun>(), null));
        Assert.Null(cmd);
    }

    [Fact]
    public void StatusPicker_S_OpensPicker_AndSelectionFilters()
    {
        var model = new Model(null);
        model.Update(new WindowSizeMsg(120, 30));
        model.SetItems(new[]
        {
            new PipelineRun { Id = 1, BuildNumber = "a", Status = "completed", Result = "succeeded",
                Definition = new PipelineDefinition { Name = "ok" } },
            new PipelineRun { Id = 2, BuildNumber = "b", Status = "completed", Result = "failed",
                Definition = new PipelineDefinition { Name = "bad" } },
        });

        model.Update(KeyMsg.Named("S"));
        Assert.True(model.IsStatusPickerVisible());
        Assert.True(model.IsCapturingInput());

        // Apply a "Failed" filter via the selection message.
        model.Update(new ListPickerSelectedMsg("Failed"));
        Assert.True(model.IsStatusFilterActive());
        Assert.Equal("Failed", model.ActiveStatus());
        Assert.Contains("status: Failed", model.FilterLabel());

        // Clearing restores no filter.
        model.Update(new ListPickerSelectedMsg(""));
        Assert.False(model.IsStatusFilterActive());
        Assert.Equal("", model.FilterLabel());
    }

    [Theory]
    [InlineData("inProgress", "", "Running")]
    [InlineData("completed", "succeeded", "Success")]
    [InlineData("completed", "failed", "Failed")]
    [InlineData("completed", "partiallySucceeded", "Partial")]
    public void GetStatusKey_MapsStatusResult(string status, string result, string want)
    {
        Assert.Equal(want, Model.GetStatusKey(status, result));
    }

    [Fact]
    public void DefaultKeybindings_IncludesExpectedKeys()
    {
        var model = new Model(null);
        var kb = model.DefaultKeybindings();
        foreach (var token in new[] { "refresh", "navigate", "details", "search", "status", "back", "help", "quit" })
            Assert.Contains(token, kb);
    }

    [Fact]
    public void Pipelines_DoesNotImplement_IRestorableTab()
    {
        Assert.False(typeof(IRestorableTab).IsAssignableFrom(typeof(Model)));
    }
}
