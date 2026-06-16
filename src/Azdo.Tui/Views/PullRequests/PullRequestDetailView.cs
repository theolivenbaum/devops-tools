using System.Text;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Diff;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tui.Views.PullRequests;

/// <summary>
/// The PR detail view: description, reviewers/votes, comment-thread counts, and
/// the list of changed files (≈ <c>pullrequests.DetailModel</c>). Selectable items
/// are an optional "General comments" entry (index 0) followed by changed files.
/// </summary>
public sealed class PullRequestDetailView
{
    /// <summary>Seam so tests can intercept browser launches (≈ the package-level <c>openURL</c>).</summary>
    public static Func<string, Exception?> OpenUrl { get; set; } = DefaultOpenUrl;

    private readonly IAzdoClient? _client;
    private readonly PullRequest _pr;
    private List<Thread> _threads = new();
    private List<IterationChange> _changedFiles = new();
    private Dictionary<string, int> _commentCounts = new();
    private int _fileIndex;
    private bool _loading;
    private bool _threadsLoaded;
    private bool _filesLoaded;
    private Exception? _err;
    private int _width;
    private int _height;
    private Viewport? _viewport;
    private bool _ready;
    private string _statusMessage = "";
    private readonly LoadingIndicator _spinner;
    private readonly StyleSet _styles;
    private readonly VotePicker _votePicker;

    public PullRequestDetailView(IAzdoClient? client, PullRequest pr, StyleSet styles)
    {
        _client = client;
        _pr = pr;
        _styles = styles;
        _spinner = new LoadingIndicator(styles);
        _spinner.SetMessage($"Loading PR #{pr.Id}...");
        _votePicker = new VotePicker(styles);
    }

    public Cmd? Init()
    {
        _loading = true;
        _threadsLoaded = false;
        _filesLoaded = false;
        _spinner.SetVisible(true);
        return Commands.Batch(FetchThreads(), FetchChangedFiles(), _spinner.Init());
    }

    public Cmd? Update(IMsg msg)
    {
        // Route input to the vote picker while visible.
        if (_votePicker.IsVisible)
            return _votePicker.Update(msg);

        switch (msg)
        {
            case VoteSelectedMsg vote:
                _loading = true;
                _spinner.SetVisible(true);
                return Commands.Batch(VotePr(vote.Vote), _spinner.Tick());

            case WindowSizeMsg ws:
                _width = ws.Width;
                _height = ws.Height;
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case KeyMsg key:
                switch (key.Key)
                {
                    case "up": case "k": MoveUp(); return null;
                    case "down": case "j": MoveDown(); return null;
                    case "pgup": PageUp(); return null;
                    case "pgdown": PageDown(); return null;
                    case "enter":
                        if (IsGeneralCommentsSelected())
                            return Commands.Of(OpenGeneralCommentsMsg.Instance);
                        int fi = _fileIndex - GeneralCommentsOffset();
                        if (fi >= 0 && fi < _changedFiles.Count)
                            return Commands.Of(new OpenFileDiffMsg(_changedFiles[fi]));
                        return null;
                    case "v":
                        _votePicker.SetSize(_width, _height);
                        _votePicker.Show();
                        return null;
                    case "r":
                        _loading = true;
                        _threadsLoaded = false;
                        _filesLoaded = false;
                        _spinner.SetVisible(true);
                        return Commands.Batch(FetchThreads(), FetchChangedFiles(), _spinner.Tick());
                    case "o":
                        return OpenInBrowser();
                }
                return null;

            case ThreadsMsg tm:
                if (tm.Err is not null) { _err = tm.Err; return null; }
                _threads = ThreadFilter.FilterSystemThreads(tm.Threads);
                _threadsLoaded = true;
                FinishLoading();
                return null;

            case ChangedFilesMsg cf:
                if (cf.Err is not null) { _err = cf.Err; return null; }
                _changedFiles = FilterFileChanges(cf.Changes);
                _fileIndex = 0;
                _filesLoaded = true;
                FinishLoading();
                return null;

            case VoteResultMsg vr:
                if (vr.Err is not null) { _err = vr.Err; return null; }
                _statusMessage = vr.Message;
                _loading = true;
                _spinner.SetVisible(true);
                return Commands.Batch(FetchThreads(), _spinner.Tick());

            case OpenUrlResultMsg ur:
                _statusMessage = ur.Err is not null
                    ? $"Failed to open browser: {ur.Err.Message}"
                    : "Opened in browser";
                return null;
        }

        return null;
    }

