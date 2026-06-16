using System.Text;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Diff;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;
using CoreDiffLine = Azdo.Core.Diff.DiffLine;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tui.Views.PullRequests;

/// <summary>Sub-view within the diff viewer (≈ <c>DiffViewMode</c>).</summary>
public enum DiffViewMode { FileList, FileView }

/// <summary>Active text-input kind (≈ <c>InputMode</c>).</summary>
public enum InputMode { None, NewComment, Reply }

/// <summary>The type of a flattened diff display line (≈ <c>diffLineType</c>).</summary>
internal enum DiffLineKind { Context, Added, Removed, HunkHeader, Comment, FileHeader }

/// <summary>A flattened rendering line in the diff view (≈ <c>diffLine</c>).</summary>
internal sealed class DiffRenderLine
{
    public DiffLineKind Type;
    public string Content = "";
    public int OldNum;
    public int NewNum;
    public int ThreadId;
    public int CommentIdx;
    public string ThreadStatus = "";
}

/// <summary>
/// The diff viewer: a list of changed files plus a per-file diff with inline
/// comment threads, and create/reply/resolve actions (≈ <c>pullrequests.DiffModel</c>).
/// </summary>
public sealed class PullRequestDiffView
{
    private readonly IAzdoClient? _client;
    private readonly PullRequest _pr;
    private List<Thread> _threads;

    private List<Thread> _generalThreads;
    private bool _viewingGeneralComments;

    private List<IterationChange> _changedFiles = new();
    private int _fileIndex;

    private IterationChange? _currentFile;
    private FileDiff? _currentDiff;
    private Dictionary<int, List<Thread>> _fileThreads = new();

    private List<DiffRenderLine> _diffLines = new();
    private int _selectedLine;

    private InputMode _inputMode = InputMode.None;
    private readonly TextInput _textInput;
    private int _replyThreadId;

    private DiffViewMode _viewMode = DiffViewMode.FileList;
    private Viewport? _viewport;
    private int _width;
    private int _height;
    private bool _ready;
    private bool _loading;
    private Exception? _err;
    private string _statusMessage = "";
    private readonly LoadingIndicator _spinner;
    private readonly StyleSet _styles;

    public PullRequestDiffView(IAzdoClient? client, PullRequest pr, IReadOnlyList<Thread> threads, StyleSet styles)
    {
        _client = client;
        _pr = pr;
        _threads = threads.ToList();
        _generalThreads = ThreadHelpers.FilterGeneralThreads(_threads);
        _styles = styles;
        _spinner = new LoadingIndicator(styles);
        _spinner.SetMessage("Loading changed files...");
        _textInput = new TextInput { Prompt = "> ", CharLimit = 500 };
    }

    public Cmd? Init()
    {
        _loading = true;
        _spinner.SetVisible(true);
        return Commands.Batch(FetchChangedFiles(), _spinner.Init());
    }

    public Cmd? InitGeneralComments()
    {
        _viewingGeneralComments = true;
        _viewMode = DiffViewMode.FileView;
        _selectedLine = 0;
        BuildGeneralCommentLines();
        if (_ready) UpdateDiffViewport();
        return FetchChangedFiles();
    }

    public Cmd? InitWithFile(IterationChange file)
    {
        _currentFile = file;
        _loading = true;
        _spinner.SetMessage("Loading diff...");
        _spinner.SetVisible(true);
        return Commands.Batch(FetchChangedFiles(), FetchFileDiff(file), _spinner.Init());
    }

