using Azdo.Tui.Rendering;

namespace Azdo.Tui.Styles;

/// <summary>
/// Pre-built <see cref="Style"/>s derived from a <see cref="Theme"/>
/// (≈ <c>styles.Styles</c>). Injected into every component via constructor.
/// </summary>
public sealed class Styles
{
    public Theme Theme { get; }

    // Tabs
    public Style TabActive { get; }
    public Style TabInactive { get; }
    public Style TabBar { get; }

    // Boxes
    public Style Box { get; }
    public Style BoxRounded { get; }
    public Style ContentBox { get; }
    public Style ModalBox { get; }

    // Text
    public Style Header { get; }
    public Style Title { get; }
    public Style Label { get; }
    public Style Value { get; }
    public Style Muted { get; }

    // Status
    public Style Success { get; }
    public Style Warning { get; }
    public Style Error { get; }
    public Style Info { get; }

    // Selection
    public Style Selected { get; }

    // Interactive
    public Style Key { get; }
    public Style Description { get; }
    public Style Link { get; }

    // UI elements
    public Style Border { get; }
    public Style Spinner { get; }
    public Style ScrollInfo { get; }

    // Connection state
    public Style Connected { get; }
    public Style Connecting { get; }
    public Style Disconnected { get; }
    public Style ConnError { get; }

    // Table
    public Style TableHeader { get; }
    public Style TableCell { get; }
    public Style TableSelected { get; }

    // Diff
    public Style DiffAdded { get; }
    public Style DiffRemoved { get; }
    public Style DiffContext { get; }
    public Style DiffHeader { get; }
    public Style DiffHunkHeader { get; }
    public Style DiffLineNum { get; }
    public Style DiffCommentCount { get; }
    public Style DiffCommentResolved { get; }

    public Styles(Theme theme)
    {
        Theme = theme;

        TabActive = Style.New().Foreground(theme.TabActiveForeground).Background(theme.TabActiveBackground).Padding(0, 2).Bold();
        TabInactive = Style.New().Foreground(theme.TabInactiveForeground).Padding(0, 2);
        TabBar = Style.New().WithBorder(Rendering.Border.Rounded).BorderForeground(theme.Border);

        Box = Style.New().BorderForeground(theme.Border).Background(theme.Background);
        BoxRounded = Style.New().WithBorder(Rendering.Border.Rounded).BorderForeground(theme.Accent).Padding(0, 1);
        ContentBox = Style.New().WithBorder(Rendering.Border.Rounded).BorderForeground(theme.Border);
        ModalBox = Style.New().WithBorder(Rendering.Border.Rounded).BorderForeground(theme.Accent).Background(theme.BackgroundAlt).Padding(1, 2);

        Header = Style.New().Foreground(theme.Primary).Bold();
        Title = Style.New().Foreground(theme.Primary).Bold();
        Label = Style.New().Foreground(theme.Warning).Bold();
        Value = Style.New().Foreground(theme.Foreground);
        Muted = Style.New().Foreground(theme.ForegroundMuted);

        Success = Style.New().Foreground(theme.Success);
        Warning = Style.New().Foreground(theme.Warning);
        Error = Style.New().Foreground(theme.Error);
        Info = Style.New().Foreground(theme.Info);

        Selected = Style.New().Foreground(theme.SelectForeground).Background(theme.SelectBackground);

        Key = Style.New().Foreground(theme.Accent).Bold();
        Description = Style.New().Foreground(theme.Foreground);
        Link = Style.New().Foreground(theme.Link).Underline();

        Border = Style.New().BorderForeground(theme.Border);
        Spinner = Style.New().Foreground(theme.Spinner);
        ScrollInfo = Style.New().Foreground(theme.Secondary);

        Connected = Style.New().Foreground(theme.Success);
        Connecting = Style.New().Foreground(theme.Warning);
        Disconnected = Style.New().Foreground(theme.ForegroundMuted);
        ConnError = Style.New().Foreground(theme.Error);

        TableHeader = Style.New().WithBorder(Rendering.Border.Normal, top: false, right: false, bottom: true, left: false).BorderForeground(theme.Border).Padding(0, 1);
        TableCell = Style.New().Foreground(theme.Foreground).Padding(0, 1);
        TableSelected = Style.New().Foreground(theme.SelectForeground).Background(theme.SelectBackground);

        DiffAdded = Style.New().Foreground(theme.Success);
        DiffRemoved = Style.New().Foreground(theme.Error);
        DiffContext = Style.New().Foreground(theme.ForegroundMuted);
        DiffHeader = Style.New().Foreground(theme.Primary).Background(theme.BackgroundAlt).Bold();
        DiffHunkHeader = Style.New().Foreground(theme.Info);
        DiffLineNum = Style.New().Foreground(theme.ForegroundMuted).Width(5).Align(HAlign.Right);
        DiffCommentCount = Style.New().Foreground(theme.Accent).Bold();
        DiffCommentResolved = Style.New().Foreground(theme.Success).Bold();
    }

    public static Styles Default() => new(Themes.Default);
}
