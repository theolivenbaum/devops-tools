namespace Azdo.Core.AzureDevOps;

/// <summary>Wraps multiple project-scoped <see cref="Client"/>s for concurrent fetching.</summary>
public sealed class MultiClient
{
    private readonly string _org;
    private readonly string _pat;
    private readonly Dictionary<string, Client> _clients; // project name → client
    private Dictionary<string, string>? _displayNames;     // API name → display name

    /// <summary>
    /// Creates clients for each project. <paramref name="displayNames"/> is an
    /// optional map of API name → display name for UI rendering.
    /// </summary>
    public MultiClient(string org, IReadOnlyList<string> projects, string pat,
        IReadOnlyDictionary<string, string>? displayNames = null,
        Func<string, string, string, Client>? clientFactory = null)
    {
        if (projects.Count == 0)
            throw new ArgumentException("at least one project is required", nameof(projects));

        _org = org;
        _pat = pat;
        _clients = new Dictionary<string, Client>(projects.Count);
        var factory = clientFactory ?? ((o, p, t) => new Client(o, p, t));
        foreach (var project in projects)
        {
            try
            {
                _clients[project] = factory(org, project, pat);
            }
            catch (Exception e)
            {
                throw new ArgumentException($"failed to create client for project \"{project}\": {e.Message}", e);
            }
        }
        _displayNames = displayNames is null ? null : new Dictionary<string, string>(displayNames);
    }

    /// <summary>Test-only constructor that injects pre-built clients directly.</summary>
    internal MultiClient(string org, string pat, Dictionary<string, Client> clients,
        Dictionary<string, string>? displayNames = null)
    {
        _org = org;
        _pat = pat;
        _clients = clients;
        _displayNames = displayNames;
    }

    /// <summary>Overrides the display-name map (test helper / late configuration).</summary>
    internal void SetDisplayNames(Dictionary<string, string> displayNames) => _displayNames = displayNames;

    /// <summary>Returns the display name for a project API name, or the API name itself.</summary>
    public string DisplayNameFor(string project)
    {
        if (_displayNames is not null && _displayNames.TryGetValue(project, out var dn))
            return dn;
        return project;
    }

    /// <summary>The project-specific client (for detail views), or null if unknown.</summary>
    public Client? ClientFor(string project) => _clients.TryGetValue(project, out var c) ? c : null;

    /// <summary>The organization name.</summary>
    public string GetOrg() => _org;

    /// <summary>True if more than one project is configured.</summary>
    public bool IsMultiProject() => _clients.Count > 1;

    /// <summary>The list of project names.</summary>
    public List<string> Projects() => _clients.Keys.ToList();

    private readonly record struct Result<T>(string Project, List<T>? Items, Exception? Error);

    /// <summary>
    /// Runs <paramref name="fetch"/> concurrently across all clients, tags successful
    /// results, merges, sorts via <paramref name="keyDescending"/>, and returns partial
    /// results with a <see cref="PartialException"/> when some (not all) projects fail.
    /// </summary>
    private async Task<List<T>> FetchAllAsync<T>(
        Func<string, Client, Task<List<T>>> fetch,
        Action<T, string> tag,
        Comparison<T> sort,
        CancellationToken ct)
    {
        var tasks = _clients.Select(async kv =>
        {
            try
            {
                var items = await fetch(kv.Key, kv.Value).ConfigureAwait(false);
                return new Result<T>(kv.Key, items, null);
            }
            catch (Exception e)
            {
                return new Result<T>(kv.Key, null, e);
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var all = new List<T>();
        var errs = new List<Exception>();
        foreach (var r in results)
        {
            if (r.Error is not null)
            {
                errs.Add(r.Error);
                continue;
            }
            foreach (var item in r.Items!)
                tag(item, r.Project);
            all.AddRange(r.Items!);
        }

        if (errs.Count == _clients.Count)
            throw new AggregateException("all projects failed", errs);

        all.Sort(sort);

        if (errs.Count > 0)
            throw new PartialException(errs.Count, _clients.Count, errs) { PartialData = all };

        return all;
    }

    private Task<List<T>> RunAsync<T>(
        Func<string, Client, Task<List<T>>> fetch,
        Action<T, string> tag,
        Comparison<T> sort,
        CancellationToken ct) => FetchAllAsync(fetch, tag, sort, ct);

    private static int CompareDesc(DateTime a, DateTime b) => b.CompareTo(a);

    /// <summary>Fetches pipeline runs from all projects, merged and sorted by QueueTime descending.</summary>
    public Task<List<PipelineRun>> ListPipelineRunsAsync(int top, CancellationToken ct = default) =>
        RunAsync(
            (_, c) => c.ListPipelineRunsAsync(top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.QueueTime, b.QueueTime),
            ct);

    /// <summary>Fetches PRs from all projects, tagged, merged and sorted by CreationDate descending.</summary>
    public Task<List<PullRequest>> ListPullRequestsAsync(int top, CancellationToken ct = default) =>
        RunAsync(
            (_, c) => c.ListPullRequestsAsync(top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.CreationDate, b.CreationDate),
            ct);

    /// <summary>Fetches PRs created by the authenticated user from all projects.</summary>
    public async Task<List<PullRequest>> ListMyPullRequestsAsync(int top, CancellationToken ct = default)
    {
        var userId = await ResolveUserIdAsync(ct).ConfigureAwait(false);
        return await RunAsync(
            (_, c) => c.ListMyPullRequestsAsync(userId, top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.CreationDate, b.CreationDate),
            ct).ConfigureAwait(false);
    }

    /// <summary>Fetches PRs where the authenticated user is a reviewer from all projects.</summary>
    public async Task<List<PullRequest>> ListPullRequestsAsReviewerAsync(int top, CancellationToken ct = default)
    {
        var userId = await ResolveUserIdAsync(ct).ConfigureAwait(false);
        return await RunAsync(
            (_, c) => c.ListPullRequestsAsReviewerAsync(userId, top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.CreationDate, b.CreationDate),
            ct).ConfigureAwait(false);
    }

    /// <summary>Fetches work items from all projects, tagged, merged and sorted by ChangedDate descending.</summary>
    public Task<List<WorkItem>> ListWorkItemsAsync(int top, CancellationToken ct = default) =>
        RunAsync(
            (_, c) => c.ListWorkItemsAsync(top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.Fields.ChangedDate, b.Fields.ChangedDate),
            ct);

    /// <summary>Fetches the org-wide metrics dataset from all projects, merged and sorted by ChangedDate descending.</summary>
    public Task<List<WorkItem>> MetricsWorkItemsAsync(DateTime since, MetricsStateNames states, CancellationToken ct = default) =>
        RunAsync(
            (_, c) => c.MetricsWorkItemsAsync(since, states, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.Fields.ChangedDate, b.Fields.ChangedDate),
            ct);

    /// <summary>Fetches work items assigned to the authenticated user (@Me) from all projects.</summary>
    public Task<List<WorkItem>> ListMyWorkItemsAsync(int top, CancellationToken ct = default) =>
        RunAsync(
            (_, c) => c.ListMyWorkItemsAsync(top, ct),
            (r, p) => { r.ProjectName = p; r.ProjectDisplayName = DisplayNameFor(p); },
            (a, b) => CompareDesc(a.Fields.ChangedDate, b.Fields.ChangedDate),
            ct);

    private async Task<string> ResolveUserIdAsync(CancellationToken ct)
    {
        foreach (var client in _clients.Values)
        {
            try
            {
                return await client.GetCurrentUserIdAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"failed to get current user ID: {e.Message}", e);
            }
        }
        throw new InvalidOperationException("no clients configured");
    }
}
