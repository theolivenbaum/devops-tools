using Azdo.Core.AzureDevOps;
using Azdo.Core.Diff;
using Azdo.Tui.Runtime;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tui.Views.PullRequests;

// --- List fetch messages (≈ list.go) ---

/// <summary>Result of fetching all PRs (≈ <c>pullRequestsMsg</c>).</summary>
public sealed record PullRequestsMsg(IReadOnlyList<PullRequest> Prs, Exception? Err) : IMsg;

/// <summary>Result of fetching the user's own PRs (≈ <c>myPullRequestsMsg</c>).</summary>
public sealed record MyPullRequestsMsg(IReadOnlyList<PullRequest> Prs, Exception? Err) : IMsg;

/// <summary>Result of fetching PRs where the user is a reviewer (≈ <c>asReviewerPullRequestsMsg</c>).</summary>
public sealed record AsReviewerPullRequestsMsg(IReadOnlyList<PullRequest> Prs, Exception? Err) : IMsg;

/// <summary>Directly set PRs, e.g. from polling (≈ <c>SetPRsMsg</c>).</summary>
public sealed record SetPRsMsg(IReadOnlyList<PullRequest> Prs) : IMsg;

// --- Detail messages (≈ detail.go) ---

/// <summary>Result of fetching PR threads (≈ <c>threadsMsg</c>).</summary>
public sealed record ThreadsMsg(IReadOnlyList<Thread> Threads, Exception? Err) : IMsg;

/// <summary>Result of fetching the changed files of a PR (≈ <c>changedFilesMsg</c>).</summary>
public sealed record ChangedFilesMsg(IReadOnlyList<IterationChange> Changes, Exception? Err) : IMsg;

/// <summary>Result of submitting a vote (≈ <c>voteResultMsg</c>).</summary>
public sealed record VoteResultMsg(string Message, Exception? Err) : IMsg;

/// <summary>Result of attempting to open a URL in the browser (≈ <c>openURLResultMsg</c>).</summary>
public sealed record OpenUrlResultMsg(Exception? Err) : IMsg;

/// <summary>User pressed enter on a changed file in the detail view (≈ <c>openFileDiffMsg</c>).</summary>
public sealed record OpenFileDiffMsg(IterationChange File) : IMsg;

/// <summary>User pressed enter on general comments in the detail view (≈ <c>openGeneralCommentsMsg</c>).</summary>
public sealed record OpenGeneralCommentsMsg : IMsg
{
    public static readonly OpenGeneralCommentsMsg Instance = new();
}

// --- Diff messages (≈ diffview.go) ---

/// <summary>Computed file diff plus inline threads (≈ <c>fileDiffMsg</c>).</summary>
public sealed record FileDiffMsg(FileDiff? Diff, Dictionary<int, List<Thread>>? FileThreads, Exception? Err) : IMsg;

/// <summary>Result of a comment/reply/resolve action (≈ <c>commentResultMsg</c>).</summary>
public sealed record CommentResultMsg(string Message, Exception? Err) : IMsg;

/// <summary>Re-fetched threads after a mutation (≈ <c>threadsRefreshMsg</c>).</summary>
public sealed record ThreadsRefreshMsg(IReadOnlyList<Thread> Threads, Exception? Err) : IMsg;

/// <summary>User wants to leave the diff view (≈ <c>exitDiffViewMsg</c>).</summary>
public sealed record ExitDiffViewMsg : IMsg
{
    public static readonly ExitDiffViewMsg Instance = new();
}
