using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Azdo.Core.State;

/// <summary>Stable on-disk identifier for a top-level tab.</summary>
public static class TabId
{
    public const string PullRequests = "pull_requests";
    public const string WorkItems = "work_items";
    public const string Pipelines = "pipelines";
}

/// <summary>Per-tab restorable navigation memory (≈ <c>state.TabMemory</c>).</summary>
public sealed class TabMemory
{
    [YamlMember(Alias = "last_detail_id")]
    public int LastDetailId { get; set; }
}

/// <summary>Per-tab memory container (≈ <c>state.TabsState</c>).</summary>
public sealed class TabsState
{
    [YamlMember(Alias = "pull_requests")]
    public TabMemory PullRequests { get; set; } = new();

    [YamlMember(Alias = "work_items")]
    public TabMemory WorkItems { get; set; } = new();
}

/// <summary>Persistent navigation state written between runs (≈ <c>state.State</c>).</summary>
public sealed class AppState
{
    /// <summary>On-disk schema version. Bump on breaking shape changes.</summary>
    public const int CurrentVersion = 1;

    [YamlMember(Alias = "version")]
    public int Version { get; set; }

    [YamlMember(Alias = "active_tab")]
    public string ActiveTab { get; set; } = "";

    [YamlMember(Alias = "tabs")]
    public TabsState Tabs { get; set; } = new();

    private static ISerializer Serializer => new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public string Marshal() => Serializer.Serialize(this);

    /// <summary>Resolves the state file path, honoring <c>XDG_STATE_HOME</c>.</summary>
    public static string Path()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return System.IO.Path.Combine(xdg, "azdo-tui", "state.yaml");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, ".local", "state", "azdo-tui", "state.yaml");
    }

    /// <summary>Reads and parses the state file. A missing file yields empty state.</summary>
    public static AppState Load(string path)
    {
        if (!File.Exists(path)) return new AppState();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new AppState();
        return Deserializer.Deserialize<AppState>(text) ?? new AppState();
    }
}
