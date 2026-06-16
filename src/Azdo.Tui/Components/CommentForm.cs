using System.Text;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>Emitted when the user submits a comment with Ctrl+S (≈ <c>components.CommentSubmittedMsg</c>).</summary>
public sealed record CommentSubmittedMsg(string Text) : IMsg;

/// <summary>Emitted when the user cancels the form with Esc (≈ <c>components.CommentFormCancelledMsg</c>).</summary>
public sealed record CommentFormCancelledMsg : IMsg
{
    public static readonly CommentFormCancelledMsg Instance = new();
}

/// <summary>
/// An inline multi-line text form for composing a work item comment
/// (≈ <c>components.CommentForm</c>). Enter inserts a newline; Ctrl+S submits;
/// Esc cancels. A minimal textarea is implemented inline since the foundation
/// <see cref="TextInput"/> is single-line.
/// </summary>
public sealed class CommentForm
{
    private const int CommentFormHeight = 5; // textarea rows shown
    private const string Placeholder = "Write a comment...";

    private readonly StyleSet _styles;
    private readonly StringBuilder _value = new();
    private bool _visible;
    private bool _focused;
    private int _width = 40;

    public CommentForm(StyleSet styles) => _styles = styles;

    public void Show() => _visible = true;
    public void Hide() { _visible = false; _focused = false; }
    public bool IsVisible => _visible;

    /// <summary>Focuses the textarea so it captures keystrokes. Returns no command.</summary>
    public Cmd? Focus() { _focused = true; return null; }

    public void Reset() => _value.Clear();
    public void SetValue(string s) { _value.Clear(); _value.Append(s); }
    public string Value() => _value.ToString();

    /// <summary>Sizes the textarea to the available width (leaving room for border/padding).</summary>
    public void SetWidth(int width)
    {
        int w = width - 4;
        if (w < 10) w = 10;
        _width = w;
    }

    /// <summary>Terminal rows the form occupies when visible (textarea + border + help line).</summary>
    public int Height() => CommentFormHeight + 3;

    /// <summary>
    /// Handles a message. Ctrl+S submits, Esc cancels, Enter inserts a newline,
    /// everything else is delegated to the inline textarea.
    /// </summary>
    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;

        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "esc":
                    // Hide synchronously so the dispatched cancel message is handled by the parent.
                    _visible = false;
                    _focused = false;
                    return Commands.Of(CommentFormCancelledMsg.Instance);

                case "ctrl+s":
                    string text = _value.ToString();
                    if (string.IsNullOrWhiteSpace(text)) return null;
                    _visible = false;
                    _focused = false;
                    return Commands.Of(new CommentSubmittedMsg(text));

                case "enter":
                    _value.Append('\n');
                    return null;

                case "backspace":
                    if (_value.Length > 0) _value.Remove(_value.Length - 1, 1);
                    return null;

                case " ":
                    _value.Append(' ');
                    return null;

                default:
                    if (key.IsRune) _value.Append(key.Rune);
                    return null;
            }
        }

        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;

        string helpText = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Render("Ctrl+S: send • Esc: cancel");

        string textareaView = RenderTextarea();

        string box = Style.New()
            .WithBorder(Border.Rounded)
            .BorderForeground(theme.Border)
            .Render(textareaView);

        return Layout.JoinVertical(HAlign.Left, box, helpText);
    }

    /// <summary>Renders the textarea body: fixed height, padded to the configured width.</summary>
    private string RenderTextarea()
    {
        string content = _value.Length == 0 && !_focused
            ? Placeholder
            : _value.ToString();

        var lines = content.Split('\n').ToList();
        while (lines.Count < CommentFormHeight) lines.Add("");
        if (lines.Count > CommentFormHeight)
            lines = lines.GetRange(lines.Count - CommentFormHeight, CommentFormHeight);

        for (int i = 0; i < lines.Count; i++)
            lines[i] = Ansi.PadRight(Ansi.Truncate(lines[i], _width), _width);

        return string.Join("\n", lines);
    }
}
