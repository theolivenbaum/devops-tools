namespace Azdo.Tui.Runtime;

/// <summary>Marker for all messages flowing through the runtime (≈ <c>tea.Msg</c>).</summary>
public interface IMsg { }

/// <summary>Window size changed (≈ <c>tea.WindowSizeMsg</c>).</summary>
public sealed record WindowSizeMsg(int Width, int Height) : IMsg;

/// <summary>Request to quit the program (≈ <c>tea.QuitMsg</c>).</summary>
public sealed record QuitMsg : IMsg
{
    public static readonly QuitMsg Instance = new();
}

/// <summary>A batch of messages to be dispatched in order (internal fan-out of a batched <see cref="Cmd"/>).</summary>
public sealed record BatchMsg(IReadOnlyList<Cmd> Commands) : IMsg;

/// <summary>A tick fired by <see cref="Commands.Tick"/>.</summary>
public sealed record TickMsg(DateTime Time) : IMsg;
