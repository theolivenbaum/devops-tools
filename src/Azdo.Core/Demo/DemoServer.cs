using System.Net;
using System.Text;
using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Core.Demo;

/// <summary>
/// A mock <see cref="HttpMessageHandler"/> serving synthetic Azure DevOps data
/// for <c>azdo demo</c> — the C# analogue of the Go <c>httptest</c> mock server,
/// but injected directly into the client so no real socket is opened. Responses
/// are produced by serializing the real model types, guaranteeing shape fidelity.
/// </summary>
public sealed class DemoServer : HttpMessageHandler
{
    public const string Org = "contoso";
    public const string ProjectNexus = "nexus-platform";
    public const string ProjectHorizon = "horizon-app";
    public const string DisplayNexus = "Nexus Platform";
    public const string DisplayHorizon = "Horizon App";
    public const string UserId = "11111111-1111-1111-1111-111111111111";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    /// <summary>Builds a demo MultiClient wired to this mock handler.</summary>
    public static MultiClient CreateClient()
    {
        var handler = new DemoServer();
        var projects = new[] { ProjectNexus, ProjectHorizon };
        var displayNames = new Dictionary<string, string>
        {
            [ProjectNexus] = DisplayNexus,
            [ProjectHorizon] = DisplayHorizon,
        };
        var client = new MultiClient(Org, projects, "demo-pat", displayNames,
            (o, p, t) => new Client(o, p, t, handler));
        foreach (var p in projects)
            client.ClientFor(p)?.SetUserID(UserId);
        return client;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        var query = uri.Query;
        string project = ProjectFromUri(uri);

        string body = Route(path, query, project, request);
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(resp);
    }

    private static string ProjectFromUri(Uri uri)
    {
        // .../{org}/{project}/_apis/...
        var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var apisIdx = Array.IndexOf(segs, "_apis");
        return apisIdx >= 1 ? segs[apisIdx - 1] : ProjectNexus;
    }

