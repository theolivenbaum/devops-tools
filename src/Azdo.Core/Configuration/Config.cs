using YamlDotNet.RepresentationModel;

namespace Azdo.Core.Configuration;

/// <summary>Thrown when the config file does not exist (≈ <c>ErrConfigNotFound</c>).</summary>
public sealed class ConfigNotFoundException(string path)
    : Exception($"config file not found: {path}");

/// <summary>Thrown when configuration values are invalid.</summary>
public sealed class ConfigValidationException(string message) : Exception(message);

/// <summary>Opt-in settings for the metrics dashboard (≈ <c>config.MetricsConfig</c>).</summary>
public sealed class MetricsConfig
{
    public bool Enabled { get; set; }
    public int IntervalDays { get; set; } = Config.DefaultMetricsIntervalDays;
    public int ActiveStaleDays { get; set; } = Config.DefaultMetricsActiveStaleDays;
    public int RFTStaleDays { get; set; } = Config.DefaultMetricsRFTStaleDays;
    public int WIPLimit { get; set; } = Config.DefaultMetricsWipLimit;
    public bool RunOneShotBackfill { get; set; }
    public MetricsStates States { get; set; } = new();
    public MetricsStates StateLabels { get; set; } = new();
}

/// <summary>The three workflow state names the metrics tab buckets on.</summary>
public sealed class MetricsStates
{
    public string Active { get; set; } = "";
    public string ReadyForTest { get; set; } = "";
    public string Closed { get; set; } = "";
}

/// <summary>
/// Application configuration loaded from <c>~/.config/azdo-tui/config.yaml</c>
/// (≈ <c>config.Config</c>). Projects may be plain strings or
/// <c>{name, display_name}</c> objects.
/// </summary>
public sealed class Config
{
    public const int DefaultPollingInterval = 60;
    public const string DefaultTheme = "dark";
    public const int DefaultMetricsIntervalDays = 14;
    public const int DefaultMetricsActiveStaleDays = 3;
    public const int DefaultMetricsRFTStaleDays = 2;
    public const int DefaultMetricsWipLimit = 4;
    public const string DefaultMetricsActiveState = "Active";
    public const string DefaultMetricsReadyForTestState = "Ready for Test";
    public const string DefaultMetricsClosedState = "Closed";

    private const string ConfigurationGuideUrl = "https://github.com/Elpulgo/azdo#configuration";

    private static readonly HashSet<string> ValidDisabledPanes = new() { "pipelines", "workitems" };

    public string Organization { get; set; } = "";
    public List<string> Projects { get; set; } = new();
    public Dictionary<string, string>? DisplayNames { get; set; }
    public int PollingInterval { get; set; } = DefaultPollingInterval;
    public string Theme { get; set; } = DefaultTheme;
    public List<string> DisabledPanes { get; set; } = new();
    public MetricsConfig Metrics { get; set; } = new();

    internal string ConfigPath { get; set; } = "";

    public bool IsPaneEnabled(string pane) => !DisabledPanes.Contains(pane);
    public bool IsMultiProject() => Projects.Count > 1;

    public string DisplayNameFor(string apiName)
        => DisplayNames is not null && DisplayNames.TryGetValue(apiName, out var dn) ? dn : apiName;

    public string GetTheme() => string.IsNullOrEmpty(Theme) ? DefaultTheme : Theme;

