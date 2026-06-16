namespace Azdo.Tui.Styles;

/// <summary>
/// Color palette for the application (≈ <c>styles.Theme</c>). Colors are strings:
/// either a hex value (<c>"#7c6f64"</c>) or an ANSI-256 index (<c>"33"</c>),
/// resolved by the rendering engine.
/// </summary>
public sealed record Theme
{
    public string Name { get; init; } = "";

    public string Primary { get; init; } = "";
    public string Secondary { get; init; } = "";
    public string Accent { get; init; } = "";

    public string Success { get; init; } = "";
    public string Warning { get; init; } = "";
    public string Error { get; init; } = "";
    public string Info { get; init; } = "";

    public string Background { get; init; } = "";
    public string BackgroundAlt { get; init; } = "";
    public string BackgroundSelect { get; init; } = "";

    public string Foreground { get; init; } = "";
    public string ForegroundMuted { get; init; } = "";
    public string ForegroundBold { get; init; } = "";

    public string SelectForeground { get; init; } = "";
    public string SelectBackground { get; init; } = "";

    public string Border { get; init; } = "";
    public string Link { get; init; } = "";
    public string Spinner { get; init; } = "";

    public string TabActiveForeground { get; init; } = "";
    public string TabActiveBackground { get; init; } = "";
    public string TabInactiveForeground { get; init; } = "";
    public string TabInactiveBackground { get; init; } = "";

    /// <summary>Throws <see cref="ThemeException"/> when the theme is invalid (name required).</summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(Name)) throw new ThemeException("theme name is required");
    }
}

/// <summary>Error raised for theme operations (≈ <c>styles.ThemeError</c>).</summary>
public sealed class ThemeException(string message) : Exception(message);
