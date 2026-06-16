using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.PullRequests;

/// <summary>The current top-level view within the Pull Requests tab (≈ <c>ViewMode</c>).</summary>
public enum PrViewMode { List, Detail, Diff }

/// <summary>
/// The Pull Requests tab (≈ <c>pullrequests.Model</c>): a generic
/// <see cref="ListView{T}"/> of PRs with my-PRs/as-reviewer filters, plus a
/// drill-down detail view and a third diff-view mode wired in on top.
/// </summary>
public sealed class Model : ITabView, IRestorableTab
{
    private readonly ListView<PullRequest> _list;
    private readonly MultiClient? _client;
    private readonly StyleSet _styles;
    private PullRequestDiffView? _diffView;
    private PrViewMode _viewMode = PrViewMode.List;
    private int _width;
    private int _height;
    private bool _myPRsOnly;
    private bool _asReviewerOnly;

    private List<PullRequest> _allPRs = new();

    private int _pendingDetailId;
    private bool _pendingRestoreHandled;

    public Model(MultiClient? client) : this(client, StyleSet.Default()) { }

    public Model(MultiClient? client, StyleSet styles)
    {
        _client = client;
        _styles = styles;
        bool isMulti = client is not null && client.IsMultiProject();

        var columns = new List<ColumnSpec>
        {
            new("Status", 10, 8),
            new("Title", 30, 15),
            new("Branches", 20, 12),
            new("Author", 15, 10),
            new("Repo", 15, 10),
            new("Reviews", 10, 6),
        };
        if (isMulti)
            columns.Insert(0, new ColumnSpec("Project", 10, 10));

        columns = ListView<PullRequest>.NormalizeWidths(columns);

        Func<IReadOnlyList<PullRequest>, StyleSet, List<string[]>> toRows = isMulti ? PrsToRowsMulti : PrsToRows;
        Func<PullRequest, string, bool> filterFunc = isMulti ? FilterPrMulti : FilterPr;

        var cfg = new ListConfig<PullRequest>
        {
            Columns = columns,
            LoadingMessage = "Loading pull requests...",
            EntityName = "pull requests",
            MinWidth = 50,
            ToRows = toRows,
            Fetch = () => FetchPullRequests(),
            EnterDetail = (item, st, w, h) =>
            {
                IAzdoClient? projectClient = _client?.ClientFor(item.ProjectName);
                var d = new PullRequestDetailView(projectClient, item, st);
                d.SetSize(w, h);
                return ((IDetailView)new PullRequestDetailAdapter(d), d.Init());
            },
            HasContextBar = mode => mode == ViewMode.Detail,
            FilterFunc = filterFunc,
        };

        _list = new ListView<PullRequest>(cfg, styles);
    }

    public Cmd? Init() => _list.Init();

    public Cmd? Update(IMsg msg)
    {
        if (msg is WindowSizeMsg w) { _width = w.Width; _height = w.Height; }

        switch (msg)
        {
            case PullRequestsMsg prm:
            {
                if (prm.Err is PartialException pe)
                {
                    _allPRs = ((List<PullRequest>?)pe.PartialData) ?? new List<PullRequest>(prm.Prs);
                    if (_myPRsOnly) return FetchMyPullRequests();
                    if (_asReviewerOnly) return FetchPullRequestsAsReviewer();
                    _list.HandleFetchResult(_allPRs, null);
                    return WithRestore(null);
                }
                _allPRs = new List<PullRequest>(prm.Prs);
                if (_myPRsOnly) return FetchMyPullRequests();
                if (_asReviewerOnly) return FetchPullRequestsAsReviewer();
                _list.HandleFetchResult(prm.Prs, prm.Err);
                return WithRestore(null);
            }

            case MyPullRequestsMsg mm:
            {
                if (mm.Err is not null)
                {
                    if (mm.Err is PartialException pe)
                    {
                        _list.SetItems(((List<PullRequest>?)pe.PartialData) ?? mm.Prs);
                        return WithRestore(null);
                    }
                    _myPRsOnly = false;
                    _list.SetItems(_allPRs);
                    return WithRestore(null);
                }
                _list.SetItems(mm.Prs);
                return WithRestore(null);
            }

            case AsReviewerPullRequestsMsg am:
            {
                if (am.Err is not null)
                {
                    if (am.Err is PartialException pe)
                    {
                        _list.SetItems(((List<PullRequest>?)pe.PartialData) ?? am.Prs);
                        return WithRestore(null);
                    }
                    _asReviewerOnly = false;
                    _list.SetItems(_allPRs);
                    return WithRestore(null);
                }
                _list.SetItems(am.Prs);
                return WithRestore(null);
            }

            case SetPRsMsg sm:
            {
                _allPRs = new List<PullRequest>(sm.Prs);
                if (!_myPRsOnly && !_asReviewerOnly)
                {
                    _list.SetItems(sm.Prs);
                    return WithRestore(null);
                }
                return null;
            }

            case KeyMsg key when _viewMode == PrViewMode.List && !_list.IsSearching:
            {
                if (key.Key == "m")
                {
                    _myPRsOnly = !_myPRsOnly;
                    if (_myPRsOnly)
                    {
                        _asReviewerOnly = false;
                        return FetchMyPullRequests();
                    }
                    _list.SetItems(_allPRs);
                    return null;
                }
                if (key.Key == "A")
                {
                    _asReviewerOnly = !_asReviewerOnly;
                    if (_asReviewerOnly)
                    {
                        _myPRsOnly = false;
                        return FetchPullRequestsAsReviewer();
                    }
                    _list.SetItems(_allPRs);
                    return null;
                }
                break;
            }
        }

        return _viewMode switch
        {
            PrViewMode.Diff => UpdateDiffView(msg),
            PrViewMode.Detail => UpdateDetail(msg),
            _ => UpdateList(msg),
        };
    }