    public Cmd? Update(IMsg msg)
    {
        switch (msg)
        {
            case WindowSizeMsg ws:
                _width = ws.Width;
                _height = ws.Height;
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case ChangedFilesMsg cf:
                if (cf.Err is not null)
                {
                    _loading = false;
                    _spinner.SetVisible(false);
                    _err = cf.Err;
                    return null;
                }
                _changedFiles = PullRequestDetailView.FilterFileChanges(cf.Changes);
                _fileIndex = 0;
                // If InitWithFile is in flight, currentFile is set; keep loading
                // until fileDiffMsg arrives to avoid flashing the file list.
                if (_currentFile is null)
                {
                    _loading = false;
                    _spinner.SetVisible(false);
                    if (_viewMode == DiffViewMode.FileList) UpdateFileListViewport();
                }
                return null;

            case FileDiffMsg fd:
                _loading = false;
                _spinner.SetVisible(false);
                if (fd.Err is not null) { _err = fd.Err; return null; }
                _currentDiff = fd.Diff;
                _fileThreads = fd.FileThreads ?? new Dictionary<int, List<Thread>>();
                _viewMode = DiffViewMode.FileView;
                _selectedLine = 0;
                BuildDiffLines();
                UpdateDiffViewport();
                return null;

            case CommentResultMsg cr:
                if (cr.Err is not null)
                {
                    _statusMessage = $"Error: {cr.Err.Message}";
                    return null;
                }
                _statusMessage = cr.Message;
                return RefreshThreads();

            case ThreadsRefreshMsg tr:
                if (tr.Err is null)
                {
                    _threads = tr.Threads.ToList();
                    _generalThreads = ThreadHelpers.FilterGeneralThreads(_threads);
                    if (_viewMode == DiffViewMode.FileView && _viewingGeneralComments)
                    {
                        BuildGeneralCommentLines();
                        UpdateDiffViewport();
                    }
                    else if (_viewMode == DiffViewMode.FileView && _currentFile is not null)
                    {
                        _fileThreads = ThreadHelpers.MapThreadsToLines(_threads, _currentFile.Item.Path);
                        BuildDiffLines();
                        UpdateDiffViewport();
                    }
                }
                return null;

            case KeyMsg key:
                if (_inputMode != InputMode.None) return UpdateInput(key);
                return _viewMode switch
                {
                    DiffViewMode.FileList => UpdateFileList(key),
                    DiffViewMode.FileView => UpdateDiffView(key),
                    _ => null,
                };
        }

        return null;
    }

    private Cmd? UpdateFileList(KeyMsg msg)
    {
        int maxIndex = FileListItemCount() - 1;
        switch (msg.Key)
        {
            case "up": case "k":
                if (_fileIndex > 0) { _fileIndex--; UpdateFileListViewport(); }
                break;
            case "down": case "j":
                if (_fileIndex < maxIndex) { _fileIndex++; UpdateFileListViewport(); }
                break;
            case "pgup":
                _fileIndex -= _viewport?.Height ?? 1;
                if (_fileIndex < 0) _fileIndex = 0;
                UpdateFileListViewport();
                break;
            case "pgdown":
                _fileIndex += _viewport?.Height ?? 1;
                if (_fileIndex > maxIndex) _fileIndex = maxIndex;
                UpdateFileListViewport();
                break;
            case "enter":
                if (IsGeneralCommentsSelected())
                {
                    _viewingGeneralComments = true;
                    _viewMode = DiffViewMode.FileView;
                    _selectedLine = 0;
                    BuildGeneralCommentLines();
                    UpdateDiffViewport();
                    return null;
                }
                int fi = SelectedFileIndex();
                if (fi >= 0 && fi < _changedFiles.Count)
                {
                    var change = _changedFiles[fi];
                    _currentFile = change;
                    _loading = true;
                    _spinner.SetMessage("Loading diff...");
                    _spinner.SetVisible(true);
                    return Commands.Batch(FetchFileDiff(change), _spinner.Tick());
                }
                break;
            case "r":
                _loading = true;
                _spinner.SetMessage("Refreshing...");
                _spinner.SetVisible(true);
                _err = null;
                return Commands.Batch(FetchChangedFiles(), _spinner.Tick());
            case "esc":
                return Commands.Of(ExitDiffViewMsg.Instance);
        }
        return null;
    }

