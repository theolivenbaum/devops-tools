using System.Net;
using Azdo.Core.AzureDevOps;
using Xunit;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tests.AzureDevOps;

public class GitTests
{
    [Fact]
    public async Task ListPullRequests_Success_ParsesAndQuery()
    {
        const string body = """
        { "count": 2, "value": [
            { "pullRequestId": 101, "title": "Add new feature", "description": "desc", "status": "active",
              "creationDate": "2024-02-06T10:00:00Z", "sourceRefName": "refs/heads/feature/new-feature",
              "targetRefName": "refs/heads/main", "isDraft": false,
              "createdBy": { "id": "user-123", "displayName": "John Doe", "uniqueName": "john@x.com" },
              "repository": { "id": "repo-456", "name": "my-repo" },
              "reviewers": [ { "id": "r1", "displayName": "Jane", "vote": 0 }, { "id": "r2", "displayName": "Bob", "vote": 10 } ] },
            { "pullRequestId": 102, "title": "Fix bug", "status": "active", "isDraft": true, "reviewers": [] }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var prs = await client.ListPullRequestsAsync(25);

        Assert.Equal(2, prs.Count);
        Assert.Equal(101, prs[0].Id);
        Assert.Equal("Add new feature", prs[0].Title);
        Assert.False(prs[0].IsDraft);
        Assert.Equal("John Doe", prs[0].CreatedBy.DisplayName);
        Assert.Equal("my-repo", prs[0].Repository.Name);
        Assert.Equal(2, prs[0].Reviewers.Count);
        Assert.True(prs[1].IsDraft);
        Assert.Empty(prs[1].Reviewers);

        var req = Assert.Single(handler.Requests);
        var q = EndpointTests.Query(req.Uri);
        Assert.Equal("7.1", q["api-version"]);
        Assert.Equal("25", q["$top"]);
        Assert.Equal("active", q["searchCriteria.status"]);
    }

    [Theory]
    [InlineData("refs/heads/feature/my-feature", "feature/my-feature")]
    [InlineData("refs/heads/main", "main")]
    [InlineData("", "")]
    [InlineData("some-branch", "some-branch")]
    public void SourceBranchShortName(string input, string want)
        => Assert.Equal(want, new PullRequest { SourceRefName = input }.SourceBranchShortName());

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/develop", "develop")]
    [InlineData("", "")]
    public void TargetBranchShortName(string input, string want)
        => Assert.Equal(want, new PullRequest { TargetRefName = input }.TargetBranchShortName());

    [Theory]
    [InlineData(10, "Approved")]
    [InlineData(5, "Approved with suggestions")]
    [InlineData(0, "No vote")]
    [InlineData(-5, "Waiting for author")]
    [InlineData(-10, "Rejected")]
    [InlineData(99, "Unknown")]
    [InlineData(-99, "Unknown")]
    public void Reviewer_VoteDescription(int vote, string want)
        => Assert.Equal(want, new Reviewer { Vote = vote }.VoteDescription());

    [Fact]
    public async Task GetPRThreads_Success()
    {
        const string body = """
        { "count": 2, "value": [
            { "id": 1, "status": "active",
              "threadContext": { "filePath": "/src/main.go", "rightFileStart": {"line":10,"offset":1}, "rightFileEnd": {"line":10,"offset":20} },
              "comments": [
                { "id": 1, "content": "This looks good!", "commentType": "text", "author": { "displayName": "John Doe" } },
                { "id": 2, "parentCommentId": 1, "content": "Thanks!", "commentType": "text", "author": { "displayName": "Jane" } } ] },
            { "id": 2, "status": "fixed", "comments": [ { "id": 3, "content": "Add error handling", "author": { "displayName": "Bob" } } ] }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var threads = await client.GetPRThreadsAsync("repo-123", 101);

        Assert.Equal(2, threads.Count);
        Assert.Equal("active", threads[0].Status);
        Assert.NotNull(threads[0].ThreadContext);
        Assert.Equal("/src/main.go", threads[0].ThreadContext!.FilePath);
        Assert.Equal(2, threads[0].Comments.Count);
        Assert.Equal("This looks good!", threads[0].Comments[0].Content);
        Assert.Equal("fixed", threads[1].Status);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/threads", req.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/src/main.go", true)]
    [InlineData("", false)]
    public void Thread_IsCodeComment(string? filePath, bool want)
    {
        var thread = new Thread { ThreadContext = filePath is null ? null : new ThreadContext { FilePath = filePath } };
        Assert.Equal(want, thread.IsCodeComment());
    }

    [Theory]
    [InlineData("active", "Active")]
    [InlineData("fixed", "Resolved")]
    [InlineData("wontFix", "Won't fix")]
    [InlineData("closed", "Closed")]
    [InlineData("pending", "Pending")]
    [InlineData("unknown", "Unknown")]
    [InlineData("", "Unknown")]
    public void Thread_StatusDescription(string status, string want)
        => Assert.Equal(want, new Thread { Status = status }.StatusDescription());

    [Fact]
    public async Task VotePullRequest_UsesUserIdInPath_AndPut()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"vote\":10}");
        var client = TestHelpers.NewClient(handler);
        client.SetUserID("user-guid-123");

