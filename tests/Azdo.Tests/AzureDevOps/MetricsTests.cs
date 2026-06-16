using System.Net;
using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class MetricsTests
{
    private static MetricsStateNames DefaultStates() =>
        new() { Active = "Active", ReadyForTest = "Ready for Test", Closed = "Closed" };

    [Fact]
    public async Task MetricsWorkItems_WiqlShape()
    {
        string capturedBody = "";
        var handler = new FakeHttpMessageHandler((req, body) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                capturedBody = body;
                return (HttpStatusCode.OK, "{\"workItems\":[]}", "application/json");
            }
            return (HttpStatusCode.OK, "{\"count\":0,\"value\":[]}", "application/json");
        });
        var client = TestHelpers.NewClient(handler);

        var since = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await client.MetricsWorkItemsAsync(since, DefaultStates());

        // The captured body is the JSON-wrapped WIQL; assert on the embedded query.
        foreach (var needle in new[]
        {
            "@project", "Active", "Ready for Test", "Closed",
            "2026-05-01", "Microsoft.VSTS.Common.ClosedDate",
        })
            Assert.Contains(needle, capturedBody);

        Assert.DoesNotContain("'New'", capturedBody);
        Assert.DoesNotContain("@Me", capturedBody);
    }

    [Fact]
    public async Task MetricsWorkItems_BatchesOver200Ids()
    {
        const int total = 250;
        int workItemsCalls = 0;
        var capturedIds = new List<string>();
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                var refs = string.Join(",", Enumerable.Range(1, total).Select(i => $"{{\"id\":{i}}}"));
                return (HttpStatusCode.OK, $"{{\"workItems\":[{refs}]}}", "application/json");
            }
            workItemsCalls++;
            var idsParam = EndpointTests.Query(req.RequestUri!)["ids"];
            capturedIds.Add(idsParam);
            var ids = idsParam.Split(',');
            var value = string.Join(",", ids.Select(id => $"{{\"id\":{id}}}"));
            return (HttpStatusCode.OK, $"{{\"count\":{ids.Length},\"value\":[{value}]}}", "application/json");
        });
        var client = TestHelpers.NewClient(handler);

        var items = await client.MetricsWorkItemsAsync(DateTime.UtcNow.AddDays(-14), DefaultStates());

        Assert.Equal(2, workItemsCalls);
        Assert.Equal(total, items.Count);
        Assert.Equal(200, capturedIds[0].Split(',').Length);
        Assert.Equal(50, capturedIds[1].Split(',').Length);
    }

    [Fact]
    public async Task MetricsWorkItems_NoResults()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"workItems\":[]}");
        var client = TestHelpers.NewClient(handler);
        var items = await client.MetricsWorkItemsAsync(DateTime.UtcNow, DefaultStates());
        Assert.Empty(items);
    }

    [Fact]
    public void BuildMetricsWiql_DefaultStates()
    {
        var since = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var got = Client.BuildMetricsWiql(since, DefaultStates());
        Assert.Contains("'Active','Ready for Test'", got);
        Assert.Contains("[System.State] = 'Closed'", got);
        Assert.Contains("'2026-05-01'", got);
    }

    [Fact]
    public void BuildMetricsWiql_CustomStates()
    {
        var since = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var got = Client.BuildMetricsWiql(since, new MetricsStateNames { Active = "In Progress", ReadyForTest = "RFT", Closed = "Done" });
        Assert.Contains("'In Progress','RFT'", got);
        Assert.Contains("[System.State] = 'Done'", got);
    }

    [Fact]
    public void BuildMetricsWiql_RejectsEmptyName()
        => Assert.Throws<ArgumentException>(() =>
            Client.BuildMetricsWiql(DateTime.UtcNow, new MetricsStateNames { Active = "", ReadyForTest = "RFT", Closed = "Done" }));

    [Fact]
    public void BuildMetricsWiql_RejectsSingleQuote()
        => Assert.Throws<ArgumentException>(() =>
            Client.BuildMetricsWiql(DateTime.UtcNow, new MetricsStateNames { Active = "Act'ive", ReadyForTest = "RFT", Closed = "Done" }));

    [Fact]
    public void ParseStateTransitions_DropsAndSorts()
    {
        WorkItemUpdate U(params (string Key, string Val)[] fields)
        {
            var u = new WorkItemUpdate();
            foreach (var (k, v) in fields)
                u.Fields[k] = new WorkItemFieldChange { NewValue = JsonSerializer.SerializeToElement(v) };
            return u;
        }

        var updates = new List<WorkItemUpdate>
        {
            U(("System.State", "Closed"), ("System.ChangedDate", "2026-05-15T10:00:00Z")),
            U(("System.AssignedTo", "Alice"), ("System.ChangedDate", "2026-05-12T10:00:00Z")), // no state -> dropped
            U(("System.State", "Active"), ("System.ChangedDate", "2026-05-10T10:00:00Z")),
            U(("System.State", "Ready for Test"), ("System.ChangedDate", "2026-05-13T10:00:00Z")),
            U(("System.State", "Resolved")), // no date -> dropped
        };

        var got = Client.ParseStateTransitions(updates);

        Assert.Equal(3, got.Count);
        Assert.Equal(new[] { "Active", "Ready for Test", "Closed" }, got.Select(t => t.State));
    }

    [Fact]
    public async Task WorkItemUpdates_ParsesResponse()
    {
        const string body = """
        { "value": [
            { "fields": { "System.State": { "newValue": "Active" }, "System.ChangedDate": { "newValue": "2026-05-10T10:00:00Z" } } },
            { "fields": { "System.State": { "newValue": "Ready for Test" }, "System.ChangedDate": { "newValue": "2026-05-13T10:00:00Z" } } }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var transitions = await client.WorkItemUpdatesAsync(42);

        Assert.Equal(2, transitions.Count);
        Assert.Equal("Active", transitions[0].State);
        Assert.Equal("Ready for Test", transitions[1].State);
        Assert.EndsWith("/wit/workItems/42/updates", handler.Requests[0].Uri.AbsolutePath);
    }
}
