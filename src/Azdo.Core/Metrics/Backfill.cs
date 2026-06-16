namespace Azdo.Core.Metrics;

/// <summary>One-shot backfill marker-file helpers.</summary>
public static class Backfill
{
    /// <summary>Whether the marker file exists. A missing file is not an error.</summary>
    public static bool BackfillAlreadyDone(string path) => File.Exists(path);

    /// <summary>
    /// Creates the marker file (idempotent), creating any missing parent directories.
    /// </summary>
    public static void MarkBackfillDone(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        // Open-or-create then close, leaving an empty marker file.
        using var _ = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write);
    }
}
