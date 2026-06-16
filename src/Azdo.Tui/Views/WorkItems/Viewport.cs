namespace Azdo.Tui.Views.WorkItems;

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

    public void SetContent(string content)
    {
        _lines = (content ?? string.Empty).Split('\n');
        ClampOffset();
    }

    public void SetYOffset(int y) { YOffset = y; ClampOffset(); }

    public void LineUp(int n) => SetYOffset(YOffset - n);
    public void LineDown(int n) => SetYOffset(YOffset + n);
    public void HalfViewUp() => SetYOffset(YOffset - Math.Max(1, Height / 2));
    public void HalfViewDown() => SetYOffset(YOffset + Math.Max(1, Height / 2));

    private int MaxOffset => Math.Max(0, _lines.Length - Height);

    private void ClampOffset()
    {
        if (YOffset > MaxOffset) YOffset = MaxOffset;
        if (YOffset < 0) YOffset = 0;
    }

    /// <summary>Fraction (0..1) of the content scrolled past, mirroring lipgloss ScrollPercent.</summary>
    public double ScrollPercent()
    {
        if (_lines.Length <= Height) return 1.0;
        int max = MaxOffset;
        if (max <= 0) return 1.0;
        double p = (double)YOffset / max;
        return Math.Clamp(p, 0.0, 1.0);
    }

    /// <summary>
    /// Renders exactly <see cref="Height"/> lines (padding with blanks when the
    /// content is shorter), matching the bubbles <c>viewport.View</c> behaviour the
    /// detail view relies on to fill its available height.
    /// </summary>
    public string View()
    {
        int end = Math.Min(_lines.Length, YOffset + Height);
        var slice = new List<string>(Height);
        for (int i = YOffset; i < end; i++) slice.Add(_lines[i]);
        while (slice.Count < Height) slice.Add(string.Empty);
        return string.Join("\n", slice);
    }
}
