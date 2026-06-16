using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Azdo.Core.AzureDevOps;

public sealed partial class Client
{
    private const string ParseHint =
        " This may indicate an API structure change. Please check for updates or report this issue";

    private static T Parse<T>(string body, string what)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (result is null)
                throw new InvalidOperationException(
                    $"failed to parse Azure DevOps API response for {what}: null result.{ParseHint}");
            return result;
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"failed to parse Azure DevOps API response for {what}: {e.Message}.{ParseHint}", e);
        }
    }

    // ----- pipelines.go -----

    /// <summary>
    /// Retrieves the most recent pipeline runs (builds) for the project, ordered
    /// by queue time descending.
    /// </summary>
    public async Task<List<PipelineRun>> ListPipelineRunsAsync(int top, CancellationToken ct = default)
    {
        var path = $"/build/builds?api-version=7.1&$top={top}&queryOrder=queueTimeDescending";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<PipelineRunsResponse>(body, "pipeline runs").Value;
    }

    // ----- logs.go -----

    /// <summary>Retrieves all logs for a specific build.</summary>
    public async Task<List<BuildLog>> ListBuildLogsAsync(int buildId, CancellationToken ct = default)
    {
        var path = $"/build/builds/{buildId}/logs?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<BuildLogsResponse>(body, "build logs").Value;
    }

    /// <summary>Retrieves the content of a specific log.</summary>
    public async Task<string> GetBuildLogContentAsync(int buildId, int logId, CancellationToken ct = default)
    {
        var path = $"/build/builds/{buildId}/logs/{logId}?api-version=7.1";
        return await GetAsync(path, ct).ConfigureAwait(false);
    }

    // ----- timeline.go -----

    /// <summary>Retrieves the timeline (stages, jobs, tasks) for a specific build.</summary>
    public async Task<Timeline> GetBuildTimelineAsync(int buildId, CancellationToken ct = default)
    {
        var path = $"/build/builds/{buildId}/timeline?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<Timeline>(body, "build timeline");
    }

    // ----- git.go -----

    /// <summary>Retrieves active pull requests across all repositories in the project.</summary>
    public async Task<List<PullRequest>> ListPullRequestsAsync(int top, CancellationToken ct = default)
    {
        var path = $"/git/pullrequests?api-version=7.1&$top={top}&searchCriteria.status=active";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<PullRequestsResponse>(body, "pull requests").Value;
    }

    /// <summary>Retrieves active pull requests created by the given user.</summary>
    public async Task<List<PullRequest>> ListMyPullRequestsAsync(string creatorId, int top, CancellationToken ct = default)
    {
        var path = $"/git/pullrequests?api-version=7.1&$top={top}&searchCriteria.status=active&searchCriteria.creatorId={creatorId}";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<PullRequestsResponse>(body, "pull requests").Value;
    }

    /// <summary>Retrieves active pull requests where the given user is a reviewer.</summary>
    public async Task<List<PullRequest>> ListPullRequestsAsReviewerAsync(string reviewerId, int top, CancellationToken ct = default)
    {
        var path = $"/git/pullrequests?api-version=7.1&$top={top}&searchCriteria.status=active&searchCriteria.reviewerId={reviewerId}";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<PullRequestsResponse>(body, "pull requests").Value;
    }

    /// <summary>Retrieves comment threads for a pull request.</summary>
    public async Task<List<Thread>> GetPRThreadsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<ThreadsResponse>(body, "PR threads").Value;
    }

    /// <summary>Sets the current user's vote on a pull request.</summary>
    public async Task VotePullRequestAsync(string repositoryId, int pullRequestId, int vote, CancellationToken ct = default)
    {
        var userId = await GetCurrentUserIdAsync(ct).ConfigureAwait(false);
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/reviewers/{userId}?api-version=7.1";
        var payload = $"{{\"vote\": {vote}}}";
        await PutAsync(path, payload, ct).ConfigureAwait(false);
    }

    /// <summary>Adds a general comment thread to a pull request.</summary>
    public async Task<Thread> AddPRCommentAsync(string repositoryId, int pullRequestId, string comment, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1";
        var payload =
            "{\n\t\t\t\"comments\": [\n\t\t\t\t{\n\t\t\t\t\t\"parentCommentId\": 0,\n\t\t\t\t\t\"content\": " +
            EscapeJsonString(comment) +
            ",\n\t\t\t\t\t\"commentType\": \"text\"\n\t\t\t\t}\n\t\t\t],\n\t\t\t\"status\": \"active\"\n\t\t}";
        var body = await PostAsync(path, payload, ct).ConfigureAwait(false);
        return Parse<Thread>(body, "thread");
    }

    /// <summary>Retrieves iterations for a pull request.</summary>
    public async Task<List<Iteration>> GetPRIterationsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<IterationsResponse>(body, "PR iterations").Value;
    }

    /// <summary>Retrieves files changed in a specific PR iteration.</summary>
    public async Task<List<IterationChange>> GetPRIterationChangesAsync(string repositoryId, int pullRequestId, int iterationId, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations/{iterationId}/changes?api-version=7.1&$compareTo=0";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<IterationChangesResponse>(body, "PR iteration changes").ChangeEntries;
    }

    /// <summary>Retrieves raw file content at a specific branch version.</summary>
    public async Task<string> GetFileContentAsync(string repositoryId, string filePath, string branchName, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/items?path={filePath}&versionType=branch&version={branchName}&api-version=7.1";
        return await GetRawAsync(path, "text/plain", ct).ConfigureAwait(false);
    }

    /// <summary>Adds a reply comment to an existing thread.</summary>
    public async Task<Comment> ReplyToThreadAsync(string repositoryId, int pullRequestId, int threadId, string content, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads/{threadId}/comments?api-version=7.1";
        var payload = $"{{\"content\": {EscapeJsonString(content)}, \"parentCommentId\": 1, \"commentType\": \"text\"}}";
        var body = await PostAsync(path, payload, ct).ConfigureAwait(false);
        return Parse<Comment>(body, "reply");
    }

    /// <summary>Updates the status of a thread (e.g. resolve it).</summary>
    public async Task UpdateThreadStatusAsync(string repositoryId, int pullRequestId, int threadId, string status, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads/{threadId}?api-version=7.1";
        var payload = $"{{\"status\": {EscapeJsonString(status)}}}";
        await PatchAsync(path, payload, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a new comment thread attached to a specific file and line.</summary>
    public async Task<Thread> AddPRCodeCommentAsync(string repositoryId, int pullRequestId, string filePath, int line, string content, CancellationToken ct = default)
    {
        var path = $"/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1";
        var payload =
            "{\n\t\t\t\"comments\": [\n\t\t\t\t{\n\t\t\t\t\t\"parentCommentId\": 0,\n\t\t\t\t\t\"content\": " +
            EscapeJsonString(content) +
            ",\n\t\t\t\t\t\"commentType\": \"text\"\n\t\t\t\t}\n\t\t\t],\n\t\t\t\"status\": \"active\",\n\t\t\t\"threadContext\": {\n\t\t\t\t\"filePath\": " +
            EscapeJsonString(filePath) +
            $",\n\t\t\t\t\"rightFileStart\": {{\"line\": {line}, \"offset\": 1}},\n\t\t\t\t\"rightFileEnd\": {{\"line\": {line}, \"offset\": 1}}\n\t\t\t}}\n\t\t}}";
        var body = await PostAsync(path, payload, ct).ConfigureAwait(false);
        return Parse<Thread>(body, "code comment");
    }

    // ----- workitems.go -----

    /// <summary>Executes a WIQL query and returns the work item IDs.</summary>
    public async Task<List<int>> QueryWorkItemIdsAsync(string query, int top, CancellationToken ct = default)
    {
        var path = $"/wit/wiql?api-version=7.1&$top={top}";
        var payload = $"{{\"query\": {EscapeJsonString(query)}}}";
        var body = await PostAsync(path, payload, ct).ConfigureAwait(false);
        var response = Parse<WiqlResponse>(body, "work item query");
        return response.WorkItems.Select(w => w.Id).ToList();
    }

    private static readonly string[] WorkItemFieldsList =
    {
        "System.Id",
        "System.Title",
        "System.State",
        "System.WorkItemType",
        "System.AssignedTo",
        "Microsoft.VSTS.Common.Priority",
        "System.ChangedDate",
        "System.IterationPath",
        "System.Description",
        "Microsoft.VSTS.TCM.ReproSteps",
        "System.Tags",
        "Microsoft.VSTS.Scheduling.StoryPoints",
        "Microsoft.VSTS.Common.StateChangeDate",
        "Microsoft.VSTS.Common.ActivatedDate",
        "Microsoft.VSTS.Common.ClosedDate",
        "System.CreatedDate",
    };

    /// <summary>Retrieves work items by their IDs (Azure DevOps supports up to 200 per request).</summary>
    public async Task<List<WorkItem>> GetWorkItemsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return new List<WorkItem>();

        var idsParam = string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        var fields = string.Join(",", WorkItemFieldsList);
        var path = $"/wit/workitems?ids={idsParam}&fields={fields}&api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<WorkItemsResponse>(body, "work items").Value;
    }

    /// <summary>Retrieves active work items in the project (capped at 50).</summary>
    public async Task<List<WorkItem>> ListWorkItemsAsync(int top, CancellationToken ct = default)
    {
        if (top > 50) top = 50;

        const string query = """
            SELECT [System.Id] FROM WorkItems
            WHERE [System.TeamProject] = @project
              AND [System.State] <> 'Closed'
              AND [System.State] <> 'Removed'
            ORDER BY [System.ChangedDate] DESC
            """;

        var ids = await QueryWorkItemIdsAsync(query, top, ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return new List<WorkItem>();
        return await GetWorkItemsAsync(ids, ct).ConfigureAwait(false);
    }

    /// <summary>Retrieves work items assigned to the authenticated user via @Me (capped at 50).</summary>
    public async Task<List<WorkItem>> ListMyWorkItemsAsync(int top, CancellationToken ct = default)
    {
        if (top > 50) top = 50;

        const string query = """
            SELECT [System.Id] FROM WorkItems
            WHERE [System.TeamProject] = @project
              AND [System.AssignedTo] = @Me
              AND [System.State] <> 'Closed'
              AND [System.State] <> 'Removed'
            ORDER BY [System.ChangedDate] DESC
            """;

        var ids = await QueryWorkItemIdsAsync(query, top, ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return new List<WorkItem>();
        return await GetWorkItemsAsync(ids, ct).ConfigureAwait(false);
    }

    /// <summary>Retrieves available states for a work item type, excluding the "Removed" category.</summary>
    public async Task<List<WorkItemTypeState>> GetWorkItemTypeStatesAsync(string workItemType, CancellationToken ct = default)
    {
        var path = $"/wit/workitemtypes/{workItemType}/states?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        var response = Parse<WorkItemTypeStatesResponse>(body, "work item type states");
        return response.Value.Where(s => s.Category != "Removed").ToList();
    }

    /// <summary>Updates the state of a work item using JSON Patch.</summary>
    public async Task UpdateWorkItemStateAsync(int id, string state, CancellationToken ct = default)
    {
        var path = $"/wit/workitems/{id}?api-version=7.1";
        var payload = $"[{{\"op\":\"replace\",\"path\":\"/fields/System.State\",\"value\":{EscapeJsonString(state)}}}]";
        await DoRequestWithContentTypeAsync(new HttpMethod("PATCH"), path, payload, "application/json-patch+json", ct)
            .ConfigureAwait(false);
    }

    // ----- comments.go -----

    private const string CommentsApiVersion = "7.1-preview.4";
    private const int CommentsTopLimit = 200;

    /// <summary>Returns up to 200 comments for a work item, sorted newest first.</summary>
    public async Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int id, CancellationToken ct = default)
    {
        var path = $"/wit/workItems/{id}/comments?api-version={CommentsApiVersion}&order=desc&$top={CommentsTopLimit}";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        return Parse<WorkItemCommentsResponse>(body, "work item comments").Comments;
    }

    /// <summary>Posts a new comment to a work item and returns the created comment.</summary>
    public async Task<WorkItemComment> AddWorkItemCommentAsync(int id, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("comment text cannot be empty", nameof(text));

        var path = $"/wit/workItems/{id}/comments?api-version={CommentsApiVersion}";
        var payload = $"{{\"text\": {EscapeJsonString(text)}}}";
        var body = await PostAsync(path, payload, ct).ConfigureAwait(false);
        return Parse<WorkItemComment>(body, "added work item comment");
    }

    // ----- metrics.go -----

    /// <summary>
    /// Fetches every in-flight item (Active / Ready for Test) plus items closed
    /// on or after <paramref name="since"/>. Org-wide (no @Me), not capped at 50.
    /// </summary>
    public async Task<List<WorkItem>> MetricsWorkItemsAsync(DateTime since, MetricsStateNames states, CancellationToken ct = default)
    {
        var query = BuildMetricsWiql(since, states);
        var ids = await QueryWorkItemIdsAsync(query, 2000, ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return new List<WorkItem>();
        return await GetWorkItemsBatchedAsync(ids, ct).ConfigureAwait(false);
    }

    /// <summary>The pure WIQL constructor for the metrics query.</summary>
    public static string BuildMetricsWiql(DateTime since, MetricsStateNames states)
    {
        foreach (var n in new[] { states.Active, states.ReadyForTest, states.Closed })
        {
            if (string.IsNullOrEmpty(n))
                throw new ArgumentException("metrics state name is empty");
            if (n.Contains('\''))
                throw new ArgumentException($"metrics state name \"{n}\" contains a single quote");
        }
        var sinceStr = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return
            "SELECT [System.Id] FROM WorkItems\n" +
            "WHERE [System.TeamProject] = @project\n" +
            "  AND (\n" +
            $"        [System.State] IN ('{states.Active}','{states.ReadyForTest}')\n" +
            $"     OR ([System.State] = '{states.Closed}' AND [Microsoft.VSTS.Common.ClosedDate] >= '{sinceStr}')\n" +
            "  )\n" +
            "ORDER BY [System.ChangedDate] DESC";
    }

    /// <summary>Fetches the revision history for a work item and returns chronological state changes.</summary>
    public async Task<List<WorkItemStateTransition>> WorkItemUpdatesAsync(int id, CancellationToken ct = default)
    {
        var path = $"/wit/workItems/{id}/updates?api-version=7.1";
        var body = await GetAsync(path, ct).ConfigureAwait(false);
        WorkItemUpdatesResponse resp;
        try
        {
            resp = JsonSerializer.Deserialize<WorkItemUpdatesResponse>(body, JsonOptions)
                   ?? new WorkItemUpdatesResponse();
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"parse updates for {id}: {e.Message}", e);
        }
        return ParseStateTransitions(resp.Value);
    }

    /// <summary>Extracts state-change events from raw /updates payloads (pure helper).</summary>
    internal static List<WorkItemStateTransition> ParseStateTransitions(List<WorkItemUpdate> updates)
    {
        var output = new List<WorkItemStateTransition>();
        foreach (var u in updates)
        {
            if (!u.Fields.TryGetValue("System.State", out var stateChange))
                continue;
            var newState = AsString(stateChange.NewValue);
            if (string.IsNullOrEmpty(newState))
                continue;

            DateTime at = default;
            if (u.Fields.TryGetValue("System.ChangedDate", out var cd))
            {
                var raw = AsString(cd.NewValue);
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
                    at = dto.UtcDateTime;
            }
            if (at == default)
                continue;

            output.Add(new WorkItemStateTransition { State = newState, At = at });
        }
        output.Sort((a, b) => a.At.CompareTo(b.At));
        return output;
    }

    private static string AsString(JsonElement v) =>
        v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    /// <summary>Fans GetWorkItems out in batches of 200. Returns concatenated results.</summary>
    private async Task<List<WorkItem>> GetWorkItemsBatchedAsync(List<int> ids, CancellationToken ct)
    {
        const int batch = 200;
        var all = new List<WorkItem>(ids.Count);
        for (int i = 0; i < ids.Count; i += batch)
        {
            int end = Math.Min(i + batch, ids.Count);
            var slice = ids.GetRange(i, end - i);
            try
            {
                var items = await GetWorkItemsAsync(slice, ct).ConfigureAwait(false);
                all.AddRange(items);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"metrics batch {i}-{end}: {e.Message}", e);
            }
        }
        return all;
    }
}
