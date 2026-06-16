using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azdo.Tui.Styles;

/// <summary>
/// Built-in theme registry plus custom-theme loading (≈ <c>styles/themes.go</c>).
/// </summary>
public static class Themes
{
    private static readonly Dictionary<string, Theme> Registry;

    static Themes()
    {
        // Built in a static constructor so the built-in theme fields (declared
        // below) are guaranteed initialized before they're inserted.
        Registry = new Dictionary<string, Theme>
        {
            ["dark"] = Dark,
            ["gruvbox"] = Gruvbox,
            ["nord"] = Nord,
            ["dracula"] = Dracula,
            ["catppuccin"] = Catppuccin,
            ["github"] = GitHub,
            ["retro"] = Retro,
            ["monokai"] = Monokai,
        };
    }

    /// <summary>Returns a theme by name, or throws <see cref="ThemeException"/> if absent.</summary>
    public static Theme GetByName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !Registry.TryGetValue(name, out var t))
            throw new ThemeException("theme not found");
        return t;
    }

    public static bool TryGetByName(string? name, out Theme theme)
    {
        if (!string.IsNullOrEmpty(name) && Registry.TryGetValue(name, out var t))
        {
            theme = t;
            return true;
        }
        theme = Default;
        return false;
    }

    public static Theme GetByNameWithFallback(string name) => TryGetByName(name, out var t) ? t : Default;

    /// <summary>The default theme is Dracula.</summary>
    public static Theme Default => Dracula;

    public static IReadOnlyList<string> ListAvailable() => Registry.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static string GetThemesDirectoryPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "azdo-tui", "themes");
    }

    /// <summary>Registers a custom theme after validation.</summary>
    public static void Register(Theme theme)
    {
        theme.Validate();
        Registry[theme.Name] = theme;
    }

    private sealed class ThemeJson
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("primary")] public string Primary { get; set; } = "";
        [JsonPropertyName("secondary")] public string Secondary { get; set; } = "";
        [JsonPropertyName("accent")] public string Accent { get; set; } = "";
        [JsonPropertyName("success")] public string Success { get; set; } = "";
        [JsonPropertyName("warning")] public string Warning { get; set; } = "";
        [JsonPropertyName("error")] public string Error { get; set; } = "";
        [JsonPropertyName("info")] public string Info { get; set; } = "";
        [JsonPropertyName("background")] public string Background { get; set; } = "";
        [JsonPropertyName("background_alt")] public string BackgroundAlt { get; set; } = "";
        [JsonPropertyName("background_select")] public string BackgroundSelect { get; set; } = "";
        [JsonPropertyName("foreground")] public string Foreground { get; set; } = "";
        [JsonPropertyName("foreground_muted")] public string ForegroundMuted { get; set; } = "";
        [JsonPropertyName("foreground_bold")] public string ForegroundBold { get; set; } = "";
        [JsonPropertyName("select_foreground")] public string SelectForeground { get; set; } = "";
        [JsonPropertyName("select_background")] public string SelectBackground { get; set; } = "";
        [JsonPropertyName("border")] public string Border { get; set; } = "";
        [JsonPropertyName("link")] public string Link { get; set; } = "";
        [JsonPropertyName("spinner")] public string Spinner { get; set; } = "";
        [JsonPropertyName("tab_active_foreground")] public string TabActiveForeground { get; set; } = "";
        [JsonPropertyName("tab_active_background")] public string TabActiveBackground { get; set; } = "";
        [JsonPropertyName("tab_inactive_foreground")] public string TabInactiveForeground { get; set; } = "";
        [JsonPropertyName("tab_inactive_background")] public string TabInactiveBackground { get; set; } = "";
    }

    /// <summary>Parses a theme from JSON; throws on invalid JSON or validation failure.</summary>
    public static Theme LoadFromJson(string json)
    {
        var tj = JsonSerializer.Deserialize<ThemeJson>(json) ?? throw new ThemeException("failed to parse theme JSON");
        var theme = new Theme
        {
            Name = tj.Name, Primary = tj.Primary, Secondary = tj.Secondary, Accent = tj.Accent,
            Success = tj.Success, Warning = tj.Warning, Error = tj.Error, Info = tj.Info,
            Background = tj.Background, BackgroundAlt = tj.BackgroundAlt, BackgroundSelect = tj.BackgroundSelect,
            Foreground = tj.Foreground, ForegroundMuted = tj.ForegroundMuted, ForegroundBold = tj.ForegroundBold,
            SelectForeground = tj.SelectForeground, SelectBackground = tj.SelectBackground,
            Border = tj.Border, Link = tj.Link, Spinner = tj.Spinner,
            TabActiveForeground = tj.TabActiveForeground, TabActiveBackground = tj.TabActiveBackground,
            TabInactiveForeground = tj.TabInactiveForeground, TabInactiveBackground = tj.TabInactiveBackground,
        };
        theme.Validate();
        return theme;
    }

    /// <summary>Loads all <c>*.json</c> themes from a directory; invalid files are skipped.</summary>
    public static int LoadCustomThemesFromDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int loaded = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var theme = LoadFromJson(File.ReadAllText(file));
                Register(theme);
                loaded++;
            }
            catch { /* skip invalid theme files */ }
        }
        return loaded;
    }

    public static readonly Theme Dark = new()
    {
        Name = "dark",
        Primary = "33", Secondary = "39", Accent = "212",
        Success = "42", Warning = "214", Error = "196", Info = "33",
        Background = "236", BackgroundAlt = "235", BackgroundSelect = "57",
        Foreground = "252", ForegroundMuted = "243", ForegroundBold = "255",
        SelectForeground = "229", SelectBackground = "57",
        Border = "240", Link = "81", Spinner = "205",
        TabActiveForeground = "229", TabActiveBackground = "57", TabInactiveForeground = "252",
    };

    public static readonly Theme Gruvbox = new()
    {
        Name = "gruvbox",
        Primary = "#458588", Secondary = "#689d6a", Accent = "#d3869b",
        Success = "#b8bb26", Warning = "#fabd2f", Error = "#fb4934", Info = "#83a598",
        Background = "#282828", BackgroundAlt = "#1d2021", BackgroundSelect = "#3c3836",
        Foreground = "#ebdbb2", ForegroundMuted = "#928374", ForegroundBold = "#fbf1c7",
        SelectForeground = "#fabd2f", SelectBackground = "#504945",
        Border = "#504945", Link = "#83a598", Spinner = "#d3869b",
        TabActiveForeground = "#fabd2f", TabActiveBackground = "#504945", TabInactiveForeground = "#a89984",
    };

    public static readonly Theme Nord = new()
    {
        Name = "nord",
        Primary = "#81a1c1", Secondary = "#88c0d0", Accent = "#b48ead",
        Success = "#a3be8c", Warning = "#ebcb8b", Error = "#bf616a", Info = "#5e81ac",
        Background = "#2e3440", BackgroundAlt = "#3b4252", BackgroundSelect = "#434c5e",
        Foreground = "#eceff4", ForegroundMuted = "#4c566a", ForegroundBold = "#eceff4",
        SelectForeground = "#eceff4", SelectBackground = "#434c5e",
        Border = "#4c566a", Link = "#88c0d0", Spinner = "#b48ead",
        TabActiveForeground = "#eceff4", TabActiveBackground = "#5e81ac", TabInactiveForeground = "#d8dee9",
    };

    public static readonly Theme Dracula = new()
    {
        Name = "dracula",
        Primary = "#bd93f9", Secondary = "#8be9fd", Accent = "#ff79c6",
        Success = "#50fa7b", Warning = "#f1fa8c", Error = "#ff5555", Info = "#8be9fd",
        Background = "#282a36", BackgroundAlt = "#21222c", BackgroundSelect = "#44475a",
        Foreground = "#f8f8f2", ForegroundMuted = "#6272a4", ForegroundBold = "#f8f8f2",
        SelectForeground = "#f8f8f2", SelectBackground = "#44475a",
        Border = "#6272a4", Link = "#8be9fd", Spinner = "#ff79c6",
        TabActiveForeground = "#f8f8f2", TabActiveBackground = "#bd93f9", TabInactiveForeground = "#f8f8f2",
    };

    public static readonly Theme Catppuccin = new()
    {
        Name = "catppuccin",
        Primary = "#89b4fa", Secondary = "#94e2d5", Accent = "#cba6f7",
        Success = "#a6e3a1", Warning = "#f9e2af", Error = "#f38ba8", Info = "#89dceb",
        Background = "#1e1e2e", BackgroundAlt = "#181825", BackgroundSelect = "#313244",
        Foreground = "#cdd6f4", ForegroundMuted = "#6c7086", ForegroundBold = "#cdd6f4",
        SelectForeground = "#cdd6f4", SelectBackground = "#45475a",
        Border = "#585b70", Link = "#89dceb", Spinner = "#f5c2e7",
        TabActiveForeground = "#1e1e2e", TabActiveBackground = "#89b4fa", TabInactiveForeground = "#bac2de",
    };

    public static readonly Theme GitHub = new()
    {
        Name = "github",
        Primary = "#58a6ff", Secondary = "#56d4dd", Accent = "#bc8cff",
        Success = "#3fb950", Warning = "#d29922", Error = "#f85149", Info = "#58a6ff",
        Background = "#0d1117", BackgroundAlt = "#161b22", BackgroundSelect = "#21262d",
        Foreground = "#c9d1d9", ForegroundMuted = "#8b949e", ForegroundBold = "#f0f6fc",
        SelectForeground = "#f0f6fc", SelectBackground = "#264f78",
        Border = "#30363d", Link = "#58a6ff", Spinner = "#bc8cff",
        TabActiveForeground = "#f0f6fc", TabActiveBackground = "#58a6ff", TabInactiveForeground = "#8b949e",
    };

    public static readonly Theme Retro = new()
    {
        Name = "retro",
        Primary = "#00ff41", Secondary = "#00cc33", Accent = "#39ff14",
        Success = "#00ff41", Warning = "#ccff00", Error = "#ff003c", Info = "#00cc33",
        Background = "#0a0a0a", BackgroundAlt = "#050505", BackgroundSelect = "#003300",
        Foreground = "#00ff41", ForegroundMuted = "#336633", ForegroundBold = "#66ff66",
        SelectForeground = "#0a0a0a", SelectBackground = "#00ff41",
        Border = "#004400", Link = "#39ff14", Spinner = "#00ff41",
        TabActiveForeground = "#0a0a0a", TabActiveBackground = "#00ff41", TabInactiveForeground = "#336633",
    };

    public static readonly Theme Monokai = new()
    {
        Name = "monokai",
        Primary = "#66d9ef", Secondary = "#a6e22e", Accent = "#f92672",
        Success = "#a6e22e", Warning = "#e6db74", Error = "#f92672", Info = "#66d9ef",
        Background = "#272822", BackgroundAlt = "#1e1f1c", BackgroundSelect = "#49483e",
        Foreground = "#f8f8f2", ForegroundMuted = "#75715e", ForegroundBold = "#f8f8f2",
        SelectForeground = "#f8f8f2", SelectBackground = "#49483e",
        Border = "#75715e", Link = "#66d9ef", Spinner = "#ae81ff",
        TabActiveForeground = "#f8f8f2", TabActiveBackground = "#f92672", TabInactiveForeground = "#75715e",
    };
}
