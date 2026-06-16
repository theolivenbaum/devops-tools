using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azdo.Core.Metrics;

/// <summary>Persistence shape for the Trends view's chosen sprints.</summary>
public sealed class Selection
{
    [JsonPropertyName("sprints")] public List<string> Sprints { get; set; } = new();
}

/// <summary>Load/save/filter helpers for the trend-view sprint selection.</summary>
public static class SelectionStore
{
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    /// <summary>
    /// Reads the saved sprint tags. Missing file returns null and no error — a
    /// fresh install simply hasn't picked sprints yet.
    /// </summary>
    public static List<string>? LoadSelection(string path)
    {
        if (!File.Exists(path))
            return null;
        var data = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<Selection>(data);
        return s?.Sprints;
    }

    /// <summary>
    /// Writes the chosen sprint tags atomically. Empty selection is allowed — the
    /// file is rewritten, not deleted.
    /// </summary>
    public static void SaveSelection(string path, IReadOnlyList<string> sprints)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var data = JsonSerializer.Serialize(new Selection { Sprints = sprints.ToList() }, WriteOpts);
        try
        {
            File.WriteAllText(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    /// <summary>
    /// Returns only those <paramref name="saved"/> entries that still appear in
    /// <paramref name="available"/>, preserving the order of <paramref name="saved"/>.
    /// </summary>
    public static List<string>? FilterAvailable(IReadOnlyList<string>? saved, IReadOnlyList<string> available)
    {
        if (saved == null || saved.Count == 0)
            return null;
        var set = new HashSet<string>(available);
        var outList = new List<string>(saved.Count);
        foreach (var s in saved)
            if (set.Contains(s))
                outList.Add(s);
        return outList;
    }
}