    private string Route(string path, string query, string project, HttpRequestMessage req)
    {
        // connectionData (org-level)
        if (path.Contains("/connectionData"))
            return $"{{\"authenticatedUser\":{{\"id\":\"{UserId}\"}}}}";

        // Pipelines
        if (path.EndsWith("/build/builds"))
            return Serialize(new PipelineRunsResponse { Value = Builds(project), Count = Builds(project).Count });
        if (path.Contains("/timeline"))
            return Serialize(Timeline());
        if (path.Contains("/logs/"))
            return "Demo log line 1\nDemo log line 2\nBuild succeeded\n";
        if (path.Contains("/logs"))
            return Serialize(new BuildLogsResponse { Value = new() { new BuildLog { Id = 1, LineCount = 3, Type = "Container" } }, Count = 1 });

        // Git / PRs
        if (path.EndsWith("/git/pullrequests"))
        {
            var prs = PullRequests(project);
            if (query.Contains("creatorId=")) prs = prs.Where(p => p.CreatedBy.Id == UserId).ToList();
            else if (query.Contains("reviewerId=")) prs = prs.Where(p => p.Reviewers.Any(r => r.Id == UserId)).ToList();
            return Serialize(new PullRequestsResponse { Value = prs, Count = prs.Count });
        }
        if (path.Contains("/threads"))
        {
            if (req.Method == HttpMethod.Post) return Serialize(Threads(project)[0]);
            return Serialize(new ThreadsResponse { Value = Threads(project), Count = Threads(project).Count });
        }
        if (path.Contains("/iterations") && path.Contains("/changes"))
            return Serialize(new IterationChangesResponse { ChangeEntries = Changes() });
        if (path.Contains("/iterations"))
            return Serialize(new IterationsResponse { Value = new() { new Iteration { Id = 1, Description = "iteration 1" } }, Count = 1 });
        if (path.Contains("/items"))
            return "line one\nline two\nline three\n";
        if (path.Contains("/reviewers/"))
            return "{}";

        // Work items
        if (path.EndsWith("/wit/wiql"))
            return Serialize(new WiqlResponse { WorkItems = WorkItems(project).Select(w => new WorkItemReference { Id = w.Id }).ToList() });
        if (path.Contains("/wit/workitemtypes/"))
            return Serialize(new WorkItemTypeStatesResponse
            {
                Value = new()
                {
                    new WorkItemTypeState { Name = "New", Category = "Proposed" },
                    new WorkItemTypeState { Name = "Active", Category = "InProgress" },
                    new WorkItemTypeState { Name = "Resolved", Category = "Resolved" },
                    new WorkItemTypeState { Name = "Closed", Category = "Completed" },
                },
                Count = 4,
            });
        if (path.Contains("/comments"))
        {
            if (req.Method == HttpMethod.Post)
                return Serialize(new WorkItemComment { Id = 99, Text = "Added comment", CreatedDate = DateTime.UtcNow });
            return Serialize(new WorkItemCommentsResponse
            {
                Comments = new() { new WorkItemComment { Id = 1, Text = "Looks good!", CreatedBy = new Identity { DisplayName = "Ada Lovelace" }, CreatedDate = DateTime.UtcNow.AddHours(-3) } },
                Count = 1,
            });
        }
        if (path.Contains("/updates"))
            return "{\"count\":0,\"value\":[]}";
        if (path.Contains("/wit/workitems"))
            return Serialize(new WorkItemsResponse { Value = WorkItems(project), Count = WorkItems(project).Count });

        return "{}";
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    // ---- synthetic data ----

    private static List<PipelineRun> Builds(string project)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            new PipelineRun { Id = 1011, BuildNumber = "20260616.3", Status = "completed", Result = "succeeded", SourceBranch = "refs/heads/main", QueueTime = now.AddMinutes(-20), StartTime = now.AddMinutes(-19), FinishTime = now.AddMinutes(-14), Definition = new() { Id = 5, Name = "CI" }, Project = new() { Name = project } },
            new PipelineRun { Id = 1009, BuildNumber = "20260616.2", Status = "completed", Result = "failed", SourceBranch = "refs/heads/feature/login", QueueTime = now.AddHours(-2), StartTime = now.AddHours(-2), FinishTime = now.AddHours(-2).AddMinutes(4), Definition = new() { Id = 5, Name = "CI" }, Project = new() { Name = project } },
            new PipelineRun { Id = 1007, BuildNumber = "20260616.1", Status = "inProgress", Result = "none", SourceBranch = "refs/heads/release/1.4", QueueTime = now.AddMinutes(-3), StartTime = now.AddMinutes(-3), Definition = new() { Id = 8, Name = "Release" }, Project = new() { Name = project } },
        };
    }

    private static Timeline Timeline()
    {
        var now = DateTime.UtcNow;
        return new Timeline
        {
            Id = "t1",
            Records = new()
            {
                new TimelineRecord { Id = "s1", Type = "Stage", Name = "Build", State = "completed", Result = "succeeded", Order = 1, StartTime = now.AddMinutes(-19), FinishTime = now.AddMinutes(-16) },
                new TimelineRecord { Id = "j1", ParentId = "s1", Type = "Job", Name = "Compile", State = "completed", Result = "succeeded", Order = 1, StartTime = now.AddMinutes(-19), FinishTime = now.AddMinutes(-17) },
                new TimelineRecord { Id = "k1", ParentId = "j1", Type = "Task", Name = "dotnet build", State = "completed", Result = "succeeded", Order = 1, Log = new LogReference { Id = 1 } },
                new TimelineRecord { Id = "s2", Type = "Stage", Name = "Test", State = "completed", Result = "succeededWithIssues", Order = 2, StartTime = now.AddMinutes(-16), FinishTime = now.AddMinutes(-14), Issues = new() { new Issue { Type = "warning", Message = "2 flaky tests" } } },
            },
        };
    }

    private static List<PullRequest> PullRequests(string project)
    {
        var now = DateTime.UtcNow;
        var repo = new Repository { Id = "repo-1", Name = project == ProjectNexus ? "nexus-api" : "horizon-web" };
        return new()
        {
            new PullRequest { Id = 4201, Title = "Add OAuth login flow", Status = "active", CreationDate = now.AddHours(-5), SourceRefName = "refs/heads/feature/login", TargetRefName = "refs/heads/main", CreatedBy = new Identity { Id = UserId, DisplayName = "Ada Lovelace" }, Repository = repo, Reviewers = new() { new Reviewer { Id = "r2", DisplayName = "Alan Turing", Vote = 10 } } },
            new PullRequest { Id = 4198, Title = "Refactor metrics aggregation", Status = "active", IsDraft = true, CreationDate = now.AddDays(-1), SourceRefName = "refs/heads/refactor/metrics", TargetRefName = "refs/heads/main", CreatedBy = new Identity { Id = "r2", DisplayName = "Alan Turing" }, Repository = repo, Reviewers = new() { new Reviewer { Id = UserId, DisplayName = "Ada Lovelace", Vote = -5 } } },
        };
    }

    private static List<Thread> Threads(string project) => new()
    {
        new Thread { Id = 1, Status = "active", PublishedDate = DateTime.UtcNow.AddHours(-4), Comments = new() { new Comment { Id = 1, Content = "Can we extract this into a helper?", CommentType = "text", Author = new Identity { DisplayName = "Alan Turing" }, PublishedDate = DateTime.UtcNow.AddHours(-4) } } },
        new Thread { Id = 2, Status = "active", ThreadContext = new ThreadContext { FilePath = "/src/auth.cs", RightFileStart = new FilePosition { Line = 12 } }, Comments = new() { new Comment { Id = 2, Content = "Nit: rename this variable.", CommentType = "text", Author = new Identity { DisplayName = "Ada Lovelace" } } } },
    };

    private static List<IterationChange> Changes() => new()
    {
        new IterationChange { ChangeType = "edit", Item = new ChangeItem { Path = "/src/auth.cs", GitObjectType = "blob" } },
        new IterationChange { ChangeType = "add", Item = new ChangeItem { Path = "/src/oauth.cs", GitObjectType = "blob" } },
    };

    private static List<WorkItem> WorkItems(string project)
    {
        var now = DateTime.UtcNow;
        WorkItem Make(int id, string title, string state, string type, string assignee, double pts) => new()
        {
            Id = id,
            Fields = new WorkItemFields
            {
                Title = title, State = state, WorkItemType = type,
                AssignedTo = new Identity { DisplayName = assignee },
                ChangedDate = now.AddHours(-id % 24), StateChangeDate = now.AddDays(-2),
                IterationPath = $"{project}\\Sprint 12", StoryPoints = pts, Tags = "backend; priority",
                Description = "Demo work item description.",
            },
        };
        return new()
        {
            Make(7001, "Implement token refresh", "Active", "User Story", "Ada Lovelace", 5),
            Make(7002, "Fix flaky auth test", "New", "Bug", "Alan Turing", 2),
            Make(7003, "Document metrics API", "Resolved", "Task", "Grace Hopper", 3),
        };
    }
}
