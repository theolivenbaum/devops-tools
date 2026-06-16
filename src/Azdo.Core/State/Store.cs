namespace Azdo.Core.State;

/// <summary>
/// Owns in-memory <see cref="AppState"/> and coordinates debounced, atomic
/// writes to disk (≈ <c>state.Store</c>). Thread-safe.
/// </summary>
public sealed class Store
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly Lock _gate = new();
    private TimeSpan _debounce = DefaultDebounce;
    private AppState _state;
    private bool _dirty;
    private Timer? _timer;
    private Exception? _writeErr;

    private Store(string path, AppState state)
    {
        _path = path;
        _state = state;
    }

    /// <summary>Creates a store seeded from the file at <paramref name="path"/>.</summary>
    public static Store Create(string path) => new(path, AppState.Load(path));

    public void SetDebounce(TimeSpan d)
    {
        lock (_gate) _debounce = d;
    }

    public AppState State()
    {
        lock (_gate) return _state;
    }

    /// <summary>Mutates state under the lock and schedules a debounced write.</summary>
    public void Apply(Action<AppState> mutate)
    {
        lock (_gate)
        {
            mutate(_state);
            _dirty = true;
            _timer?.Dispose();
            _timer = new Timer(_ => FlushAsync(), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushAsync()
    {
        try { Flush(); }
        catch (Exception ex) { lock (_gate) _writeErr = ex; }
    }

    /// <summary>Synchronously writes pending state. No-op when clean.</summary>
    public void Flush()
    {
        AppState snapshot;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (!_dirty) return;
            snapshot = _state;
        }

        var data = snapshot.Marshal();
        WriteAtomic(_path, data);

        lock (_gate) _dirty = false;
    }

    public Exception? LastWriteError()
    {
        lock (_gate) return _writeErr;
    }

    /// <summary>Writes via temp file + rename so a crash never leaves a half-written file.</summary>
    private static void WriteAtomic(string path, string data)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".state-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
