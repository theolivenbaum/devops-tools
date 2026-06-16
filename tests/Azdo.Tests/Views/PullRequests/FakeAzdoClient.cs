using Azdo.Core.AzureDevOps;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tests.Views.PullRequests;

/// <summary>
/// A configurable in-memory <see cref="IAzdoClient"/> for view tests. Only the
/// PR/Git surface is wired; unused members throw.
/// </summary>
internal sealed class FakeAzdoClient : IAzdoClient
{
    public string Org { get; init; } = "testorg";
    public string Project { get; init; } = "testproject";

    public List<Thread> Threads { get; set; } = new();
    public List<Iteration> Iterations { get; set; } = new() { new Iteration { Id = 1 } };
    public List<IterationChange> Changes { get; set; } = new();
    public Dictionary<string, string> FileContents { get; set; } = new();

    public int VoteCalls { get; private set; }
    public int LastVote { get; private set; }
    public int CodeCommentCalls { get; private set; }
    public string? LastCodeCommentFile { get; private set; }
    public int LastCodeCommentLine { get; private set; }
    public int GeneralCommentCalls { get; private set; }
    public int ReplyCalls { get; private set; }
    public int LastReplyThreadId { get; private set; }
    public int ResolveCalls { get; private set; }
    public int LastResolveThreadId { get; private set; }
    public string? LastResolveStatus { get; private set; }

    public string GetOrg() => Org;
    public string GetProject() => Project;

    public Task<List<Thread>> GetPRThreadsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default)
        => Task.FromResult(new List<Thread>(Threads));

    public Task VotePullRequestAsync(string repositoryId, int pullRequestId, int vote, CancellationToken ct = default)
    {
        VoteCalls++;
        LastVote = vote;
        return Task.CompletedTask;
    }

    public Task<List<Iteration>> GetPRIterationsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default)
        => Task.FromResult(new List<Iteration>(Iterations));

    public Task<List<IterationChange>> GetPRIterationChangesAsync(string repositoryId, int pullRequestId, int iterationId, CancellationToken ct = default)
        => Task.FromResult(new List<IterationChange>(Changes));

    public Task<string> GetFileContentAsync(string repositoryId, string filePath, string branchName, CancellationToken ct = default)
        => Task.FromResult(FileContents.TryGetValue($"{filePath}@{branchName}", out var c) ? c
            : FileContents.TryGetValue(branchName, out var b) ? b : "");

    public Task<Thread> AddPRCodeCommentAsync(string repositoryId, int pullRequestId, string filePath, int line, string content, CancellationToken ct = default)
    {
        CodeCommentCalls++;
        LastCodeCommentFile = filePath;
        LastCodeCommentLine = line;
        return Task.FromResult(new Thread());
    }

    public Task<Thread> AddPRCommentAsync(string repositoryId, int pullRequestId, string comment, CancellationToken ct = default)
    {
        GeneralCommentCalls++;
        return Task.FromResult(new Thread());
    }

    public Task<Comment> ReplyToThreadAsync(string repositoryId, int pullRequestId, int threadId, string content, CancellationToken ct = default)
    {
        ReplyCalls++;
        LastReplyThreadId = threadId;
        return Task.FromResult(new Comment());
    }

    public Task UpdateThreadStatusAsync(string repositoryId, int pullRequestId, int threadId, string status, CancellationToken ct = default)
    {
        ResolveCalls++;
        LastResolveThreadId = threadId;
        LastResolveStatus = status;
        return Task.CompletedTask;
    }

    // --- Unused surface ---
    public Task<string> GetCurrentUserIdAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<PipelineRun>> ListPipelineRunsAsync(int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Timeline> GetBuildTimelineAsync(int buildId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<BuildLog>> ListBuildLogsAsync(int buildId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> GetBuildLogContentAsync(int buildId, int logId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<PullRequest>> ListPullRequestsAsync(int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<PullRequest>> ListMyPullRequestsAsync(string creatorId, int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<PullRequest>> ListPullRequestsAsReviewerAsync(string reviewerId, int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<int>> QueryWorkItemIdsAsync(string query, int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItem>> GetWorkItemsAsync(IReadOnlyList<int> ids, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItem>> ListWorkItemsAsync(int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItem>> ListMyWorkItemsAsync(int top, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItemTypeState>> GetWorkItemTypeStatesAsync(string workItemType, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UpdateWorkItemStateAsync(int id, string state, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WorkItemComment> AddWorkItemCommentAsync(int id, string text, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItem>> MetricsWorkItemsAsync(DateTime since, MetricsStateNames states, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<WorkItemStateTransition>> WorkItemUpdatesAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
}
