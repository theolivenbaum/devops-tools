using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.PullRequests;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.PullRequests;

public class DetailViewTests
{
    private static readonly StyleSet S = StyleSet.Default();

    private static PullRequestDetailView Ready(FakeAzdoClient client, PullRequest pr)
    {
        var d = new PullRequestDetailView(client, pr, S);
        d.SetSize(120, 40);
        return d;
    }

    [Fact]
    public void Init_FetchesThreadsAndFiles()
    {
        var client = new FakeAzdoClient
        {
            Threads = { TestSupport.CodeThread(1, "/a.cs", 3, "active", "looks good") },
            Changes = { TestSupport.Change("/a.cs") },
        };
        var d = Ready(client, TestSupport.Pr(7));

        foreach (var m in TestSupport.RunAll(d.Init()))
            d.Update(m);

        Assert.Single(d.GetChangedFiles());
        Assert.Single(d.GetThreads());
        Assert.Contains("a.cs", Ansi.Strip(d.View()));
    }

    [Fact]
    public void SetChangedFiles_FiltersTreeAndEmpty()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(1));
        d.SetChangedFiles(new[]
        {
            TestSupport.Change("/real.cs"),
            TestSupport.Change("/folder", objectType: "tree"),
            TestSupport.Change(""),
        });
        Assert.Single(d.GetChangedFiles());
    }

    [Fact]
    public void EnterOnGeneralComments_EmitsOpenGeneralCommentsMsg()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(1));
        d.SetThreads(new[] { TestSupport.GeneralThread(5, "active", "hi") });
        d.SetChangedFiles(new[] { TestSupport.Change("/a.cs") });
        // fileIndex starts at 0 = general comments entry
        var cmd = d.Update(KeyMsg.Named("enter"));
        Assert.IsType<OpenGeneralCommentsMsg>(TestSupport.Run(cmd));
    }

    [Fact]
    public void EnterOnFile_EmitsOpenFileDiffMsg()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(1));
        d.SetChangedFiles(new[] { TestSupport.Change("/a.cs"), TestSupport.Change("/b.cs") });
        // No general comments, so index 0 is the first file.
        var msg = TestSupport.Run(d.Update(KeyMsg.Named("enter")));
        var open = Assert.IsType<OpenFileDiffMsg>(msg);
        Assert.Equal("/a.cs", open.File.Item.Path);
    }

    [Fact]
    public void VoteKey_OpensPicker_AndSelectionVotes()
    {
        var client = new FakeAzdoClient();
        var d = Ready(client, TestSupport.Pr(1));
        d.SetThreads(System.Array.Empty<Azdo.Core.AzureDevOps.Thread>());
        d.SetChangedFiles(System.Array.Empty<IterationChange>());

        d.Update(KeyMsg.Rune_('v'));
        Assert.True(d.IsVotePickerVisible);

        // Picker selects Approve (top option) on enter and emits VoteSelectedMsg.
        var pickerCmd = d.Update(KeyMsg.Named("enter"));
        var voteMsg = TestSupport.Run(pickerCmd);
        var selected = Assert.IsType<VoteSelectedMsg>(voteMsg);

        // Feeding the VoteSelectedMsg back triggers VotePullRequestAsync.
        foreach (var m in TestSupport.RunAll(d.Update(selected)))
            d.Update(m);
        Assert.Equal(1, client.VoteCalls);
        Assert.Equal(selected.Vote, client.LastVote);
    }

    [Fact]
    public void OpenInBrowser_UsesSeam()
    {
        string? opened = null;
        var prev = PullRequestDetailView.OpenUrl;
        PullRequestDetailView.OpenUrl = url => { opened = url; return null; };
        try
        {
            var d = Ready(new FakeAzdoClient { Org = "org", Project = "proj" }, TestSupport.Pr(42));
            d.SetThreads(System.Array.Empty<Azdo.Core.AzureDevOps.Thread>());
            d.SetChangedFiles(System.Array.Empty<IterationChange>());
            var msg = TestSupport.Run(d.Update(KeyMsg.Rune_('o')));
            Assert.IsType<OpenUrlResultMsg>(msg);
            Assert.NotNull(opened);
            Assert.Contains("pullrequest/42", opened);
        }
        finally
        {
            PullRequestDetailView.OpenUrl = prev;
        }
    }

    [Fact]
    public void GetContextItems_IncludesVoteAndOpen()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(1));
        var keys = d.GetContextItems().Select(c => c.Key).ToList();
        Assert.Contains("v", keys);
        Assert.Contains("o", keys);
    }

    [Fact]
    public void AdapterDelegatesToModel()
    {
        var d = Ready(new FakeAzdoClient(), TestSupport.Pr(99, "Adapter PR"));
        d.SetThreads(System.Array.Empty<Azdo.Core.AzureDevOps.Thread>());
        d.SetChangedFiles(System.Array.Empty<IterationChange>());
        IDetailView adapter = new PullRequestDetailAdapter(d);
        Assert.Contains("Adapter PR", Ansi.Strip(adapter.View()));
        Assert.Equal(d.GetContextItems().Count, adapter.GetContextItems().Count);
    }
}