    public static string GetPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "azdo-tui", "config.yaml");
    }

    public static Config Load() => LoadFrom(GetPath());

    public static Config LoadFrom(string configPath)
    {
        var dir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(dir);

        if (!File.Exists(configPath))
            throw new ConfigNotFoundException(configPath);

        var text = File.ReadAllText(configPath);
        var cfg = new Config { ConfigPath = configPath };

        var stream = new YamlStream();
        using (var reader = new StringReader(text)) stream.Load(reader);
        if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root)
            ParseInto(cfg, root);

        ApplyMetricsDefaults(cfg.Metrics);
        cfg.Validate();
        return cfg;
    }

    private static void ParseInto(Config cfg, YamlMappingNode root)
    {
        cfg.Organization = Scalar(root, "organization") ?? "";
        cfg.Theme = Scalar(root, "theme") ?? DefaultTheme;
        if (int.TryParse(Scalar(root, "polling_interval"), out var pi)) cfg.PollingInterval = pi;
        else cfg.PollingInterval = DefaultPollingInterval;

        // projects: list of strings or {name, display_name}
        if (root.Children.TryGetValue(new YamlScalarNode("projects"), out var projectsNode)
            && projectsNode is YamlSequenceNode seq)
        {
            var displayNames = new Dictionary<string, string>();
            foreach (var item in seq)
            {
                if (item is YamlScalarNode s && !string.IsNullOrEmpty(s.Value))
                    cfg.Projects.Add(s.Value);
                else if (item is YamlMappingNode m)
                {
                    var name = Scalar(m, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    cfg.Projects.Add(name!);
                    var dn = Scalar(m, "display_name");
                    if (!string.IsNullOrEmpty(dn) && dn != name) displayNames[name!] = dn!;
                }
            }
            if (displayNames.Count > 0) cfg.DisplayNames = displayNames;
        }

        // deprecated single project
        if (cfg.Projects.Count == 0)
        {
            var single = Scalar(root, "project");
            if (!string.IsNullOrEmpty(single)) cfg.Projects.Add(single!);
        }

        var disabled = Scalar(root, "disabled_panes");
        if (!string.IsNullOrEmpty(disabled))
            foreach (var p in disabled!.Split(','))
            {
                var t = p.Trim();
                if (t.Length > 0) cfg.DisabledPanes.Add(t);
            }

        if (root.Children.TryGetValue(new YamlScalarNode("metrics"), out var mNode) && mNode is YamlMappingNode metrics)
            ParseMetrics(cfg.Metrics, metrics);
    }

    private static void ParseMetrics(MetricsConfig m, YamlMappingNode node)
    {
        m.Enabled = Bool(node, "enabled");
        if (int.TryParse(Scalar(node, "interval_days"), out var id)) m.IntervalDays = id;
        if (int.TryParse(Scalar(node, "active_stale_days"), out var asd)) m.ActiveStaleDays = asd;
        if (int.TryParse(Scalar(node, "rft_stale_days"), out var rsd)) m.RFTStaleDays = rsd;
        if (int.TryParse(Scalar(node, "wip_limit"), out var wl)) m.WIPLimit = wl;
        m.RunOneShotBackfill = Bool(node, "run_one_shot_backfill");
        if (node.Children.TryGetValue(new YamlScalarNode("states"), out var st) && st is YamlMappingNode states)
        {
            m.States.Active = Scalar(states, "active") ?? "";
            m.States.ReadyForTest = Scalar(states, "ready_for_test") ?? "";
            m.States.Closed = Scalar(states, "closed") ?? "";
        }
        if (node.Children.TryGetValue(new YamlScalarNode("state_labels"), out var sl) && sl is YamlMappingNode labels)
        {
            m.StateLabels.Active = Scalar(labels, "active") ?? "";
            m.StateLabels.ReadyForTest = Scalar(labels, "ready_for_test") ?? "";
            m.StateLabels.Closed = Scalar(labels, "closed") ?? "";
        }
    }

    private static void ApplyMetricsDefaults(MetricsConfig m)
    {
        if (m.IntervalDays == 0) m.IntervalDays = DefaultMetricsIntervalDays;
        if (m.WIPLimit == 0) m.WIPLimit = DefaultMetricsWipLimit;
        if (string.IsNullOrEmpty(m.States.Active)) m.States.Active = DefaultMetricsActiveState;
        if (string.IsNullOrEmpty(m.States.ReadyForTest)) m.States.ReadyForTest = DefaultMetricsReadyForTestState;
        if (string.IsNullOrEmpty(m.States.Closed)) m.States.Closed = DefaultMetricsClosedState;
    }

    private static string? Scalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s ? s.Value : null;

    private static bool Bool(YamlMappingNode node, string key)
        => bool.TryParse(Scalar(node, key), out var b) && b;

    public static Config NewWithPath(string org, List<string> projects, int pollingInterval, string theme, string configPath)
        => new() { Organization = org, Projects = projects, PollingInterval = pollingInterval, Theme = theme, ConfigPath = configPath };

    public void Validate()
    {
        if (string.IsNullOrEmpty(Organization))
            throw new ConfigValidationException(
                $"'organization' is not set in config.yaml\n\nAdd your Azure DevOps organization name to the config file:\n\n  organization: your-org-name\n\nFor more details, visit: {ConfigurationGuideUrl}");

        if (Projects.Count == 0)
            throw new ConfigValidationException(
                $"no projects configured in config.yaml\n\nAdd at least one project to the config file:\n\n  projects:\n    - your-project-name\n\nFor more details, visit: {ConfigurationGuideUrl}");

        for (int i = 0; i < Projects.Count; i++)
            if (string.IsNullOrEmpty(Projects[i]))
                throw new ConfigValidationException($"project name at index {i} cannot be empty");

        if (PollingInterval <= 0)
            throw new ConfigValidationException($"polling_interval must be greater than 0, got {PollingInterval}");

        if (string.IsNullOrEmpty(Theme))
            throw new ConfigValidationException("theme cannot be empty");

        foreach (var p in DisabledPanes)
            if (!ValidDisabledPanes.Contains(p))
                throw new ConfigValidationException($"invalid disabled pane \"{p}\": only 'pipelines' and 'workitems' can be disabled");

        if (Metrics.Enabled)
        {
            if (Metrics.IntervalDays <= 0) throw new ConfigValidationException($"metrics.interval_days must be > 0, got {Metrics.IntervalDays}");
            if (Metrics.ActiveStaleDays < 0) throw new ConfigValidationException($"metrics.active_stale_days must be >= 0, got {Metrics.ActiveStaleDays}");
            if (Metrics.RFTStaleDays < 0) throw new ConfigValidationException($"metrics.rft_stale_days must be >= 0, got {Metrics.RFTStaleDays}");
            if (Metrics.WIPLimit <= 0) throw new ConfigValidationException($"metrics.wip_limit must be > 0, got {Metrics.WIPLimit}");
            ValidateStateName("metrics.states.active", Metrics.States.Active);
            ValidateStateName("metrics.states.ready_for_test", Metrics.States.ReadyForTest);
            ValidateStateName("metrics.states.closed", Metrics.States.Closed);
            var names = new[]
            {
                Metrics.States.Active.Trim().ToLowerInvariant(),
                Metrics.States.ReadyForTest.Trim().ToLowerInvariant(),
                Metrics.States.Closed.Trim().ToLowerInvariant(),
            };
            for (int i = 0; i < names.Length; i++)
                for (int j = i + 1; j < names.Length; j++)
                    if (names[i] == names[j])
                        throw new ConfigValidationException($"metrics.states: duplicate state name \"{names[i]}\" — Active / Ready for Test / Closed must each be distinct");
        }
    }

    private static void ValidateStateName(string key, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ConfigValidationException($"{key} must not be empty");
        if (trimmed.Contains('\'')) throw new ConfigValidationException($"{key} contains a single quote — not allowed (WIQL safety)");
    }

    /// <summary>Writes config to disk, round-tripping unmanaged keys (metrics, etc.).</summary>
    public void Save()
    {
        Validate();
        var configPath = string.IsNullOrEmpty(ConfigPath) ? GetPath() : ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        YamlMappingNode root;
        if (File.Exists(configPath))
        {
            var stream = new YamlStream();
            using var reader = new StringReader(File.ReadAllText(configPath));
            stream.Load(reader);
            root = stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode m
                ? m : new YamlMappingNode();
        }
        else root = new YamlMappingNode();

        Set(root, "organization", Organization);

        var projectsSeq = new YamlSequenceNode();
        foreach (var p in Projects)
        {
            if (DisplayNames is not null && DisplayNames.TryGetValue(p, out var dn))
                projectsSeq.Add(new YamlMappingNode(
                    new YamlScalarNode("name"), new YamlScalarNode(p),
                    new YamlScalarNode("display_name"), new YamlScalarNode(dn)));
            else
                projectsSeq.Add(new YamlScalarNode(p));
        }
        root.Children[new YamlScalarNode("projects")] = projectsSeq;

        Set(root, "polling_interval", PollingInterval.ToString());
        Set(root, "theme", Theme);
        if (DisabledPanes.Count > 0) Set(root, "disabled_panes", string.Join(",", DisabledPanes));

        var doc = new YamlDocument(root);
        var outStream = new YamlStream(doc);
        using var sw = new StringWriter();
        outStream.Save(sw, assignAnchors: false);
        File.WriteAllText(configPath, sw.ToString());
    }

    public void UpdateTheme(string themeName)
    {
        if (string.IsNullOrEmpty(themeName)) throw new ConfigValidationException("theme name cannot be empty");
        Theme = themeName;
        Save();
    }

    private static void Set(YamlMappingNode root, string key, string value)
        => root.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
}
