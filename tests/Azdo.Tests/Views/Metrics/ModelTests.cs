using Azdo.Core.AzureDevOps;
using Azdo.Core.Configuration;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.Metrics;
using ViewMode = Azdo.Tui.Views.Metrics.ViewMode;
using Xunit;

namespace Azdo.Tests.Views.Metrics;

public class ModelTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Config MakeConfig() => new()
    {
        Metrics = new MetricsConfig
        {
            Enabled = true,
            IntervalDays = 14,
            ActiveStaleDays = 3,
            RFTStaleDays = 2,
            WIPLimit = 4,
        },
    };

    private static Model MakeModel()
    {
        var m = new Model(null, MakeConfig(), null);
        m.SetNow(() => FixedNow);
        return m;
    }

    private static WorkItem MkItem(int id, string state, string user, string project, double points, int changedDaysAgo, params string[] tags)
    {
        var wi = new WorkItem { Id = id, ProjectName = project, ProjectDisplayName = project };
        wi.Fields.State = state;
        wi.Fields.Title = "item-" + state.ToLowerInvariant();
        wi.Fields.StoryPoints = points;
        wi.Fields.Tags = string.Join("; ", tags);
        wi.Fields.StateChangeDate = FixedNow.AddDays(-changedDaysAgo);
        if (state == "Closed") wi.Fields.ClosedDate = FixedNow.AddDays(-changedDaysAgo);
        if (user != "") wi.Fields.AssignedTo = new Identity { DisplayName = user };
        return wi;
    }

    private static KeyMsg Rune(char c) => KeyMsg.Rune_(c);

    [Fact]
    public void HandleFetchResult_PopulatesAggregate()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(1, "Active", "Alice", "proj", 3, 5),
            MkItem(2, "Ready for Test", "Alice", "proj", 2, 1),
            MkItem(3, "Closed", "Bob", "proj", 5, 1),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        Assert.Equal(2, m.UserRows.Count);
        Assert.Single(m.Flags);
        Assert.False(m.IsLoading);
        Assert.Equal(FixedNow, m.LastUpdated);
    }

    [Fact]
    public void TagFilter_ReAggregates()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(1, "Active", "Alice", "proj", 1, 1, "sprint-42"),
            MkItem(2, "Active", "Bob", "proj", 1, 1),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        Assert.Equal(2, m.UserRows.Count);
        m.Update(new TagSelectedMsg("sprint-42"));
        Assert.Equal("sprint-42", m.ActiveTagValue);
        Assert.Single(m.UserRows);
        Assert.Equal("Alice", m.UserRows[0].User);
    }

    [Fact]
    public void TagFilter_Clear()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(1, "Active", "Alice", "proj", 1, 1, "sprint-42"),
            MkItem(2, "Active", "Bob", "proj", 1, 1),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        m.Update(new TagSelectedMsg("sprint-42"));
        m.Update(new TagSelectedMsg(""));
        Assert.Equal("", m.ActiveTagValue);
        Assert.Equal(2, m.UserRows.Count);
    }

    [Fact]
    public void FlagFilter_Cycles()
    {
        var m = MakeModel();
        Assert.Equal(FlagFilter.All, m.CurrentFlagFilter);
        m.Update(Rune('f'));
        Assert.Equal(FlagFilter.ActiveStale, m.CurrentFlagFilter);
        m.Update(Rune('f'));
        Assert.Equal(FlagFilter.RFTStale, m.CurrentFlagFilter);
        m.Update(Rune('f'));
        Assert.Equal(FlagFilter.All, m.CurrentFlagFilter);
    }

    [Fact]
    public void FlagFilter_DropsNonMatching()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(1, "Active", "Alice", "proj", 1, 5),
            MkItem(2, "Ready for Test", "Bob", "proj", 1, 10),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        Assert.Equal(2, m.Flags.Count);
        m.Update(Rune('f'));
        var vis = m.VisibleFlags();
        Assert.Single(vis);
        Assert.Equal("active-stale", vis[0].Reason);
        m.Update(Rune('f'));
        vis = m.VisibleFlags();
        Assert.Single(vis);
        Assert.Equal("rft-stale", vis[0].Reason);
        m.Update(Rune('f'));
        Assert.Equal(2, m.VisibleFlags().Count);
    }

    [Fact]
    public void PaneFocus_TogglesOnTab()
    {
        var m = MakeModel();
        Assert.Equal(FocusedPane.Flags, m.CurrentFocusedPane);
        m.Update(KeyMsg.Named("tab"));
        Assert.Equal(FocusedPane.Users, m.CurrentFocusedPane);
        m.Update(KeyMsg.Named("tab"));
        Assert.Equal(FocusedPane.Flags, m.CurrentFocusedPane);
    }

    [Fact]
    public void HandleFetchResult_PartialError()
    {
        var m = MakeModel();
        var items = new List<WorkItem> { MkItem(1, "Active", "Alice", "proj", 1, 1) };
        var pe = new PartialException(1, 3, Array.Empty<Exception>());
        m.Update(new MetricsLoadedMsg(items, pe, FixedNow));
        Assert.Single(m.UserRows);
        var msg = m.GetStatusMessage();
        Assert.Contains("1 of 3", msg);
    }

    [Fact]
    public void HandleFetchResult_FatalError()
    {
        var m = MakeModel();
        m.Update(new MetricsLoadedMsg(null, new Exception("boom"), FixedNow));
        Assert.Empty(m.UserRows);
        Assert.NotEqual("", m.GetStatusMessage());
    }

    [Fact]
    public void View_RendersHeaderInfo()
    {
        var m = MakeModel();
        m.Update(new MetricsLoadedMsg(new List<WorkItem>(), null, FixedNow));
        m.Update(new WindowSizeMsg(120, 30));
        var outStr = m.View();
        Assert.Contains("14d", outStr);
        Assert.Contains("3d", outStr);
        Assert.Contains("2d", outStr);
    }

    private static List<WorkItem> ManyUserItems(int n)
    {
        var items = new List<WorkItem>(n);
        for (int i = 0; i < n; i++)
            items.Add(MkItem(1000 + i, "Active", "user" + i, "proj", 1, 1));
        return items;
    }

    [Fact]
    public void RenderFlagsPane_AlignsColumns()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(6887, "Active", "Al", "proj", 1, 5),
            MkItem(101054, "Active", "Veronica-Longname", "very-long-proj", 1, 7),
            MkItem(42, "Ready for Test", "Bob", "p", 1, 5),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        var lines = m.RenderFlagsPaneForTest().Split('\n');
        Assert.True(lines.Length >= 4);
        int wantWidth = Ansi.Width(lines[1]);
        for (int i = 1; i < lines.Length; i++)
            Assert.Equal(wantWidth, Ansi.Width(lines[i]));
    }

    [Fact]
    public void RenderUsersPane_AlignsColumns()
    {
        var m = MakeModel();
        var items = new List<WorkItem>
        {
            MkItem(1, "Active", "Alice", "proj", 1, 5),
            MkItem(2, "Active", "Alice", "proj", 1, 1),
            MkItem(3, "Active", "Alice", "proj", 1, 1),
            MkItem(4, "Active", "Alice", "proj", 1, 1),
            MkItem(5, "Ready for Test", "Alice", "proj", 1, 1),
            MkItem(6, "Active", "Bob", "proj", 1, 1),
            MkItem(7, "Active", "Veronica-Longname-Person", "proj", 1, 5),
        };
        m.Update(new MetricsLoadedMsg(items, null, FixedNow));
        var lines = m.RenderUsersPaneForTest().Split('\n');
        Assert.True(lines.Length >= 5);
        int wantWidth = Ansi.Width(lines[2]);
        for (int i = 2; i < lines.Length; i++)
            Assert.Equal(wantWidth, Ansi.Width(lines[i]));
    }

    [Fact]
    public void Viewport_ScrollsOnPageDown()
    {
        var m = MakeModel();
        m.Update(new WindowSizeMsg(100, 10));
        m.Update(new MetricsLoadedMsg(ManyUserItems(20), null, FixedNow));
        int before = m.ViewportYOffset;
        m.Update(KeyMsg.Named("pgdown"));
        Assert.True(m.ViewportYOffset > before);
    }

    [Fact]
    public void Viewport_CursorAutoScrolls()
    {
        var m = MakeModel();
        m.Update(new WindowSizeMsg(100, 10));
        m.Update(new MetricsLoadedMsg(ManyUserItems(20), null, FixedNow));
        m.Update(KeyMsg.Named("tab"));
        int start = m.ViewportYOffset;
        for (int i = 0; i < 19; i++)
            m.Update(KeyMsg.Named("down"));
        Assert.True(m.ViewportYOffset > start);
        Assert.Equal(19, m.UserCursor);
    }

    [Fact]
    public void VToggle_SwitchesMode()
    {
        var m = MakeModel();
        Assert.Equal(ViewMode.Live, m.CurrentMode);
        m.Update(Rune('v'));
        Assert.Equal(ViewMode.Trends, m.CurrentMode);
        m.Update(Rune('v'));
        Assert.Equal(ViewMode.TrendsChart, m.CurrentMode);
        m.Update(Rune('v'));
        Assert.Equal(ViewMode.Live, m.CurrentMode);
    }

    [Fact]
    public void TagsSelectedMsg_UpdatesSelection()
    {
        var m = MakeModel();
        m.Update(new TagsSelectedMsg(new[] { "sprint-42" }));
        Assert.Single(m.SelectedSprints);
        Assert.Equal("sprint-42", m.SelectedSprints[0]);
    }

    [Fact]
    public void BackfillDoneMsg_SuccessSetsStatus()
    {
        var m = MakeModel();
        m.Update(new BackfillDoneMsg(47, 1234, 2, false, null));
        Assert.Contains("1234", m.GetStatusMessage());
        Assert.Contains("run_one_shot_backfill", m.GetStatusMessage());
    }

    [Fact]
    public void BackfillDoneMsg_ErrorSurfaced()
    {
        var m = MakeModel();
        m.Update(new BackfillDoneMsg(0, 0, 0, false, new Exception("HTTP 503")));
        Assert.Contains("503", m.GetStatusMessage());
    }

    [Fact]
    public void BackfillDoneMsg_AlreadyDoneIsQuiet()
    {
        var m = MakeModel();
        m.Update(new BackfillDoneMsg(0, 0, 0, true, null));
        Assert.Equal("", m.GetStatusMessage());
    }

    [Fact]
    public void DefaultKeybindings_MentionsCoreKeys()
    {
        var m = MakeModel();
        var kb = m.DefaultKeybindings();
        Assert.Contains("refresh", kb);
        Assert.Contains("live/trends", kb);
    }
}
