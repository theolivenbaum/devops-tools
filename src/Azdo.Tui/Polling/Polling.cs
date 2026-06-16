using Azdo.Core.AzureDevOps;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Polling;

/// <summary>Connection/health state shown in the status bar (≈ <c>polling.ConnectionState</c>).</summary>
public enum ConnectionState
{
    Connected,
    Connecting,
    Disconnected,
    Error,
}

/// <summary>Sent when pipeline runs are fetched — carries runs or an error (≈ <c>PipelineRunsUpdated</c>).</summary>
public sealed record PipelineRunsUpdated(List<PipelineRun> Runs, Exception? Err) : IMsg;

/// <summary>Sent on each polling interval tick (≈ <c>polling.TickMsg</c>).</summary>
public sealed record PollTickMsg : IMsg
{
    public static readonly PollTickMsg Instance = new();
}

/// <summary>
/// Manages background polling of pipeline data (≈ <c>polling.Poller</c>). Emits
/// <see cref="PipelineRunsUpdated"/> on fetch and re-arms a tick each interval.
/// </summary>
public sealed class Poller
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);
    public const int DefaultRunCount = 30;

    private readonly MultiClient _client;
    private TimeSpan _interval;
    private int _runCount = DefaultRunCount;
    private volatile bool _stopped;
    private readonly Lock _gate = new();

    public Poller(MultiClient client, TimeSpan interval)
    {
        _client = client;
        if (interval <= TimeSpan.Zero) interval = DefaultInterval;
        else if (interval < MinInterval) interval = MinInterval;
        _interval = interval;
    }

    public void SetInterval(TimeSpan interval)
    {
        lock (_gate) _interval = interval < MinInterval ? MinInterval : interval;
    }

    public void SetRunCount(int count)
    {
        lock (_gate) _runCount = count < 1 ? DefaultRunCount : count;
    }

    public void Stop() => _stopped = true;
    public bool IsStopped => _stopped;

    /// <summary>Returns a command that fetches pipeline runs, or null when stopped.</summary>
    public Cmd? FetchPipelineRuns()
    {
        if (_stopped) return null;
        int runCount;
        lock (_gate) runCount = _runCount;
        return async () =>
        {
            try
            {
                var runs = await _client.ListPipelineRunsAsync(runCount).ConfigureAwait(false);
                return new PipelineRunsUpdated(runs, null);
            }
            catch (PartialException pe)
            {
                var partial = pe.PartialData as List<PipelineRun> ?? new List<PipelineRun>();
                return new PipelineRunsUpdated(partial, pe);
            }
            catch (Exception e)
            {
                return new PipelineRunsUpdated(new List<PipelineRun>(), e);
            }
        };
    }

    /// <summary>Returns a command that fires a single tick after the interval, or null when stopped.</summary>
    public Cmd? StartPolling()
    {
        if (_stopped) return null;
        TimeSpan interval;
        lock (_gate) interval = _interval;
        return Commands.Tick(interval, _ => PollTickMsg.Instance);
    }

    /// <summary>Handles a tick: fetch + re-arm the timer.</summary>
    public Cmd? OnTick() => _stopped ? null : Commands.Batch(FetchPipelineRuns(), StartPolling());
}

/// <summary>
/// Graceful-degradation error tracking for polling (≈ <c>polling.ErrorHandler</c>):
/// keeps last-known-good data, classifies partial failures, and surfaces
/// recovery messages.
/// </summary>
public sealed class ErrorHandler
{
    public const int MaxRecoverableErrors = 5;

    private readonly Lock _gate = new();
    private Exception? _currentError;
    private int _consecutiveErrors;
    private DateTime _lastErrorTime;
    private List<PipelineRun> _lastKnownGoodData = new();
    private string _partialWarning = "";

    public void SetError(Exception err)
    {
        lock (_gate)
        {
            _currentError = err;
            _consecutiveErrors++;
            _lastErrorTime = DateTime.Now;
        }
    }

    public void ClearError()
    {
        lock (_gate) { _currentError = null; _consecutiveErrors = 0; }
    }

    public bool HasError() { lock (_gate) return _currentError is not null; }
    public Exception? GetError() { lock (_gate) return _currentError; }
    public int ConsecutiveErrors() { lock (_gate) return _consecutiveErrors; }

    public void SetLastKnownGoodData(List<PipelineRun> runs)
    {
        lock (_gate) _lastKnownGoodData = new List<PipelineRun>(runs);
    }

    public List<PipelineRun> GetLastKnownGoodData()
    {
        lock (_gate) return new List<PipelineRun>(_lastKnownGoodData);
    }

    /// <summary>Processes an update; returns (runs to display, hadFullError).</summary>
    public (List<PipelineRun> Runs, bool HasError) ProcessUpdate(PipelineRunsUpdated msg)
    {
        if (msg.Err is not null)
        {
            if (msg.Err is PartialException pe)
            {
                SetLastKnownGoodData(msg.Runs);
                ClearError();
                SetPartialWarning(pe.Message);
                return (msg.Runs, false);
            }
            SetError(msg.Err);
            return (GetLastKnownGoodData(), true);
        }
        SetLastKnownGoodData(msg.Runs);
        ClearError();
        ClearPartialWarning();
        return (msg.Runs, false);
    }

    public string PartialWarning() { lock (_gate) return _partialWarning; }
    private void SetPartialWarning(string m) { lock (_gate) _partialWarning = m; }
    private void ClearPartialWarning() { lock (_gate) _partialWarning = ""; }

    public string ErrorMessage() { lock (_gate) return _currentError?.Message ?? ""; }
    public DateTime LastErrorTime() { lock (_gate) return _lastErrorTime; }
    public bool ShouldShowError() { lock (_gate) return _currentError is not null; }
    public bool IsRecoverable() { lock (_gate) return _consecutiveErrors <= MaxRecoverableErrors; }

    public string RecoveryMessage()
    {
        lock (_gate)
        {
            if (_currentError is null) return "";
            return _consecutiveErrors <= MaxRecoverableErrors
                ? "Connection issue. Retrying..."
                : "Connection failed. Check your network and press 'r' to retry.";
        }
    }
}
