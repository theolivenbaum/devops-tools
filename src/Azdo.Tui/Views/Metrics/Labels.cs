namespace Azdo.Tui.Views.Metrics;

/// <summary>
/// State-label helpers: resolve the three column-header labels from the
/// configured state names, honoring explicit <c>metrics.state_labels</c>
/// overrides.
/// </summary>
public static class Labels
{
    /// <summary>
    /// The override if non-empty, else an auto-derived abbreviation from
    /// <paramref name="name"/>:
    /// multi-word names → lowercase initials ("Ready for Test" → "rft");
    /// single-word names → lowercase as-is ("Active" → "active").
    /// </summary>
    public static string LabelFor(string name, string @override)
    {
        if (!string.IsNullOrWhiteSpace(@override))
            return @override;
        var words = (name ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "";
        if (words.Length == 1)
            return words[0].ToLowerInvariant();
        var chars = new char[words.Length];
        for (int i = 0; i < words.Length; i++)
            chars[i] = char.ToLowerInvariant(words[i][0]);
        return new string(chars);
    }

    /// <summary>
    /// Same as <see cref="LabelFor"/> but with the first letter upper-cased so
    /// column headers feel like proper titles. The override survives intact.
    /// </summary>
    public static string LabelTitle(string name, string @override)
    {
        if (!string.IsNullOrWhiteSpace(@override))
            return @override;
        return TitleCase(LabelFor(name, ""));
    }

    /// <summary>Upper-cases the first character of an ASCII string.</summary>
    public static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
