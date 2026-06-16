namespace Azdo.Tui.Runtime;

/// <summary>
/// An asynchronous side effect that eventually yields a message (or none) — the
/// C# equivalent of <c>tea.Cmd</c> (<c>func() tea.Msg</c>). Returning <c>null</c>
/// produces no message.
/// </summary>
public delegate Task<IMsg?> Cmd();

/// <summary>Factory helpers for common commands (≈ the package-level <c>tea</c> funcs).</summary>
public static class Commands
{
    /// <summary>A command that immediately yields <paramref name="msg"/>.</summary>
    public static Cmd Of(IMsg msg) => () => Task.FromResult<IMsg?>(msg);

    /// <summary>Wraps a synchronous producer.</summary>
    public static Cmd FromFunc(Func<IMsg?> f) => () => Task.FromResult(f());

    /// <summary>Wraps an async producer.</summary>
    public static Cmd FromAsync(Func<Task<IMsg?>> f) => () => f();

    /// <summary>Quits the program.</summary>
    public static readonly Cmd Quit = Of(QuitMsg.Instance);

    /// <summary>
    /// Runs several commands; the runtime fans them out concurrently and feeds
    /// each result back into the model. <c>null</c> entries are dropped.
    /// </summary>
    public static Cmd? Batch(params Cmd?[] cmds)
    {
        var live = cmds.Where(c => c is not null).Cast<Cmd>().ToList();
        if (live.Count == 0) return null;
        if (live.Count == 1) return live[0];
        return Of(new BatchMsg(live));
    }

    public static Cmd? Batch(IEnumerable<Cmd?> cmds) => Batch(cmds.ToArray());

    /// <summary>Fires <paramref name="produce"/> after <paramref name="delay"/>.</summary>
    public static Cmd Tick(TimeSpan delay, Func<DateTime, IMsg> produce) => async () =>
    {
        await Task.Delay(delay).ConfigureAwait(false);
        return produce(DateTime.Now);
    };
}
