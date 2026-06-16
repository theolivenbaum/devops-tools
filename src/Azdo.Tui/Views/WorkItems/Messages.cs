using Azdo.Core.AzureDevOps;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Views.WorkItems;

// --- List fetch messages (≈ list.go) ---

/// <summary>Result of fetching all work items (≈ <c>workItemsMsg</c>).</summary>
public sealed record WorkItemsMsg(IReadOnlyList<WorkItem> WorkItems, Exception? Err) : IMsg;

/// <summary>Result of fetching the user's @Me work items (≈ <c>myWorkItemsMsg</c>).</summary>
public sealed record MyWorkItemsMsg(IReadOnlyList<WorkItem> WorkItems, Exception? Err) : IMsg;

/// <summary>Directly set work items, e.g. from polling (≈ <c>SetWorkItemsMsg</c>).</summary>
public sealed record SetWorkItemsMsg(IReadOnlyList<WorkItem> WorkItems) : IMsg;

/// <summary>Emitted after a work item state is successfully updated (≈ <c>WorkItemStateChangedMsg</c>).</summary>
public sealed record WorkItemStateChangedMsg : IMsg
{
    public static readonly WorkItemStateChangedMsg Instance = new();
}

// --- Detail messages (≈ detail.go) ---

/// <summary>Result of attempting to open a URL in the browser (≈ <c>openURLResultMsg</c>).</summary>
public sealed record OpenUrlResultMsg(Exception? Err) : IMsg;

/// <summary>Result of a state update (≈ <c>stateUpdateResultMsg</c>).</summary>
public sealed record StateUpdateResultMsg(string NewState, Exception? Err) : IMsg;

/// <summary>Result of fetching available work item type states (≈ <c>statesLoadedMsg</c>).</summary>
public sealed record StatesLoadedMsg(IReadOnlyList<WorkItemTypeState> States, Exception? Err) : IMsg;

/// <summary>Result of fetching work item discussion comments (≈ <c>commentsLoadedMsg</c>).</summary>
public sealed record CommentsLoadedMsg(IReadOnlyList<WorkItemComment> Comments, Exception? Err) : IMsg;

/// <summary>Result of posting a new comment (≈ <c>commentPostedMsg</c>).</summary>
public sealed record CommentPostedMsg(WorkItemComment? Comment, Exception? Err) : IMsg;
