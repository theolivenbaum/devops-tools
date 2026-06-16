using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

// Path helpers read the AZDO_CONFIG_DIR env var, which is process-global. Run
// these serially so concurrent tests don't observe a half-set override.
[Collection("MetricsPaths")]
public class PathsTests
{
    private static void WithConfigDir(string? value, Action body)
    {
        var prev = Environment.GetEnvironmentVariable("AZDO_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("AZDO_CONFIG_DIR", value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZDO_CONFIG_DIR", prev);
        }
    }

    [Fact]
    public void HonorConfigDirOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        WithConfigDir(dir, () =>
        {
            Assert.Equal(Path.Combine(dir, "metrics.jsonl"), MetricsPaths.DefaultSnapshotPath());
            Assert.Equal(Path.Combine(dir, "metrics-selection.json"), MetricsPaths.DefaultSelectionPath());
            Assert.Equal(Path.Combine(dir, ".metrics-backfill-done"), MetricsPaths.DefaultBackfillMarkerPath());
        });
    }

    [Fact]
    public void FallBackToHomeConfig()
    {
        WithConfigDir(null, () =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var want = Path.Combine(home, ".config", "azdo-tui", "metrics.jsonl");
            Assert.Equal(want, MetricsPaths.DefaultSnapshotPath());
        });
    }
}

[CollectionDefinition("MetricsPaths", DisableParallelization = true)]
public class MetricsPathsCollection { }
