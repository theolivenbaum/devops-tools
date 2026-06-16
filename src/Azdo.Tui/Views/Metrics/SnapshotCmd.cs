using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Views.Metrics;

public sealed partial class Model
{
    private const int GapFallbackConcurrency = 4;
    private static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(90);

    /// <summary>Reads the snapshot JSONL and persisted sprint selection. Tolerates missing files.</summary>
    internal static Cmd LoadSnapshotsCmd() => Commands.FromAsync(() =>
    {
        try
        {
            var snapPath = MetricsPaths.DefaultSnapshotPath();
            var snaps = Snapshots.ReadSnapshots(snapPath);
            var selPath = MetricsPaths.DefaultSelectionPath();
            List<string>? selected = null;
            try { selected = SelectionStore.LoadSelection(selPath); } catch { /* ignore */ }
            return Task.FromResult<IMsg?>(new SnapshotsLoadedMsg(snaps, selected, null));
        }
        catch (Exception e)
        {
            return Task.FromResult<IMsg?>(new SnapshotsLoadedMsg(new List<Snapshot>(), null, e));
        }
    });

    /// <summary>Persists the chosen sprint tags to disk. Fire-and-forget.</summary>
    internal static Cmd SaveSelectionCmd(IReadOnlyList<string> sprints)
    {
        var chosen = sprints.ToList();
        return Commands.FromAsync(() =>
        {
            try
            {
                var path = MetricsPaths.DefaultSelectionPath();
                SelectionStore.SaveSelection(path, chosen);
            }
            catch { /* swallow */ }
            return Task.FromResult<IMsg?>(null);
        });
    }

    /// <summary>
    /// Persists today's snapshot rows, runs gap-fallback via /updates for items
    /// whose state jumped or moved backward, prunes the file to 90 days, and
    /// returns a <see cref="SnapshotSavedMsg"/> with counts.
    /// </summary>
    internal static Cmd SaveSnapshotCmd(MultiClient? client, List<WorkItem> items, DateTime now, StateConfig states)
        => Commands.FromAsync(async () =>
    {
        if (client is null)
            return new SnapshotSavedMsg(0, 0, false, null);
        try
        {
            var path = MetricsPaths.DefaultSnapshotPath();
            MetricsPaths.EnsureSnapshotDir(path);
            var existing = Snapshots.ReadSnapshots(path);
            var today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (Snapshots.HasSnapshotForToday(existing, today))
                return new SnapshotSavedMsg(0, 0, true, null);

            var todaySnaps = Snapshots.BuildSnapshots(items, now);
            var latest = Snapshots.LatestPerItem(existing);

            var (gapRows, skipped) = await RunGapFallbackAsync(client, todaySnaps, latest, now, states).ConfigureAwait(false);

            var all = new List<Snapshot>(gapRows.Count + todaySnaps.Count);
            all.AddRange(gapRows);
            all.AddRange(todaySnaps);
            Snapshots.AppendSnapshots(path, all, SnapshotRetention, now);
            return new SnapshotSavedMsg(all.Count, skipped, false, null);
        }
        catch (Exception e)
        {
            return new SnapshotSavedMsg(0, 0, false, e);
        }
    });

    private static async Task<(List<Snapshot> Rows, int Skipped)> RunGapFallbackAsync(
        MultiClient client, List<Snapshot> todaySnaps, Dictionary<int, Snapshot> latest, DateTime now, StateConfig states)
    {
        var needs = new List<(Snapshot Today, Snapshot Prev)>();
        foreach (var snap in todaySnaps)
        {
            latest.TryGetValue(snap.Id, out var prev);
            prev ??= new Snapshot();
            if (Transitions.ClassifyTransition(prev.State, snap.State, states) == GapAction.NeedsFallback)
                needs.Add((snap, prev));
        }
        if (needs.Count == 0)
            return (new List<Snapshot>(), 0);

        var rowsPer = new List<Snapshot>[needs.Count];
        var skippedFlags = new bool[needs.Count];

        using var sem = new SemaphoreSlim(GapFallbackConcurrency);
        var tasks = new List<Task>();
        for (int idx = 0; idx < needs.Count; idx++)
        {
            int i = idx;
            var n = needs[i];
            await sem.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var cli = client.ClientFor(n.Today.Project);
                    if (cli is null) { skippedFlags[i] = true; return; }
                    List<WorkItemStateTransition> txns;
                    try { txns = await cli.WorkItemUpdatesAsync(n.Today.Id).ConfigureAwait(false); }
                    catch { skippedFlags[i] = true; return; }

                    DateTime prevDate;
                    if (!Snapshots.TryParseDate(n.Prev.TS, out prevDate))
                        prevDate = now.AddDays(-90);

                    var template = n.Today.Clone();
                    template.TS = "";
                    template.State = "";
                    template.Source = Snapshots.SourceUpdates;
                    rowsPer[i] = Transitions.SynthesizeGapRows(
                        Transitions.FromAzDevTransitions(txns), prevDate, now, template);
                }
                finally { sem.Release(); }
            }));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var all = new List<Snapshot>();
        int skipped = 0;
        for (int i = 0; i < needs.Count; i++)
        {
            if (skippedFlags[i]) { skipped++; continue; }
            if (rowsPer[i] is not null) all.AddRange(rowsPer[i]);
        }
        return (all, skipped);
    }
}
