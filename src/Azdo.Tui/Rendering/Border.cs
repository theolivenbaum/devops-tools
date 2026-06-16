namespace Azdo.Tui.Rendering;

/// <summary>Box-drawing border definition (mirrors Lip Gloss borders).</summary>
public sealed record Border(
    string Top, string Bottom, string Left, string Right,
    string TopLeft, string TopRight, string BottomLeft, string BottomRight)
{
    public static readonly Border Rounded = new("─", "─", "│", "│", "╭", "╮", "╰", "╯");
    public static readonly Border Normal = new("─", "─", "│", "│", "┌", "┐", "└", "┘");
    public static readonly Border Thick = new("━", "━", "┃", "┃", "┏", "┓", "┗", "┛");
    public static readonly Border Double = new("═", "═", "║", "║", "╔", "╗", "╚", "╝");
    public static readonly Border None = new(" ", " ", " ", " ", " ", " ", " ", " ");
}
