using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.WorkItems;
using Xunit;
using static Azdo.Tests.Views.WorkItems.TestHelpers;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.WorkItems;

public class DetailModelTests
{
    private static StyleSet S => StyleSet.Default();
    private static DetailModel NewDetail(WorkItem wi, Client? client = null) => new(client, wi, S);

    private static List<WorkItemComment> SampleComments() => new()
    {
        new() { Id = 45, Text = "Newest discussion point", CreatedBy = new Identity { DisplayName = "Jane Doe" }, CreatedDate = new DateTime(2026, 5, 2, 10, 30, 0, DateTimeKind.Utc) },
        new() { Id = 44, Text = "An earlier remark", CreatedBy = new Identity { DisplayName = "John Roe" }, CreatedDate = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc) },
    };

    [Fact]
    public void CommentsLoaded_RendersInViewport()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T", Description = "desc" } });
        m.SetSize(100, 40);
        m.Update(new CommentsLoadedMsg(SampleComments(), null));

        var view = m.View();
        foreach (var want in new[] { "Discussion", "Jane Doe", "Newest discussion point", "John Roe", "An earlier remark", "2026-05-02 10:30" })
            Assert.Contains(want, view);
    }

    [Fact]
    public void Comments_RenderedNewestFirst()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(120, 60);
        m.Update(new CommentsLoadedMsg(SampleComments(), null));

        var view = m.View();
        int newestIdx = view.IndexOf("Newest discussion point", StringComparison.Ordinal);
        int olderIdx = view.IndexOf("An earlier remark", StringComparison.Ordinal);
        Assert.True(newestIdx >= 0 && olderIdx >= 0);
        Assert.True(newestIdx < olderIdx);
    }

    [Fact]
    public void NoComments_ShowsHint()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        m.Update(new CommentsLoadedMsg(Array.Empty<WorkItemComment>(), null));
        Assert.Contains("No comments", m.View());
    }

    [Fact]
    public void CommentsLoadError_ShowsMessage()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        m.Update(new CommentsLoadedMsg(Array.Empty<WorkItemComment>(), new InvalidOperationException("network down")));
        Assert.Contains("Could not load comments", m.View());
    }

    [Fact]
    public void CKey_OpensCommentForm()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        Assert.False(m.IsCommentFormVisible);
        m.Update(Rune('c'));
        Assert.True(m.IsCommentFormVisible);
    }

    [Fact]
    public void CommentFormCancel_Hides()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        m.Update(Rune('c'));
        Assert.True(m.IsCommentFormVisible);

        var cmd = m.Update(Key("esc"));
        Assert.False(m.IsCommentFormVisible);
        Assert.NotNull(cmd);
        m.Update(Run(cmd)!);
        Assert.False(m.IsCommentFormVisible);
    }

    private static (DetailModel, Cmd?) OpenAndSubmitComment(DetailModel m, string text)
    {
        m.Update(Rune('c'));
        foreach (var ch in text) m.Update(Rune(ch));
        var cmd = m.Update(Key("ctrl+s"));
        Assert.NotNull(cmd);
        var submitted = Run(cmd);
        Assert.IsType<CommentSubmittedMsg>(submitted);
        var postCmd = m.Update(submitted!);
        return (m, postCmd);
    }

    [Fact]
    public void CommentSubmit_TriggersPost()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        var (_, cmd) = OpenAndSubmitComment(m, "my new comment");

        Assert.True(m.Posting);
        Assert.NotNull(cmd);
        Assert.False(m.IsCommentFormVisible);
    }

    [Fact]
    public void CommentPostError_KeepsDraft()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        OpenAndSubmitComment(m, "draft text");
        m.Update(new CommentPostedMsg(null, new InvalidOperationException("denied")));

        Assert.False(m.Posting);
        var status = m.GetStatusMessage().ToLowerInvariant();
        Assert.True(status.Contains("error") || status.Contains("fail"));
        Assert.True(m.IsCommentFormVisible);
        Assert.Equal("draft text", m.CommentForm.Value());
    }

    [Fact]
    public void CommentPostSuccess_Refetches()
    {
        var m = NewDetail(new WorkItem { Id = 1, Fields = new() { Title = "T" } });
        m.SetSize(100, 40);
        OpenAndSubmitComment(m, "shipped");
        var cmd = m.Update(new CommentPostedMsg(new WorkItemComment { Id = 9, Text = "shipped" }, null));

        Assert.False(m.Posting);
        Assert.NotNull(cmd);
        Assert.NotEqual("", m.GetStatusMessage());
    }

    [Fact]
    public void GetContextItems_IncludesCommentStateAndBrowser()
    {
        var m = NewDetail(new WorkItem { Id = 1 });
        var keys = m.GetContextItems().Select(i => i.Key).ToList();
        Assert.Contains("c", keys);
        Assert.Contains("w", keys);
        Assert.Contains("o", keys);
    }

    [Fact]
    public void Viewport_UsesFullAvailableHeight()
    {
        var wi = new WorkItem
        {
            Id = 123,
            Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug", Priority = 1, Description = string.Concat(Enumerable.Repeat("Long description text. ", 50)) },
        };
        var m = NewDetail(wi);
        const int height = 30;
        m.SetSize(80, height);

        var lines = m.View().Split('\n');
        Assert.Equal(height, lines.Length);
    }

    [Fact]
    public void Detail_ShowsTitleStateAndType()
    {
        var m = NewDetail(new WorkItem { Id = 456, Fields = new() { Title = "Important bug fix", State = "Active", WorkItemType = "Bug", Priority = 1 } });
        m.SetSize(100, 30);
        var view = m.View();
        Assert.Contains("456", view);
        Assert.Contains("Important bug fix", view);
        Assert.Contains("Active", view);
        Assert.Contains("Bug", view);
    }

    [Fact]
    public void Detail_BugShowsReproSteps()
    {
        var m = NewDetail(new WorkItem { Id = 100, Fields = new() { Title = "Login crash", State = "Active", WorkItemType = "Bug", Priority = 1, ReproSteps = "1. Open app\n2. Click login\n3. App crashes" } });
        m.SetSize(100, 30);
        var view = m.View();
        Assert.Contains("Open app", view);
        Assert.DoesNotContain("No description", view);
    }

    [Fact]
    public void Detail_BugWithoutReproStepsFallsBackToDescription()
    {
        var m = NewDetail(new WorkItem { Id = 101, Fields = new() { Title = "Minor issue", State = "New", WorkItemType = "Bug", Priority = 3, Description = "This is a bug description fallback" } });
        m.SetSize(100, 30);
        Assert.Contains("bug description fallback", m.View());
    }

    [Fact]
    public void Detail_TaskShowsDescription()
    {
        var m = NewDetail(new WorkItem { Id = 102, Fields = new() { Title = "Implement feature", State = "Active", WorkItemType = "Task", Priority = 2, Description = "Task description content here" } });
        m.SetSize(100, 30);
        Assert.Contains("Task description content here", m.View());
    }

    [Fact]
    public void Detail_ShowsTags()
    {
        var m = NewDetail(new WorkItem { Id = 300, Fields = new() { Title = "Tagged work item", State = "Active", WorkItemType = "Task", Priority = 2, Tags = "Sprint 5; Frontend; Critical" } });
        m.SetSize(100, 30);
        var view = m.View();
        Assert.Contains("Tags", view);
        Assert.Contains("Sprint 5", view);
        Assert.Contains("Frontend", view);
        Assert.Contains("Critical", view);
    }

    [Fact]
    public void Detail_NoTagsSectionWhenEmpty()
    {
        var m = NewDetail(new WorkItem { Id = 301, Fields = new() { Title = "No tags item", State = "Active", WorkItemType = "Task", Priority = 2 } });
        m.SetSize(100, 30);
        Assert.DoesNotContain("Tags:", m.View());
    }

    [Fact]
    public void Detail_ShowsLastChangedTimestamp()
    {
        var m = NewDetail(new WorkItem { Id = 500, Fields = new() { Title = "Timestamped item", State = "Active", WorkItemType = "Task", Priority = 2, ChangedDate = new DateTime(2026, 3, 1, 14, 30, 0, DateTimeKind.Utc), Description = "Some description" } });
        m.SetSize(100, 40);
        var view = m.View();
        Assert.Contains("2026-03-01 14:30", view);
        Assert.Contains("Last changed", view);
    }

    [Fact]
    public void Detail_ZeroChangedDateIsHidden()
    {
        var m = NewDetail(new WorkItem { Id = 502, Fields = new() { Title = "No timestamp item", State = "New", WorkItemType = "Task", Priority = 3 } });
        m.SetSize(100, 30);
        Assert.DoesNotContain("Last changed", m.View());
    }

    [Fact]
    public void Detail_LongDescriptionWrapsWithinViewWidth()
    {
        var longLine = string.Concat(Enumerable.Repeat("word ", 80));
        var m = NewDetail(new WorkItem { Id = 700, Fields = new() { Title = "Overflow test", State = "Active", WorkItemType = "Task", Priority = 2, Description = longLine } });
        const int viewWidth = 80;
        m.SetSize(viewWidth, 40);

        foreach (var line in m.View().Split('\n'))
            Assert.True(Azdo.Tui.Rendering.Ansi.Width(line) <= viewWidth);
    }

    [Fact]
    public void Detail_WrappedDescriptionPreservesContent()
    {
        const string description = "The quick brown fox jumps over the lazy dog and then continues running across the field until reaching the distant forest";
        var m = NewDetail(new WorkItem { Id = 702, Fields = new() { Title = "Content preservation test", State = "Active", WorkItemType = "Task", Priority = 2, Description = description } });
        m.SetSize(40, 30);

        var view = m.View();
        foreach (var word in description.Split(' '))
            Assert.Contains(word, view);
    }

    [Fact]
    public void GetScrollPercent_ZeroBeforeReady()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test" } });
        Assert.Equal(0, m.GetScrollPercent());
        m.SetSize(80, 20);
        Assert.True(m.GetScrollPercent() >= 0);
    }

    [Fact]
    public void WKey_StartsLoadingStates()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug" } });
        m.SetSize(80, 30);
        var cmd = m.Update(Rune('w'));
        Assert.True(m.Loading);
        Assert.NotNull(cmd);
    }

    [Fact]
    public void StatesLoaded_OpensStatePicker()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug" } });
        m.SetSize(80, 30);
        m.Update(new StatesLoadedMsg(new List<WorkItemTypeState>
        {
            new() { Name = "New", Color = "b2b2b2", Category = "Proposed" },
            new() { Name = "Active", Color = "007acc", Category = "InProgress" },
            new() { Name = "Resolved", Color = "ff9d00", Category = "Resolved" },
            new() { Name = "Closed", Color = "339933", Category = "Completed" },
        }, null));
        Assert.True(m.IsStatePickerVisible);
    }

    [Fact]
    public void StateUpdate_Success_UpdatesState()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug" } });
        m.SetSize(80, 30);
        m.Update(new StateUpdateResultMsg("Resolved", null));
        Assert.Equal("Resolved", m.GetWorkItem().Fields.State);
        Assert.NotEqual("", m.GetStatusMessage());
    }

    [Fact]
    public void StateUpdate_Error_KeepsState()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug" } });
        m.SetSize(80, 30);
        m.Update(new StateUpdateResultMsg("", new InvalidOperationException("access denied")));
        Assert.Equal("Active", m.GetWorkItem().Fields.State);
        Assert.Contains("Error", m.GetStatusMessage());
    }

    [Fact]
    public void StatePicker_RoutesInputAndEscCloses()
    {
        var m = NewDetail(new WorkItem { Id = 123, Fields = new() { Title = "Test item", State = "Active", WorkItemType = "Bug" } });
        m.SetSize(80, 30);
        m.Update(new StatesLoadedMsg(new List<WorkItemTypeState>
        {
            new() { Name = "New", Category = "Proposed" },
            new() { Name = "Active", Category = "InProgress" },
        }, null));
        Assert.True(m.IsStatePickerVisible);
        m.Update(Key("down"));
        m.Update(Key("esc"));
        Assert.False(m.IsStatePickerVisible);
    }

    [Fact]
    public void Detail_LinkBeforeDescription()
    {
        var client = new Client("myorg", "myproject", "fake-pat");
        var m = NewDetail(new WorkItem { Id = 200, Fields = new() { Title = "Test ordering", State = "Active", WorkItemType = "Task", Priority = 2, Description = "This is the description text" } }, client);
        m.SetSize(100, 40);
        var view = m.View();
        int linkIdx = view.IndexOf("Open in browser", StringComparison.Ordinal);
        int descIdx = view.IndexOf("Description", StringComparison.Ordinal);
        Assert.True(linkIdx >= 0 && descIdx >= 0);
        Assert.True(linkIdx < descIdx);
    }

    [Fact]
    public void OKey_OpensBrowser()
    {
        var orig = DetailModel.OpenUrl;
        try
        {
            string? opened = null;
            DetailModel.OpenUrl = url => { opened = url; return null; };

            var client = new Client("myorg", "myproject", "fake-pat");
            var m = NewDetail(new WorkItem { Id = 999, Fields = new() { Title = "Test", State = "Active", WorkItemType = "Bug" } }, client);
            m.SetSize(80, 30);

            var cmd = m.Update(Rune('o'));
            Assert.NotNull(cmd);
            var msg = Run(cmd);
            Assert.IsType<OpenUrlResultMsg>(msg);
            Assert.Equal("https://dev.azure.com/myorg/myproject/_workitems/edit/999", opened);
        }
        finally { DetailModel.OpenUrl = orig; }
    }

    [Fact]
    public void OKey_NoClient_SetsStatusMessage()
    {
        var m = NewDetail(new WorkItem { Id = 1 });
        m.SetSize(80, 30);
        var cmd = m.Update(Rune('o'));
        Assert.Null(cmd);
        Assert.NotEqual("", m.GetStatusMessage());
    }

    [Fact]
    public void OpenUrlResult_Success_SetsStatusMessage()
    {
        var m = NewDetail(new WorkItem { Id = 1 });
        m.SetSize(80, 30);
        m.Update(new OpenUrlResultMsg(null));
        Assert.NotEqual("", m.GetStatusMessage());
    }

    [Fact]
    public void OpenUrlResult_Error_SetsStatusMessage()
    {
        var m = NewDetail(new WorkItem { Id = 1 });
        m.SetSize(80, 30);
        m.Update(new OpenUrlResultMsg(new InvalidOperationException("no browser")));
        var status = m.GetStatusMessage().ToLowerInvariant();
        Assert.True(status.Contains("fail") || status.Contains("error"));
    }
}
