using System.Net;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class MultiClientTests
{
    private static Client PipelineClient(string project, params PipelineRun[] runs)
    {
        var body = TestHelpers.Json(new PipelineRunsResponse { Value = runs.ToList() });
        return new Client("testorg", project, "testpat", FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body));
    }

    private static Client PrClient(string project, params PullRequest[] prs)
    {
        var body = TestHelpers.Json(new PullRequestsResponse { Value = prs.ToList() });
        return new Client("testorg", project, "testpat", FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body));
    }

    private static Client WorkItemClient(string project, params WorkItem[] items)
    {
        // Responds to WIQL POST with ids, and GET with full items.
        var ids = string.Join(",", items.Select(i => $"{{\"id\":{i.Id}}}"));
        var value = TestHelpers.Json(new WorkItemsResponse { Value = items.ToList() });
        var handler = new FakeHttpMessageHandler((req, _) =>
            req.Method == HttpMethod.Post
                ? (HttpStatusCode.OK, $"{{\"workItems\":[{ids}]}}", "application/json")
                : (HttpStatusCode.OK, value, "application/json"));
        return new Client("testorg", project, "testpat", handler);
    }

    private static Client ErrorClient(string project)
        => new("testorg", project, "testpat", FakeHttpMessageHandler.Constant(HttpStatusCode.InternalServerError, "{\"message\":\"internal error\"}"));

    private static MultiClient Build(Dictionary<string, Client> clients, Dictionary<string, string>? names = null)
        => new("testorg", "testpat", clients, names);

    [Fact]
    public void NewMultiClient_CreatesClientsPerProject()
    {
        var mc = new MultiClient("myorg", new[] { "alpha", "beta" }, "pat123");
        Assert.Equal("myorg", mc.GetOrg());
        var projects = mc.Projects();
        projects.Sort();
        Assert.Equal(new[] { "alpha", "beta" }, projects);
    }

    [Fact]
    public void NewMultiClient_EmptyProjectName_Throws()
        => Assert.Throws<ArgumentException>(() => new MultiClient("myorg", new[] { "alpha", "" }, "pat123"));

    [Fact]
    public void NewMultiClient_EmptyProjectsList_Throws()
        => Assert.Throws<ArgumentException>(() => new MultiClient("myorg", Array.Empty<string>(), "pat123"));

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void IsMultiProject(int count, bool want)
    {
        var projects = Enumerable.Range(0, count).Select(i => $"p{i}").ToArray();
        var mc = new MultiClient("org", projects, "pat");
        Assert.Equal(want, mc.IsMultiProject());
    }

    [Fact]
    public void ClientFor_ReturnsCorrectClient_OrNull()
    {
        var mc = new MultiClient("org", new[] { "alpha", "beta" }, "pat");
        var c = mc.ClientFor("alpha");
        Assert.NotNull(c);
        Assert.Equal("alpha", c!.GetProject());
        Assert.Null(mc.ClientFor("nonexistent"));
    }

    [Fact]
    public async Task ListPipelineRuns_MergedAndSorted()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = PipelineClient("alpha",
                new PipelineRun { Id = 1, QueueTime = now.AddMinutes(-1) },
                new PipelineRun { Id = 3, QueueTime = now.AddMinutes(-3) }),
            ["beta"] = PipelineClient("beta",
                new PipelineRun { Id = 2, QueueTime = now.AddMinutes(-2) }),
        });

        var runs = await mc.ListPipelineRunsAsync(10);

        Assert.Equal(3, runs.Count);
        Assert.Equal(new[] { 1, 2, 3 }, runs.Select(r => r.Id));
    }

    [Fact]
    public async Task ListPipelineRuns_PartialFailure_ReturnsDataAndPartialError()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = PipelineClient("alpha", new PipelineRun { Id = 1, QueueTime = now }),
            ["beta"] = ErrorClient("beta"),
        });

        var ex = await Assert.ThrowsAsync<PartialException>(() => mc.ListPipelineRunsAsync(10));
        Assert.Equal(2, ex.Total);
        Assert.Equal(1, ex.Failed);
        var data = Assert.IsType<List<PipelineRun>>(ex.PartialData);
        Assert.Single(data);
        Assert.Equal(1, data[0].Id);
    }

    [Fact]
    public void PartialException_Message()
    {
        var pe = new PartialException(1, 3, new[] { new Exception("project beta failed") });
        Assert.Contains("1", pe.Message);
        Assert.Contains("3", pe.Message);
    }

    [Fact]
    public async Task ListPipelineRuns_AllFail_Throws()
    {
        var mc = Build(new()
        {
            ["alpha"] = ErrorClient("alpha"),
            ["beta"] = ErrorClient("beta"),
        });
        await Assert.ThrowsAsync<AggregateException>(() => mc.ListPipelineRunsAsync(10));
    }

    [Fact]
    public async Task ListPullRequests_MergedSortedTagged()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = PrClient("alpha", new PullRequest { Id = 10, Title = "Alpha PR", CreationDate = now.AddHours(-1) }),
            ["beta"] = PrClient("beta", new PullRequest { Id = 20, Title = "Beta PR", CreationDate = now }),
        });

        var prs = await mc.ListPullRequestsAsync(25);

        Assert.Equal(2, prs.Count);
        Assert.Equal(20, prs[0].Id);
        Assert.Equal("beta", prs[0].ProjectName);
        Assert.Equal("alpha", prs[1].ProjectName);
    }

    [Fact]
    public async Task ListWorkItems_MergedSortedTagged()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = WorkItemClient("alpha", new WorkItem { Id = 100, Fields = new WorkItemFields { Title = "Alpha WI", ChangedDate = now.AddHours(-2) } }),
            ["beta"] = WorkItemClient("beta", new WorkItem { Id = 200, Fields = new WorkItemFields { Title = "Beta WI", ChangedDate = now } }),
        });

        var items = await mc.ListWorkItemsAsync(50);

        Assert.Equal(2, items.Count);
        Assert.Equal(200, items[0].Id);
        Assert.Equal("beta", items[0].ProjectName);
        Assert.Equal("alpha", items[1].ProjectName);
    }

    [Fact]
    public async Task SingleProject_BehavesLikeSingleClient()
    {
        var mc = Build(new() { ["only"] = PipelineClient("only", new PipelineRun { Id = 1, QueueTime = DateTime.UtcNow }) });
        var result = await mc.ListPipelineRunsAsync(10);
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void DisplayNameFor_WithAndWithoutNames()
    {
        var mc = new MultiClient("myorg", new[] { "ugly-api" }, "pat123",
            new Dictionary<string, string> { ["ugly-api"] = "Friendly" });
        Assert.Equal("Friendly", mc.DisplayNameFor("ugly-api"));
        Assert.Equal("other", mc.DisplayNameFor("other"));

        var mc2 = new MultiClient("myorg", new[] { "proj" }, "pat123");
        Assert.Equal("proj", mc2.DisplayNameFor("proj"));
    }

    [Fact]
    public async Task ListPipelineRuns_TagsDisplayName()
    {
        var mc = Build(
            new() { ["ugly-api"] = PipelineClient("ugly-api", new PipelineRun { Id = 1, QueueTime = DateTime.UtcNow }) },
            new() { ["ugly-api"] = "Friendly" });

        var result = await mc.ListPipelineRunsAsync(10);
        Assert.Single(result);
        Assert.Equal("ugly-api", result[0].ProjectName);
        Assert.Equal("Friendly", result[0].ProjectDisplayName);
    }

    [Fact]
    public async Task ListMyWorkItems_MergedSortedTagged()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = WorkItemClient("alpha", new WorkItem { Id = 100, Fields = new WorkItemFields { ChangedDate = now.AddHours(-2) } }),
            ["beta"] = WorkItemClient("beta", new WorkItem { Id = 200, Fields = new WorkItemFields { ChangedDate = now } }),
        });

        var items = await mc.ListMyWorkItemsAsync(50);

        Assert.Equal(2, items.Count);
        Assert.Equal(200, items[0].Id);
        Assert.Equal("beta", items[0].ProjectName);
    }

    [Fact]
    public async Task MetricsWorkItems_MergedAndTagged()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = WorkItemClient("alpha", new WorkItem { Id = 100, Fields = new WorkItemFields { State = "Active", ChangedDate = now.AddHours(-2) } }),
            ["beta"] = WorkItemClient("beta", new WorkItem { Id = 200, Fields = new WorkItemFields { State = "Ready for Test", ChangedDate = now } }),
        });

        var items = await mc.MetricsWorkItemsAsync(now.AddDays(-14),
            new MetricsStateNames { Active = "Active", ReadyForTest = "Ready for Test", Closed = "Closed" });

        Assert.Equal(2, items.Count);
        Assert.Equal(200, items[0].Id);
        Assert.Equal("beta", items[0].ProjectName);
        Assert.Equal("alpha", items[1].ProjectName);
    }

    [Fact]
    public async Task MetricsWorkItems_PartialFailure()
    {
        var now = DateTime.UtcNow;
        var mc = Build(new()
        {
            ["alpha"] = WorkItemClient("alpha", new WorkItem { Id = 100, Fields = new WorkItemFields { State = "Active", ChangedDate = now } }),
            ["beta"] = ErrorClient("beta"),
        });

        var ex = await Assert.ThrowsAsync<PartialException>(() => mc.MetricsWorkItemsAsync(now.AddDays(-14),
            new MetricsStateNames { Active = "Active", ReadyForTest = "Ready for Test", Closed = "Closed" }));
        Assert.Equal(1, ex.Failed);
        Assert.Equal(2, ex.Total);
        var data = Assert.IsType<List<WorkItem>>(ex.PartialData);
        Assert.Single(data);
    }
}
