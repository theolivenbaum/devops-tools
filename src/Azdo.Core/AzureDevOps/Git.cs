using System.Text.Json.Serialization;

namespace Azdo.Core.AzureDevOps;

/// <summary>A pull request in Azure DevOps.</summary>
public sealed class PullRequest
{
    [JsonPropertyName("pullRequestId")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>"active", "completed", "abandoned".</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("creationDate")] public DateTime CreationDate { get; set; }
    /// <summary>e.g. "refs/heads/feature/my-feature".</summary>
    [JsonPropertyName("sourceRefName")] public string SourceRefName { get; set; } = "";
    /// <summary>e.g. "refs/heads/main".</summary>
    [JsonPropertyName("targetRefName")] public string TargetRefName { get; set; } = "";
    [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
    [JsonPropertyName("createdBy")] public Identity CreatedBy { get; set; } = new();
    [JsonPropertyName("repository")] public Repository Repository { get; set; } = new();
    [JsonPropertyName("reviewers")] public List<Reviewer> Reviewers { get; set; } = new();

    /// <summary>Set by MultiClient, not from API.</summary>
    [JsonIgnore] public string ProjectName { get; set; } = "";
    /// <summary>Set by MultiClient, display name for UI.</summary>
    [JsonIgnore] public string ProjectDisplayName { get; set; } = "";

    /// <summary>The short source branch name without the refs/heads/ prefix.</summary>
    public string SourceBranchShortName()
    {
        if (string.IsNullOrEmpty(SourceRefName))
            return "";
        if (SourceRefName.StartsWith("refs/heads/", StringComparison.Ordinal))
            return SourceRefName["refs/heads/".Length..];
        return SourceRefName;
    }

    /// <summary>The short target branch name without the refs/heads/ prefix.</summary>
    public string TargetBranchShortName()
    {
        if (string.IsNullOrEmpty(TargetRefName))
            return "";
        if (TargetRefName.StartsWith("refs/heads/", StringComparison.Ordinal))
            return TargetRefName["refs/heads/".Length..];
        return TargetRefName;
    }
}

/// <summary>A user identity in Azure DevOps.</summary>
public sealed class Identity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    /// <summary>Typically email.</summary>
    [JsonPropertyName("uniqueName")] public string UniqueName { get; set; } = "";
}

/// <summary>A Git repository in Azure DevOps.</summary>
public sealed class Repository
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

/// <summary>A reviewer on a pull request.</summary>
public sealed class Reviewer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    /// <summary>10: approved, 5: approved with suggestions, 0: no vote, -5: waiting, -10: rejected.</summary>
    [JsonPropertyName("vote")] public int Vote { get; set; }

    /// <summary>A human-readable description of the reviewer's vote.</summary>
    public string VoteDescription() => Vote switch
    {
        10 => "Approved",
        5 => "Approved with suggestions",
        0 => "No vote",
        -5 => "Waiting for author",
        -10 => "Rejected",
        _ => "Unknown",
    };
}

/// <summary>API response for listing pull requests.</summary>
public sealed class PullRequestsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<PullRequest> Value { get; set; } = new();
}

/// <summary>A comment thread on a pull request.</summary>
public sealed class Thread
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("publishedDate")] public DateTime PublishedDate { get; set; }
    [JsonPropertyName("lastUpdatedDate")] public DateTime LastUpdatedDate { get; set; }
    /// <summary>"active", "fixed", "wontFix", "closed", "pending".</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("threadContext")] public ThreadContext? ThreadContext { get; set; }
    [JsonPropertyName("comments")] public List<Comment> Comments { get; set; } = new();
    [JsonPropertyName("isDeleted")] public bool IsDeleted { get; set; }

    /// <summary>True if this thread is attached to a specific code location.</summary>
    public bool IsCodeComment() => ThreadContext is not null && !string.IsNullOrEmpty(ThreadContext.FilePath);

    /// <summary>A human-readable description of the thread status.</summary>
    public string StatusDescription() => Status switch
    {
        "active" => "Active",
        "fixed" => "Resolved",
        "wontFix" => "Won't fix",
        "closed" => "Closed",
        "pending" => "Pending",
        "" => "Unknown",
        _ => "Unknown",
    };
}

/// <summary>Location information for code comments.</summary>
public sealed class ThreadContext
{
    [JsonPropertyName("filePath")] public string FilePath { get; set; } = "";
    [JsonPropertyName("rightFileStart")] public FilePosition? RightFileStart { get; set; }
    [JsonPropertyName("rightFileEnd")] public FilePosition? RightFileEnd { get; set; }
}

/// <summary>A position in a file.</summary>
public sealed class FilePosition
{
    [JsonPropertyName("line")] public int Line { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

/// <summary>A single comment in a thread.</summary>
public sealed class Comment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("parentCommentId")] public int ParentCommentId { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("publishedDate")] public DateTime PublishedDate { get; set; }
    [JsonPropertyName("lastUpdatedDate")] public DateTime LastUpdatedDate { get; set; }
    /// <summary>"text", "system".</summary>
    [JsonPropertyName("commentType")] public string CommentType { get; set; } = "";
    [JsonPropertyName("author")] public Identity Author { get; set; } = new();
}

/// <summary>API response for listing threads.</summary>
public sealed class ThreadsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<Thread> Value { get; set; } = new();
}

/// <summary>A single iteration (push) on a pull request.</summary>
public sealed class Iteration
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

/// <summary>API response for listing iterations.</summary>
public sealed class IterationsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("value")] public List<Iteration> Value { get; set; } = new();
}

/// <summary>A file changed in a PR iteration.</summary>
public sealed class IterationChange
{
    [JsonPropertyName("changeId")] public int ChangeId { get; set; }
    [JsonPropertyName("item")] public ChangeItem Item { get; set; } = new();
    /// <summary>"add", "edit", "delete", "rename".</summary>
    [JsonPropertyName("changeType")] public string ChangeType { get; set; } = "";
    [JsonPropertyName("originalPath")] public string OriginalPath { get; set; } = "";
}

/// <summary>Item details in an iteration change.</summary>
public sealed class ChangeItem
{
    [JsonPropertyName("objectId")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    /// <summary>"blob" for files, "tree" for folders.</summary>
    [JsonPropertyName("gitObjectType")] public string GitObjectType { get; set; } = "";
}

/// <summary>API response for iteration changes.</summary>
public sealed class IterationChangesResponse
{
    [JsonPropertyName("changeEntries")] public List<IterationChange> ChangeEntries { get; set; } = new();
}

/// <summary>Vote values for pull request reviews.</summary>
public static class Vote
{
    public const int Approve = 10;
    public const int ApproveWithSuggestions = 5;
    public const int NoVote = 0;
    public const int WaitForAuthor = -5;
    public const int Reject = -10;
}
