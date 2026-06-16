using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Views.Metrics;

/// <summary>Fetch-completion message for the metrics tab.</summary>
public sealed record MetricsLoadedMsg(List<WorkItem>? Items, Exception? Err, DateTime FetchedAt) : IMsg;

/// <summary>Sent when an attempt to open a URL completes.</summary>
public sealed record OpenUrlResultMsg(Exception? Err) : IMsg;

/// <summary>
/// Arrives after the daily snapshot-write attempt completes. Mutually
/// exclusive flags: success (<see cref="Saved"/>), already-saved-today
/// (<see cref="AlreadyToday"/>), or failure (<see cref="Err"/>).
/// </summary>
public sealed record SnapshotSavedMsg(int Saved, int Skipped, bool AlreadyToday, Exception? Err) : IMsg;

/// <summary>
/// Carries the snapshot file contents plus the user's persisted sprint
/// selection. Fired on Init and after every successful snapshot write.
/// </summary>
public sealed record SnapshotsLoadedMsg(List<Snapshot> Snaps, List<string>? Selected, Exception? Err) : IMsg;

/// <summary>
/// Signals the result of the one-shot backfill attempt. Mutually exclusive
/// flags: <see cref="Err"/> on failure; <see cref="AlreadyDone"/> when the
/// marker was already present; <see cref="Saved"/> / <see cref="Skipped"/> on
/// a real run.
/// </summary>
public sealed record BackfillDoneMsg(int Total, int Saved, int Skipped, bool AlreadyDone, Exception? Err) : IMsg;