    private Cmd? UpdateDiffView(KeyMsg msg)
    {
        switch (msg.Key)
        {
            case "up": case "k":
                if (_selectedLine > 0) { _selectedLine--; UpdateDiffViewport(); EnsureDiffLineVisible(); }
                break;
            case "down": case "j":
                if (_selectedLine < _diffLines.Count - 1) { _selectedLine++; UpdateDiffViewport(); EnsureDiffLineVisible(); }
                break;
            case "pgup":
                _selectedLine -= _viewport?.Height ?? 1;
                if (_selectedLine < 0) _selectedLine = 0;
                UpdateDiffViewport(); EnsureDiffLineVisible();
                break;
            case "pgdown":
                _selectedLine += _viewport?.Height ?? 1;
                if (_selectedLine >= _diffLines.Count) _selectedLine = _diffLines.Count - 1;
                if (_selectedLine < 0) _selectedLine = 0;
                UpdateDiffViewport(); EnsureDiffLineVisible();
                break;
            case "c":
                if (_viewingGeneralComments)
                {
                    StartInput(InputMode.NewComment, "New comment...");
                    return null;
                }
                var line = CurrentDiffLine();
                if (line is not null &&
                    (line.Type == DiffLineKind.Added || line.Type == DiffLineKind.Context || line.Type == DiffLineKind.Removed))
                {
                    StartInput(InputMode.NewComment, "New comment...");
                }
                break;
            case "p":
                int replyThread = FindNearestThread();
                if (replyThread > 0)
                {
                    _replyThreadId = replyThread;
                    StartInput(InputMode.Reply, "Reply...");
                }
                break;
            case "x":
                int resolveThread = FindNearestThread();
                if (resolveThread > 0) return ResolveThread(resolveThread);
                break;
            case "n":
                JumpToNextComment(1);
                UpdateDiffViewport(); EnsureDiffLineVisible();
                break;
            case "N":
                JumpToNextComment(-1);
                UpdateDiffViewport(); EnsureDiffLineVisible();
                break;
            case "esc":
                if (_viewingGeneralComments) _viewingGeneralComments = false;
                return Commands.Of(ExitDiffViewMsg.Instance);
        }
        return null;
    }

    private void StartInput(InputMode mode, string placeholder)
    {
        _inputMode = mode;
        _textInput.Value = "";
        _textInput.Placeholder = placeholder;
        _textInput.Focus();
    }

    private Cmd? UpdateInput(KeyMsg msg)
    {
        switch (msg.Key)
        {
            case "esc":
                _inputMode = InputMode.None;
                _textInput.Blur();
                return null;
            case "enter":
                string content = _textInput.Value.Trim();
                if (content == "") return null;
                _textInput.Blur();
                var mode = _inputMode;
                _inputMode = InputMode.None;
                switch (mode)
                {
                    case InputMode.NewComment:
                        if (_viewingGeneralComments)
                            return CreateGeneralComment(content);
                        var line = CurrentDiffLine();
                        if (line is not null && _currentFile is not null)
                        {
                            int lineNum = line.NewNum != 0 ? line.NewNum : line.OldNum;
                            return CreateCodeComment(_currentFile.Item.Path, lineNum, content);
                        }
                        break;
                    case InputMode.Reply:
                        if (_replyThreadId > 0)
                            return ReplyToThread(_replyThreadId, content);
                        break;
                }
                return null;
        }

        _textInput.HandleKey(msg);
        return null;
    }

    public string View()
    {
        if (_err is not null)
            return Style.New().Width(_width).Render($"Error: {_err.Message}\n\nPress r to retry, Esc to go back");
        if (_loading)
            return Style.New().Width(_width).Render(_spinner.View());

        return _viewMode switch
        {
            DiffViewMode.FileList => Style.New().Width(_width).Render(ViewFileList()),
            DiffViewMode.FileView => Style.New().Width(_width).Render(ViewFileDiff()),
            _ => "",
        };
    }

    private string ViewFileList()
    {
        if (!_ready || _viewport is null) return "";
        var sb = new StringBuilder();
        sb.Append(_styles.Header.Render($"Changed files ({_changedFiles.Count})"));
        sb.Append('\n');
        sb.Append(_viewport.View());
        return sb.ToString();
    }

