using Azdo.Core.AzureDevOps;
using Azdo.Tui.Rendering;
using Azdo.Tui.Views.PullRequests;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.PullRequests;

public class ListHelpersTests
{
    private static readonly StyleSet S = StyleSet.Default();

    [Fact]
    public void StatusIcon_Draft_ShowsDraft()
        => Assert.Contains("Draft", Ansi.Strip(Model.StatusIcon("active", true, S)));

    [Theory]
    [InlineData("active", "Active")]
    [InlineData("completed", "Merged")]
    [InlineData("abandoned", "Closed")]
    public void StatusIcon_MapsStatus(string status, string expected)
        => Assert.Contains(expected, Ansi.Strip(Model.StatusIcon(status, false, S)));

    [Fact]
    public void VoteIcon_NoReviewers_ShowsDash()
        => Assert.Equal("-", Ansi.Strip(Model.VoteIcon(new List<Reviewer>(), S)));

    [Fact]
    public void VoteIcon_RejectedTakesPriority()
    {
        var reviewers = new List<Reviewer>
        {
            new() { Vote = 10 },
            new() { Vote = -10 },
        };
        Assert.Contains("✗ 2", Ansi.Strip(Model.VoteIcon(reviewers, S)));
    }

    [Fact]
    public void VoteIcon_AllApproved_ShowsCheck()
    {
        var reviewers = new List<Reviewer> { new() { Vote = 10 }, new() { Vote = 10 } };
        Assert.Contains("✓ 2", Ansi.Strip(Model.VoteIcon(reviewers, S)));
    }

    [Fact]
    public void PrsToRows_ProducesSixColumns()
    {
        var rows = Model.PrsToRows(new[] { TestSupport.Pr(1, "Hello") }, S);
        var row = Assert.Single(rows);
        Assert.Equal(6, row.Length);
        Assert.Equal("Hello", row[1]);
        Assert.Contains("→", row[2]); // branch info
    }

    [Fact]
    public void PrsToRowsMulti_PrependsProject()
    {
        var pr = TestSupport.Pr(1);
        pr.ProjectDisplayName = "Proj A";
        var rows = Model.PrsToRowsMulti(new[] { pr }, S);
        var row = Assert.Single(rows);
        Assert.Equal(7, row.Length);
        Assert.Equal("Proj A", row[0]);
    }

    [Fact]
    public void FilterPr_MatchesTitleCaseInsensitive()
    {
        var pr = TestSupport.Pr(1, "Add Feature");
        Assert.True(Model.FilterPr(pr, "feature"));
        Assert.False(Model.FilterPr(pr, "nope"));
        Assert.True(Model.FilterPr(pr, "")); // empty matches all
    }

    [Fact]
    public void FilterPrMulti_MatchesProjectName()
    {
        var pr = TestSupport.Pr(1, "Other");
        pr.ProjectDisplayName = "Payments";
        Assert.True(Model.FilterPrMulti(pr, "payments"));
    }
}
