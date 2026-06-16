using System.Text;
using System.Text.RegularExpressions;

namespace Azdo.Tui.Rendering;

/// <summary>
/// ANSI-aware text utilities — the C# equivalent of the bits of Lip Gloss /
/// <c>charmbracelet/x/ansi</c> the TUI relies on: visible width measurement,
/// escape stripping, width-aware truncation, and word wrapping. All functions
/// account for embedded SGR escape sequences and (approximate) wide runes.
/// </summary>
public static partial class Ansi
{
    public const char Esc = '\x1b';
    public const string Reset = "\x1b[0m";

    [GeneratedRegex("\x1b\\[[0-9;:]*m")]
    private static partial Regex SgrRegex();

    /// <summary>Removes all SGR escape sequences from <paramref name="s"/>.</summary>
    public static string Strip(string s) => string.IsNullOrEmpty(s) ? s : SgrRegex().Replace(s, string.Empty);

    /// <summary>Display width of a single rune (code point). 0 for zero-width, 2 for wide.</summary>
    public static int RuneWidth(int r)
    {
        if (r == 0) return 0;
        if (r < 32 || (r >= 0x7f && r < 0xa0)) return 0;
        if ((r >= 0x0300 && r <= 0x036f) || r == 0x200b || r == 0xfeff) return 0;
        if (IsWide(r)) return 2;
        return 1;
    }

    private static bool IsWide(int r) =>
        (r >= 0x1100 && r <= 0x115f) ||
        (r >= 0x2e80 && r <= 0x303e) ||
        (r >= 0x3041 && r <= 0x33ff) ||
        (r >= 0x3400 && r <= 0x4dbf) ||
        (r >= 0x4e00 && r <= 0x9fff) ||
        (r >= 0xa000 && r <= 0xa4cf) ||
        (r >= 0xac00 && r <= 0xd7a3) ||
        (r >= 0xf900 && r <= 0xfaff) ||
        (r >= 0xfe30 && r <= 0xfe4f) ||
        (r >= 0xff00 && r <= 0xff60) ||
        (r >= 0xffe0 && r <= 0xffe6) ||
        (r >= 0x1f300 && r <= 0x1faff) ||
        (r >= 0x20000 && r <= 0x3fffd);

    /// <summary>Visible display width of a string, ignoring SGR escapes.</summary>
    public static int Width(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var stripped = Strip(s);
        int w = 0;
        foreach (var rune in stripped.EnumerateRunes()) w += RuneWidth(rune.Value);
        return w;
    }

    /// <summary>Number of lines in a (possibly multi-line) string.</summary>
    public static int Height(string s) => string.IsNullOrEmpty(s) ? 1 : s.Split('\n').Length;

    /// <summary>Visible width of the widest line.</summary>
    public static int MaxLineWidth(string s)
    {
        int max = 0;
        foreach (var line in s.Split('\n'))
        {
            int w = Width(line);
            if (w > max) max = w;
        }
        return max;
    }

    /// <summary>
    /// Truncates a string to <paramref name="max"/> display cells, preserving SGR
    /// escapes (and appending a reset if the cut lands inside styled text).
    /// </summary>
    public static string Truncate(string s, int max)
    {
        if (max <= 0) return string.Empty;
        if (Width(s) <= max) return s;

        var sb = new StringBuilder();
        int w = 0;
        bool open = false;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == Esc)
            {
                int m = MatchEscape(s, i);
                if (m > 0)
                {
                    var seq = s.Substring(i, m);
                    sb.Append(seq);
                    open = seq != Reset;
                    i += m;
                    continue;
                }
            }
            int rune = char.ConvertToUtf32(s, i);
            int rw = RuneWidth(rune);
            if (w + rw > max) break;
            sb.Append(char.ConvertFromUtf32(rune));
            w += rw;
            i += char.IsSurrogatePair(s, i) ? 2 : 1;
        }
        if (open) sb.Append(Reset);
        return sb.ToString();
    }

    /// <summary>Pads a string on the right with spaces to <paramref name="width"/> cells.</summary>
    public static string PadRight(string s, int width)
    {
        int diff = width - Width(s);
        return diff > 0 ? s + new string(' ', diff) : s;
    }

    /// <summary>Pads a string on the left with spaces to <paramref name="width"/> cells.</summary>
    public static string PadLeft(string s, int width)
    {
        int diff = width - Width(s);
        return diff > 0 ? new string(' ', diff) + s : s;
    }

    private static int MatchEscape(string s, int i)
    {
        if (i + 1 >= s.Length || s[i + 1] != '[') return 0;
        int j = i + 2;
        while (j < s.Length && ((s[j] >= '0' && s[j] <= '9') || s[j] == ';' || s[j] == ':')) j++;
        if (j < s.Length && s[j] == 'm') return j - i + 1;
        return 0;
    }

    /// <summary>
    /// Word-wraps plain text to <paramref name="width"/> cells. Long words are
    /// hard-broken. Intended for unstyled content.
    /// </summary>
    public static string Wrap(string s, int width)
    {
        if (width <= 0) return s;
        var outLines = new List<string>();
        foreach (var rawLine in s.Split('\n'))
        {
            if (Width(rawLine) <= width)
            {
                outLines.Add(rawLine);
                continue;
            }
            var current = new StringBuilder();
            int curW = 0;
            foreach (var word in SplitKeepingSpaces(rawLine))
            {
                int ww = Width(word);
                if (curW > 0 && curW + ww > width)
                {
                    outLines.Add(current.ToString());
                    current.Clear();
                    curW = 0;
                    if (word == " ") continue;
                }
                if (ww > width)
                {
                    if (curW > 0) { outLines.Add(current.ToString()); current.Clear(); curW = 0; }
                    var piece = word;
                    while (Width(piece) > width)
                    {
                        var head = Truncate(piece, width);
                        outLines.Add(head);
                        piece = piece[head.Length..];
                    }
                    current.Append(piece);
                    curW = Width(piece);
                    continue;
                }
                current.Append(word);
                curW += ww;
            }
            outLines.Add(current.ToString());
        }
        return string.Join("\n", outLines);
    }

    private static IEnumerable<string> SplitKeepingSpaces(string line)
    {
        var sb = new StringBuilder();
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                yield return " ";
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
