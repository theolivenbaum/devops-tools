using System.Net;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class EndpointTests
{
    internal static Dictionary<string, string> Query(Uri uri)
    {
        var result = new Dictionary<string, string>();
        var q = uri.Query.TrimStart('?');
        if (q.Length == 0) return result;
        foreach (var pair in q.Split('&'))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0) { result[Uri.UnescapeDataString(pair)] = ""; continue; }
            result[Uri.UnescapeDataString(pair[..idx])] = Uri.UnescapeDataString(pair[(idx + 1)..]);
        }
        return result;
    }

    [Fact]
    public async Task ListPipelineRuns_Success_ParsesAndVerifiesQuery()
    {
        const string body = """
        { "count": 2, "value": [
            { "id": 12345, "buildNumber": "20240206.1", "status": "completed", "result": "succeeded",
              "sourceBranch": "refs/heads/main", "queueTime": "2024-02-06T10:00:00Z",
              "startTime": "2024-02-06T10:01:00Z", "finishTime": "2024-02-06T10:15:00Z",
              "definition": { "id": 42, "name": "CI-Pipeline" } },
            { "id": 12346, "status": "inProgress", "result": null,
              "queueTime": "2024-02-06T11:00:00Z", "startTime": "2024-02-06T11:01:00Z" }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var runs = await client.ListPipelineRunsAsync(25);

        Assert.Equal(2, runs.Count);
        Assert.Equal(12345, runs[0].Id);
        Assert.Equal("CI-Pipeline", runs[0].Definition.Name);
        Assert.Null(runs[1].FinishTime);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("GET", req.Method);
        Assert.Equal("/test-org/test-project/_apis/build/builds", req.Uri.AbsolutePath);
        var q = Query(req.Uri);
        Assert.Equal("7.1", q["api-version"]);
        Assert.Equal("25", q["$top"]);
        Assert.Equal("queueTimeDescending", q["queryOrder"]);
    }

    [Fact]
    public async Task ListPipelineRuns_Empty()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"count\":0,\"value\":[]}");
        var client = TestHelpers.NewClient(handler);
        Assert.Empty(await client.ListPipelineRunsAsync(25));
    }

    [Fact]
    public async Task ListPipelineRuns_HttpError()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Unauthorized, "{}");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<AzdoHttpException>(() => client.ListPipelineRunsAsync(25));
    }

    [Fact]
    public async Task ListPipelineRuns_InvalidJson()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{invalid json");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListPipelineRunsAsync(25));
    }

    [Fact]
    public async Task ListBuildLogs_Success()
    {
        const string body = """
        { "count": 2, "value": [
            { "id": 5, "type": "Container", "lineCount": 100 },
            { "id": 6, "type": "Container", "lineCount": 250 }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var logs = await client.ListBuildLogsAsync(12345);

        Assert.Equal(2, logs.Count);
        Assert.Equal(5, logs[0].Id);
        Assert.Equal(250, logs[1].LineCount);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/build/builds/12345/logs", req.Uri.AbsolutePath);
        Assert.Equal("7.1", Query(req.Uri)["api-version"]);
    }

    [Fact]
    public async Task GetBuildLogContent_ReturnsRawText()
    {
        const string content = "line1\nline2\nline3";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, content, "text/plain");
        var client = TestHelpers.NewClient(handler);

        var got = await client.GetBuildLogContentAsync(12345, 5);

        Assert.Equal(content, got);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/build/builds/12345/logs/5", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetBuildLogContent_HttpError()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.NotFound, "{}");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<AzdoHttpException>(() => client.GetBuildLogContentAsync(12345, 999));
    }

    [Fact]
    public async Task GetBuildTimeline_Success()
    {
        const string body = """
        { "id": "timeline-12345", "changeId": 10, "records": [
            { "id": "stage-build", "parentId": null, "type": "Stage", "name": "Build",
              "state": "completed", "result": "succeeded", "order": 1,
              "startTime": "2024-02-06T10:00:00Z", "finishTime": "2024-02-06T10:10:00Z" },
            { "id": "job-compile", "parentId": "stage-build", "type": "Job", "name": "Compile",
              "state": "completed", "result": "succeeded", "order": 1,
              "log": { "id": 5, "type": "Container", "url": "https://x/logs/5" } }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var timeline = await client.GetBuildTimelineAsync(12345);

        Assert.Equal("timeline-12345", timeline.Id);
        Assert.Equal(2, timeline.Records.Count);
        Assert.Equal("Stage", timeline.Records[0].Type);
        Assert.Null(timeline.Records[0].ParentId);
        Assert.NotNull(timeline.Records[1].Log);
        Assert.Equal(5, timeline.Records[1].Log!.Id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/build/builds/12345/timeline", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetBuildTimeline_InvalidJson()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{invalid");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetBuildTimelineAsync(12345));
    }
}
