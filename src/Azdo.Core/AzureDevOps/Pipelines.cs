using System.Text.Json.Serialization;

namespace Azdo.Core.AzureDevOps;

/// <summary>A build timeline containing stages, jobs, and tasks.</summary>
public sealed class Timeline
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("changeId")] public int ChangeId { get; set; }
    [JsonPropertyName("lastChangedBy")] public string LastChangedBy { get; set; } = "";
    [JsonPropertyName("lastChangedOn")] public DateTime? LastChangedOn { get; set; }
    [JsonPropertyName("records")] public List<TimelineRecord> Records { get; set; } = new();
}

/// <summary>A single record in the timeline (stage, job, or task).</summary>
public sealed class TimelineRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("parentId")] public string? ParentId { get; set; }
    /// <summary>"Stage", "Job", "Task", "Phase", "Checkpoint".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>"pending", "inProgress", "completed".</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "";
    /// <summary>"succeeded", "succeededWithIssues", "failed", "canceled", "skipped", "abandoned".</summary>
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("startTime")] public DateTime? StartTime { get; set; }
    [JsonPropertyName("finishTime")] public DateTime? FinishTime { get; set; }
    [JsonPropertyName("log")] public LogReference? Log { get; set; }
    [JsonPropertyName("issues")] public List<Issue> Issues { get; set; } = new();
}

/// <summary>Metadata about a build log referenced from a timeline record.</summary>
public sealed class LogReference
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

/// <summary>An error or warning in a timeline record.</summary>
public sealed class Issue
{
    /// <summary>"error", "warning".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>Metadata about a build log.</summary>
public sealed class BuildLog
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("lineCount")] public int LineCount { get; set; }
    [JsonPropertyName("createdOn")] public DateTime? CreatedOn { get; set; }
    [JsonPropertyName("lastChangedOn")] public DateTime? LastChangedOn { get; set; }
}

/// <summary>API response for listing build logs.</summary>
public sealed class BuildLogsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<BuildLog> Value { get; set; } = new();
}

/// <summary>A build/pipeline run in Azure DevOps.</summary>
public sealed class PipelineRun
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("buildNumber")] public string BuildNumber { get; set; } = "";
    /// <summary>"inProgress", "completed", "canceling", "postponed", "notStarted".</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    /// <summary>"succeeded", "failed", "canceled", "partiallySucceeded", "none".</summary>
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    /// <summary>e.g. "refs/heads/main".</summary>
    [JsonPropertyName("sourceBranch")] public string SourceBranch { get; set; } = "";
    /// <summary>Git commit SHA.</summary>
    [JsonPropertyName("sourceVersion")] public string SourceVersion { get; set; } = "";
    [JsonPropertyName("queueTime")] public DateTime QueueTime { get; set; }
    [JsonPropertyName("startTime")] public DateTime? StartTime { get; set; }
    [JsonPropertyName("finishTime")] public DateTime? FinishTime { get; set; }
    [JsonPropertyName("definition")] public PipelineDefinition Definition { get; set; } = new();
    [JsonPropertyName("project")] public Project Project { get; set; } = new();
    [JsonPropertyName("_links")] public Links Links { get; set; } = new();

    /// <summary>Set by MultiClient, not from API.</summary>
    [JsonIgnore] public string ProjectName { get; set; } = "";
    /// <summary>Set by MultiClient, display name for UI.</summary>
    [JsonIgnore] public string ProjectDisplayName { get; set; } = "";

    /// <summary>The short branch name without the refs/heads/ or refs/tags/ prefix.</summary>
    public string BranchShortName()
    {
        if (string.IsNullOrEmpty(SourceBranch))
            return "";
        if (SourceBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
            return SourceBranch["refs/heads/".Length..];
        if (SourceBranch.StartsWith("refs/tags/", StringComparison.Ordinal))
            return SourceBranch["refs/tags/".Length..];
        return SourceBranch;
    }

    /// <summary>A human-readable duration string for the pipeline run.</summary>
    public string Duration()
    {
        if (StartTime is null)
            return "-";
        if (FinishTime is null)
            return "-";
        return Format.Duration(FinishTime.Value - StartTime.Value);
    }

    /// <summary>A formatted timestamp for display in the pipeline table.</summary>
    public string Timestamp() => QueueTime.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>A pipeline definition.</summary>
public sealed class PipelineDefinition
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

/// <summary>An Azure DevOps project.</summary>
public sealed class Project
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

/// <summary>HATEOAS links.</summary>
public sealed class Links
{
    [JsonPropertyName("web")] public Link Web { get; set; } = new();
}

/// <summary>A single HATEOAS link.</summary>
public sealed class Link
{
    [JsonPropertyName("href")] public string Href { get; set; } = "";
}

/// <summary>API response for listing pipeline runs.</summary>
public sealed class PipelineRunsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<PipelineRun> Value { get; set; } = new();
}

/// <summary>Shared formatting helpers ported from the Go package.</summary>
public static class Format
{
    /// <summary>Formats a duration in a human-readable way without milliseconds.</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1))
            return $"{(int)d.TotalSeconds}s";
        if (d < TimeSpan.FromHours(1))
        {
            int mins = (int)d.TotalMinutes;
            int secs = (int)d.TotalSeconds % 60;
            return $"{mins}m{secs}s";
        }
        int hours = (int)d.TotalHours;
        int m = (int)d.TotalMinutes % 60;
        int s = (int)d.TotalSeconds % 60;
        return $"{hours}h{m}m{s}s";
    }
}
