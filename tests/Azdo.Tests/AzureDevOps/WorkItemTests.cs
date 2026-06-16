using System.Net;
using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class WorkItemTests
{
    [Theory]
    [InlineData("New", "○")]
    [InlineData("new", "○")]
    [InlineData("Active", "◐")]
    [InlineData("active", "◐")]
    [InlineData("Resolved", "●")]
    [InlineData("resolved", "●")]
    [InlineData("Ready for Test", "●")]
    [InlineData("ready for test", "●")]
    [InlineData("Closed", "✓")]
    [InlineData("closed", "✓")]
    [InlineData("Removed", "✗")]
    [InlineData("removed", "✗")]
    [InlineData("Unknown", "○")]
    [InlineData("", "○")]
    public void StateIcon(string state, string want)
        => Assert.Equal(want, new WorkItem { Fields = new WorkItemFields { State = state } }.StateIcon());

    [Fact]
    public void AssignedToName_Nil_ReturnsDash()
        => Assert.Equal("-", new WorkItem().AssignedToName());

    [Fact]
    public void AssignedToName_WithIdentity()
        => Assert.Equal("John Doe", new WorkItem { Fields = new WorkItemFields { AssignedTo = new Identity { DisplayName = "John Doe" } } }.AssignedToName());

    [Fact]
    public void ReproSteps_Deserialization()
    {
        const string json = """{ "System.Title": "A bug", "System.WorkItemType": "Bug", "Microsoft.VSTS.TCM.ReproSteps": "<div>Steps</div>" }""";
        var fields = JsonSerializer.Deserialize<WorkItemFields>(json, Client.JsonOptions)!;
        Assert.Equal("<div>Steps</div>", fields.ReproSteps);
    }

    [Theory]
    [InlineData("Bug", "", "Steps to reproduce the bug", "Steps to reproduce the bug")]
    [InlineData("Bug", "Some description", "Steps to reproduce", "Steps to reproduce")]
    [InlineData("Bug", "Bug description", "", "Bug description")]
    [InlineData("Task", "Task description", "", "Task description")]
    [InlineData("User Story", "Story description", "", "Story description")]
    [InlineData("Bug", "", "", "")]
    public void EffectiveDescription(string type, string desc, string repro, string want)
    {
        var wi = new WorkItem { Fields = new WorkItemFields { WorkItemType = type, Description = desc, ReproSteps = repro } };
        Assert.Equal(want, wi.EffectiveDescription());
    }

    [Fact]
    public void Tags_Deserialization()
    {
        const string json = """{ "System.Title": "Tagged item", "System.Tags": "Sprint 5; Frontend; Critical" }""";
        var fields = JsonSerializer.Deserialize<WorkItemFields>(json, Client.JsonOptions)!;
        Assert.Equal("Sprint 5; Frontend; Critical", fields.Tags);
    }

    [Theory]
    [InlineData("Sprint 5; Frontend; Critical", new[] { "Sprint 5", "Frontend", "Critical" })]
    [InlineData("Backend", new[] { "Backend" })]
    [InlineData("", new string[0])]
    [InlineData("  Sprint 5 ;  Frontend ;  Critical  ", new[] { "Sprint 5", "Frontend", "Critical" })]
    public void TagList(string tags, string[] want)
        => Assert.Equal(want, new WorkItem { Fields = new WorkItemFields { Tags = tags } }.TagList());

    [Fact]
    public void TimeInCurrentState_ZeroDate_ReturnsZero()
        => Assert.Equal(TimeSpan.Zero, new WorkItem().TimeInCurrentState(DateTime.UtcNow));

    [Fact]
    public void TimeInCurrentState_ThreeDaysAgo()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var wi = new WorkItem { Fields = new WorkItemFields { StateChangeDate = now.AddDays(-3) } };
        Assert.Equal(TimeSpan.FromDays(3), wi.TimeInCurrentState(now));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(1.5, 1.5)]
    [InlineData(21, 21)]
    public void EffectivePoints(double points, double want)
        => Assert.Equal(want, new WorkItem { Fields = new WorkItemFields { StoryPoints = points } }.EffectivePoints());

    public static IEnumerable<object[]> CompletedSinceCases()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        return new[]
        {
            new object[] { "Active", start.AddDays(5), false },
            new object[] { "Closed", default(DateTime), false },
            new object[] { "Closed", start.AddDays(-1), false },
            new object[] { "Closed", start, false },
            new object[] { "Closed", start.AddDays(3), true },
            new object[] { "closed", start.AddDays(3), true },
            new object[] { "New", start.AddDays(3), false },
            new object[] { "Ready for Test", start.AddDays(3), false },
        };
    }

    [Theory]
    [MemberData(nameof(CompletedSinceCases))]
    public void IsCompletedSince(string state, DateTime closedDate, bool want)
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var wi = new WorkItem { Fields = new WorkItemFields { State = state, ClosedDate = closedDate } };
        Assert.Equal(want, wi.IsCompletedSince(start));
    }

    [Fact]
    public async Task GetWorkItems_EmptyIds_NoCall()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{}");
        var client = TestHelpers.NewClient(handler);
        var items = await client.GetWorkItemsAsync(Array.Empty<int>());
        Assert.Empty(items);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetWorkItems_RequestsRequiredFields()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"count\":1,\"value\":[{\"id\":1}]}");
        var client = TestHelpers.NewClient(handler);

        await client.GetWorkItemsAsync(new[] { 1 });

        var url = handler.Requests[0].Uri.ToString();
        foreach (var f in new[]
        {
            "Microsoft.VSTS.TCM.ReproSteps", "System.Tags",
            "Microsoft.VSTS.Scheduling.StoryPoints", "Microsoft.VSTS.Common.StateChangeDate",
            "Microsoft.VSTS.Common.ActivatedDate", "Microsoft.VSTS.Common.ClosedDate", "System.CreatedDate",
        })
            Assert.Contains(f, url);
    }

    [Fact]
    public async Task QueryWorkItemIds_PostsWiql_ReturnsIds()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK,
            "{\"workItems\":[{\"id\":123,\"url\":\"u\"},{\"id\":456}]}");
        var client = TestHelpers.NewClient(handler);

        var ids = await client.QueryWorkItemIdsAsync("SELECT [System.Id] FROM WorkItems", 50);

        Assert.Equal(new[] { 123, 456 }, ids);
        Assert.Equal("POST", handler.Requests[0].Method);
        Assert.EndsWith("/wit/wiql", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task ListWorkItems_QueryScopedToProject_AndTwoCalls()
    {
        var handler = new FakeHttpMessageHandler((req, _) =>
            req.Method == HttpMethod.Post
                ? (HttpStatusCode.OK, "{\"workItems\":[{\"id\":100}]}", "application/json")
                : (HttpStatusCode.OK, "{\"count\":1,\"value\":[{\"id\":100,\"fields\":{\"System.Title\":\"Item 1\"}}]}", "application/json"));
        var client = TestHelpers.NewClient(handler);

        var items = await client.ListWorkItemsAsync(50);

        Assert.Single(items);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("@project", handler.Requests[0].Body);
    }

    [Fact]
    public async Task ListMyWorkItems_QueryContainsAtMeAndProject()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"workItems\":[]}");
        var client = TestHelpers.NewClient(handler);

        await client.ListMyWorkItemsAsync(50);

        Assert.Contains("@Me", handler.Requests[0].Body);
        Assert.Contains("@project", handler.Requests[0].Body);
    }

    [Fact]
    public async Task ListWorkItems_NoResults_NoGetCall()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"workItems\":[]}");
        var client = TestHelpers.NewClient(handler);
        var items = await client.ListWorkItemsAsync(50);
        Assert.Empty(items);
        Assert.Single(handler.Requests); // only the WIQL POST
    }

    [Fact]
    public async Task GetWorkItemTypeStates_ExcludesRemovedCategory()
    {
        const string body = """
        { "count": 5, "value": [
            { "name": "New", "category": "Proposed" },
            { "name": "Active", "category": "InProgress" },
            { "name": "Resolved", "category": "Resolved" },
            { "name": "Closed", "category": "Completed" },
            { "name": "Removed", "category": "Removed" }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var states = await client.GetWorkItemTypeStatesAsync("Bug");

        Assert.Equal(4, states.Count);
        Assert.DoesNotContain(states, s => s.Category == "Removed");
        Assert.EndsWith("/wit/workitemtypes/Bug/states", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task UpdateWorkItemState_UsesJsonPatchContentType()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"id\":123}");
        var client = TestHelpers.NewClient(handler);

        await client.UpdateWorkItemStateAsync(123, "Resolved");

        var req = Assert.Single(handler.Requests);
        Assert.Equal("PATCH", req.Method);
        Assert.Contains("application/json-patch+json", req.ContentType);
        Assert.Contains("/fields/System.State", req.Body);
        Assert.Contains("Resolved", req.Body);
    }

    [Fact]
    public async Task UpdateWorkItemState_ApiError()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.BadRequest, "");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<AzdoHttpException>(() => client.UpdateWorkItemStateAsync(123, "InvalidState"));
    }
}