    private string ViewFileDiff()
    {
        if (!_ready || _viewport is null) return "";
        var sb = new StringBuilder();

        if (_viewingGeneralComments)
        {
            sb.Append(_styles.DiffHeader.Render(" General comments "));
            sb.Append('\n');
        }
        else if (_currentFile is not null)
        {
            sb.Append(_styles.DiffHeader.Render($" {_currentFile.Item.Path} "));
            sb.Append('\n');
        }

        sb.Append(_viewport.View());

        if (_inputMode != InputMode.None)
        {
            sb.Append('\n');
            sb.Append(_textInput.View());
        }

        return sb.ToString();
    }

    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;

        int headerLines = 1;
        int viewportHeight = height - headerLines;
        if (_inputMode != InputMode.None) viewportHeight--;
        if (viewportHeight < 1) viewportHeight = 1;

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

        if (_viewMode == DiffViewMode.FileList) UpdateFileListViewport();
        else UpdateDiffViewport();
    }

    public IReadOnlyList<ContextItem> GetContextItems()
    {
        if (_inputMode != InputMode.None)
            return new[] { new ContextItem("enter", "submit"), new ContextItem("esc", "cancel") };

        return _viewMode switch
        {
            DiffViewMode.FileList => new[]
            {
                new ContextItem("pgup/pgdn", "page"),
                new ContextItem("enter", "open"),
            },
            DiffViewMode.FileView => new[]
            {
                new ContextItem("c", "comment"),
                new ContextItem("p", "reply"),
                new ContextItem("x", "resolve"),
                new ContextItem("n/N", "next/prev comment"),
            },
            _ => Array.Empty<ContextItem>(),
        };
    }

    public double GetScrollPercent() => !_ready || _viewport is null ? 0 : _viewport.ScrollPercent() * 100;
    public string GetStatusMessage() => _statusMessage;

    /// <summary>True when a comment/reply text input is active (≈ <c>IsInputActive</c>).</summary>
    public bool IsInputActive() => _inputMode != InputMode.None;

    public DiffViewMode GetViewMode() => _viewMode;

    // --- Rendering helpers ---

    private void UpdateFileListViewport()
    {
        if (!_ready || _viewport is null) return;
        var sb = new StringBuilder();

        string generalLabel = $"  💬 General comments ({_generalThreads.Count})";
        sb.Append(_fileIndex == 0 ? _styles.Selected.Render(generalLabel) : _styles.Info.Render(generalLabel));

        for (int i = 0; i < _changedFiles.Count; i++)
        {
            var change = _changedFiles[i];
            sb.Append('\n');
            var (icon, style) = PullRequestDetailView.ChangeTypeDisplay(change.ChangeType, _styles);
            string line = $"  {icon} {change.Item.Path}";
            if (change.ChangeType == "rename" && !string.IsNullOrEmpty(change.OriginalPath))
                line = $"  {icon} {change.OriginalPath} -> {change.Item.Path}";
            sb.Append(i + 1 == _fileIndex ? _styles.Selected.Render(line) : style.Render(line));
        }

        if (_changedFiles.Count == 0)
        {
            sb.Append('\n');
            sb.Append(_styles.Muted.Render("  No changed files"));
        }

        _viewport.SetContent(sb.ToString());
        EnsureFileIndexVisible();
    }

    private void EnsureFileIndexVisible()
    {
        if (!_ready || _viewport is null || _changedFiles.Count == 0) return;
        if (_fileIndex < _viewport.YOffset)
            _viewport.SetYOffset(_fileIndex);
        else if (_fileIndex >= _viewport.YOffset + _viewport.Height)
            _viewport.SetYOffset(_fileIndex - _viewport.Height + 1);
    }

    private void BuildDiffLines()
    {
        _diffLines = new List<DiffRenderLine>();
        if (_currentDiff is null) return;

        foreach (var hunk in _currentDiff.Hunks)
        {
            _diffLines.Add(new DiffRenderLine
            {
                Type = DiffLineKind.HunkHeader,
                Content = $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@",
            });

            foreach (var line in hunk.Lines)
            {
                var kind = line.Type switch
                {
                    LineType.Added => DiffLineKind.Added,
                    LineType.Removed => DiffLineKind.Removed,
                    _ => DiffLineKind.Context,
                };

                _diffLines.Add(new DiffRenderLine
                {
                    Type = kind,
                    Content = line.Content,
                    OldNum = line.OldNum,
                    NewNum = line.NewNum,
                });

                int lineNum = line.NewNum != 0 ? line.NewNum : line.OldNum;
                if (line.Type != LineType.Removed && _fileThreads.TryGetValue(lineNum, out var threads))
                {
                    foreach (var thread in threads)
                    {
                        for (int ci = 0; ci < thread.Comments.Count; ci++)
                        {
                            var comment = thread.Comments[ci];
                            string ts = comment.PublishedDate.ToString("yyyy-MM-dd HH:mm");
                            _diffLines.Add(new DiffRenderLine
                            {
                                Type = DiffLineKind.Comment,
                                Content = $"@[{comment.Author.DisplayName}] ({ts}): {comment.Content}",
                                ThreadId = thread.Id,
                                CommentIdx = ci,
                                ThreadStatus = thread.Status,
                            });
                        }
                    }
                    _fileThreads.Remove(lineNum);
                }
            }
        }
    }

    private bool IsGeneralCommentsSelected() => _fileIndex == 0;
    private int FileListItemCount() => 1 + _changedFiles.Count;
    private int SelectedFileIndex() => _fileIndex - 1;

    private void BuildGeneralCommentLines()
    {
        _diffLines = new List<DiffRenderLine>();
        for (int ti = 0; ti < _generalThreads.Count; ti++)
        {
            var thread = _generalThreads[ti];
            if (ti > 0)
                _diffLines.Add(new DiffRenderLine { Type = DiffLineKind.HunkHeader, Content = "───" });

            for (int ci = 0; ci < thread.Comments.Count; ci++)
            {
                var comment = thread.Comments[ci];
                string ts = comment.PublishedDate.ToString("yyyy-MM-dd HH:mm");
                _diffLines.Add(new DiffRenderLine
                {
                    Type = DiffLineKind.Comment,
                    Content = $"@[{comment.Author.DisplayName}] ({ts}): {comment.Content}",
                    ThreadId = thread.Id,
                    CommentIdx = ci,
                    ThreadStatus = thread.Status,
                });
            }
        }
    }

    private void UpdateDiffViewport()
    {
        if (!_ready || _viewport is null) return;
        var sb = new StringBuilder();
        for (int i = 0; i < _diffLines.Count; i++)
        {
            sb.Append(RenderDiffLine(_diffLines[i], i == _selectedLine));
            if (i < _diffLines.Count - 1) sb.Append('\n');
        }
        if (_diffLines.Count == 0)
            sb.Append(_styles.Muted.Render("  No changes"));
        _viewport.SetContent(sb.ToString());
    }

    private string RenderDiffLine(DiffRenderLine line, bool selected)
    {
        string result;
        switch (line.Type)
        {
            case DiffLineKind.HunkHeader:
                result = _styles.DiffHunkHeader.Render(line.Content);
                break;
            case DiffLineKind.Context:
            {
                string oldNum = line.OldNum.ToString().PadLeft(4);
                string newNum = line.NewNum.ToString().PadLeft(4);
                string gutter = _styles.DiffLineNum.Render(oldNum) + " " + _styles.DiffLineNum.Render(newNum);
                result = gutter + "  " + _styles.DiffContext.Render(line.Content);
                break;
            }
            case DiffLineKind.Added:
            {
                string oldNum = "    ";
                string newNum = line.NewNum.ToString().PadLeft(4);
                string gutter = _styles.DiffLineNum.Render(oldNum) + " " + _styles.DiffLineNum.Render(newNum);
                result = gutter + _styles.DiffAdded.Render(" +" + line.Content);
                break;
            }
            case DiffLineKind.Removed:
            {
                string oldNum = line.OldNum.ToString().PadLeft(4);
                string newNum = "    ";
                string gutter = _styles.DiffLineNum.Render(oldNum) + " " + _styles.DiffLineNum.Render(newNum);
                result = gutter + _styles.DiffRemoved.Render(" -" + line.Content);
                break;
            }
            case DiffLineKind.Comment:
            {
                bool isResolved = line.ThreadStatus is "fixed" or "wontFix" or "closed";
                string firstIndent, contIndent;
                if (line.CommentIdx > 0) { firstIndent = "  └ "; contIndent = "    "; }
                else if (isResolved) { firstIndent = ""; contIndent = "           "; }
                else { firstIndent = ""; contIndent = ""; }

                var contentLines = line.Content.Split('\n');
                for (int i = 0; i < contentLines.Length; i++)
                    contentLines[i] = (i == 0 ? firstIndent : contIndent) + contentLines[i];
                string rendered = _styles.Info.Render(string.Join("\n", contentLines));
                result = isResolved && line.CommentIdx == 0
                    ? _styles.DiffCommentResolved.Render("[Resolved]") + " " + rendered
                    : rendered;
                break;
            }
            case DiffLineKind.FileHeader:
                result = _styles.DiffHeader.Render(line.Content);
                break;
            default:
                result = line.Content;
                break;
        }

        if (selected) result = _styles.Selected.Render(result);
        return result;
    }

    private int VisualLineForDiffLine(int idx)
    {
        int vis = 0;
        for (int i = 0; i < idx && i < _diffLines.Count; i++)
        {
            vis++; // line separator between entries
            if (_diffLines[i].Type == DiffLineKind.Comment)
                vis += _diffLines[i].Content.Count(c => c == '\n');
        }
        return vis;
    }

    private void EnsureDiffLineVisible()
    {
        if (!_ready || _viewport is null || _diffLines.Count == 0) return;
        int visLine = VisualLineForDiffLine(_selectedLine);
        if (visLine < _viewport.YOffset)
            _viewport.SetYOffset(visLine);
        else if (visLine >= _viewport.YOffset + _viewport.Height)
            _viewport.SetYOffset(visLine - _viewport.Height + 1);
    }

    private DiffRenderLine? CurrentDiffLine()
        => _selectedLine < 0 || _selectedLine >= _diffLines.Count ? null : _diffLines[_selectedLine];

    private int FindNearestThread()
    {
        if (_diffLines.Count == 0) return 0;
        for (int i = _selectedLine; i >= 0 && i < _diffLines.Count; i--)
            if (_diffLines[i].Type == DiffLineKind.Comment && _diffLines[i].ThreadId > 0)
                return _diffLines[i].ThreadId;
        for (int i = _selectedLine; i >= 0 && i < _diffLines.Count; i++)
            if (_diffLines[i].Type == DiffLineKind.Comment && _diffLines[i].ThreadId > 0)
                return _diffLines[i].ThreadId;
        return 0;
    }

    private void JumpToNextComment(int direction)
    {
        if (_diffLines.Count == 0) return;
        for (int i = _selectedLine + direction; i >= 0 && i < _diffLines.Count; i += direction)
        {
            if (_diffLines[i].Type == DiffLineKind.Comment) { _selectedLine = i; return; }
        }
    }

    // --- Commands ---

    private Cmd FetchChangedFiles() => Commands.FromAsync(async () =>
    {
        if (_client is null)
            return new ChangedFilesMsg(Array.Empty<IterationChange>(), new InvalidOperationException("no client available"));
        try
        {
            var iterations = await _client.GetPRIterationsAsync(_pr.Repository.Id, _pr.Id).ConfigureAwait(false);
            if (iterations.Count == 0) return new ChangedFilesMsg(Array.Empty<IterationChange>(), null);
            int latestId = iterations[^1].Id;
            var changes = await _client.GetPRIterationChangesAsync(_pr.Repository.Id, _pr.Id, latestId).ConfigureAwait(false);
            return new ChangedFilesMsg(changes, null);
        }
        catch (Exception e)
        {
            return new ChangedFilesMsg(Array.Empty<IterationChange>(), e);
        }
    });

    private Cmd FetchFileDiff(IterationChange change) => Commands.FromAsync(async () =>
    {
        if (_client is null)
            return new FileDiffMsg(null, null, new InvalidOperationException("no client available"));
        try
        {
            string targetBranch = _pr.TargetBranchShortName();
            string sourceBranch = _pr.SourceBranchShortName();
            string oldContent = "", newContent = "";

            switch (change.ChangeType)
            {
                case "add":
                    newContent = await _client.GetFileContentAsync(_pr.Repository.Id, change.Item.Path, sourceBranch).ConfigureAwait(false);
                    break;
                case "delete":
                    oldContent = await _client.GetFileContentAsync(_pr.Repository.Id, change.Item.Path, targetBranch).ConfigureAwait(false);
                    break;
                case "rename":
                    string oldPath = string.IsNullOrEmpty(change.OriginalPath) ? change.Item.Path : change.OriginalPath;
                    oldContent = await _client.GetFileContentAsync(_pr.Repository.Id, oldPath, targetBranch).ConfigureAwait(false);
                    newContent = await _client.GetFileContentAsync(_pr.Repository.Id, change.Item.Path, sourceBranch).ConfigureAwait(false);
                    break;
                default: // "edit"
                    oldContent = await _client.GetFileContentAsync(_pr.Repository.Id, change.Item.Path, targetBranch).ConfigureAwait(false);
                    newContent = await _client.GetFileContentAsync(_pr.Repository.Id, change.Item.Path, sourceBranch).ConfigureAwait(false);
                    break;
            }

            var hunks = DiffEngine.ComputeDiff(oldContent, newContent, 5);
            var fileDiff = new FileDiff
            {
                Path = change.Item.Path,
                ChangeType = change.ChangeType,
                OldPath = change.OriginalPath,
                Hunks = hunks,
            };
            var fileThreads = ThreadHelpers.MapThreadsToLines(_threads, change.Item.Path);
            return new FileDiffMsg(fileDiff, fileThreads, null);
        }
        catch (Exception e)
        {
            return new FileDiffMsg(null, null, e);
        }
    });

    private Cmd CreateCodeComment(string filePath, int line, string content) => Commands.FromAsync(async () =>
    {
        if (_client is null) return new CommentResultMsg("", new InvalidOperationException("no client available"));
        try
        {
            await _client.AddPRCodeCommentAsync(_pr.Repository.Id, _pr.Id, filePath, line, content).ConfigureAwait(false);
            return new CommentResultMsg("Comment added", null);
        }
        catch (Exception e) { return new CommentResultMsg("", e); }
    });

    private Cmd CreateGeneralComment(string content) => Commands.FromAsync(async () =>
    {
        if (_client is null) return new CommentResultMsg("", new InvalidOperationException("no client available"));
        try
        {
            await _client.AddPRCommentAsync(_pr.Repository.Id, _pr.Id, content).ConfigureAwait(false);
            return new CommentResultMsg("Comment added", null);
        }
        catch (Exception e) { return new CommentResultMsg("", e); }
    });

    private Cmd ReplyToThread(int threadId, string content) => Commands.FromAsync(async () =>
    {
        if (_client is null) return new CommentResultMsg("", new InvalidOperationException("no client available"));
        try
        {
            await _client.ReplyToThreadAsync(_pr.Repository.Id, _pr.Id, threadId, content).ConfigureAwait(false);
            return new CommentResultMsg("Reply added", null);
        }
        catch (Exception e) { return new CommentResultMsg("", e); }
    });

    private Cmd ResolveThread(int threadId) => Commands.FromAsync(async () =>
    {
        if (_client is null) return new CommentResultMsg("", new InvalidOperationException("no client available"));
        try
        {
            await _client.UpdateThreadStatusAsync(_pr.Repository.Id, _pr.Id, threadId, "fixed").ConfigureAwait(false);
            return new CommentResultMsg("Thread resolved", null);
        }
        catch (Exception e) { return new CommentResultMsg("", e); }
    });

    private Cmd RefreshThreads() => Commands.FromAsync(async () =>
    {
        if (_client is null) return new ThreadsRefreshMsg(Array.Empty<Thread>(), new InvalidOperationException("no client available"));
        try
        {
            var threads = await _client.GetPRThreadsAsync(_pr.Repository.Id, _pr.Id).ConfigureAwait(false);
            return new ThreadsRefreshMsg(ThreadFilter.FilterSystemThreads(threads), null);
        }
        catch (Exception e) { return new ThreadsRefreshMsg(Array.Empty<Thread>(), e); }
    });
}
