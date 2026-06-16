namespace Azdo.Tui.Views.Metrics;

/// <summary>
/// Maps data coordinates (sprint index, metric value) onto canvas cell
/// coordinates. (0,0) is the top-left of the canvas; Y increases downward.
/// Mirrors the Go reference <c>chartGeom</c>.
/// </summary>
public sealed class ChartGeom
{
    public int W { get; }
    public int H { get; }
    public int GutterW { get; }
    public int AxisX { get; }
    public int PlotLeft { get; }
    public int PlotRight { get; }
    public int PlotTop { get; }
    public int PlotBottom { get; }
    public int N { get; }
    public double YMax { get; }

    private ChartGeom(int w, int h, int gutterW, int axisX, int plotLeft, int plotRight,
        int plotTop, int plotBottom, int n, double yMax)
    {
        W = w; H = h; GutterW = gutterW; AxisX = axisX; PlotLeft = plotLeft;
        PlotRight = plotRight; PlotTop = plotTop; PlotBottom = plotBottom; N = n; YMax = yMax;
    }

    /// <summary>Computes layout, sizing the Y-label gutter to the widest tick label.</summary>
    public static ChartGeom New(int w, int h, int n, double yMax)
    {
        int gutterW = 1;
        foreach (var v in new[] { yMax, yMax / 2, 0 })
            gutterW = Math.Max(gutterW, Model.FmtAxisVal(v).Length);
        int axisX = gutterW;
        return new ChartGeom(w, h, gutterW, axisX, axisX + 1, w - 1, 0, h - 1, n, yMax);
    }

    /// <summary>Maps a sprint index to a canvas column (the centre of its cluster).</summary>
    public int XFor(int i)
    {
        if (N <= 1) return (PlotLeft + PlotRight) / 2;
        int span = PlotRight - PlotLeft;
        return PlotLeft + (int)Math.Round((double)i / (N - 1) * span);
    }

    /// <summary>Maps a metric value to a canvas row (clamped to the plot area).</summary>
    public int YFor(double v)
    {
        int rows = PlotBottom - PlotTop;
        if (YMax <= 0 || rows <= 0) return PlotBottom;
        double frac = Math.Clamp(v / YMax, 0, 1);
        return PlotBottom - (int)Math.Round(frac * rows);
    }
}

/// <summary>The inclusive column range [X0,X1] occupied by one user's bar.</summary>
public struct BarSpan
{
    public int X0;
    public int X1;
}
