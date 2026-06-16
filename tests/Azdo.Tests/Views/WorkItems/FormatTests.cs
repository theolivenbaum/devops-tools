using Azdo.Core.AzureDevOps;
using Azdo.Tui.Views.WorkItems;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.WorkItems;

public class FormatTests
{
    private static StyleSet S => StyleSet.Default();

    [Theory]
    [InlineData("Bug", "Bug")]
    [InlineData("Task", "Task")]
    [InlineData("User Story", "Story")]
    [InlineData("Feature", "Feature")]
    public void TypeIcon_ContainsExpectedLabel(string type, string want)
        => Assert.Contains(want, Format.TypeIcon(type, S));

    [Theory]
    [InlineData("New", "New")]
    [InlineData("Active", "Active")]
    [InlineData("Closed", "Closed")]
    public void StateText_ContainsExpectedLabel(string state, string want)
        => Assert.Contains(want, Format.StateText(state, S));

    [Theory]
    [InlineData(1, "P1")]
    [InlineData(2, "P2")]
    [InlineData(3, "P3")]
    [InlineData(4, "P4")]
    public void PriorityText_ContainsExpectedLabel(int prio, string want)
        => Assert.Contains(want, Format.PriorityText(prio, S));

    [Fact]
    public void WorkItemsToRows_BuildsExpectedCells()
    {
        var items = new List<WorkItem>
        {
            new() { Id = 123, Fields = new() { Title = "Fix critical bug", State = "Active", WorkItemType = "Bug", Priority = 1, AssignedTo = new Identity { DisplayName = "John Doe" } } },
            new() { Id = 456, Fields = new() { Title = "Add new feature", State = "New", WorkItemType = "Task", Priority = 2, AssignedTo = null } },
        };

        var rows = Format.WorkItemsToRows(items, S);

        Assert.Equal(2, rows.Count);
        Assert.Equal("123", rows[0][1]);
        Assert.Equal("Fix critical bug", rows[0][2]);
        Assert.Equal("John Doe", rows[0][5]);
        Assert.Equal("-", rows[1][5]);
    }

    [Fact]
    public void WorkItemsToRowsMulti_IncludesProjectColumn()
    {
        var items = new List<WorkItem>
        {
            new() { Id = 100, Fields = new() { Title = "Test Item", WorkItemType = "Task", State = "Active", Priority = 2 }, ProjectName = "alpha", ProjectDisplayName = "alpha" },
        };

        var rows = Format.WorkItemsToRowsMulti(items, S);

        Assert.Single(rows);
        Assert.Equal(7, rows[0].Length);
        Assert.Equal("alpha", rows[0][0]);
    }

    [Theory]
    [InlineData("login", true)]
    [InlineData("LOGIN", true)]
    [InlineData("42", true)]
    [InlineData("active", true)]
    [InlineData("jane", true)]
    [InlineData("Bug", true)]
    [InlineData("nonexistent", false)]
    [InlineData("", true)]
    public void FilterWorkItem_MatchesExpected(string query, bool want)
    {
        var wi = new WorkItem
        {
            Id = 42,
            Fields = new() { Title = "Fix critical login bug", State = "Active", WorkItemType = "Bug", AssignedTo = new Identity { DisplayName = "Jane Smith" } },
        };
        Assert.Equal(want, Format.FilterWorkItem(wi, query));
    }

    [Theory]
    [InlineData("Sprint", true)]
    [InlineData("sprint 1", true)]
    [InlineData("backend", true)]
    [InlineData("urgent", true)]
    [InlineData("Sprint 2", false)]
    public void FilterWorkItem_MatchesTags(string query, bool want)
    {
        var wi = new WorkItem { Id = 42, Fields = new() { Title = "Fix login bug", State = "Active", WorkItemType = "Bug", Tags = "Sprint 1; Backend; Urgent" } };
        Assert.Equal(want, Format.FilterWorkItem(wi, query));
    }

    [Fact]
    public void FilterWorkItem_NilAssignedTo_DoesNotCrash()
    {
        var wi = new WorkItem { Id = 10, Fields = new() { Title = "Unassigned task", State = "New", WorkItemType = "Task", AssignedTo = null } };
        Assert.True(Format.FilterWorkItem(wi, "unassigned"));
        Assert.False(Format.FilterWorkItem(wi, "jane"));
    }

