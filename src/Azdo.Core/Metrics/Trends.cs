using System.Globalization;

namespace Azdo.Core.Metrics;

/// <summary>The time range during which a sprint tag was active in the snapshot file.</summary>
public sealed class SprintWindow
{
    public string Tag { get; set; } = "";
    public DateTime Start { get; set; }

    /// <summary>Inclusive.</summary>
    public DateTime End { get; set; }

    public SprintWindow() { }

    public SprintWindow(string tag, DateTime start, DateTime end)
    {
        Tag = tag;
        Start = start;
        End = end;
    }
}

/// <summary>One row of the sprint-on-sprint comparison.</summary>
public sealed class TrendRow
{
    public string User { get; set; } = "";
    public List<TrendCell> Cells { get; set; } = new();
}

/// <summary>The four numbers rendered in each sprint's column for one user, plus the overload flag.</summary>
public sealed class TrendCell
{
    public double Points { get; set; }
    public double AvgWIP { get; set; }
    public int StuckCount { get; set; }
    public TimeSpan CycleTime { get; set; }
    public bool OverloadedAnyDay { get; set; }
}

/// <summary>Pure trend windowing and per-user × per-sprint aggregation.</summary>
public static class Trends
{
    private const string BucketActive = "active";
    private const string BucketRFT = "rft";
    private const string BucketClosed = "closed";

    /// <summary>
    /// Returns the time range for <paramref name="tag"/> derived purely from
    /// snapshot rows. ok=false if the tag is never seen.
    /// </summary>
    public static (SprintWindow Window, bool Ok) DeriveSprintWindow(
        IReadOnlyList<Snapshot> snaps, string tag, DateTime now, StateConfig states)
    {
        string earliest = "", latestOpen = "", latestClosed = "", latestAny = "";
        foreach (var s in snaps)
        {
            if (!HasTag(s, tag)) continue;
            if (earliest == "" || string.CompareOrdinal(s.TS, earliest) < 0) earliest = s.TS;
            if (latestAny == "" || string.CompareOrdinal(s.TS, latestAny) > 0) latestAny = s.TS;
            if (states.IsClosed(s.State))
            {
                if (latestClosed == "" || string.CompareOrdinal(s.TS, latestClosed) > 0) latestClosed = s.TS;
            }
            else
            {
                if (latestOpen == "" || string.CompareOrdinal(s.TS, latestOpen) > 0) latestOpen = s.TS;
            }
        }
        if (earliest == "")
            return (new SprintWindow(), false);

        var start = ParseDay(earliest);
        DateTime end;
        if (latestOpen != "" && string.CompareOrdinal(latestOpen, latestClosed) > 0)
            end = now; // still in flight
        else if (latestOpen != "")
            end = ParseDay(latestOpen);
        else
            end = ParseDay(latestAny); // all observations closed

        return (new SprintWindow(tag, start, end), true);
    }