    private Cmd? UpdateList(IMsg msg)
    {
        var cmd = _list.Update(msg);
        _viewMode = _list.GetViewMode() == ViewMode.Detail ? PrViewMode.Detail : PrViewMode.List;
        return cmd;
    }

    private Cmd? UpdateDetail(IMsg msg)
    {
        switch (msg)
        {
            case OpenGeneralCommentsMsg:
                if (_list.Detail is PullRequestDetailAdapter ga)
                {
                    var detail = ga.Model;
                    var pr = detail.GetPr();
                    IAzdoClient? pc = _client?.ClientFor(pr.ProjectName);
                    _diffView = new PullRequestDiffView(pc, pr, detail.GetThreads(), _styles);
                    _diffView.SetSize(_width, _height);
                    _viewMode = PrViewMode.Diff;
                    return _diffView.InitGeneralComments();
                }
                return null;

            case OpenFileDiffMsg fd:
                if (_list.Detail is PullRequestDetailAdapter fa)
                {
                    var detail = fa.Model;
                    var pr = detail.GetPr();
                    IAzdoClient? pc = _client?.ClientFor(pr.ProjectName);
                    _diffView = new PullRequestDiffView(pc, pr, detail.GetThreads(), _styles);
                    _diffView.SetSize(_width, _height);
                    _viewMode = PrViewMode.Diff;
                    return _diffView.InitWithFile(fd.File);
                }
                return null;

            case KeyMsg { Key: "esc" }:
                // Let an open modal (e.g. vote picker) handle esc first.
                if (_list.Detail is PullRequestDetailAdapter ea && ea.Model.IsVotePickerVisible)
                    return _list.Update(msg);
                var escCmd = _list.Update(msg);
                _viewMode = _list.GetViewMode() == ViewMode.Detail ? PrViewMode.Detail : PrViewMode.List;
                return escCmd;
        }

        var cmd = _list.Update(msg);
        _viewMode = _list.GetViewMode() == ViewMode.Detail ? PrViewMode.Detail : PrViewMode.List;
        return cmd;
    }

    private Cmd? UpdateDiffView(IMsg msg)
    {
        if (_diffView is null)
        {
            _viewMode = PrViewMode.Detail;
            return null;
        }

        switch (msg)
        {
            case ExitDiffViewMsg:
                _viewMode = PrViewMode.Detail;
                _diffView = null;
                return null;
            case WindowSizeMsg ws:
                _diffView.SetSize(ws.Width, ws.Height);
                break;
        }

        return _diffView.Update(msg);
    }

    public string View()
        => _viewMode == PrViewMode.Diff && _diffView is not null ? _diffView.View() : _list.View();

    // --- ITabView ---

    public bool IsSearching()
    {
        if (_list.IsSearching) return true;
        if (_viewMode == PrViewMode.Diff && _diffView is not null && _diffView.IsInputActive()) return true;
        return false;
    }

