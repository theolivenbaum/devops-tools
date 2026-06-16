using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views;
using Azdo.Tui.Views.PullRequests;
using Xunit;

namespace Azdo.Tests.Views.PullRequests;

public class ModelTests
{
    private static Model New() => new((Azdo.Core.AzureDevOps.MultiClient?)null);

    [Fact]
    public void Implements_TabAndRestorableInterfaces()
    {
        var m = New();
        Assert.IsAssignableFrom<ITabView>(m);
        Assert.IsAssignableFrom<IRestorableTab>(m);
    }

    [Fact]
    public void StartsInListMode()
    {
        var m = New();
        Assert.Equal(PrViewMode.List, m.GetViewMode());
        Assert.Equal(0, m.DetailItemId());
    }

    [Fact]
    public void FilterLabel_ReflectsActiveFilter()
    {
        var m = New();
        Assert.Equal("", m.FilterLabel());

        // With a null client, 'm' toggles myPRsOnly then fetches (null client -> empty).
        m.Update(new WindowSizeMsg(120, 40));
        m.Update(KeyMsg.Rune_('m'));
        Assert.True(m.IsMyPRsActive());
        Assert.Equal("My PRs", m.FilterLabel());

        // 'A' is mutually exclusive with 'm'.
        m.Update(KeyMsg.Named("A"));
        Assert.True(m.IsAsReviewerActive());
        Assert.False(m.IsMyPRsActive());
        Assert.Equal("Reviewer", m.FilterLabel());
    }

    [Fact]
    public void MyPRs_TogglingOff_RestoresAll()
    {
        var m = New();
        m.Update(KeyMsg.Rune_('m'));
        Assert.True(m.IsMyPRsActive());
        m.Update(KeyMsg.Rune_('m'));
        Assert.False(m.IsMyPRsActive());
    }

    [Fact]
    public void SetPRsMsg_PopulatesList()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        var prs = new[] { TestSupport.Pr(1, "One"), TestSupport.Pr(2, "Two") };
        m.Update(new SetPRsMsg(prs));
        Assert.Contains("One", Ansi.Strip(m.View()));
    }

    [Fact]
    public void DefaultKeybindings_ContainsExpectedKeys()
    {
        var s = Ansi.Strip(New().DefaultKeybindings());
        Assert.Contains("refresh", s);
        Assert.Contains("my PRs", s);
        Assert.Contains("as reviewer", s);
        Assert.Contains("•", s); // separator
    }

    [Fact]
    public void PendingDetailRestore_OpensDetailOnPopulate()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        m.SetPendingDetailRestore(2);
        var prs = new[] { TestSupport.Pr(1, "One"), TestSupport.Pr(2, "Two") };
        m.Update(new SetPRsMsg(prs));

        Assert.Equal(PrViewMode.Detail, m.GetViewMode());
        Assert.Equal(2, m.DetailItemId());
    }

    [Fact]
    public void PendingDetailRestore_ConsumedOnce()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        m.SetPendingDetailRestore(99); // not present
        m.Update(new SetPRsMsg(new[] { TestSupport.Pr(1) }));
        Assert.Equal(PrViewMode.List, m.GetViewMode());

        // A later populate that *does* contain id 99 must not hijack into detail.
        m.Update(new SetPRsMsg(new[] { TestSupport.Pr(99) }));
        Assert.Equal(PrViewMode.List, m.GetViewMode());
    }

    [Fact]
    public void EnterOnList_OpensDetail()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        m.Update(new SetPRsMsg(new[] { TestSupport.Pr(5, "Detail me") }));
        m.Update(KeyMsg.Named("enter"));
        Assert.Equal(PrViewMode.Detail, m.GetViewMode());
        Assert.Equal(5, m.DetailItemId());
    }

    [Fact]
    public void OpenFileDiffMsg_FromDetail_EntersDiffMode()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        m.Update(new SetPRsMsg(new[] { TestSupport.Pr(5) }));
        m.Update(KeyMsg.Named("enter")); // into detail
        Assert.Equal(PrViewMode.Detail, m.GetViewMode());

        m.Update(new OpenFileDiffMsg(TestSupport.Change("/a.cs")));
        Assert.Equal(PrViewMode.Diff, m.GetViewMode());
        Assert.True(m.HasContextBar());
    }

    [Fact]
    public void ExitDiffViewMsg_ReturnsToDetail()
    {
        var m = New();
        m.Update(new WindowSizeMsg(120, 40));
        m.Update(new SetPRsMsg(new[] { TestSupport.Pr(5) }));
        m.Update(KeyMsg.Named("enter"));
        m.Update(new OpenFileDiffMsg(TestSupport.Change("/a.cs")));
        Assert.Equal(PrViewMode.Diff, m.GetViewMode());

        m.Update(ExitDiffViewMsg.Instance);
        Assert.Equal(PrViewMode.Detail, m.GetViewMode());
    }

    [Fact]
    public void IsSearching_FalseInitially()
        => Assert.False(New().IsSearching());
}
