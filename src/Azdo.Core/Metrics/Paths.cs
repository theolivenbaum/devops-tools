namespace Azdo.Core.Metrics;

/// <summary>Resolves the base directory and standard file locations for metrics persistence.</summary>
public static class MetricsPaths
{
    /// <summary>
    /// Base directory for metrics persistence files (snapshots, sprint selection,
    /// backfill marker). AZDO_CONFIG_DIR, when set, wins outright (demo mode points
    /// it at a temp directory). Otherwise the standard ~/.config/azdo-tui location.
    /// </summary>
    public static string ConfigDir()
    {
        var dir = Environment.GetEnvironmentVariable("AZDO_CONFIG_DIR");
        if (!string.IsNullOrEmpty(dir))
            return dir;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "azdo-tui");
    }

    /// <summary>Standard snapshot file location (metrics.jsonl).</summary>
    public static string DefaultSnapshotPath() => Path.Combine(ConfigDir(), "metrics.jsonl");

    /// <summary>Standard trend-view sprint selection location (metrics-selection.json).</summary>
    public static string DefaultSelectionPath() => Path.Combine(ConfigDir(), "metrics-selection.json");

    /// <summary>Standard backfill marker location (.metrics-backfill-done).</summary>
    public static string DefaultBackfillMarkerPath() => Path.Combine(ConfigDir(), ".metrics-backfill-done");

    /// <summary>Creates the directory holding the snapshot file if it doesn't exist. Safe to repeat.</summary>
    public static void EnsureSnapshotDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
