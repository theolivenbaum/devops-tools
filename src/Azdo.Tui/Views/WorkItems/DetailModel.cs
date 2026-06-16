using System.Text;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Util;
using Azdo.Tui.Components;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.WorkItems;

/// <summary>
/// The work item detail view (≈ <c>DetailModel</c> in detail.go). Implements
/// <see cref="IDetailView"/> so it can be driven uniformly by <see cref="ListView{T}"/>.
/// Mutable, matching the Go pointer-based model.
/// </summary>
public sealed class DetailModel : IDetailView
{
    /// <summary>
    /// Package-level seam so tests can intercept browser launches (≈ the Go
    /// <c>openURL</c> var). Returns an exception on failure, null on success.
    /// </summary>
    public static Func<string, Exception?> OpenUrl { get; set; } = url =>
    {
        try { Browser.Open(url); return null; }
        catch (Exception e) { return e; }
    };

    private readonly Client? _client;
    private WorkItem _workItem;
    private int _width;
    private int _height;
    private Viewport _viewport = new(0, 1);
    private bool _ready;
    private readonly StyleSet _styles;
    private readonly StatePicker _statePicker;
    private bool _loading;
    private readonly LoadingIndicator _spinner;
    private string _statusMessage = "";

    private IReadOnlyList<WorkItemComment> _comments = Array.Empty<WorkItemComment>();
    private bool _commentsLoading;
    private Exception? _commentsErr;
    private readonly CommentForm _commentForm;
    private bool _posting;
    private string _pendingComment = "";

    public DetailModel(Client? client, WorkItem wi, StyleSet styles)
    {
        _client = client;
        _workItem = wi;
        _styles = styles;
        _statePicker = new StatePicker(styles);
        _spinner = new LoadingIndicator(styles);
        _commentForm = new CommentForm(styles);
    }

    // Accessors used by the list Model and tests.
    public WorkItem GetWorkItem() => _workItem;
    public bool IsCommentFormVisible => _commentForm.IsVisible;
    public bool IsStatePickerVisible => _statePicker.IsVisible;
    public CommentForm CommentForm => _commentForm;
    public StatePicker StatePicker => _statePicker;
    public bool Posting => _posting;
    public bool Loading => _loading;

    /// <summary>
    /// Kicks off the comment fetch so the Discussion section populates as soon as
    /// the detail view opens (≈ <c>Init</c>).
    /// </summary>
    public Cmd? Init()
    {
        _commentsLoading = true;
        if (_ready) UpdateViewportContent();
        return FetchComments();
    }

    public Cmd? Update(IMsg msg)
    {
        // Route input to the state picker while it is visible.
        if (_statePicker.IsVisible)
            return _statePicker.Update(msg);

        // Route input to the comment form while it is open. The form hides itself
        // synchronously on submit/cancel, so the resulting messages fall through.
        if (_commentForm.IsVisible)
            return _commentForm.Update(msg);

        switch (msg)
        {
            case CommentSubmittedMsg cs:
                _pendingComment = cs.Text;
                _posting = true;
                _spinner.SetVisible(true);
                _spinner.SetMessage("Posting comment...");
                ResizeViewport();
                return Commands.Batch(PostComment(cs.Text), _spinner.Tick());

            case CommentFormCancelledMsg:
                _pendingComment = "";
                ResizeViewport();
                return null;

            case CommentsLoadedMsg cl:
                _commentsLoading = false;
                _commentsErr = cl.Err;
                _comments = cl.Comments;
                UpdateViewportContent();
                return null;

            case CommentPostedMsg cp:
                _posting = false;
                _spinner.SetVisible(false);
                if (cp.Err is not null)
                {
                    _statusMessage = $"Error posting comment: {cp.Err.Message}";
                    _commentForm.Reset();
                    _commentForm.SetValue(_pendingComment);
                    _commentForm.SetWidth(_width);
                    _commentForm.Show();
                    ResizeViewport();
                    return _commentForm.Focus();
                }
                _pendingComment = "";
                _statusMessage = "Comment added";
                _commentsLoading = true;
                UpdateViewportContent();
                return FetchComments();

            case StateSelectedMsg ss:
                _loading = true;
                _spinner.SetVisible(true);
                _spinner.SetMessage("Updating state...");
                return Commands.Batch(UpdateState(ss.State), _spinner.Tick());

            case StateUpdateResultMsg sr:
                _loading = false;
                _spinner.SetVisible(false);
                if (sr.Err is not null)
                {
                    _statusMessage = $"Error: {sr.Err.Message}";
                    return null;
                }
                _workItem.Fields.State = sr.NewState;
                _statusMessage = $"State changed to {sr.NewState}";
                UpdateViewportContent();
                return Commands.Of(WorkItemStateChangedMsg.Instance);

            case OpenUrlResultMsg ou:
                _statusMessage = ou.Err is not null
                    ? $"Failed to open browser: {ou.Err.Message}"
                    : "Opened in browser";
                return null;

            case StatesLoadedMsg sl:
                _loading = false;
                _spinner.SetVisible(false);
                if (sl.Err is not null)
                {
                    _statusMessage = $"Error: {sl.Err.Message}";
                    return null;
                }
                _statePicker.SetStates(
                    sl.States.Select(s => new WorkItemStateOption(s.Name, s.Category)),
                    _workItem.Fields.State);
                _statePicker.SetSize(_width, _height);
                _statePicker.Show();
                return null;

            case SpinnerTickMsg when _loading:
                return _spinner.Update(msg);

            case WindowSizeMsg ws:
                _width = ws.Width;
                _height = ws.Height;
                return null;

            case KeyMsg key:
                switch (key.Key)
                {
                    case "w":
                        _loading = true;
                        _spinner.SetVisible(true);
                        _spinner.SetMessage("Loading states...");
                        return Commands.Batch(FetchStates(), _spinner.Tick());
                    case "o":
                        return OpenInBrowser();
                    case "c":
                        if (_posting) return null;
                        _commentForm.Reset();
                        _commentForm.SetWidth(_width);
                        _commentForm.Show();
                        ResizeViewport();
                        return _commentForm.Focus();
                    case "up": case "k":
                        _viewport.LineUp(1);
                        return null;
                    case "down": case "j":
                        _viewport.LineDown(1);
                        return null;
                    case "pgup":
                        _viewport.HalfViewUp();
                        return null;
                    case "pgdown":
                        _viewport.HalfViewDown();
                        return null;
                }
                return null;
        }

        return null;
    }

