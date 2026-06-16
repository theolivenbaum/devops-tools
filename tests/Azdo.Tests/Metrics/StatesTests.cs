using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class StatesTests
{
    [Theory]
    [InlineData("Active")]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("  Active  ")]
    public void IsActive_CaseInsensitive(string s)
    {
        var sc = new StateConfig("Active", "Ready for Test", "Closed");
        Assert.True(sc.IsActive(s));
    }

    [Fact]
    public void IsActive_DoesNotMatchRFT()
    {
        var sc = new StateConfig("Active", "Ready for Test", "Closed");
        Assert.False(sc.IsActive("Ready for Test"));
    }

    [Theory]
    [InlineData("Ready for Test")]
    [InlineData("Ready For Test")]
    [InlineData("ready for test")]
    [InlineData("READY FOR TEST")]
    public void IsRFT_DualCasing(string s)
    {
        var sc = new StateConfig("Active", "Ready for Test", "Closed");
        Assert.True(sc.IsRFT(s));
    }

    [Fact]
    public void IsClosed_CanonicalDone()
    {
        var sc = new StateConfig("Active", "Ready for Test", "Done");
        Assert.True(sc.IsClosed("Done"));
        Assert.False(sc.IsClosed("Closed"));
    }

    [Fact]
    public void Order_ReturnsThreeStates()
    {
        var sc = new StateConfig("Active", "RFT", "Done");
        Assert.Equal(new[] { "Active", "RFT", "Done" }, sc.Order());
    }

    [Theory]
    [InlineData("Active", 0, true)]
    [InlineData("active", 0, true)]
    [InlineData("Ready for Test", 1, true)]
    [InlineData("ready for test", 1, true)]
    [InlineData("READY FOR TEST", 1, true)]
    [InlineData("Closed", 2, true)]
    [InlineData("New", -1, false)]
    [InlineData("", -1, false)]
    public void Index_CaseInsensitive(string state, int wantIndex, bool wantOk)
    {
        var sc = new StateConfig("Active", "Ready for Test", "Closed");
        var (idx, ok) = sc.IndexOf(state);
        Assert.Equal(wantOk, ok);
        Assert.Equal(wantIndex, idx);
    }

    [Fact]
    public void DefaultStates_Values()
    {
        var sc = StateConfig.DefaultStates();
        Assert.Equal("Active", sc.Active);
        Assert.Equal("Ready for Test", sc.ReadyForTest);
        Assert.Equal("Closed", sc.Closed);
    }
}
