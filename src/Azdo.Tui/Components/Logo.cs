using Azdo.Tui.Rendering;
using Azdo.Tui.Styles;

namespace Azdo.Tui.Components;

/// <summary>ASCII-art "azdo" logo (≈ <c>components.Logo</c>).</summary>
public sealed class Logo(Styles.Styles styles)
{
    public static readonly string[] Art =
    {
        "╔═╗╔═╗╔╦╗╔═╗",
        "╠═╣╔═╝ ║║║ ║",
        "╩ ╩╚═╝═╩╝╚═╝",
    };

    public int Height => Art.Length;

    public string View()
        => Style.New().Foreground(styles.Theme.Primary).Bold().Render(string.Join("\n", Art));
}
