using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class SelectionTests
{
    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name);

    [Fact]
    public void LoadSelection_MissingFile()
    {
        var rows = SelectionStore.LoadSelection(TempFile("nope.json"));
        Assert.Null(rows);
    }

    [Fact]
    public void SaveLoadSelection_RoundTrip()
    {
        var path = TempFile("sel.json");
        var input = new[] { "sprint-40", "sprint-41", "sprint-42" };
        SelectionStore.SaveSelection(path, input);
        var output = SelectionStore.LoadSelection(path);
        Assert.Equal(input, output);
    }

    [Fact]
    public void SaveSelection_AtomicLeavesNoTempFile()
    {
        var path = TempFile("sel.json");
        SelectionStore.SaveSelection(path, new[] { "a" });
        var dir = Path.GetDirectoryName(path)!;
        foreach (var e in Directory.GetFiles(dir))
            Assert.NotEqual(".tmp", Path.GetExtension(e));
    }

    [Fact]
    public void FilterAvailable_DropsUnknown()
    {
        var saved = new[] { "sprint-40", "sprint-41", "stale-tag", "sprint-42" };
        var available = new[] { "sprint-40", "sprint-42", "sprint-43" };
        var got = SelectionStore.FilterAvailable(saved, available);
        Assert.Equal(new[] { "sprint-40", "sprint-42" }, got);
    }

    [Fact]
    public void FilterAvailable_EmptySaved()
    {
        Assert.Null(SelectionStore.FilterAvailable(null, new[] { "a" }));
        Assert.Null(SelectionStore.FilterAvailable(Array.Empty<string>(), new[] { "a" }));
    }
}
