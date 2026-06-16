using SpectreColor = Spectre.Console.Color;

namespace Azdo.Tui.Rendering;

/// <summary>
/// Resolves Lip Gloss-style color strings (hex <c>"#7c6f64"</c> or ANSI-256
/// index <c>"33"</c>) into SGR escape parameters. Hex parsing reuses
/// Spectre.Console's <see cref="SpectreColor"/>.
/// </summary>
public readonly struct TerminalColor
{
    private readonly bool _isTrueColor;
    private readonly byte _r, _g, _b;
    private readonly int _ansi256;
    public bool IsSet { get; }

    private TerminalColor(bool trueColor, byte r, byte g, byte b, int ansi256, bool isSet)
    {
        _isTrueColor = trueColor;
        _r = r; _g = g; _b = b;
        _ansi256 = ansi256;
        IsSet = isSet;
    }

    public static readonly TerminalColor None = default;

    public static TerminalColor Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return None;
        value = value.Trim();
        if (value[0] == '#')
        {
            try
            {
                var c = SpectreColor.FromHex(value);
                return new TerminalColor(true, c.R, c.G, c.B, 0, true);
            }
            catch { return None; }
        }
        if (int.TryParse(value, out var idx) && idx is >= 0 and <= 255)
            return new TerminalColor(false, 0, 0, 0, idx, true);
        return None;
    }

    /// <summary>SGR parameter list for a foreground color, e.g. "38;2;124;111;100".</summary>
    public string ForegroundParams() => _isTrueColor ? $"38;2;{_r};{_g};{_b}" : $"38;5;{_ansi256}";

    /// <summary>SGR parameter list for a background color.</summary>
    public string BackgroundParams() => _isTrueColor ? $"48;2;{_r};{_g};{_b}" : $"48;5;{_ansi256}";
}
