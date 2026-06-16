using Azdo.Tui.Runtime;

namespace Azdo.Tui.Components;

/// <summary>Animation tick for the loading indicator (≈ bubbles <c>spinner.TickMsg</c>).</summary>
public sealed record SpinnerTickMsg : IMsg
{
    public static readonly SpinnerTickMsg Instance = new();
}

/// <summary>
/// A braille-dot spinner with an optional message (≈ <c>components.LoadingIndicator</c>).
/// Mutable, matching the Go pointer-based component.
/// </summary>
public sealed class LoadingIndicator(Styles.Styles styles)
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private int _frame;
    private string _message = "Loading...";
    private bool _visible;

    public void SetMessage(string msg) => _message = msg;
    public void SetVisible(bool visible) => _visible = visible;
    public bool IsVisible => _visible;
    public void Toggle() => _visible = !_visible;

    public Cmd Init() => Tick();

    /// <summary>Advances the frame on a tick and re-arms the timer while visible.</summary>
    public Cmd? Update(IMsg msg)
    {
        if (msg is SpinnerTickMsg)
        {
            _frame = (_frame + 1) % Frames.Length;
            return _visible ? Tick() : null;
        }
        return null;
    }

    public Cmd Tick() => Commands.Tick(Interval, _ => SpinnerTickMsg.Instance);

    public string View()
        => !_visible ? string.Empty : Frames[_frame] + " " + styles.Spinner.Render(_message);
}
