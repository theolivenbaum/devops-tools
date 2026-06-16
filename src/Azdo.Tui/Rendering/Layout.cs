namespace Azdo.Tui.Rendering;

/// <summary>
/// Block layout helpers mirroring <c>lipgloss.JoinHorizontal</c>,
/// <c>JoinVertical</c>, and <c>Place</c>. Blocks are multi-line strings; these
/// pad/align them so they compose into rectangular regions.
/// </summary>
public static class Layout
{
    public static int Width(string s) => Ansi.MaxLineWidth(s);
    public static int Height(string s) => Ansi.Height(s);

    /// <summary>Stacks blocks side by side, aligning them vertically.</summary>
    public static string JoinHorizontal(VAlign align, params string[] blocks)
    {
        if (blocks.Length == 0) return string.Empty;
        var grids = new List<List<string>>();
        int maxHeight = 0;
        foreach (var b in blocks)
        {
            var lines = b.Split('\n').ToList();
            grids.Add(lines);
            maxHeight = Math.Max(maxHeight, lines.Count);
        }

        // Pad each block to maxHeight (per vertical alignment) and to its own width.
        for (int g = 0; g < grids.Count; g++)
        {
            var lines = grids[g];
            int w = lines.Max(Ansi.Width);
            for (int i = 0; i < lines.Count; i++) lines[i] = Ansi.PadRight(lines[i], w);
            int missing = maxHeight - lines.Count;
            var blank = new string(' ', w);
            switch (align)
            {
                case VAlign.Top:
                    for (int i = 0; i < missing; i++) lines.Add(blank);
                    break;
                case VAlign.Bottom:
                    for (int i = 0; i < missing; i++) lines.Insert(0, blank);
                    break;
                default:
                    int top = missing / 2, bot = missing - top;
                    for (int i = 0; i < top; i++) lines.Insert(0, blank);
                    for (int i = 0; i < bot; i++) lines.Add(blank);
                    break;
            }
        }

        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < maxHeight; row++)
        {
            foreach (var lines in grids) sb.Append(lines[row]);
            if (row < maxHeight - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Stacks blocks vertically, aligning them horizontally.</summary>
    public static string JoinVertical(HAlign align, params string[] blocks)
    {
        if (blocks.Length == 0) return string.Empty;
        var allLines = new List<string>();
        int maxWidth = 0;
        foreach (var b in blocks)
            foreach (var line in b.Split('\n'))
                maxWidth = Math.Max(maxWidth, Ansi.Width(line));

        foreach (var b in blocks)
        {
            foreach (var line in b.Split('\n'))
            {
                int diff = maxWidth - Ansi.Width(line);
                allLines.Add(align switch
                {
                    HAlign.Right => new string(' ', diff) + line,
                    HAlign.Center => new string(' ', diff / 2) + line + new string(' ', diff - diff / 2),
                    _ => line + new string(' ', diff),
                });
            }
        }
        return string.Join("\n", allLines);
    }

    /// <summary>Places content within a box of the given size.</summary>
    public static string Place(int width, int height, HAlign h, VAlign v, string content)
        => Style.New().Width(width).Height(height).Align(h).AlignVertical(v).Render(content);

    public static string PlaceHorizontal(int width, HAlign h, string content)
        => Style.New().Width(width).Align(h).Render(content);

    public static string PlaceVertical(int height, VAlign v, string content)
        => Style.New().Height(height).AlignVertical(v).Render(content);
}
