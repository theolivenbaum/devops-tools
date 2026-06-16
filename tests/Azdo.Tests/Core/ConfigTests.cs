using Azdo.Core.Configuration;
using Xunit;

namespace Azdo.Tests.Core;

public class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "azdo-cfg-" + Guid.NewGuid().ToString("N"));

    private string Write(string yaml)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Load_MissingFile_Throws()
        => Assert.Throws<ConfigNotFoundException>(() => Config.LoadFrom(Path.Combine(_dir, "nope.yaml")));

    [Fact]
    public void Load_SimpleProjects()
    {
        var path = Write("organization: acme\nprojects:\n  - proj-a\n  - proj-b\ntheme: nord\npolling_interval: 30\n");
        var cfg = Config.LoadFrom(path);
        Assert.Equal("acme", cfg.Organization);
        Assert.Equal(new[] { "proj-a", "proj-b" }, cfg.Projects);
        Assert.Equal("nord", cfg.Theme);
        Assert.Equal(30, cfg.PollingInterval);
        Assert.True(cfg.IsMultiProject());
    }

    [Fact]
    public void Load_ProjectsWithDisplayNames()
    {
        var path = Write("organization: acme\nprojects:\n  - name: api-name\n    display_name: Friendly\n");
        var cfg = Config.LoadFrom(path);
        Assert.Equal(new[] { "api-name" }, cfg.Projects);
        Assert.Equal("Friendly", cfg.DisplayNameFor("api-name"));
        Assert.Equal("other", cfg.DisplayNameFor("other"));
    }

    [Fact]
    public void Load_DeprecatedSingleProject()
    {
        var path = Write("organization: acme\nproject: solo\n");
        var cfg = Config.LoadFrom(path);
        Assert.Equal(new[] { "solo" }, cfg.Projects);
    }

    [Fact]
    public void Load_DisabledPanes()
    {
        var path = Write("organization: acme\nprojects:\n  - a\ndisabled_panes: pipelines,workitems\n");
        var cfg = Config.LoadFrom(path);
        Assert.False(cfg.IsPaneEnabled("pipelines"));
        Assert.False(cfg.IsPaneEnabled("workitems"));
    }

    [Fact]
    public void Validate_NoOrg_Throws()
    {
        var cfg = new Config { Projects = { "a" } };
        Assert.Throws<ConfigValidationException>(() => cfg.Validate());
    }

    [Fact]
    public void Save_RoundTrips()
    {
        var path = Path.Combine(_dir, "config.yaml");
        Directory.CreateDirectory(_dir);
        var cfg = Config.NewWithPath("acme", new() { "p1" }, 45, "dracula", path);
        cfg.Save();
        var reloaded = Config.LoadFrom(path);
        Assert.Equal("acme", reloaded.Organization);
        Assert.Equal(45, reloaded.PollingInterval);
        Assert.Equal("dracula", reloaded.Theme);
    }

    [Fact]
    public void Save_PreservesUnmanagedKeys()
    {
        var path = Write("organization: acme\nprojects:\n  - a\nmetrics:\n  enabled: true\n  wip_limit: 7\n");
        var cfg = Config.LoadFrom(path);
        cfg.UpdateTheme("nord");
        var text = File.ReadAllText(path);
        Assert.Contains("metrics", text);
        Assert.Contains("wip_limit", text);
    }
}
