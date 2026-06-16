using System.Text;

namespace Azdo.Tui.Rendering;

public enum HAlign { Left, Center, Right }
public enum VAlign { Top, Center, Bottom }

/// <summary>
/// Immutable, fluent text style — the C# equivalent of <c>lipgloss.Style</c>.
/// Every mutator returns a copy, so a base style can be reused without being
/// altered (e.g. <c>styles.ContentBox.Width(w).Render(s)</c>). <see cref="Render"/>
/// emits a string with embedded SGR escapes and box-drawing borders.
/// </summary>
public sealed class Style
{
    private TerminalColor _fg, _bg, _borderFg;
    private bool _bold, _faint, _italic, _underline;
    private int _padT, _padR, _padB, _padL;
    private int? _width, _height, _maxWidth, _maxHeight;
    private HAlign _hAlign = HAlign.Left;
    private VAlign _vAlign = VAlign.Top;
    private Border? _border;
    private bool _bT = true, _bB = true, _bL = true, _bR = true;

    public static Style New() => new();

    private Style Clone() => new()
    {
        _fg = _fg, _bg = _bg, _borderFg = _borderFg,
        _bold = _bold, _faint = _faint, _italic = _italic, _underline = _underline,
        _padT = _padT, _padR = _padR, _padB = _padB, _padL = _padL,
        _width = _width, _height = _height, _maxWidth = _maxWidth, _maxHeight = _maxHeight,
        _hAlign = _hAlign, _vAlign = _vAlign,
        _border = _border, _bT = _bT, _bB = _bB, _bL = _bL, _bR = _bR,
    };

    public Style Foreground(string? color) { var s = Clone(); s._fg = TerminalColor.Parse(color); return s; }
    public Style Background(string? color) { var s = Clone(); s._bg = TerminalColor.Parse(color); return s; }
    public Style Bold(bool v = true) { var s = Clone(); s._bold = v; return s; }
    public Style Faint(bool v = true) { var s = Clone(); s._faint = v; return s; }
    public Style Italic(bool v = true) { var s = Clone(); s._italic = v; return s; }
    public Style Underline(bool v = true) { var s = Clone(); s._underline = v; return s; }

    public Style Width(int w) { var s = Clone(); s._width = w; return s; }
    public Style Height(int h) { var s = Clone(); s._height = h; return s; }
    public Style MaxWidth(int w) { var s = Clone(); s._maxWidth = w; return s; }
    public Style MaxHeight(int h) { var s = Clone(); s._maxHeight = h; return s; }
    public Style Align(HAlign a) { var s = Clone(); s._hAlign = a; return s; }
    public Style AlignVertical(VAlign a) { var s = Clone(); s._vAlign = a; return s; }

    public Style Padding(int all) => Padding(all, all, all, all);
    public Style Padding(int vertical, int horizontal) => Padding(vertical, horizontal, vertical, horizontal);
    public Style Padding(int top, int right, int bottom, int left)
    {
        var s = Clone(); s._padT = top; s._padR = right; s._padB = bottom; s._padL = left; return s;
    }

    public Style WithBorder(Border border, bool top = true, bool right = true, bool bottom = true, bool left = true)
    {
        var s = Clone(); s._border = border; s._bT = top; s._bR = right; s._bB = bottom; s._bL = left; return s;
    }

    public Style BorderForeground(string? color) { var s = Clone(); s._borderFg = TerminalColor.Parse(color); return s; }

    public int GetHorizontalFrameSize()
    {
        int f = _padL + _padR;
        if (_border is not null) { if (_bL) f++; if (_bR) f++; }
        return f;
    }

    public int GetVerticalFrameSize()
    {
        int f = _padT + _padB;
        if (_border is not null) { if (_bT) f++; if (_bB) f++; }
        return f;
    }

    private string SgrPrefix()
    {
        var parts = new List<string>();
        if (_bold) parts.Add("1");
        if (_faint) parts.Add("2");
        if (_italic) parts.Add("3");
        if (_underline) parts.Add("4");
        if (_fg.IsSet) parts.Add(_fg.ForegroundParams());
        if (_bg.IsSet) parts.Add(_bg.BackgroundParams());
        return parts.Count == 0 ? string.Empty : $"\x1b[{string.Join(';', parts)}m";
    }

