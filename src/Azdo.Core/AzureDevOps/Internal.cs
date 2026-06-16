using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azdo.Core.AzureDevOps;

/// <summary>Response from the connection data API (org-level identity).</summary>
internal sealed class ConnectionDataResponse
{
    [JsonPropertyName("authenticatedUser")] public AuthenticatedUserRef AuthenticatedUser { get; set; } = new();

    internal sealed class AuthenticatedUserRef
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }
}

/// <summary>One entry in the /updates response. Only System.State + System.ChangedDate are modeled.</summary>
internal sealed class WorkItemUpdate
{
    [JsonPropertyName("fields")] public Dictionary<string, WorkItemFieldChange> Fields { get; set; } = new();
}

internal sealed class WorkItemFieldChange
{
    [JsonPropertyName("newValue")] public JsonElement NewValue { get; set; }
}

internal sealed class WorkItemUpdatesResponse
{
    [JsonPropertyName("value")] public List<WorkItemUpdate> Value { get; set; } = new();
}