        await client.VotePullRequestAsync("repo-123", 101, Vote.Approve);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("PUT", req.Method);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/reviewers/user-guid-123", req.Uri.AbsolutePath);
        Assert.Contains("\"vote\": 10", req.Body);
    }

    [Fact]
    public async Task VotePullRequest_HttpError()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Forbidden, "{}");
        var client = TestHelpers.NewClient(handler);
        client.SetUserID("user-guid-123");
        await Assert.ThrowsAsync<AzdoHttpException>(() => client.VotePullRequestAsync("repo-123", 101, Vote.Approve));
    }

    [Fact]
    public async Task AddPRComment_Success_PostsThread()
    {
        const string body = """{ "id": 5, "status": "active", "comments": [ { "id": 1, "content": "LGTM!" } ] }""";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Created, body);
        var client = TestHelpers.NewClient(handler);

        var thread = await client.AddPRCommentAsync("repo-123", 101, "LGTM!");

        Assert.Equal(5, thread.Id);
        Assert.Single(thread.Comments);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/threads", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetPRIterations_Success()
    {
        const string body = """{ "count": 2, "value": [ {"id":1,"description":"Initial push"}, {"id":2,"description":"Address review"} ] }""";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var iterations = await client.GetPRIterationsAsync("repo-123", 101);

        Assert.Equal(2, iterations.Count);
        Assert.Equal("Initial push", iterations[0].Description);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/iterations", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetPRIterationChanges_Success_AllChangeTypes()
    {
        const string body = """
        { "changeEntries": [
            { "changeId": 1, "item": {"objectId":"abc","path":"/src/main.go"}, "changeType": "edit" },
            { "changeId": 2, "item": {"objectId":"def","path":"/src/new.go"}, "changeType": "add" },
            { "changeId": 3, "item": {"objectId":"ghi","path":"/src/old.go"}, "changeType": "delete" },
            { "changeId": 4, "item": {"objectId":"jkl","path":"/src/renamed.go"}, "changeType": "rename", "originalPath": "/src/original.go" }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var changes = await client.GetPRIterationChangesAsync("repo-123", 101, 2);

        Assert.Equal(4, changes.Count);
        Assert.Equal("edit", changes[0].ChangeType);
        Assert.Equal("/src/main.go", changes[0].Item.Path);
        Assert.Equal("rename", changes[3].ChangeType);
        Assert.Equal("/src/original.go", changes[3].OriginalPath);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/iterations/2/changes", req.Uri.AbsolutePath);
        Assert.Equal("0", EndpointTests.Query(req.Uri)["$compareTo"]);
    }

    [Fact]
    public async Task GetFileContent_Success_SendsAcceptTextPlain()
    {
        const string content = "package main\n\nfunc main() {}\n";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, content, "text/plain");
        var client = TestHelpers.NewClient(handler);

        var got = await client.GetFileContentAsync("repo-123", "/src/main.go", "main");

        Assert.Equal(content, got);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/items", req.Uri.AbsolutePath);
        var q = EndpointTests.Query(req.Uri);
        Assert.Equal("/src/main.go", q["path"]);
        Assert.Equal("branch", q["versionType"]);
        Assert.Equal("main", q["version"]);
        Assert.Equal("text/plain", req.Accept);
    }

    [Fact]
    public async Task ReplyToThread_Success()
    {
        const string body = """{ "id": 3, "parentCommentId": 1, "content": "Good point, will fix!", "commentType": "text" }""";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Created, body);
        var client = TestHelpers.NewClient(handler);

        var comment = await client.ReplyToThreadAsync("repo-123", 101, 5, "Good point, will fix!");

        Assert.Equal(3, comment.Id);
        Assert.Equal("Good point, will fix!", comment.Content);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/threads/5/comments", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task UpdateThreadStatus_Success_Patches()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"id\":5,\"status\":\"fixed\"}");
        var client = TestHelpers.NewClient(handler);

        await client.UpdateThreadStatusAsync("repo-123", 101, 5, "fixed");

        var req = Assert.Single(handler.Requests);
        Assert.Equal("PATCH", req.Method);
        Assert.Equal("/test-org/test-project/_apis/git/repositories/repo-123/pullRequests/101/threads/5", req.Uri.AbsolutePath);
        Assert.Contains("\"fixed\"", req.Body);
    }

    [Fact]
    public async Task AddPRCodeComment_Success()
    {
        const string body = """
        { "id": 10, "status": "active",
          "threadContext": { "filePath": "/src/main.go", "rightFileStart": {"line":42,"offset":1}, "rightFileEnd": {"line":42,"offset":1} },
          "comments": [ { "id": 1, "content": "Should we add error handling here?" } ] }
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Created, body);
        var client = TestHelpers.NewClient(handler);

        var thread = await client.AddPRCodeCommentAsync("repo-123", 101, "/src/main.go", 42, "Should we add error handling here?");

        Assert.Equal(10, thread.Id);
        Assert.NotNull(thread.ThreadContext);
        Assert.Equal("/src/main.go", thread.ThreadContext!.FilePath);
        Assert.Equal(42, thread.ThreadContext.RightFileStart!.Line);
        Assert.Single(thread.Comments);
        // Body must reference the file and line.
        Assert.Contains("/src/main.go", handler.Requests[0].Body);
        Assert.Contains("42", handler.Requests[0].Body);
    }
}
