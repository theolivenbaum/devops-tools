namespace Azdo.Tui.Views.Metrics;

/// <summary>
/// A minimal scrollable viewport over a block of pre-rendered text lines
/// (≈ bubbles <c>viewport.Model</c>). Only the slice that fits the configured
/// height is returned by <see cref="View"/>; scrolling is by visual line.
/// </summary>
internal sealed class Viewport
{
    private string[] _lines = Array.Empty<string>();

    public int Width { get; set; }
    public int Height { get; set; }
    public int YOffset { get; private set; }

    public Viewport(int width, int height)
    {
        Width = width;
        Height = Math.Max(1, height);
    }

    public int TotalLines => _lines.Length;

    public void SetContent(string content)
    {
        _lines = (content ?? string.Empty).Split('\n');
        ClampOffset();
    }

    public void SetYOffset(int y)
    {
        YOffset = y;
        ClampOffset();
    }

    public void LineUp(int n) => SetYOffset(YOffset - n);
    public void LineDown(int n) => SetYOffset(YOffset + n);

    private int MaxOffset => Math.Max(0, _lines.Length - Height);

    private void ClampOffset()
    {
        if (YOffset > MaxOffset) YOffset = MaxOffset;
        if (YOffset < 0) YOffset = 0;
    }

    public double ScrollPercent()
    {
        if (_lines.Length <= Height) return 1.0;
        int max = MaxOffset;
        if (max <= 0) return 1.0;
        return Math.Clamp((double)YOffset / max, 0.0, 1.0);
    }

    public string View()
    {
        if (_lines.Length == 0) return string.Empty;
        int end = Math.Min(_lines.Length, YOffset + Height);
        var slice = new List<string>(Height);
        for (int i = YOffset; i < end; i++) slice.Add(_lines[i]);
        return string.Join("\n", slice);
    }
}
