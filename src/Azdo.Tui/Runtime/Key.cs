namespace Azdo.Tui.Runtime;

/// <summary>
/// A key press (≈ <c>tea.KeyMsg</c>). <see cref="ToString"/> yields the same
/// canonical names Bubble Tea uses (<c>"up"</c>, <c>"enter"</c>, <c>"ctrl+c"</c>,
/// <c>"pgup"</c>, or the literal rune for printable keys) so view code can match
/// on string keys exactly like the Go original.
/// </summary>
public sealed class KeyMsg : IMsg
{
    public string Key { get; }
    public char Rune { get; }
    public bool IsRune { get; }

    private KeyMsg(string key, char rune = '\0', bool isRune = false)
    {
        Key = key; Rune = rune; IsRune = isRune;
    }

    public override string ToString() => Key;

    public static KeyMsg Rune_(char c) => new(c.ToString(), c, true);
    public static KeyMsg Named(string name) => new(name);

    public static KeyMsg FromConsole(ConsoleKeyInfo k)
    {
        bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;

        switch (k.Key)
        {
            case ConsoleKey.UpArrow: return Named("up");
            case ConsoleKey.DownArrow: return Named("down");
            case ConsoleKey.LeftArrow: return Named("left");
            case ConsoleKey.RightArrow: return Named("right");
            case ConsoleKey.Enter: return Named("enter");
            case ConsoleKey.Escape: return Named("esc");
            case ConsoleKey.Tab: return Named(shift ? "shift+tab" : "tab");
            case ConsoleKey.PageUp: return Named("pgup");
            case ConsoleKey.PageDown: return Named("pgdown");
            case ConsoleKey.Home: return Named("home");
            case ConsoleKey.End: return Named("end");
            case ConsoleKey.Backspace: return Named("backspace");
            case ConsoleKey.Delete: return Named("delete");
            case ConsoleKey.Spacebar: return new KeyMsg(" ", ' ', true);
        }

        char ch = k.KeyChar;
        if (ctrl)
        {
            // Control characters arrive as 0x01..0x1a for ctrl+a..ctrl+z.
            if (ch is >= (char)1 and <= (char)26)
                return Named("ctrl+" + (char)('a' + (ch - 1)));
            if (k.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
                return Named("ctrl+" + char.ToLowerInvariant((char)k.Key));
        }

        if (ch != '\0' && !char.IsControl(ch))
            return Rune_(ch);

        return Named(k.Key.ToString().ToLowerInvariant());
    }
}
