using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class PipelineTypeTests
{
    [Fact]
    public void PipelineRun_Unmarshal_CompleteRun()
    {
        const string json = """
        {
            "id": 12345,
            "buildNumber": "20240206.1",
            "status": "completed",
            "result": "succeeded",
            "sourceBranch": "refs/heads/main",
            "sourceVersion": "abc123def456",
            "queueTime": "2024-02-06T10:00:00Z",
            "startTime": "2024-02-06T10:01:00Z",
            "finishTime": "2024-02-06T10:15:00Z",
            "definition": { "id": 42, "name": "CI-Pipeline" },
            "project": { "id": "proj-123", "name": "MyProject" },
            "_links": { "web": { "href": "https://dev.azure.com/org/proj/_build/results?buildId=12345" } }
        }
        """;

        var got = JsonSerializer.Deserialize<PipelineRun>(json, Client.JsonOptions)!;

        Assert.Equal(12345, got.Id);
        Assert.Equal("20240206.1", got.BuildNumber);
        Assert.Equal("completed", got.Status);
        Assert.Equal("succeeded", got.Result);
        Assert.Equal("refs/heads/main", got.SourceBranch);
        Assert.Equal("abc123def456", got.SourceVersion);
        Assert.Equal(TestHelpers.Utc("2024-02-06T10:00:00Z"), got.QueueTime);
        Assert.Equal(TestHelpers.Utc("2024-02-06T10:01:00Z"), got.StartTime);
        Assert.Equal(TestHelpers.Utc("2024-02-06T10:15:00Z"), got.FinishTime);
        Assert.Equal(42, got.Definition.Id);
        Assert.Equal("CI-Pipeline", got.Definition.Name);
        Assert.Equal("proj-123", got.Project.Id);
        Assert.Equal("MyProject", got.Project.Name);
        Assert.Equal("https://dev.azure.com/org/proj/_build/results?buildId=12345", got.Links.Web.Href);
    }

    [Fact]
    public void PipelineRun_Unmarshal_InProgress_NullFinishTime()
    {
        const string json = """
        {
            "id": 12346, "status": "inProgress", "result": null,
            "sourceBranch": "refs/heads/feature/test",
            "queueTime": "2024-02-06T11:00:00Z",
            "startTime": "2024-02-06T11:01:00Z"
        }
        """;
        var got = JsonSerializer.Deserialize<PipelineRun>(json, Client.JsonOptions)!;
        Assert.Equal("inProgress", got.Status);
        Assert.True(string.IsNullOrEmpty(got.Result));
        Assert.Null(got.FinishTime);
        Assert.NotNull(got.StartTime);
    }

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/feature/my-feature", "feature/my-feature")]
    [InlineData("main", "main")]
    [InlineData("refs/tags/v1.0.0", "v1.0.0")]
    [InlineData("", "")]
    public void BranchShortName(string input, string want)
    {
        var run = new PipelineRun { SourceBranch = input };
        Assert.Equal(want, run.BranchShortName());
    }

    public static IEnumerable<object[]> DurationCases() => new[]
    {
        new object[] { TestHelpers.Utc("2024-02-06T10:00:00Z"), (object)TestHelpers.Utc("2024-02-06T10:05:00Z"), "5m0s" },
        new object[] { TestHelpers.Utc("2024-02-06T10:00:00Z"), TestHelpers.Utc("2024-02-06T12:30:45Z"), "2h30m45s" },
        new object[] { TestHelpers.Utc("2024-02-06T10:00:00.000Z"), TestHelpers.Utc("2024-02-06T10:23:14.567Z"), "23m14s" },
        new object[] { TestHelpers.Utc("2024-02-06T10:00:00Z"), TestHelpers.Utc("2024-02-06T10:00:45Z"), "45s" },
    };

    [Theory]
    [MemberData(nameof(DurationCases))]
    public void Duration_Completed(DateTime start, DateTime finish, string want)
    {
        var run = new PipelineRun { StartTime = start, FinishTime = finish };
        Assert.Equal(want, run.Duration());
    }

    [Fact]
    public void Duration_InProgress_ReturnsDash()
    {
        var run = new PipelineRun { StartTime = TestHelpers.Utc("2024-02-06T10:00:00Z"), FinishTime = null };
        Assert.Equal("-", run.Duration());
    }

    [Fact]
    public void Duration_NotStarted_ReturnsDash()
    {
        var run = new PipelineRun { StartTime = null, FinishTime = null };
        Assert.Equal("-", run.Duration());
    }

    [Theory]
    [InlineData("2024-02-10T14:30:00Z", "2024-02-10 14:30")]
    [InlineData("2026-10-29T21:32:00Z", "2026-10-29 21:32")]
    public void Timestamp(string queue, string want)
    {
        var run = new PipelineRun { QueueTime = TestHelpers.Utc(queue) };
        Assert.Equal(want, run.Timestamp());
    }

    [Theory]
    [InlineData(0.5, "0s")]   // under a minute -> seconds
    [InlineData(59, "59s")]
    [InlineData(60, "1m0s")]
    [InlineData(3661, "1h1m1s")]
    public void Format_Duration(double seconds, string want)
        => Assert.Equal(want, Format.Duration(TimeSpan.FromSeconds(seconds)));
}