    [Fact]
    public void FilterWorkItemMulti_MatchesTagsAndProject()
    {
        var wi = new WorkItem { Id = 42, Fields = new() { Title = "Test", WorkItemType = "Task", Tags = "Sprint 1; Backend" }, ProjectName = "alpha" };
        Assert.True(Format.FilterWorkItemMulti(wi, "backend"));
        Assert.True(Format.FilterWorkItemMulti(wi, "alpha"));
        Assert.False(Format.FilterWorkItemMulti(wi, "beta"));
    }

    [Fact]
    public void CollectUniqueTags_DedupesAndSorts()
    {
        var items = new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Tags = "Sprint 1; Backend" } },
            new() { Id = 2, Fields = new() { Tags = "Sprint 1; Frontend" } },
            new() { Id = 3, Fields = new() { Tags = "Sprint 2; Backend" } },
            new() { Id = 4, Fields = new() { Tags = "" } },
        };

        var tags = Format.CollectUniqueTags(items);
        Assert.Equal(4, tags.Count);
        Assert.Contains("Sprint 1", tags);
        Assert.Contains("Backend", tags);
        Assert.Contains("Frontend", tags);
        Assert.Contains("Sprint 2", tags);
    }

    [Fact]
    public void CollectUniqueTags_Sorted()
    {
        var items = new List<WorkItem> { new() { Id = 1, Fields = new() { Tags = "Zebra; Alpha; Middle" } } };
        var tags = Format.CollectUniqueTags(items);
        Assert.Equal(new[] { "Alpha", "Middle", "Zebra" }, tags);
    }

    [Fact]
    public void ApplyTagFilter_FiltersCorrectly()
    {
        var items = new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Tags = "Sprint 1; Backend" } },
            new() { Id = 2, Fields = new() { Tags = "Sprint 1; Frontend" } },
            new() { Id = 3, Fields = new() { Tags = "Sprint 2; Backend" } },
        };

        Assert.Equal(2, Format.ApplyTagFilter(items, "Backend").Count);
        Assert.Equal(2, Format.ApplyTagFilter(items, "Sprint 1").Count);
        Assert.Empty(Format.ApplyTagFilter(items, "Nonexistent"));
        Assert.Equal(3, Format.ApplyTagFilter(items, "").Count);
    }

    [Theory]
    [InlineData("<p>Hello</p>", "Hello")]
    [InlineData("<div>Hello <b>World</b></div>", "Hello World")]
    [InlineData("Plain text", "Plain text")]
    [InlineData("&nbsp;spaces&nbsp;", "spaces")]
    [InlineData("&lt;not&gt; tags", "<not> tags")]
    [InlineData("&amp;&quot;&#39;", "&\"'")]
    [InlineData("<p>Line 1</p><p>Line 2</p>", "Line 1\nLine 2")]
    [InlineData("Hello<br>World", "Hello\nWorld")]
    [InlineData("Hello<br/>World", "Hello\nWorld")]
    public void StripHtmlTags_ProducesExpected(string input, string expected)
        => Assert.Equal(expected, Format.StripHtmlTags(input));

    [Theory]
    [InlineData("Project\\Sprint 1", "Project\\Sprint 1")]
    [InlineData("Project\\Release 1\\Sprint 1", "Release 1\\Sprint 1")]
    [InlineData("Very\\Long\\Path\\Sprint 1", "Path\\Sprint 1")]
    [InlineData("Single", "Single")]
    [InlineData("", "")]
    public void ShortenIterationPath_ProducesExpected(string input, string expected)
        => Assert.Equal(expected, Format.ShortenIterationPath(input));

    [Theory]
    [InlineData("myorg", "myproject", 123, "https://dev.azure.com/myorg/myproject/_workitems/edit/123")]
    [InlineData("", "project", 123, "")]
    [InlineData("org", "", 123, "")]
    public void BuildWorkItemUrl_ProducesExpected(string org, string project, int id, string want)
        => Assert.Equal(want, Format.BuildWorkItemUrl(org, project, id));
}
