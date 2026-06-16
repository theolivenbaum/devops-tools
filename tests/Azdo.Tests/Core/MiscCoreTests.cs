using Azdo.Core.Diff;
using Azdo.Core.State;
using Azdo.Core.Version;
using Xunit;

namespace Azdo.Tests.Core;

public class VersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("v1.0.0", "v2.0.0", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("bad", "1.0.0", false)]
    public void IsNewer(string current, string latest, bool expected)
        => Assert.Equal(expected, VersionChecker.IsNewer(current, latest));
}

public class DiffEngineTests
{
    [Fact]
    public void NoChanges_NoHunks()
    {
        var hunks = DiffEngine.ComputeDiff("a\nb\nc\n", "a\nb\nc\n", 3);
        Assert.Empty(hunks);
    }

    [Fact]
    public void AddedLine_ProducesHunk()
    {
        var hunks = DiffEngine.ComputeDiff("a\nb\n", "a\nx\nb\n", 3);
        Assert.Single(hunks);
        Assert.Contains(hunks[0].Lines, l => l.Type == LineType.Added && l.Content == "x");
    }

    [Fact]
    public void RemovedLine_ProducesHunk()
    {
        var hunks = DiffEngine.ComputeDiff("a\nb\nc\n", "a\nc\n", 3);
        Assert.Contains(hunks[0].Lines, l => l.Type == LineType.Removed && l.Content == "b");
    }
}

public class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "azdo-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var s = AppState.Load(Path.Combine(_dir, "state.yaml"));
        Assert.Equal("", s.ActiveTab);
    }

    [Fact]
    public void Store_ApplyFlush_Persists()
    {
        var path = Path.Combine(_dir, "state.yaml");
        var store = Store.Create(path);
        store.Apply(s =>
        {
            s.Version = AppState.CurrentVersion;
            s.ActiveTab = TabId.WorkItems;
            s.Tabs.WorkItems.LastDetailId = 42;
        });
        store.Flush();

        var reloaded = AppState.Load(path);
        Assert.Equal(TabId.WorkItems, reloaded.ActiveTab);
        Assert.Equal(42, reloaded.Tabs.WorkItems.LastDetailId);
    }
}
