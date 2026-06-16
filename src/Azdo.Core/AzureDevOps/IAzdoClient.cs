namespace Azdo.Core.AzureDevOps;

/// <summary>
/// The Azure DevOps operations the UI needs. Implemented by <see cref="Client"/>.
/// Mirrors the Go interfaces (PipelineClient, etc.) collapsed into one surface.
/// </summary>
public interface IAzdoClient
{
    string GetOrg();
    string GetProject();
    Task<string> GetCurrentUserIdAsync(CancellationToken ct = default);

    // Pipelines / builds
    Task<List<PipelineRun>> ListPipelineRunsAsync(int top, CancellationToken ct = default);
    Task<Timeline> GetBuildTimelineAsync(int buildId, CancellationToken ct = default);
    Task<List<BuildLog>> ListBuildLogsAsync(int buildId, CancellationToken ct = default);
    Task<string> GetBuildLogContentAsync(int buildId, int logId, CancellationToken ct = default);

    // Git / pull requests
    Task<List<PullRequest>> ListPullRequestsAsync(int top, CancellationToken ct = default);
    Task<List<PullRequest>> ListMyPullRequestsAsync(string creatorId, int top, CancellationToken ct = default);
    Task<List<PullRequest>> ListPullRequestsAsReviewerAsync(string reviewerId, int top, CancellationToken ct = default);
    Task<List<Thread>> GetPRThreadsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);
    Task VotePullRequestAsync(string repositoryId, int pullRequestId, int vote, CancellationToken ct = default);
    Task<Thread> AddPRCommentAsync(string repositoryId, int pullRequestId, string comment, CancellationToken ct = default);
    Task<List<Iteration>> GetPRIterationsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);
    Task<List<IterationChange>> GetPRIterationChangesAsync(string repositoryId, int pullRequestId, int iterationId, CancellationToken ct = default);
    Task<string> GetFileContentAsync(string repositoryId, string filePath, string branchName, CancellationToken ct = default);
    Task<Comment> ReplyToThreadAsync(string repositoryId, int pullRequestId, int threadId, string content, CancellationToken ct = default);
    Task UpdateThreadStatusAsync(string repositoryId, int pullRequestId, int threadId, string status, CancellationToken ct = default);
    Task<Thread> AddPRCodeCommentAsync(string repositoryId, int pullRequestId, string filePath, int line, string content, CancellationToken ct = default);

    // Work items
    Task<List<int>> QueryWorkItemIdsAsync(string query, int top, CancellationToken ct = default);
    Task<List<WorkItem>> GetWorkItemsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
    Task<List<WorkItem>> ListWorkItemsAsync(int top, CancellationToken ct = default);
    Task<List<WorkItem>> ListMyWorkItemsAsync(int top, CancellationToken ct = default);
    Task<List<WorkItemTypeState>> GetWorkItemTypeStatesAsync(string workItemType, CancellationToken ct = default);
    Task UpdateWorkItemStateAsync(int id, string state, CancellationToken ct = default);
    Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int id, CancellationToken ct = default);
    Task<WorkItemComment> AddWorkItemCommentAsync(int id, string text, CancellationToken ct = default);

    // Metrics
    Task<List<WorkItem>> MetricsWorkItemsAsync(DateTime since, MetricsStateNames states, CancellationToken ct = default);
    Task<List<WorkItemStateTransition>> WorkItemUpdatesAsync(int id, CancellationToken ct = default);
}
