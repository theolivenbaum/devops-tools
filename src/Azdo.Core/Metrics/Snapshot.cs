using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azdo.Core.AzureDevOps;

namespace Azdo.Core.Metrics;

/// <summary>
/// One observed work item state on a particular calendar day. Append-only daily
/// rows form the basis of the Trends view.
/// </summary>
public sealed class Snapshot
{
    /// <summary>"YYYY-MM-DD".</summary>
    [JsonPropertyName("ts")] public string TS { get; set; } = "";
    [JsonPropertyName("id")] public int Id { get; set; }

    /// <summary>API project name.</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("assignedTo")] public string AssignedTo { get; set; } = "";
    [JsonPropertyName("points")] public double Points { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("iteration")] public string Iteration { get; set; } = "";

    /// <summary>RFC3339 copy of StateChangeDate.</summary>
    [JsonPropertyName("stateSince")] public string StateSince { get; set; } = "";

    /// <summary><see cref="Snapshots.SourceSnapshot"/> or <see cref="Snapshots.SourceUpdates"/>.</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    /// <summary>Shallow copy used as a template by the synthesizer.</summary>
    public Snapshot Clone() => new()
    {
        TS = TS,
        Id = Id,
        Project = Project,
        State = State,
        AssignedTo = AssignedTo,
        Points = Points,
        Tags = Tags,
        Iteration = Iteration,
        StateSince = StateSince,
        Source = Source,
    };
}

/// <summary>JSONL snapshot file I/O and helpers.</summary>
public static class Snapshots
{
    /// <summary>Marks rows observed live during the daily snapshot run.</summary>
    public const string SourceSnapshot = "snapshot";

    /// <summary>Marks rows synthesized from the /updates revision history.</summary>
    public const string SourceUpdates = "updates";

    // Serializes AppendSnapshots calls so the daily snapshot writer and the
    // one-shot backfill cannot race on the read-merge-write-rename sequence.
    private static readonly object AppendLock = new();

    private const string DateLayout = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Reads a JSONL snapshot file. Returns an empty list if the file does not
    /// exist. Malformed lines are skipped silently.
    /// </summary>
    public static List<Snapshot> ReadSnapshots(string path)
    {
        if (!File.Exists(path))
            return new List<Snapshot>();

        var outRows = new List<Snapshot>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            try
            {
                var s = JsonSerializer.Deserialize<Snapshot>(line, JsonOpts);
                if (s is not null)
                    outRows.Add(s);
            }
            catch (JsonException)
            {
                // skip malformed line
            }
        }
        return outRows;
    }

    /// <summary>
    /// Merges <paramref name="newSnaps"/> into the existing file, deduplicating by
    /// (TS, ID) where newSnaps wins, and pruning rows older than
    /// <paramref name="retention"/> measured from <paramref name="now"/>. Writes
    /// atomically via a temp file + rename.
    /// </summary>
    public static void AppendSnapshots(string path, IReadOnlyList<Snapshot> newSnaps, TimeSpan retention, DateTime now)
    {
        lock (AppendLock)
        {
            var existing = ReadSnapshots(path);

            var merged = new Dictionary<(string TS, int Id), Snapshot>(existing.Count + newSnaps.Count);
            foreach (var s in existing) merged[(s.TS, s.Id)] = s;
            foreach (var s in newSnaps) merged[(s.TS, s.Id)] = s;

            var cutoff = now - retention;
            var kept = new List<Snapshot>(merged.Count);
            foreach (var s in merged.Values)
            {
                if (!TryParseDate(s.TS, out var d)) continue;
                if (d < cutoff) continue;
                kept.Add(s);
            }

            kept.Sort((a, b) =>
            {
                if (a.TS != b.TS) return string.CompareOrdinal(a.TS, b.TS);
                return a.Id.CompareTo(b.Id);
            });

            var tmp = path + ".tmp";
            try
            {
                using (var sw = new StreamWriter(tmp, append: false))
                {
                    foreach (var s in kept)
                        sw.WriteLine(JsonSerializer.Serialize(s, JsonOpts));
                }
                // Atomic replace; File.Move overwrites with overwrite=true.
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw;
            }
        }
    }

    /// <summary>
    /// Converts the current live work-item fetch into today's snapshot rows,
    /// tagged with Source="snapshot".
    /// </summary>
    public static List<Snapshot> BuildSnapshots(IReadOnlyList<WorkItem> items, DateTime now)
    {
        var today = now.ToString(DateLayout, CultureInfo.InvariantCulture);
        var outRows = new List<Snapshot>(items.Count);
        foreach (var wi in items)
        {
            outRows.Add(new Snapshot
            {
                TS = today,
                Id = wi.Id,
                Project = wi.ProjectName,
                State = wi.Fields.State,
                AssignedTo = wi.AssignedToName(),
                Points = wi.EffectivePoints(),
                Tags = wi.TagList(),
                Iteration = wi.Fields.IterationPath,
                StateSince = Rfc3339Or(wi.Fields.StateChangeDate),
                Source = SourceSnapshot,
            });
        }
        return outRows;
    }

    /// <summary>The most recent snapshot row for each item ID, by TS lexicographic order.</summary>
    public static Dictionary<int, Snapshot> LatestPerItem(IReadOnlyList<Snapshot> snaps)
    {
        var outMap = new Dictionary<int, Snapshot>();
        foreach (var s in snaps)
        {
            if (!outMap.TryGetValue(s.Id, out var cur) || string.CompareOrdinal(s.TS, cur.TS) > 0)
                outMap[s.Id] = s;
        }
        return outMap;
    }

    /// <summary>Whether the snapshot file already contains a row dated <paramref name="today"/>.</summary>
    public static bool HasSnapshotForToday(IReadOnlyList<Snapshot> snaps, string today)
    {
        foreach (var s in snaps)
            if (s.TS == today) return true;
        return false;
    }

    internal static bool TryParseDate(string ts, out DateTime d) =>
        DateTime.TryParseExact(ts, DateLayout, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d);

    private static string Rfc3339Or(DateTime t)
    {
        if (t == default) return "";
        return t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
