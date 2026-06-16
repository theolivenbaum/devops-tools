using Azdo.Tui.Rendering;
using Azdo.Tui.Runtime;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Components;

/// <summary>Emitted when a vote option is selected (≈ <c>components.VoteSelectedMsg</c>).</summary>
public sealed record VoteSelectedMsg(int Vote) : IMsg;

/// <summary>
/// Azure DevOps PR vote values. Kept local to avoid a dependency on Core's
/// <c>azdevops</c> constants (values must match the API).
/// </summary>
public static class VoteValues
{
    public const int Approve = 10;
    public const int ApproveWithSuggestions = 5;
    public const int NoVote = 0;
    public const int WaitForAuthor = -5;
    public const int Reject = -10;
}

/// <summary>A single vote choice (≈ <c>components.VoteOption</c>).</summary>
public sealed class VoteOption
{
    public string Label { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Vote { get; init; }
}

/// <summary>A modal for selecting a PR vote (≈ <c>components.VotePicker</c>).</summary>
public sealed class VotePicker
{
    private const int MinModalWidth = 80;

    private readonly StyleSet _styles;
    private bool _visible;
    private int _width;
    private int _height;
    private readonly List<VoteOption> _options;
    private int _cursor;

    public VotePicker(StyleSet styles)
    {
        _styles = styles;
        _options = new List<VoteOption>
        {
            new() { Label = "Approve", Icon = "✓", Vote = VoteValues.Approve },
            new() { Label = "Approve with suggestions", Icon = "~", Vote = VoteValues.ApproveWithSuggestions },
            new() { Label = "Wait for author", Icon = "◐", Vote = VoteValues.WaitForAuthor },
            new() { Label = "Reject", Icon = "✗", Vote = VoteValues.Reject },
            new() { Label = "Reset feedback", Icon = "○", Vote = VoteValues.NoVote },
        };
    }

    public void Show() => _visible = true;
    public void Hide() => _visible = false;
    public bool IsVisible => _visible;
    public void SetSize(int width, int height) { _width = width; _height = height; }
    public int GetCursor() => _cursor;

    public Cmd? Update(IMsg msg)
    {
        if (!_visible) return null;
        if (msg is KeyMsg key)
        {
            switch (key.Key)
            {
                case "esc":
                case "q":
                    _visible = false;
                    return null;
                case "up":
                case "k":
                    if (_cursor > 0) _cursor--;
                    return null;
                case "down":
                case "j":
                    if (_cursor < _options.Count - 1) _cursor++;
                    return null;
                case "enter":
                    var selected = _options[_cursor];
                    _visible = false;
                    return Commands.Of(new VoteSelectedMsg(selected.Vote));
            }
        }
        return null;
    }

    public string View()
    {
        if (!_visible) return "";

        var theme = _styles.Theme;
        const string titleText = "Vote on Pull Request";
        const string helpTextStr = "↑/↓: navigate • enter: select • esc/q: cancel";

        int maxWidth = MinModalWidth;
        if (titleText.Length > maxWidth) maxWidth = titleText.Length;
        if (helpTextStr.Length > maxWidth) maxWidth = helpTextStr.Length;

        foreach (var opt in _options)
        {
            int lineLen = $"> {opt.Icon} {opt.Label}".Length;
            if (lineLen > maxWidth) maxWidth = lineLen;
        }

        var optionList = new System.Text.StringBuilder();
        for (int i = 0; i < _options.Count; i++)
        {
            var opt = _options[i];
            string cursor = i == _cursor ? ">" : " ";
            string line = $"{cursor} {opt.Icon} {opt.Label}";

            line = i == _cursor
                ? Style.New().Foreground(theme.SelectForeground).Background(theme.SelectBackground).Width(maxWidth).Render(line)
                : Style.New().Foreground(theme.Foreground).Background(theme.Background).Width(maxWidth).Render(line);

            optionList.Append(line).Append('\n');
        }

        string title = Style.New()
            .Foreground(theme.Primary)
            .Background(theme.Background)
            .Bold()
            .Width(maxWidth)
            .Render(titleText);

        string helpText = Style.New()
            .Foreground(theme.ForegroundMuted)
            .Background(theme.Background)
            .Width(maxWidth)
            .Render(helpTextStr);

        string content = Layout.JoinVertical(HAlign.Left, title, "", optionList.ToString(), helpText);

        var modalStyle = Style.New()
            .WithBorder(Border.Rounded)
            .BorderForeground(theme.Border)
            .Padding(1, 2)
            .Background(theme.Background);

        var modal = modalStyle.Render(content);

        if (_width > 0 && _height > 0)
            modal = Layout.Place(_width, _height, HAlign.Center, VAlign.Center, modal);

        return modal;
    }
}
