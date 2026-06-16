using Azdo.Core.AzureDevOps;
using Azdo.Tui.Runtime;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tests.Views.PullRequests;

/// <summary>Shared helpers for the Pull Requests view tests.</summary>
internal static class TestSupport
{
    /// <summary>Runs a command to completion synchronously, returning its message (or null).</summary>
    public static IMsg? Run(Cmd? cmd)
    {
        if (cmd is null) return null;
        return cmd().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs a command and flattens a single level of batch, returning all produced
    /// messages. Spinner ticks are excluded to keep assertions focused.
    /// </summary>
    public static List<IMsg> RunAll(Cmd? cmd)
    {
        var result = new List<IMsg>();
        var msg = Run(cmd);
        if (msg is BatchMsg batch)
        {
            foreach (var c in batch.Commands)
            {
                var m = Run(c);
                if (m is not null) result.Add(m);
            }
        }
        else if (msg is not null)
        {
            result.Add(msg);
        }
        return result;
    }

    public static PullRequest Pr(int id, string title = "Title", string status = "active", bool draft = false)
        => new()
        {
            Id = id,
            Title = title,
            Status = status,
            IsDraft = draft,
            SourceRefName = "refs/heads/feature/x",
            TargetRefName = "refs/heads/main",
            CreatedBy = new Identity { DisplayName = "John Doe" },
            Repository = new Repository { Id = "repo-1", Name = "my-repo" },
            CreationDate = new DateTime(2024, 2, 6, 10, 0, 0, DateTimeKind.Utc),
        };

    public static Thread CodeThread(int id, string filePath, int line, string status = "active", params string[] comments)
        => new()
        {
            Id = id,
            Status = status,
            ThreadContext = new ThreadContext
            {
                FilePath = filePath,
                RightFileStart = new FilePosition { Line = line },
            },
            Comments = comments.Select(c => new Comment
            {
                Content = c,
                Author = new Identity { DisplayName = "Reviewer" },
                CommentType = "text",
            }).ToList(),
        };

    public static Thread GeneralThread(int id, string status = "active", params string[] comments)
        => new()
        {
            Id = id,
            Status = status,
            ThreadContext = null,
            Comments = comments.Select(c => new Comment
            {
                Content = c,
                Author = new Identity { DisplayName = "Reviewer" },
                CommentType = "text",
            }).ToList(),
        };

    public static IterationChange Change(string path, string changeType = "edit", string originalPath = "", string objectType = "blob")
        => new()
        {
            Item = new ChangeItem { Path = path, GitObjectType = objectType },
            ChangeType = changeType,
            OriginalPath = originalPath,
        };
}
