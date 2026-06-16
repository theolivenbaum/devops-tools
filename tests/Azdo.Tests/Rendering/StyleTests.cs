using Azdo.Tui.Rendering;
using Xunit;

namespace Azdo.Tests.Rendering;

public class StyleTests
{
    [Fact]
    public void Width_PadsAndAligns()
    {
        var rendered = Style.New().Width(10).Render("hi");
        Assert.Equal(10, Ansi.Width(rendered));
    }

    [Fact]
    public void AlignRight_PadsLeft()
    {
        var rendered = Style.New().Width(6).Align(HAlign.Right).Render("hi");
        Assert.Equal("    hi", Ansi.Strip(rendered));
    }

    [Fact]
    public void Foreground_EmitsSgr()
    {
        var rendered = Style.New().Foreground("#ff0000").Render("x");
        Assert.Contains("\x1b[", rendered);
        Assert.Equal("x", Ansi.Strip(rendered));
    }

    [Fact]
    public void Border_AddsFrame()
    {
        var rendered = Style.New().WithBorder(Border.Rounded).Render("hi");
        var lines = rendered.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("╭", lines[0]);
        Assert.Contains("╯", lines[2]);
    }

    [Fact]
    public void Border_WidthIncludesSides()
    {
        var rendered = Style.New().Width(4).WithBorder(Border.Normal).Render("hi");
        // content width 4 + 2 border columns
        Assert.Equal(6, Ansi.Width(rendered.Split('\n')[0]));
    }

    [Fact]
    public void Height_PadsLineCount()
    {
        var rendered = Style.New().Height(3).Render("a");
        Assert.Equal(3, rendered.Split('\n').Length);
    }

    [Fact]
    public void FrameSizes()
    {
        var s = Style.New().Padding(1, 2).WithBorder(Border.Rounded);
        Assert.Equal(2 + 2 + 2, s.GetHorizontalFrameSize()); // padL+padR + 2 borders
        Assert.Equal(1 + 1 + 2, s.GetVerticalFrameSize());
    }
}

public class LayoutTests
{
    [Fact]
    public void JoinHorizontal_AlignsHeights()
    {
        var joined = Layout.JoinHorizontal(VAlign.Top, "a\nb", "c");
        var lines = joined.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("ac", Ansi.Strip(lines[0]));
    }

    [Fact]
    public void JoinVertical_AlignsWidths()
    {
        var joined = Layout.JoinVertical(HAlign.Left, "long line", "x");
        var lines = joined.Split('\n');
        Assert.Equal(Ansi.Width(lines[0]), Ansi.Width(lines[1]));
    }

    [Fact]
    public void Place_FillsBox()
    {
        var placed = Layout.Place(10, 3, HAlign.Center, VAlign.Center, "x");
        var lines = placed.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Equal(10, Ansi.Width(l)));
    }
}
