using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Views.Metrics;

/// <summary>The dependency-injection seam for backfill /updates fetches (used by tests).</summary>
public delegate Task<(List<WorkItemStateTransition>? Txns, Exception? Err)> BackfillFetcher(string project, int id);

public sealed partial class Model
{
    private const int BackfillWindowDays = 90;
    private const int BackfillConcurrency = 4;

    /// <summary>Sentinel returned when the MultiClient has no client for the requested project.</summary>
    internal static readonly Exception ErrBackfillNoClient = new InvalidOperationException("no client for project");

    /// <summary>
    /// Fans /updates fetches across <paramref name="items"/> with bounded
    /// concurrency and synthesizes per-day snapshot rows back to (now - 90 days).
    /// Returns the union of all synthesized rows plus a count of items whose fetch
    /// failed. Pure-ish: HTTP injection via <paramref name="fetch"/>, no file I/O.
    /// </summary>
    public static async Task<(List<Snapshot> Rows, int Skipped)> BuildBackfillRowsAsync(
        IReadOnlyList<WorkItem> items, BackfillFetcher fetch, DateTime now, int concurrency)
    {
        if (items.Count == 0)
            return (new List<Snapshot>(), 0);
        if (concurrency < 1) concurrency = 1;

        var rowsPer = new List<Snapshot>[items.Count];
        var skippedFlags = new bool[items.Count];
        var since = now.AddDays(-BackfillWindowDays);

        using var sem = new SemaphoreSlim(concurrency);
        var tasks = new List<Task>();
        for (int idx = 0; idx < items.Count; idx++)
        {
            int i = idx;
            var wi = items[i];
            await sem.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var (txns, err) = await fetch(wi.ProjectName, wi.Id).ConfigureAwait(false);
                    if (err is not null || txns is null) { skippedFlags[i] = true; return; }
                    var template = SnapshotTemplate(wi);
                    rowsPer[i] = Transitions.SynthesizeGapRows(
                        Transitions.FromAzDevTransitions(txns), since, now, template);
                }
                finally { sem.Release(); }
            }));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var all = new List<Snapshot>();
        int skipped = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (skippedFlags[i]) { skipped++; continue; }
            if (rowsPer[i] is not null) all.AddRange(rowsPer[i]);
        }
        return (all, skipped);
    }

    /// <summary>Per-item static fields used as the row template passed to SynthesizeGapRows.</summary>
    internal static Snapshot SnapshotTemplate(WorkItem wi) => new()
    {
        Id = wi.Id,
        Project = wi.ProjectName,
        AssignedTo = wi.AssignedToName(),
        Points = wi.EffectivePoints(),
        Tags = wi.TagList(),
        Iteration = wi.Fields.IterationPath,
        Source = Snapshots.SourceUpdates,
    };

    /// <summary>
    /// Entry point for the one-shot backfill. Checks the marker and short-circuits
    /// if already done; otherwise fetches candidate items, fans /updates fetches
    /// out, appends synthesized rows, and writes the marker.
    /// </summary>
    internal static Cmd RunBackfillCmd(MultiClient? client, DateTime now, StateConfig states)
        => Commands.FromAsync(async () =>
    {
        if (client is null)
            return new BackfillDoneMsg(0, 0, 0, false, null);
        try
        {
            var markerPath = MetricsPaths.DefaultBackfillMarkerPath();
            if (Backfill.BackfillAlreadyDone(markerPath))
                return new BackfillDoneMsg(0, 0, 0, true, null);

            var since = now.AddDays(-BackfillWindowDays);
            var items = await client.MetricsWorkItemsAsync(since, ToMetricsStateNames(states)).ConfigureAwait(false);

            BackfillFetcher fetch = async (project, id) =>
            {
                var cli = client.ClientFor(project);
                if (cli is null) return (null, ErrBackfillNoClient);
                try { return (await cli.WorkItemUpdatesAsync(id).ConfigureAwait(false), null); }
                catch (Exception e) { return (null, e); }
            };

            var (rows, skipped) = await BuildBackfillRowsAsync(items, fetch, now, BackfillConcurrency).ConfigureAwait(false);

            if (rows.Count > 0)
            {
                var path = MetricsPaths.DefaultSnapshotPath();
                MetricsPaths.EnsureSnapshotDir(path);
                Snapshots.AppendSnapshots(path, rows, TimeSpan.FromDays(BackfillWindowDays), now);
            }

            Backfill.MarkBackfillDone(markerPath);
            return new BackfillDoneMsg(items.Count, rows.Count, skipped, false, null);
        }
        catch (Exception e)
        {
            return new BackfillDoneMsg(0, 0, 0, false, e);
        }
    });
}
