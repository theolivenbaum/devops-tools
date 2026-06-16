using System.Text;
using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>
/// A message any view can emit to signal a critical API error to the root model,
/// which shows the error modal overlay (≈ <c>components.CriticalErrorMsg</c>).
/// </summary>
public sealed record CriticalErrorMsg(string Title, string Message, string Hint) : IMsg;

/// <summary>Classified error information for display in the error modal (≈ <c>components.ErrorInfo</c>).</summary>
public sealed record ErrorInfo(string Title, string Message, string Hint);

/// <summary>
/// Examines an exception and returns an <see cref="ErrorInfo"/> for known critical
/// errors that should be shown in the error modal (≈ <c>components.ClassifyError</c>).
/// Returns <c>null</c> for transient or unknown errors. Matches on the exception
/// message only so it stays independent of Core types.
/// </summary>
public static class ErrorClassifier
{
    public static ErrorInfo? Classify(Exception? err)
    {
        if (err is null) return null;

        string msg = err.Message;

        if (msg.Contains("HTTP 404") || msg.Contains("resource not found"))
            return new ErrorInfo(
                "Configuration Error",
                "The API returned 'not found'. Your organization or project name in the configuration may be incorrect.",
                "Check your config file and verify the organization and project names match your Azure DevOps setup.");

        if (msg.Contains("HTTP 401") || msg.Contains("authentication failed"))
            return new ErrorInfo(
                "Authentication Error",
                "Your Personal Access Token (PAT) may be expired or invalid.",
                "Run 'azdo auth' to update your PAT.");

        // NOTE: a 403 (insufficient permissions) is intentionally NOT classified
        // as a critical, modal-worthy error. It only means the PAT is missing the
        // scope for one feature (e.g. Build Read for pipelines); the features whose
        // scopes ARE present must keep working. Callers surface 403s inline in the
        // affected tab via the fall-through path. See <see cref="IsPermissionError"/>.

        if (msg.Contains("HTTP 400") || msg.Contains("HTTP request failed with status"))
            return new ErrorInfo(
                "Configuration Error",
                "The API returned an error. Your organization or project name in the configuration may be incorrect.",
                "Check your config file and verify the organization and project names match your Azure DevOps setup.");

        return null;
    }

    /// <summary>
    /// True when the error is a permission failure (HTTP 403): the PAT is valid
    /// but lacks the scope required for one specific feature. Such errors are
    /// surfaced inline in the affected tab and must never block the whole app,
    /// so the features whose scopes are present keep working.
    /// </summary>
    public static bool IsPermissionError(Exception? err)
    {
        if (err is null) return false;
        var msg = err.Message;
        return msg.Contains("HTTP 403") || msg.Contains("access denied");
    }

    /// <summary>
    /// Returns a command that emits a <see cref="CriticalErrorMsg"/> when the error
    /// is critical; otherwise <c>null</c> (≈ <c>components.NewCriticalErrorCmd</c>).
    /// </summary>
    public static Cmd? NewCriticalErrorCmd(Exception? err)
    {
        var info = Classify(err);
        if (info is null) return null;
        return Commands.Of(new CriticalErrorMsg(info.Title, info.Message, info.Hint));
    }
}

/// <summary>
/// An overlay that displays critical error messages (≈ <c>components.ErrorModal</c>).
/// Dismiss on "esc" or "q".
/// </summary>
public sealed class ErrorModal
{
    private const int MinErrorModalWidth = 50;
    private const int ModalHorizontalOverhead = 6;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private string _title = "";
    private string _message = "";
    private string _hint = "";

    public ErrorModal(StyleSet styles) => _styles = styles;

    public void Show(string title, string message, string hint)
    {
        _title = title;
        _message = message;
        _hint = hint;
        _visible = true;
    }

    public void Hide() => _visible = false;
    public bool IsVisible => _visible;
    public void SetSize(int width, int height) { _width = width; _height = height; }

    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;
        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "esc":
                case "q":
                    Hide();
                    return null;
            }
        }
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        int contentWidth = MinErrorModalWidth;
        if (_title.Length > contentWidth) contentWidth = _title.Length;
        if (_message.Length > contentWidth) contentWidth = _message.Length;

        if (_width > 0)
        {
            int maxContentWidth = _width - ModalHorizontalOverhead;
            if (maxContentWidth < 0) maxContentWidth = 0;
            if (contentWidth > maxContentWidth) contentWidth = maxContentWidth;
        }

        var theme = _styles.Theme;

        var modalStyle = Style.New()
            .WithBorder(Border.Rounded)
            .BorderForeground(theme.Error)
            .Padding(1, 2)
            .Background(theme.BackgroundAlt);

        var titleStyle = Style.New()
            .Foreground(theme.Error)
            .Bold()
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var messageStyle = Style.New()
            .Foreground(theme.Foreground)
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var hintStyle = Style.New()
            .Foreground(theme.Accent)
            .Bold()
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var footerStyle = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Width(contentWidth)
            .Background(theme.BackgroundAlt);

        var content = new StringBuilder();

        // titleStyle has MarginBottom(1) in Go -> emulate with an extra blank line.
        content.Append(titleStyle.Render(_title));
        content.Append('\n');
        content.Append('\n');
        content.Append(messageStyle.Render(_message));
        content.Append('\n');

        if (_hint != "")
        {
            // hintStyle has MarginTop(1) in Go -> emulate with a leading blank line.
            content.Append('\n');
            content.Append(hintStyle.Render(_hint));
            content.Append('\n');
        }

        content.Append('\n');
        content.Append(footerStyle.Render("Press esc to dismiss"));

        var modal = modalStyle.Render(content.ToString());

        if (_width > 0 && _height > 0)
            return Layout.Place(_width, _height, HAlign.Center, VAlign.Center, modal);

        return modal;
    }
}
