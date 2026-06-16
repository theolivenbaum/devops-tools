using System.Globalization;
using Azdo.Core.AzureDevOps;

namespace Azdo.Core.Metrics;

/// <summary>
/// Tells the snapshot writer what to do for a given prev→curr state transition.
/// </summary>
public enum GapAction
{
    /// <summary>Normal single-step transition (or first observation).</summary>
    Write = 0,

    /// <summary>Multi-step forward jump or backward transition.</summary>
    NeedsFallback,
}

/// <summary>A single state change at a point in time, extracted from /updates history.</summary>
public sealed class StateTransition
{
    public string State { get; set; } = "";
    public DateTime At { get; set; }

    public StateTransition() { }

    public StateTransition(string state, DateTime at)
    {
        State = state;
        At = at;
    }
}

/// <summary>Pure transition classification and gap-row synthesis helpers.</summary>
public static class Transitions
{
    /// <summary>
    /// Decides whether the writer can append today's observation directly, or
    /// whether the gap-fallback path has to fire /updates to recover missing
    /// intermediate states.
    /// </summary>
    public static GapAction ClassifyTransition(string prev, string curr, StateConfig states)
    {
        if (string.IsNullOrEmpty(prev))
            return GapAction.Write;
        if (string.Equals((prev ?? "").Trim(), (curr ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return GapAction.Write;
        var (pi, pok) = states.IndexOf(prev!);
        var (ci, cok) = states.IndexOf(curr ?? "");
        if (!pok || !cok)
            return GapAction.NeedsFallback;
        if (ci == pi + 1)
            return GapAction.Write;
        return GapAction.NeedsFallback;
    }

    /// <summary>Converts azdevops transitions into the metrics-layer shape.</summary>
    public static List<StateTransition> FromAzDevTransitions(IReadOnlyList<WorkItemStateTransition> input)
    {
        var outList = new List<StateTransition>(input.Count);
        foreach (var t in input)
            outList.Add(new StateTransition(t.State, t.At));
        return outList;
    }

    /// <summary>
    /// Produces one daily snapshot row per calendar day strictly between
    /// <paramref name="since"/> (exclusive) and <paramref name="until"/> (exclusive),
    /// filling in the state the item was in on each of those days. The row for
    /// <paramref name="until"/> itself is NOT emitted. All emitted rows are marked
    /// Source="updates".
    /// </summary>
    public static List<Snapshot> SynthesizeGapRows(
        IReadOnlyList<StateTransition> transitions, DateTime since, DateTime until, Snapshot template)
    {
        var rows = new List<Snapshot>();
        if (transitions == null || transitions.Count == 0)
            return rows;

        var sorted = transitions.ToList();
        sorted.Sort((a, b) => a.At.CompareTo(b.At));

        var startDay = StripTime(since).AddDays(1); // exclusive on since
        var endDay = StripTime(until);              // exclusive on until

        for (var d = startDay; d < endDay; d = d.AddDays(1))
        {
            if (!StateOn(sorted, d, out var state, out var stateSince))
                continue;
            var row = template.Clone();
            row.TS = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            row.State = state;
            row.StateSince = stateSince.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            row.Source = SourceUpdatesConst;
            rows.Add(row);
        }
        return rows;
    }

    private const string SourceUpdatesConst = Snapshots.SourceUpdates;

    /// <summary>
    /// The state the item was in at the end of day <paramref name="day"/>, plus
    /// the transition timestamp that put it there. ok=false if the item had not
    /// yet been created on that day.
    /// </summary>
    private static bool StateOn(List<StateTransition> sorted, DateTime day, out string state, out DateTime since)
    {
        var endOfDay = day.Add(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (sorted[i].At <= endOfDay)
            {
                state = sorted[i].State;
                since = sorted[i].At;
                return true;
            }
        }
        state = "";
        since = default;
        return false;
    }

    private static DateTime StripTime(DateTime t) =>
        new(t.Year, t.Month, t.Day, 0, 0, 0, t.Kind);
}
