using Azdo.Tui.Runtime;

namespace Azdo.Tui.Components;

/// <summary>
/// A minimal single-line text input (≈ bubbles <c>textinput.Model</c>): rune
/// entry, backspace/delete, cursor movement, char limit, and a rendered caret.
/// </summary>
public sealed class TextInput
{
    private readonly System.Text.StringBuilder _value = new();
    private int _cursor;

    public string Prompt { get; set; } = "> ";
    public string Placeholder { get; set; } = "";
    public int CharLimit { get; set; } = 0; // 0 = unlimited
    public bool Focused { get; private set; }
    public bool MaskInput { get; set; } = false; // for PAT entry

    public string Value
    {
        get => _value.ToString();
        set { _value.Clear(); _value.Append(value); _cursor = _value.Length; }
    }

    public void Focus() => Focused = true;
    public void Blur() => Focused = false;
    public void Reset() { _value.Clear(); _cursor = 0; }

    /// <summary>Handles a key press while focused. Returns <c>true</c> when the value changed.</summary>
    public bool HandleKey(KeyMsg key)
    {
        if (!Focused) return false;
        switch (key.Key)
        {
            case "backspace":
                if (_cursor > 0) { _value.Remove(_cursor - 1, 1); _cursor--; return true; }
                return false;
            case "delete":
                if (_cursor < _value.Length) { _value.Remove(_cursor, 1); return true; }
                return false;
            case "left":
                if (_cursor > 0) _cursor--;
                return false;
            case "right":
                if (_cursor < _value.Length) _cursor++;
                return false;
            case "home":
                _cursor = 0; return false;
            case "end":
                _cursor = _value.Length; return false;
            default:
                if (key.IsRune)
                {
                    if (CharLimit > 0 && _value.Length >= CharLimit) return false;
                    _value.Insert(_cursor, key.Rune);
                    _cursor++;
                    return true;
                }
                return false;
        }
    }

    public string View()
    {
        var text = MaskInput ? new string('•', _value.Length) : _value.ToString();
        if (text.Length == 0 && !Focused && Placeholder.Length > 0)
            return Prompt + Placeholder;
        if (!Focused) return Prompt + text;

        // Render a caret at the cursor position.
        int caret = MaskInput ? text.Length : _cursor;
        var withCaret = caret >= text.Length
            ? text + "▏"
            : text[..caret] + "▏" + text[caret..];
        return Prompt + withCaret;
    }
}
