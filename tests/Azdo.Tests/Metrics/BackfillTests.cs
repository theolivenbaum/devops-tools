using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

[Collection("MetricsPaths")]
public class BackfillTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void BackfillAlreadyDone_MissingFileReturnsFalse()
    {
        var path = Path.Combine(TempDir(), ".metrics-backfill-done");
        Assert.False(Backfill.BackfillAlreadyDone(path));
    }

    [Fact]
    public void BackfillAlreadyDone_PresentFileReturnsTrue()
    {
        var path = Path.Combine(TempDir(), ".metrics-backfill-done");
        File.WriteAllText(path, "");
        Assert.True(Backfill.BackfillAlreadyDone(path));
    }

    [Fact]
    public void MarkBackfillDone_CreatesMarker()
    {
        var path = Path.Combine(TempDir(), ".metrics-backfill-done");
        Backfill.MarkBackfillDone(path);
        Assert.True(Backfill.BackfillAlreadyDone(path));
    }

    [Fact]
    public void MarkBackfillDone_IsIdempotent()
    {
        var path = Path.Combine(TempDir(), ".metrics-backfill-done");
        Backfill.MarkBackfillDone(path);
        Backfill.MarkBackfillDone(path);
        Assert.True(Backfill.BackfillAlreadyDone(path));
    }

    [Fact]
    public void MarkBackfillDone_CreatesParentDir()
    {
        var path = Path.Combine(TempDir(), "nested", "sub", ".metrics-backfill-done");
        Backfill.MarkBackfillDone(path);
        Assert.True(Backfill.BackfillAlreadyDone(path));
    }

    [Fact]
    public void DefaultBackfillMarkerPath_UsesAzdoTuiConfig()
    {
        var prev = Environment.GetEnvironmentVariable("AZDO_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("AZDO_CONFIG_DIR", null);
            var path = MetricsPaths.DefaultBackfillMarkerPath();
            Assert.Equal(".metrics-backfill-done", Path.GetFileName(path));
            Assert.Equal("azdo-tui", Path.GetFileName(Path.GetDirectoryName(path)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZDO_CONFIG_DIR", prev);
        }
    }
}