    private string ApplySgr(string line, string prefix)
    {
        if (prefix.Length == 0) return line;
        // Reapply our SGR after any nested reset so an outer background keeps filling.
        var body = line.Replace(Ansi.Reset, Ansi.Reset + prefix);
        return prefix + body + Ansi.Reset;
    }

    public string Render(string input)
    {
        input ??= string.Empty;
        int innerWidth = _width.HasValue ? Math.Max(0, _width.Value - _padL - _padR) : -1;

        // Wrap content to the inner width when a width is fixed.
        var text = innerWidth >= 0 ? Ansi.Wrap(input, innerWidth) : input;
        var lines = text.Split('\n').ToList();

        int blockW = innerWidth >= 0 ? innerWidth : Ansi.MaxLineWidth(text);

        // Horizontal alignment & padding within the block.
        for (int i = 0; i < lines.Count; i++)
            lines[i] = AlignLine(lines[i], blockW);

        // Left/right padding (spaces).
        if (_padL > 0 || _padR > 0)
        {
            var lp = new string(' ', _padL);
            var rp = new string(' ', _padR);
            for (int i = 0; i < lines.Count; i++) lines[i] = lp + lines[i] + rp;
        }
        int fullW = blockW + _padL + _padR;

        // Vertical padding.
        for (int i = 0; i < _padT; i++) lines.Insert(0, new string(' ', fullW));
        for (int i = 0; i < _padB; i++) lines.Add(new string(' ', fullW));

        // Height: pad to target line count using vertical alignment.
        if (_height.HasValue && lines.Count < _height.Value)
        {
            int missing = _height.Value - lines.Count;
            var blank = new string(' ', fullW);
            switch (_vAlign)
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

        // Apply colors/attributes to each line so the background fills evenly.
        var prefix = SgrPrefix();
        if (prefix.Length > 0)
            for (int i = 0; i < lines.Count; i++) lines[i] = ApplySgr(lines[i], prefix);

        // Border.
        if (_border is not null)
            lines = ApplyBorder(lines, fullW);

        // Max constraints.
        if (_maxWidth.HasValue)
            for (int i = 0; i < lines.Count; i++) lines[i] = Ansi.Truncate(lines[i], _maxWidth.Value);
        if (_maxHeight.HasValue && lines.Count > _maxHeight.Value)
            lines = lines.Take(_maxHeight.Value).ToList();

        return string.Join("\n", lines);
    }

    private string AlignLine(string line, int width)
    {
        int diff = width - Ansi.Width(line);
        if (diff <= 0) return line;
        return _hAlign switch
        {
            HAlign.Right => new string(' ', diff) + line,
            HAlign.Center => new string(' ', diff / 2) + line + new string(' ', diff - diff / 2),
            _ => line + new string(' ', diff),
        };
    }

    private List<string> ApplyBorder(List<string> lines, int width)
    {
        var b = _border!;
        string ColorB(string s)
        {
            if (!_borderFg.IsSet) return s;
            return $"\x1b[{_borderFg.ForegroundParams()}m{s}{Ansi.Reset}";
        }

        var outLines = new List<string>();
        if (_bT)
        {
            var lc = _bL ? b.TopLeft : string.Empty;
            var rc = _bR ? b.TopRight : string.Empty;
            outLines.Add(ColorB(lc + Repeat(b.Top, width) + rc));
        }
        string l = _bL ? ColorB(b.Left) : string.Empty;
        string r = _bR ? ColorB(b.Right) : string.Empty;
        foreach (var line in lines) outLines.Add(l + line + r);
        if (_bB)
        {
            var lc = _bL ? b.BottomLeft : string.Empty;
            var rc = _bR ? b.BottomRight : string.Empty;
            outLines.Add(ColorB(lc + Repeat(b.Bottom, width) + rc));
        }
        return outLines;
    }

    private static string Repeat(string unit, int width)
    {
        if (width <= 0) return string.Empty;
        var sb = new StringBuilder();
        while (Ansi.Width(sb.ToString()) < width) sb.Append(unit);
        return Ansi.Truncate(sb.ToString(), width);
    }
}
