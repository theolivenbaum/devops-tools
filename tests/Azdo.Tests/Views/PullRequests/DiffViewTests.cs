using Azdo.Core.AzureDevOps;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.PullRequests;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tests.Views.PullRequests;

public class DiffViewTests
{
    private static readonly StyleSet S = StyleSet.Default();

    private static PullRequestDiffView Ready(FakeAzdoClient client, PullRequest pr, IReadOnlyList<Thread>? threads = null)
    {
        var d = new PullRequestDiffView(client, pr, threads ?? System.Array.Empty<Thread>(), S);
        d.SetSize(120, 40);
        return d;
    }

    private static void Pump(PullRequestDiffView d, Cmd? cmd)
    {
        foreach (var m in TestSupport.RunAll(cmd))
            d.Update(m);
    }

    [Fact]
    public void Init_LoadsFileList()
    {
        var client = new FakeAzdoClient { Changes = { TestSupport.Change("/a.cs"), TestSupport.Change("/b.cs") } };
        var d = Ready(client, TestSupport.Pr(1));
        Pump(d, d.Init());
        Assert.Equal(DiffViewMode.FileList, d.GetViewMode());
        var view = Ansi.Strip(d.View());
        Assert.Contains("Changed files (2)", view);
        Assert.Contains("a.cs", view);
    }

    [Fact]
    public void EnterOnFile_LoadsAndRendersDiff()
    {
        var client = new FakeAzdoClient
        {
            Changes = { TestSupport.Change("/a.cs", "edit") },
            FileContents =
            {
                ["/a.cs@main"] = "line1\nline2\nline3\n",
                ["/a.cs@feature/x"] = "line1\nCHANGED\nline3\n",
            },
        };
        var d = Ready(client, TestSupport.Pr(1));
        Pump(d, d.Init());

        // Move down to first file (index 1), then enter.
        d.Update(KeyMsg.Named("down"));
        Pump(d, d.Update(KeyMsg.Named("enter")));

        Assert.Equal(DiffViewMode.FileView, d.GetViewMode());
        var view = Ansi.Strip(d.View());
        Assert.Contains("CHANGED", view);
        Assert.Contains("a.cs", view);
    }

    [Fact]
    public void InitWithFile_OpensDiffDirectly()
    {
        var client = new FakeAzdoClient
        {
            Changes = { TestSupport.Change("/a.cs", "add") },
            FileContents = { ["/a.cs@feature/x"] = "new1\nnew2\n" },
        };
        var d = Ready(client, TestSupport.Pr(1));
        Pump(d, d.InitWithFile(TestSupport.Change("/a.cs", "add")));
        Assert.Equal(DiffViewMode.FileView, d.GetViewMode());
        Assert.Contains("new1", Ansi.Strip(d.View()));
    }

    [Fact]
    public void InlineComment_RenderedOnMappedLine()
    {
        var threads = new[] { TestSupport.CodeThread(5, "/a.cs", 2, "active", "please fix") };
        var client = new FakeAzdoClient
        {
            Changes = { TestSupport.Change("/a.cs", "edit") },
            FileContents =
            {
                ["/a.cs@main"] = "a\nb\nc\n",
                ["/a.cs@feature/x"] = "a\nB2\nc\n",
            },
        };
        var d = Ready(client, TestSupport.Pr(1), threads);
        Pump(d, d.InitWithFile(TestSupport.Change("/a.cs", "edit")));
        Assert.Contains("please fix", Ansi.Strip(d.View()));
    }

    [Fact]
    public void GeneralComments_RenderAndCreate()
    {
        var threads = new[] { TestSupport.GeneralThread(9, "active", "general note") };
        var client = new FakeAzdoClient();
        var d = Ready(client, TestSupport.Pr(1), threads);
        Pump(d, d.InitGeneralComments());
        Assert.Contains("general note", Ansi.Strip(d.View()));

        // Press 'c' to create a new general comment, type, submit.
        d.Update(KeyMsg.Rune_('c'));
        Assert.True(d.IsInputActive());
        d.Update(KeyMsg.Rune_('h'));
        d.Update(KeyMsg.Rune_('i'));
        Pump(d, d.Update(KeyMsg.Named("enter")));
        Assert.Equal(1, client.GeneralCommentCalls);
        Assert.False(d.IsInputActive());
    }

    [Fact]
    public void Resolve_NearestThread_CallsUpdateStatus()
    {
        var threads = new[] { TestSupport.CodeThread(11, "/a.cs", 2, "active", "fix me") };
        var client = new FakeAzdoClient
        {
            Changes = { TestSupport.Change("/a.cs", "edit") },
            FileContents = { ["/a.cs@main"] = "a\nb\n", ["/a.cs@feature/x"] = "a\nB\n" },
        };
        var d = Ready(client, TestSupport.Pr(1), threads);
        Pump(d, d.InitWithFile(TestSupport.Change("/a.cs", "edit")));

        Pump(d, d.Update(KeyMsg.Rune_('x')));
        Assert.Equal(1, client.ResolveCalls);
        Assert.Equal(11, client.LastResolveThreadId);
        Assert.Equal("fixed", client.LastResolveStatus);
    }

    [Fact]
    public void Reply_NearestThread_CallsReply()
    {
        var threads = new[] { TestSupport.CodeThread(13, "/a.cs", 2, "active", "discuss") };
        var client = new FakeAzdoClient
        {
            Changes = { TestSupport.Change("/a.cs", "edit") },
            FileContents = { ["/a.cs@main"] = "a\nb\n", ["/a.cs@feature/x"] = "a\nB\n" },
        };
        var d = Ready(client, TestSupport.Pr(1), threads);
        Pump(d, d.InitWithFile(TestSupport.Change("/a.cs", "edit")));

        d.Update(KeyMsg.Rune_('p'));
        Assert.True(d.IsInputActive());
        d.Update(KeyMsg.Rune_('o'));
        d.Update(KeyMsg.Rune_('k'));
        Pump(d, d.Update(KeyMsg.Named("enter")));
        Assert.Equal(1, client.ReplyCalls);
        Assert.Equal(13, client.LastReplyThreadId);
    }

    [Fact]
    public void Esc_FromFileList_ExitsDiff()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(1));
        Pump(d, d.Init());
        var msg = TestSupport.Run(d.Update(KeyMsg.Named("esc")));
        Assert.IsType<ExitDiffViewMsg>(msg);
    }

    [Fact]
    public void ContextItems_ChangeWithMode()
    {
        var client = new FakeAzdoClient { Changes = { TestSupport.Change("/a.cs", "edit") }, FileContents = { ["/a.cs@main"] = "a\n", ["/a.cs@feature/x"] = "b\n" } };
        var d = Ready(client, TestSupport.Pr(1));
        Pump(d, d.Init());
        Assert.Contains("enter", d.GetContextItems().Select(c => c.Key));

        d.Update(KeyMsg.Named("down"));
        Pump(d, d.Update(KeyMsg.Named("enter")));
        var keys = d.GetContextItems().Select(c => c.Key).ToList();
        Assert.Contains("c", keys);
        Assert.Contains("x", keys);
    }
}