    private Cmd? OpenInBrowser()
    {
        if (_client is null)
        {
            _statusMessage = "Cannot open: no Azure DevOps client";
            return null;
        }
        string url = Format.BuildWorkItemUrl(_client.GetOrg(), _client.GetProject(), _workItem.Id);
        if (url == "")
        {
            _statusMessage = "Cannot open: missing organization or project";
            return null;
        }
        return Commands.FromFunc(() => new OpenUrlResultMsg(OpenUrl(url)));
    }

    private Cmd FetchStates()
    {
        var client = _client;
        string type = _workItem.Fields.WorkItemType;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new StatesLoadedMsg(Array.Empty<WorkItemTypeState>(), new InvalidOperationException("no client available"));
            try
            {
                var states = await client.GetWorkItemTypeStatesAsync(type).ConfigureAwait(false);
                return new StatesLoadedMsg(states, null);
            }
            catch (Exception e)
            {
                return new StatesLoadedMsg(Array.Empty<WorkItemTypeState>(), e);
            }
        });
    }

    private Cmd FetchComments()
    {
        var client = _client;
        int id = _workItem.Id;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new CommentsLoadedMsg(Array.Empty<WorkItemComment>(), new InvalidOperationException("no client available"));
            try
            {
                var comments = await client.GetWorkItemCommentsAsync(id).ConfigureAwait(false);
                return new CommentsLoadedMsg(comments, null);
            }
            catch (Exception e)
            {
                return new CommentsLoadedMsg(Array.Empty<WorkItemComment>(), e);
            }
        });
    }

    private Cmd PostComment(string text)
    {
        var client = _client;
        int id = _workItem.Id;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new CommentPostedMsg(null, new InvalidOperationException("no client available"));
            try
            {
                var comment = await client.AddWorkItemCommentAsync(id, text).ConfigureAwait(false);
                return new CommentPostedMsg(comment, null);
            }
            catch (Exception e)
            {
                return new CommentPostedMsg(null, e);
            }
        });
    }

    private Cmd UpdateState(string state)
    {
        var client = _client;
        int id = _workItem.Id;
        return Commands.FromAsync(async () =>
        {
            if (client is null)
                return new StateUpdateResultMsg("", new InvalidOperationException("no client available"));
            try
            {
                await client.UpdateWorkItemStateAsync(id, state).ConfigureAwait(false);
                return new StateUpdateResultMsg(state, null);
            }
            catch (Exception e)
            {
                return new StateUpdateResultMsg("", e);
            }
        });
    }

    public string View()
    {
        // State picker overlay takes precedence.
        if (_statePicker.IsVisible)
            return _statePicker.View();

        var sb = new StringBuilder();
        var wi = _workItem;

        // Fixed header with ID and title (no type icon).
        sb.Append(_styles.Header.Render($"#{wi.Id}: {wi.Fields.Title}"));
        sb.Append('\n');

        // Type, state and priority.
        var metadataStyle = Style.New().Foreground(_styles.Theme.Secondary);
        sb.Append(metadataStyle.Render($"{wi.Fields.WorkItemType}  |  {wi.StateIcon()} {wi.Fields.State}  |  P{wi.Fields.Priority}"));
        sb.Append('\n');

        // Separator.
        int separatorWidth = Math.Min(_width - 2, 60);
        if (separatorWidth < 1) separatorWidth = 60;
        sb.Append(new string('─', separatorWidth));
        sb.Append('\n');

        // Scrollable viewport content.
        if (_ready)
            sb.Append(_viewport.View());

        // Inline comment form, rendered below the viewport when open.
        if (_commentForm.IsVisible)
        {
            sb.Append('\n');
            sb.Append(_commentForm.View());
        }

        return Style.New().Width(_width).Render(sb.ToString());
    }

    private void UpdateViewportContent()
    {
        var sb = new StringBuilder();
        var wi = _workItem;

        // Assigned To.
        if (wi.Fields.AssignedTo is not null)
        {
            sb.Append(_styles.Label.Render("Assigned To: "));
            sb.Append(wi.Fields.AssignedTo.DisplayName);
            sb.Append("\n\n");
        }
        else
        {
            sb.Append(_styles.Label.Render("Assigned To: "));
            sb.Append(_styles.Muted.Render("Unassigned"));
            sb.Append("\n\n");
        }

        // Iteration Path.
        if (wi.Fields.IterationPath != "")
        {
            sb.Append(_styles.Label.Render("Iteration: "));
            sb.Append(Format.ShortenIterationPath(wi.Fields.IterationPath));
            sb.Append("\n\n");
        }

        // Last changed timestamp.
        if (wi.Fields.ChangedDate != default)
        {
            sb.Append(_styles.Label.Render("Last changed: "));
            sb.Append(wi.Fields.ChangedDate.ToString("yyyy-MM-dd HH:mm"));
            sb.Append("\n\n");
        }

        // Tags.
        var tags = wi.TagList();
        if (tags.Count > 0)
        {
            sb.Append(_styles.Label.Render("Tags: "));
            sb.Append(string.Join(", ", tags));
            sb.Append("\n\n");
        }

        // Link to work item (shown before description for quick access).
        if (_client is not null)
        {
            string url = Format.BuildWorkItemUrl(_client.GetOrg(), _client.GetProject(), wi.Id);
            if (url != "")
            {
                sb.Append(Format.Hyperlink(_styles.Link.Render("Open in browser"), url));
                sb.Append("\n\n");
            }
        }

        // Description (HTML stripped). Bugs use ReproSteps; others use Description.
        string effectiveDesc = wi.EffectiveDescription();
        if (effectiveDesc != "")
        {
            sb.Append(_styles.Label.Render("Description"));
            sb.Append('\n');
            string cleanDesc = Format.StripHtmlTags(effectiveDesc);
            sb.Append(_styles.Value.Width(_width).Render(cleanDesc));
            sb.Append('\n');
        }
        else
        {
            sb.Append(_styles.Muted.Render("No description"));
            sb.Append('\n');
        }

        // Discussion (comments), newest first.
        WriteDiscussion(sb);

        _viewport.SetContent(sb.ToString());
    }

    private void WriteDiscussion(StringBuilder sb)
    {
        sb.Append('\n');
        sb.Append(_styles.Label.Render($"Discussion ({_comments.Count})"));
        sb.Append("\n\n");

        if (_commentsLoading)
        {
            sb.Append(_styles.Muted.Render("Loading comments..."));
            sb.Append('\n');
            return;
        }
        if (_commentsErr is not null)
        {
            sb.Append(_styles.Muted.Render($"Could not load comments: {_commentsErr.Message}"));
            sb.Append('\n');
            return;
        }
        if (_comments.Count == 0)
        {
            sb.Append(_styles.Muted.Render("No comments yet. Press c to add one."));
            sb.Append('\n');
            return;
        }

        var metaStyle = Style.New().Foreground(_styles.Theme.Secondary);
        var bodyStyle = _styles.Value.Width(_width);
        foreach (var c in _comments)
        {
            string author = c.CreatedBy.DisplayName;
            if (author == "") author = "Unknown";
            string header = c.CreatedDate != default
                ? $"{author}  ·  {c.CreatedDate:yyyy-MM-dd HH:mm}"
                : author;
            sb.Append(metaStyle.Render(header));
            sb.Append('\n');
            sb.Append(bodyStyle.Render(Format.StripHtmlTags(c.Text)));
            sb.Append("\n\n");
        }
    }

    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
        _commentForm.SetWidth(width);

        if (!_ready)
        {
            _viewport = new Viewport(width, 1);
            _ready = true;
        }

        ResizeViewport();
        UpdateViewportContent();
    }

    /// <summary>
    /// Non-viewport rows the detail view renders: header (3) plus the comment
    /// form (a spacer + the form) when open (≈ <c>reservedLines</c>).
    /// </summary>
    private int ReservedLines()
    {
        int lines = 3;
        if (_commentForm.IsVisible)
            lines += 1 + _commentForm.Height();
        return lines;
    }

    private void ResizeViewport()
    {
        if (!_ready) return;
        int h = _height - ReservedLines();
        if (h < 1) h = 1;
        _viewport.Width = _width;
        _viewport.Height = h;
    }

    public IReadOnlyList<ContextItem> GetContextItems() => new[]
    {
        new ContextItem("w", "Change state"),
        new ContextItem("c", "comment"),
        new ContextItem("o", "open in browser"),
        new ContextItem("↑↓", "scroll"),
        new ContextItem("esc", "back"),
    };

    public double GetScrollPercent() => _ready ? _viewport.ScrollPercent() * 100 : 0;

    public string GetStatusMessage() => _statusMessage;
}