    /// <summary>
    /// Folds snapshot rows into per-user × per-sprint cells. For each window, only
    /// rows whose Tags include the window's Tag AND whose snapshot TS falls inside
    /// [Start, End] contribute.
    /// </summary>
    public static List<TrendRow> TrendAggregate(
        IReadOnlyList<Snapshot> snaps, IReadOnlyList<SprintWindow> windows, Thresholds th, DateTime now)
    {
        var rows = new List<TrendRow>();
        if (windows.Count == 0)
            return rows;

        var users = new HashSet<string>();
        var store = new Dictionary<(string User, int WIdx), Acc>();

        Acc GetAcc(string user, int wIdx)
        {
            var k = (user, wIdx);
            if (!store.TryGetValue(k, out var a))
            {
                a = new Acc();
                store[k] = a;
            }
            return a;
        }

        var states = th.States;

        foreach (var s in snaps)
        {
            if (string.IsNullOrEmpty(s.AssignedTo) || s.AssignedTo == "-")
                continue;
            if (!Snapshots.TryParseDate(s.TS, out var d))
                continue;

            string bucket;
            if (states.IsActive(s.State)) bucket = BucketActive;
            else if (states.IsRFT(s.State)) bucket = BucketRFT;
            else if (states.IsClosed(s.State)) bucket = BucketClosed;
            else continue;

            for (int wIdx = 0; wIdx < windows.Count; wIdx++)
            {
                var w = windows[wIdx];
                if (!HasTag(s, w.Tag)) continue;
                if (d < StripTime(w.Start) || d > StripTime(w.End)) continue;

                users.Add(s.AssignedTo);
                var a = GetAcc(s.AssignedTo, wIdx);

                if (bucket == BucketActive || bucket == BucketRFT)
                {
                    if (!a.StateDays.TryGetValue(s.Id, out var byBucket))
                    {
                        byBucket = new Dictionary<string, HashSet<string>>();
                        a.StateDays[s.Id] = byBucket;
                    }
                    if (!byBucket.TryGetValue(bucket, out var dayset))
                    {
                        dayset = new HashSet<string>();
                        byBucket[bucket] = dayset;
                    }
                    dayset.Add(s.TS);

                    if (!a.DailyWIP.TryGetValue(s.TS, out var ids))
                    {
                        ids = new HashSet<int>();
                        a.DailyWIP[s.TS] = ids;
                    }
                    ids.Add(s.Id);

                    if (bucket == BucketActive)
                    {
                        if (!a.ClosedItemFirst.TryGetValue(s.Id, out var cur) || d < cur)
                            a.ClosedItemFirst[s.Id] = d;
                    }
                }
                else // closed
                {
                    if (!a.ClosedItemDone.TryGetValue(s.Id, out var cur) || d < cur)
                        a.ClosedItemDone[s.Id] = d;
                    a.ClosedPoints[s.Id] = s.Points;
                }
            }
        }

        var names = users.ToList();
        names.Sort(StringComparer.Ordinal);

        foreach (var u in names)
        {
            var row = new TrendRow { User = u, Cells = new List<TrendCell>(windows.Count) };
            for (int wIdx = 0; wIdx < windows.Count; wIdx++)
                row.Cells.Add(new TrendCell());

            for (int wIdx = 0; wIdx < windows.Count; wIdx++)
            {
                if (!store.TryGetValue((u, wIdx), out var a))
                    continue;

                int total = 0, days = 0, peak = 0;
                foreach (var ids in a.DailyWIP.Values)
                {
                    int n = ids.Count;
                    if (n > peak) peak = n;
                    total += n;
                    days++;
                }
                double avg = days > 0 ? (double)total / days : 0.0;

                var sumDur = TimeSpan.Zero;
                int closedN = 0;
                foreach (var kv in a.ClosedItemDone)
                {
                    if (!a.ClosedItemFirst.TryGetValue(kv.Key, out var startD))
                        continue;
                    var dur = kv.Value - startD;
                    if (dur < TimeSpan.Zero) continue;
                    sumDur += dur;
                    closedN++;
                }
                var cy = closedN > 0 ? TimeSpan.FromTicks(sumDur.Ticks / closedN) : TimeSpan.Zero;

                var stuckSet = new HashSet<int>();
                foreach (var kv in a.StateDays)
                {
                    if (th.ActiveStaleDays > 0
                        && kv.Value.TryGetValue(BucketActive, out var ad) && ad.Count > th.ActiveStaleDays)
                        stuckSet.Add(kv.Key);
                    if (th.RFTStaleDays > 0
                        && kv.Value.TryGetValue(BucketRFT, out var rd) && rd.Count > th.RFTStaleDays)
                        stuckSet.Add(kv.Key);
                }

                double points = 0;
                foreach (var p in a.ClosedPoints.Values) points += p;

                row.Cells[wIdx] = new TrendCell
                {
                    Points = points,
                    AvgWIP = avg,
                    StuckCount = stuckSet.Count,
                    CycleTime = cy,
                    OverloadedAnyDay = th.WIPLimit > 0 && peak > th.WIPLimit,
                };
            }
            rows.Add(row);
        }
        return rows;
    }

    internal static bool HasTag(Snapshot s, string tag)
    {
        foreach (var t in s.Tags)
            if (t == tag) return true;
        return false;
    }

    private static DateTime ParseDay(string ts) =>
        DateTime.ParseExact(ts, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static DateTime StripTime(DateTime t) =>
        new(t.Year, t.Month, t.Day, 0, 0, 0, t.Kind);

    private sealed class Acc
    {
        public Dictionary<int, double> ClosedPoints { get; } = new();
        public Dictionary<string, HashSet<int>> DailyWIP { get; } = new();
        public Dictionary<int, Dictionary<string, HashSet<string>>> StateDays { get; } = new();
        public Dictionary<int, DateTime> ClosedItemFirst { get; } = new();
        public Dictionary<int, DateTime> ClosedItemDone { get; } = new();
    }
}
