using Azdo.Tui.Views.Metrics;
using Xunit;

namespace Azdo.Tests.Views.Metrics;

public class LabelsTests
{
    [Fact]
    public void LabelFor_OverrideWins()
    {
        Assert.Equal("rft", Labels.LabelFor("Ready for Test", "rft"));
        Assert.Equal("ACT", Labels.LabelFor("Active", "ACT"));
    }

    [Theory]
    [InlineData("Ready for Test", "rft")]
    [InlineData("In Progress", "ip")]
    [InlineData("Ready For Test", "rft")]
    public void LabelFor_AutoDerive_MultiWord(string input, string want)
        => Assert.Equal(want, Labels.LabelFor(input, ""));

    [Theory]
    [InlineData("Active", "active")]
    [InlineData("Closed", "closed")]
    [InlineData("Done", "done")]
    [InlineData("RFT", "rft")]
    public void LabelFor_AutoDerive_SingleWord(string input, string want)
        => Assert.Equal(want, Labels.LabelFor(input, ""));

    [Fact]
    public void LabelFor_EmptyName()
    {
        Assert.Equal("", Labels.LabelFor("", ""));
        Assert.Equal("", Labels.LabelFor("   ", ""));
    }

    [Fact]
    public void LabelTitle_TitleCasesAutoDerived()
    {
        Assert.Equal("Active", Labels.LabelTitle("Active", ""));
        Assert.Equal("Rft", Labels.LabelTitle("Ready for Test", ""));
    }

    [Fact]
    public void LabelTitle_OverridePreservedAsIs()
        => Assert.Equal("RFT", Labels.LabelTitle("Ready for Test", "RFT"));
}