    private void FinishLoading()
    {
        if (!_threadsLoaded || !_filesLoaded) return;
        _loading = false;
        _spinner.SetVisible(false);
        _commentCounts = ThreadHelpers.CountCommentsPerFile(_threads);
        if (_ready) UpdateViewportContent();
    }

    public string View()
    {
        if (_votePicker.IsVisible) return _votePicker.View();

        string Wrap(string content) => Style.New().Width(_width).Render(content);

        if (_err is not null)
            return Wrap($"Error: {_err.Message}\n\nPress r to retry, Esc to go back");
        if (_loading)
            return Wrap(_spinner.View());

        var sb = new StringBuilder();
        sb.Append(_styles.Header.Render($"PR #{_pr.Id}: {_pr.Title}"));
        sb.Append('\n');
        sb.Append(_styles.Muted.Render($"{_pr.SourceBranchShortName()} → {_pr.TargetBranchShortName()}"));
        sb.Append('\n');
        int sepWidth = Math.Min(_width - 2, 60);
        if (sepWidth < 1) sepWidth = 60;
        sb.Append(new string('─', sepWidth));
        sb.Append('\n');

        if (_ready && _viewport is not null)
            sb.Append(_viewport.View());

        return Style.New().Width(_width).Render(sb.ToString());
    }

    private void UpdateViewportContent()
    {
        if (_viewport is null) return;
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(_pr.Description))
        {
            sb.Append(_styles.Value.Width(_width).Render(_pr.Description));
            sb.Append("\n\n");
        }

        if (_client is not null)
        {
            var prUrl = BuildPrOverviewUrl(_client.GetOrg(), _client.GetProject(), _pr.Repository.Id, _pr.Id);
            if (prUrl != "")
            {
                sb.Append(Hyperlink(_styles.Link.Render("Go to PR"), prUrl));
                sb.Append("\n\n");
            }
        }

        if (_pr.CreationDate != default)
        {
            sb.Append(_styles.Label.Render("Created: "));
            sb.Append(_pr.CreationDate.ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrEmpty(_pr.CreatedBy.DisplayName))
                sb.Append(" by " + _pr.CreatedBy.DisplayName);
            sb.Append("\n\n");
        }

        if (_pr.Reviewers.Count > 0)
        {
            sb.Append(_styles.Label.Render("Reviewers"));
            sb.Append('\n');
            foreach (var reviewer in _pr.Reviewers)
            {
                string icon = ReviewerVoteIcon(reviewer.Vote, _styles);
                string voteDesc = ReviewerVoteDescription(reviewer.Vote);
                sb.Append($"  {icon} {reviewer.DisplayName} ({_styles.Muted.Render(voteDesc)})\n");
            }
            sb.Append('\n');
        }

        var generalThreads = ThreadHelpers.FilterGeneralThreads(_threads);
        if (generalThreads.Count > 0)
        {
            string generalLine = $"  💬 General comments ({generalThreads.Count})";
            sb.Append(_fileIndex == 0 ? _styles.Selected.Render(generalLine) : _styles.Info.Render(generalLine));
            sb.Append("\n\n");
        }

        sb.Append(_styles.Label.Render($"Changed files ({_changedFiles.Count})"));
        sb.Append('\n');

        if (_changedFiles.Count > 0)
        {
            for (int i = 0; i < _changedFiles.Count; i++)
            {
                sb.Append(RenderFileEntry(_changedFiles[i], i + GeneralCommentsOffset() == _fileIndex));
                sb.Append('\n');
            }
        }
        else
        {
            sb.Append(_styles.Muted.Render("  No changed files"));
            sb.Append('\n');
        }

