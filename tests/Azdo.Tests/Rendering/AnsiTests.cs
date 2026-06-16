using Azdo.Tui.Rendering;
using Xunit;

namespace Azdo.Tests.Rendering;

public class AnsiTests
{
    [Theory]
    [InlineData("hello", 5)]
    [InlineData("", 0)]
    [InlineData("日本語", 6)]
    public void Width_PlainText(string s, int expected) => Assert.Equal(expected, Ansi.Width(s));

    [Fact]
    public void Width_IgnoresSgrEscapes()
    {
        var styled = "\x1b[31mhello\x1b[0m";
        Assert.Equal(5, Ansi.Width(styled));
    }

    [Fact]
    public void Strip_RemovesEscapes()
        => Assert.Equal("hello", Ansi.Strip("\x1b[1;38;5;9mhello\x1b[0m"));

    [Fact]
    public void Truncate_RespectsVisibleWidth()
    {
        Assert.Equal("hel", Ansi.Truncate("hello", 3));
        Assert.Equal("hello", Ansi.Truncate("hello", 10));
    }

    [Fact]
    public void Truncate_ClosesOpenStyle()
    {
        var styled = "\x1b[31mhello\x1b[0m";
        var cut = Ansi.Truncate(styled, 3);
        Assert.Equal(3, Ansi.Width(cut));
        Assert.EndsWith(Ansi.Reset, cut);
    }

    [Fact]
    public void Wrap_BreaksOnWidth()
    {
        var wrapped = Ansi.Wrap("the quick brown fox", 9);
        var lines = wrapped.Split('\n');
        Assert.All(lines, l => Assert.True(Ansi.Width(l) <= 9, l));
    }

    [Fact]
    public void Wrap_HardBreaksLongWord()
    {
        var wrapped = Ansi.Wrap("abcdefghij", 4);
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, wrapped.Split('\n'));
    }
}