    public bool IsCapturingInput()
    {
        if (_viewMode == PrViewMode.Diff && _diffView is not null && _diffView.IsInputActive()) return true;
        if (_viewMode == PrViewMode.Detail && _list.Detail is PullRequestDetailAdapter a && a.Model.IsVotePickerVisible) return true;
        return false;
    }

    public bool HasContextBar()
    {
        if (_viewMode == PrViewMode.Diff) return true;
        return _list.HasContextBar();
    }

    public IReadOnlyList<ContextItem> GetContextItems()
        => _viewMode == PrViewMode.Diff && _diffView is not null ? _diffView.GetContextItems() : _list.GetContextItems();

    public double GetScrollPercent()
        => _viewMode == PrViewMode.Diff && _diffView is not null ? _diffView.GetScrollPercent() : _list.GetScrollPercent();

    public string GetStatusMessage()
        => _viewMode == PrViewMode.Diff && _diffView is not null ? _diffView.GetStatusMessage() : _list.GetStatusMessage();

    public string FilterLabel()
    {
        if (_myPRsOnly) return "My PRs";
        if (_asReviewerOnly) return "Reviewer";
        return "";
    }

    public string DefaultKeybindings()
    {
        string sep = Style.New().BorderForeground(_styles.Theme.Border).Render("");
        string sepStr = _styles.Description.Foreground(_styles.Theme.Border).Render(" • ");
        var items = new (string Key, string Desc)[]
        {
            ("r", " refresh"),
            ("↑↓", " navigate"),
            ("enter", " details"),
            ("f", " search"),
            ("m", " my PRs"),
            ("A", " as reviewer"),
            ("esc", " back"),
            ("?", " help"),
            ("q", " quit"),
        };
        return string.Join(sepStr,
            items.Select(i => _styles.Key.Render(i.Key) + _styles.Description.Render(i.Desc)));
    }

    // --- IRestorableTab ---

    public int DetailItemId()
    {
        if (_viewMode != PrViewMode.Detail) return 0;
        if (_list.Detail is PullRequestDetailAdapter a) return a.Model.GetPr().Id;
        return 0;
    }

    public void SetPendingDetailRestore(int id)
    {
        _pendingDetailId = id;
        _pendingRestoreHandled = false;
    }

    /// <summary>Attempts to open detail for the pending PR id; marks the intent handled.</summary>
    private Cmd? TryRestoreDetail()
    {
        if (_pendingRestoreHandled || _pendingDetailId == 0) return null;
        int target = _pendingDetailId;
        _pendingDetailId = 0;
        _pendingRestoreHandled = true;

        int idx = _list.FindIndex(pr => pr.Id == target);
        if (idx < 0) return null;
        _list.SetCursor(idx);
        var cmd = _list.OpenSelectedDetail();
        _viewMode = PrViewMode.Detail;
        return cmd;
    }

    private Cmd? WithRestore(Cmd? prev)
    {
        var restoreCmd = TryRestoreDetail();
        if (prev is null) return restoreCmd;
        if (restoreCmd is null) return prev;
        return Commands.Batch(prev, restoreCmd);
    }

    // --- Testing / state accessors ---

    public PrViewMode GetViewMode() => _viewMode;
    public bool IsMyPRsActive() => _myPRsOnly;
    public bool IsAsReviewerActive() => _asReviewerOnly;

    // --- Fetch commands ---