        _viewport.SetContent(sb.ToString());
    }

    private string RenderFileEntry(IterationChange change, bool selected)
    {
        var (icon, style) = ChangeTypeDisplay(change.ChangeType, _styles);

        string path = change.Item.Path;
        if (change.ChangeType == "rename" && !string.IsNullOrEmpty(change.OriginalPath))
            path = $"{change.OriginalPath} -> {change.Item.Path}";

        string line = $"  {icon} {path}";

        _commentCounts.TryGetValue(change.Item.Path, out int count);
        if (count > 0)
            line += " " + _styles.DiffCommentCount.Render($"({count})");

        return selected ? _styles.Selected.Render(line) : style.Render(line);
    }

    internal static (string Icon, Style Style) ChangeTypeDisplay(string changeType, StyleSet s) => changeType switch
    {
        "add" => ("+", s.Success),
        "edit" => ("~", s.Info),
        "delete" => ("-", s.Error),
        "rename" => ("→", s.Warning),
        _ => ("?", s.Muted),
    };

    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;

        const int headerLines = 3; // title + branch + separator
        int viewportHeight = Math.Max(1, height - headerLines);

        if (!_ready)
        {
            _viewport = new Viewport(width, viewportHeight);
            _ready = true;
        }
        else if (_viewport is not null)
        {
            _viewport.Width = width;
            _viewport.Height = viewportHeight;
        }

        UpdateViewportContent();
    }

    private void EnsureSelectedVisible()
    {
        if (!_ready || _viewport is null || TotalSelectableItems() == 0) return;
        int selectedLine = GetSelectedItemLineOffset();
        if (selectedLine < _viewport.YOffset)
            _viewport.SetYOffset(selectedLine);
        else if (selectedLine >= _viewport.YOffset + _viewport.Height)
            _viewport.SetYOffset(selectedLine - _viewport.Height + 1);
    }

    /// <summary>Sets threads directly (test helper); filters system threads.</summary>
    public void SetThreads(IReadOnlyList<Thread> threads)
    {
        _threads = ThreadFilter.FilterSystemThreads(threads);
        _threadsLoaded = true;
        _commentCounts = ThreadHelpers.CountCommentsPerFile(_threads);
        if (_ready) UpdateViewportContent();
    }

    /// <summary>Sets changed files directly (test helper).</summary>
    public void SetChangedFiles(IReadOnlyList<IterationChange> files)
    {
        _changedFiles = FilterFileChanges(files);
        _fileIndex = 0;
        _filesLoaded = true;
        if (_ready) UpdateViewportContent();
    }

    public void MoveUp()
    {
        if (!_ready || _viewport is null) return;
        if (_fileIndex > 0)
        {
            _fileIndex--;
            int saved = _viewport.YOffset;
            UpdateViewportContent();
            _viewport.SetYOffset(saved);
            EnsureSelectedVisible();
        }
        else
        {
            _viewport.LineUp(1);
        }
    }

    public void MoveDown()
    {
        if (!_ready || _viewport is null) return;
        int maxIndex = TotalSelectableItems() - 1;
        if (maxIndex >= 0 && _fileIndex < maxIndex)
        {
            _fileIndex++;
            int saved = _viewport.YOffset;
            UpdateViewportContent();
            _viewport.SetYOffset(saved);
            EnsureSelectedVisible();
        }
        else
        {
            _viewport.LineDown(1);
        }
    }

    public void PageUp()
    {
        if (!_ready || _viewport is null) return;
        _viewport.HalfViewUp();
        UpdateSelectionFromViewport();
    }

    public void PageDown()
    {
        if (!_ready || _viewport is null) return;
        _viewport.HalfViewDown();
        UpdateSelectionFromViewport();
    }

    private void UpdateSelectionFromViewport()
    {
        if (_viewport is null) return;
        int total = TotalSelectableItems();
        if (total == 0) return;

        int targetLine = _viewport.YOffset + 2;
        int bestIdx = 0;
        for (int i = 0; i < total; i++)
        {
            _fileIndex = i;
            if (GetSelectedItemLineOffset() <= targetLine) bestIdx = i;
        }
        _fileIndex = bestIdx;

        int saved = _viewport.YOffset;
        UpdateViewportContent();
        _viewport.SetYOffset(saved);
    }

    private int GetSelectedItemLineOffset()
    {
        int lineOffset = 0;
        if (!string.IsNullOrEmpty(_pr.Description))
            lineOffset += _pr.Description.Count(c => c == '\n') + 2;
        if (_client is not null && !string.IsNullOrEmpty(_pr.Repository.Id))
            lineOffset += 2;
        if (_pr.CreationDate != default)
            lineOffset += 2;
        if (_pr.Reviewers.Count > 0)
            lineOffset += 1 + _pr.Reviewers.Count + 1;

        int gcOffset = GeneralCommentsOffset();
        if (gcOffset > 0 && _fileIndex == 0)
            return lineOffset;

        if (gcOffset > 0) lineOffset += 2;
        lineOffset += 1; // "Changed files (N)" header

        int fi = _fileIndex - gcOffset;
        lineOffset += fi;
        return lineOffset;
    }

    private int GeneralCommentsOffset()
        => ThreadHelpers.FilterGeneralThreads(_threads).Count > 0 ? 1 : 0;

    private bool IsGeneralCommentsSelected()
        => GeneralCommentsOffset() > 0 && _fileIndex == 0;

    private int TotalSelectableItems()
        => GeneralCommentsOffset() + _changedFiles.Count;

    public int SelectedIndex => _fileIndex;

    public IterationChange? SelectedFile()
    {
        int fi = _fileIndex - GeneralCommentsOffset();
        if (fi < 0 || fi >= _changedFiles.Count) return null;
        return _changedFiles[fi];
    }

    public IReadOnlyList<ContextItem> GetContextItems() => new[]
    {
        new ContextItem("enter", "open"),
        new ContextItem("↑↓", "navigate"),
        new ContextItem("v", "vote"),
        new ContextItem("o", "open in browser"),
        new ContextItem("r", "refresh"),
    };

    private Cmd? OpenInBrowser()
    {
        if (_client is null)
        {
            _statusMessage = "Cannot open: no Azure DevOps client";
            return null;
        }
        var url = BuildPrOverviewUrl(_client.GetOrg(), _client.GetProject(), _pr.Repository.Id, _pr.Id);
        if (url == "")
        {
            _statusMessage = "Cannot open: missing organization, project, or repository";
            return null;
        }
        return Commands.FromFunc(() => new OpenUrlResultMsg(OpenUrl(url)));
    }

    public IReadOnlyList<Thread> GetThreads() => _threads;
    public IReadOnlyList<IterationChange> GetChangedFiles() => _changedFiles;
    public double GetScrollPercent() => !_ready || _viewport is null ? 0 : _viewport.ScrollPercent() * 100;
    public string GetStatusMessage() => _statusMessage;
    public PullRequest GetPr() => _pr;

    /// <summary>True if the vote picker modal is open (≈ <c>m.votePicker.IsVisible()</c>).</summary>
    public bool IsVotePickerVisible => _votePicker.IsVisible;

    // --- Commands ---

    private Cmd FetchThreads() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new ThreadsMsg(Array.Empty<Thread>(), null);
        try
        {
            var threads = await _client.GetPRThreadsAsync(_pr.Repository.Id, _pr.Id).ConfigureAwait(false);
            return new ThreadsMsg(threads, null);
        }
        catch (Exception e)
        {
            return new ThreadsMsg(Array.Empty<Thread>(), e);
        }
    });

    private Cmd FetchChangedFiles() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new ChangedFilesMsg(Array.Empty<IterationChange>(), null);
        try
        {
            var iterations = await _client.GetPRIterationsAsync(_pr.Repository.Id, _pr.Id).ConfigureAwait(false);
            if (iterations.Count == 0)
                return new ChangedFilesMsg(Array.Empty<IterationChange>(), null);
            int latestId = iterations[^1].Id;
            var changes = await _client.GetPRIterationChangesAsync(_pr.Repository.Id, _pr.Id, latestId).ConfigureAwait(false);
            return new ChangedFilesMsg(changes, null);
        }
        catch (Exception e)
        {
            return new ChangedFilesMsg(Array.Empty<IterationChange>(), e);
        }
    });

    private Cmd VotePr(int vote) => Commands.FromAsync(async () =>
    {
        if (_client is null) return new VoteResultMsg("", null);
        try
        {
            await _client.VotePullRequestAsync(_pr.Repository.Id, _pr.Id, vote).ConfigureAwait(false);
            return new VoteResultMsg(VoteResultDescription(vote), null);
        }
        catch (Exception e)
        {
            return new VoteResultMsg("", e);
        }
    });

    // --- Helpers ---

    internal static List<IterationChange> FilterFileChanges(IReadOnlyList<IterationChange> changes)
    {
        var filtered = new List<IterationChange>(changes.Count);
        foreach (var c in changes)
        {
            if (string.IsNullOrEmpty(c.Item.Path) || c.Item.Path == "/") continue;
            if (c.Item.GitObjectType == "tree") continue;
            filtered.Add(c);
        }
        return filtered;
    }

    internal static string Hyperlink(string text, string url)
        => url == "" ? text : $"\x1b]8;;{url}\x07{text}\x1b]8;;\x07";

    internal static string BuildPrOverviewUrl(string org, string project, string repoId, int prId)
    {
        if (org == "" || project == "" || repoId == "") return "";
        return $"https://dev.azure.com/{org}/{project}/_git/{repoId}/pullrequest/{prId}";
    }

    internal static string ReviewerVoteIcon(int vote, StyleSet s) => vote switch
    {
        10 => s.Success.Render("✓"),
        5 => s.Warning.Render("~"),
        0 => s.Muted.Render("○"),
        -5 => s.Warning.Render("◐"),
        -10 => s.Error.Render("✗"),
        _ => s.Muted.Render("?"),
    };

    internal static string ReviewerVoteDescription(int vote) => vote switch
    {
        10 => "Approved",
        5 => "Approved with suggestions",
        0 => "No vote",
        -5 => "Waiting for author",
        -10 => "Rejected",
        _ => "Unknown",
    };

    internal static string VoteResultDescription(int vote) => vote switch
    {
        Vote.Approve => "PR approved",
        Vote.ApproveWithSuggestions => "PR approved with suggestions",
        Vote.WaitForAuthor => "Waiting for author",
        Vote.Reject => "PR rejected",
        Vote.NoVote => "Vote reset",
        _ => "Vote submitted",
    };

    private static Exception? DefaultOpenUrl(string url)
    {
        // NOTE: No browser package exists in the C# port; default is a no-op that
        // reports success. The App layer can override OpenUrl with a real launcher.
        return null;
    }
}

/// <summary>
/// Wraps <see cref="PullRequestDetailView"/> to satisfy <see cref="IDetailView"/>
/// (≈ <c>pullrequests.detailAdapter</c>).
/// </summary>
public sealed class PullRequestDetailAdapter : IDetailView
{
    public PullRequestDetailView Model { get; }

    public PullRequestDetailAdapter(PullRequestDetailView model) => Model = model;

    public Cmd? Update(IMsg msg) => Model.Update(msg);
    public string View() => Model.View();
    public void SetSize(int width, int height) => Model.SetSize(width, height);
    public IReadOnlyList<ContextItem> GetContextItems() => Model.GetContextItems();
    public double GetScrollPercent() => Model.GetScrollPercent();
    public string GetStatusMessage() => Model.GetStatusMessage();
}
