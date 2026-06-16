using System.Text.Json.Serialization;

namespace Azdo.Core.AzureDevOps;

/// <summary>A state available for a work item type.</summary>
public sealed class WorkItemTypeState
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

/// <summary>Response from the work item type states API.</summary>
public sealed class WorkItemTypeStatesResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<WorkItemTypeState> Value { get; set; } = new();
}

/// <summary>A work item in Azure DevOps.</summary>
public sealed class WorkItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("rev")] public int Rev { get; set; }
    [JsonPropertyName("fields")] public WorkItemFields Fields { get; set; } = new();
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>Set by MultiClient, not from API.</summary>
    [JsonIgnore] public string ProjectName { get; set; } = "";
    /// <summary>Set by MultiClient, display name for UI.</summary>
    [JsonIgnore] public string ProjectDisplayName { get; set; } = "";

    /// <summary>An icon for the work item state. Workflow: New → Active → Resolved/Ready for Test → Closed.</summary>
    public string StateIcon()
    {
        string s = Fields.State.ToLowerInvariant();
        if (s == "new") return "○";
        if (s == "active") return "◐";
        if (s == "resolved" || s.Contains("ready")) return "●";
        if (s == "closed") return "✓";
        if (s == "removed") return "✗";
        return "○";
    }

    /// <summary>
    /// The appropriate description field based on work item type. Bugs use
    /// Microsoft.VSTS.TCM.ReproSteps; other types use System.Description.
    /// </summary>
    public string EffectiveDescription()
    {
        if (Fields.WorkItemType == "Bug" && !string.IsNullOrEmpty(Fields.ReproSteps))
            return Fields.ReproSteps;
        return Fields.Description;
    }

    /// <summary>The tags as a trimmed list split on semicolons; empty when there are no tags.</summary>
    public List<string> TagList()
    {
        if (string.IsNullOrEmpty(Fields.Tags))
            return new List<string>();
        var tags = new List<string>();
        foreach (var raw in Fields.Tags.Split(';'))
        {
            var t = raw.Trim();
            if (t.Length > 0)
                tags.Add(t);
        }
        return tags;
    }

    /// <summary>How long the work item has been in its current state. Zero when StateChangeDate is unset.</summary>
    public TimeSpan TimeInCurrentState(DateTime now)
    {
        if (Fields.StateChangeDate == default)
            return TimeSpan.Zero;
        return now - Fields.StateChangeDate;
    }

    /// <summary>The work item's story-point estimate.</summary>
    public double EffectivePoints() => Fields.StoryPoints;

    /// <summary>
    /// Whether the item is Closed and was closed strictly after <paramref name="start"/>.
    /// Items with a zero ClosedDate or a non-Closed state return false.
    /// </summary>
    public bool IsCompletedSince(DateTime start) =>
        string.Equals(Fields.State, "Closed", StringComparison.OrdinalIgnoreCase) &&
        Fields.ClosedDate != default &&
        Fields.ClosedDate > start;

    /// <summary>The display name of the assigned user, or "-" if unassigned.</summary>
    public string AssignedToName() => Fields.AssignedTo is null ? "-" : Fields.AssignedTo.DisplayName;
}

/// <summary>The fields of a work item.</summary>
public sealed class WorkItemFields
{
    [JsonPropertyName("System.Title")] public string Title { get; set; } = "";
    [JsonPropertyName("System.State")] public string State { get; set; } = "";
    [JsonPropertyName("System.WorkItemType")] public string WorkItemType { get; set; } = "";
    [JsonPropertyName("System.AssignedTo")] public Identity? AssignedTo { get; set; }
    [JsonPropertyName("Microsoft.VSTS.Common.Priority")] public int Priority { get; set; }
    [JsonPropertyName("System.ChangedDate")] public DateTime ChangedDate { get; set; }
    [JsonPropertyName("System.IterationPath")] public string IterationPath { get; set; } = "";
    [JsonPropertyName("System.Description")] public string Description { get; set; } = "";
    [JsonPropertyName("Microsoft.VSTS.TCM.ReproSteps")] public string ReproSteps { get; set; } = "";
    [JsonPropertyName("System.Tags")] public string Tags { get; set; } = "";

    [JsonPropertyName("Microsoft.VSTS.Scheduling.StoryPoints")] public double StoryPoints { get; set; }
    [JsonPropertyName("Microsoft.VSTS.Common.StateChangeDate")] public DateTime StateChangeDate { get; set; }
    [JsonPropertyName("Microsoft.VSTS.Common.ActivatedDate")] public DateTime ActivatedDate { get; set; }
    [JsonPropertyName("Microsoft.VSTS.Common.ClosedDate")] public DateTime ClosedDate { get; set; }
    [JsonPropertyName("System.CreatedDate")] public DateTime CreatedDate { get; set; }
}

/// <summary>A reference to a work item from WIQL queries.</summary>
public sealed class WorkItemReference
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

/// <summary>Response from a WIQL query.</summary>
public sealed class WiqlResponse
{
    [JsonPropertyName("workItems")] public List<WorkItemReference> WorkItems { get; set; } = new();
}

/// <summary>Response from getting work items.</summary>
public sealed class WorkItemsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<WorkItem> Value { get; set; } = new();
}

/// <summary>A single comment from a work item's Discussion section.</summary>
public sealed class WorkItemComment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("createdBy")] public Identity CreatedBy { get; set; } = new();
    [JsonPropertyName("createdDate")] public DateTime CreatedDate { get; set; }
}

/// <summary>CommentList wrapper returned by the work item comments GET endpoint.</summary>
public sealed class WorkItemCommentsResponse
{
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("comments")] public List<WorkItemComment> Comments { get; set; } = new();
}

/// <summary>One state change on a work item, extracted from the /updates revision history.</summary>
public sealed class WorkItemStateTransition
{
    public string State { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// The configured state names used by the metrics WIQL. Mirrors the metrics
/// StateConfig so this layer doesn't depend on the metrics package. All three are required.
/// </summary>
public sealed class MetricsStateNames
{
    public string Active { get; set; } = "";
    public string ReadyForTest { get; set; } = "";
    public string Closed { get; set; } = "";
}