    private Cmd FetchPullRequests() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new PullRequestsMsg(Array.Empty<PullRequest>(), null);
        try
        {
            var prs = await _client.ListPullRequestsAsync(25).ConfigureAwait(false);
            return new PullRequestsMsg(prs, null);
        }
        catch (PartialException pe)
        {
            return new PullRequestsMsg(((List<PullRequest>?)pe.PartialData) ?? new List<PullRequest>(), pe);
        }
        catch (Exception e)
        {
            return new PullRequestsMsg(Array.Empty<PullRequest>(), e);
        }
    });

    private Cmd FetchMyPullRequests() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new MyPullRequestsMsg(Array.Empty<PullRequest>(), null);
        try
        {
            var prs = await _client.ListMyPullRequestsAsync(25).ConfigureAwait(false);
            return new MyPullRequestsMsg(prs, null);
        }
        catch (PartialException pe)
        {
            return new MyPullRequestsMsg(((List<PullRequest>?)pe.PartialData) ?? new List<PullRequest>(), pe);
        }
        catch (Exception e)
        {
            return new MyPullRequestsMsg(Array.Empty<PullRequest>(), e);
        }
    });

    private Cmd FetchPullRequestsAsReviewer() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new AsReviewerPullRequestsMsg(Array.Empty<PullRequest>(), null);
        try
        {
            var prs = await _client.ListPullRequestsAsReviewerAsync(25).ConfigureAwait(false);
            return new AsReviewerPullRequestsMsg(prs, null);
        }
        catch (PartialException pe)
        {
            return new AsReviewerPullRequestsMsg(((List<PullRequest>?)pe.PartialData) ?? new List<PullRequest>(), pe);
        }
        catch (Exception e)
        {
            return new AsReviewerPullRequestsMsg(Array.Empty<PullRequest>(), e);
        }
    });

    // --- Rows / filters / icons ---

    public static List<string[]> PrsToRows(IReadOnlyList<PullRequest> items, StyleSet s)
    {
        var rows = new List<string[]>(items.Count);
        foreach (var pr in items)
        {
            string branchInfo = $"{pr.SourceBranchShortName()} → {pr.TargetBranchShortName()}";
            rows.Add(new[]
            {
                StatusIcon(pr.Status, pr.IsDraft, s),
                pr.Title,
                branchInfo,
                pr.CreatedBy.DisplayName,
                pr.Repository.Name,
                VoteIcon(pr.Reviewers, s),
            });
        }
        return rows;
    }

    public static List<string[]> PrsToRowsMulti(IReadOnlyList<PullRequest> items, StyleSet s)
    {
        var rows = new List<string[]>(items.Count);
        foreach (var pr in items)
        {
            string branchInfo = $"{pr.SourceBranchShortName()} → {pr.TargetBranchShortName()}";
            rows.Add(new[]
            {
                pr.ProjectDisplayName,
                StatusIcon(pr.Status, pr.IsDraft, s),
                pr.Title,
                branchInfo,
                pr.CreatedBy.DisplayName,
                pr.Repository.Name,
                VoteIcon(pr.Reviewers, s),
            });
        }
        return rows;
    }

    public static string StatusIcon(string status, bool isDraft, StyleSet s)
    {
        if (isDraft) return s.Warning.Render("◐ Draft");
        return status.ToLowerInvariant() switch
        {
            "active" => s.Info.Render("● Active"),
            "completed" => s.Success.Render("✓ Merged"),
            "abandoned" => s.Muted.Render("○ Closed"),
            _ => s.Muted.Render(status),
        };
    }

    public static string VoteIcon(IReadOnlyList<Reviewer> reviewers, StyleSet s)
    {
        if (reviewers.Count == 0) return s.Muted.Render("-");

        bool hasRejected = false, hasWaiting = false, hasApprovedWithSuggestions = false, hasApproved = false, hasNoVote = false;
        foreach (var r in reviewers)
        {
            switch (r.Vote)
            {
                case -10: hasRejected = true; break;
                case -5: hasWaiting = true; break;
                case 5: hasApprovedWithSuggestions = true; break;
                case 10: hasApproved = true; break;
                case 0: hasNoVote = true; break;
            }
        }

        int count = reviewers.Count;
        if (hasRejected) return s.Error.Render($"✗ {count}");
        if (hasWaiting) return s.Warning.Render($"◐ {count}");
        if (hasApprovedWithSuggestions) return s.Warning.Render($"~ {count}");
        if (hasApproved) return s.Success.Render($"✓ {count}");
        if (hasNoVote) return s.Muted.Render($"○ {count}");
        return s.Muted.Render($"- {count}");
    }

    public static bool FilterPr(PullRequest pr, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        return pr.Title.ToLowerInvariant().Contains(q)
            || pr.CreatedBy.DisplayName.ToLowerInvariant().Contains(q)
            || pr.Repository.Name.ToLowerInvariant().Contains(q)
            || pr.SourceRefName.ToLowerInvariant().Contains(q)
            || pr.TargetRefName.ToLowerInvariant().Contains(q);
    }

    public static bool FilterPrMulti(PullRequest pr, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        return pr.ProjectDisplayName.ToLowerInvariant().Contains(q)
            || pr.ProjectName.ToLowerInvariant().Contains(q)
            || pr.Title.ToLowerInvariant().Contains(q)
            || pr.CreatedBy.DisplayName.ToLowerInvariant().Contains(q)
            || pr.Repository.Name.ToLowerInvariant().Contains(q)
            || pr.SourceRefName.ToLowerInvariant().Contains(q)
            || pr.TargetRefName.ToLowerInvariant().Contains(q);
    }
}
