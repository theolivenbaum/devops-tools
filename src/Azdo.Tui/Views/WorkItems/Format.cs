using System.Text;
using System.Text.RegularExpressions;
using Azdo.Core.AzureDevOps;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tui.Views.WorkItems;

/// <summary>
/// Pure formatting / filtering helpers ported from list.go and detail.go.
/// Kept internal-to-package but public for unit testing.
/// </summary>
public static partial class Format
{
    // ----- icon / text formatting (list.go) -----

    /// <summary>Styled text label for the work item type (≈ <c>typeIconWithStyles</c>).</summary>
    public static string TypeIcon(string workItemType, StyleSet s) => workItemType switch
    {
        "Bug" => s.Error.Render("Bug"),
        "Task" => s.Info.Render("Task"),
        "User Story" => s.Success.Render("Story"),
        "Feature" => s.Description.Foreground(s.Theme.Accent).Render("Feature"),
        "Epic" => s.Warning.Render("Epic"),
        "Issue" => s.Error.Render("Issue"),
        _ => s.Muted.Render("Item"),
    };

    /// <summary>Styled text for the work item state (≈ <c>stateTextWithStyles</c>).</summary>
    public static string StateText(string state, StyleSet s)
    {
        string lower = state.ToLowerInvariant();
        if (lower == "new") return s.Muted.Render("New");
        if (lower == "active") return s.Info.Render("Active");
        if (lower == "resolved") return s.Warning.Render("Resolved");
        if (lower.Contains("ready")) return s.Description.Foreground(s.Theme.Secondary).Render(state);
        if (lower == "closed") return s.Success.Render("Closed");
        if (lower == "removed") return s.Error.Render("Removed");
        return s.Muted.Render(state);
    }

    /// <summary>Styled text for the priority (≈ <c>priorityTextWithStyles</c>).</summary>
    public static string PriorityText(int priority, StyleSet s) => priority switch
    {
        1 => s.Error.Render("P1"),
        2 => s.Warning.Render("P2"),
        3 => s.Warning.Render("P3"),
        4 => s.Muted.Render("P4"),
        _ => s.Muted.Render($"P{priority}"),
    };

    // ----- row builders (list.go) -----

    /// <summary>Converts work items to table rows (≈ <c>workItemsToRows</c>).</summary>
    public static List<string[]> WorkItemsToRows(IReadOnlyList<WorkItem> items, StyleSet s)
    {
        var rows = new List<string[]>(items.Count);
        foreach (var wi in items)
        {
            rows.Add(new[]
            {
                TypeIcon(wi.Fields.WorkItemType, s),
                wi.Id.ToString(),
                wi.Fields.Title,
                StateText(wi.Fields.State, s),
                PriorityText(wi.Fields.Priority, s),
                wi.AssignedToName(),
            });
        }
        return rows;
    }

    /// <summary>Converts work items to table rows with a Project column (≈ <c>workItemsToRowsMulti</c>).</summary>
    public static List<string[]> WorkItemsToRowsMulti(IReadOnlyList<WorkItem> items, StyleSet s)
    {
        var rows = new List<string[]>(items.Count);
        foreach (var wi in items)
        {
            rows.Add(new[]
            {
                wi.ProjectDisplayName,
                TypeIcon(wi.Fields.WorkItemType, s),
                wi.Id.ToString(),
                wi.Fields.Title,
                StateText(wi.Fields.State, s),
                PriorityText(wi.Fields.Priority, s),
                wi.AssignedToName(),
            });
        }
        return rows;
    }

    // ----- filters (list.go) -----

    /// <summary>True if the work item matches the search query (≈ <c>filterWorkItem</c>).</summary>
    public static bool FilterWorkItem(WorkItem wi, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        if (wi.Fields.Title.ToLowerInvariant().Contains(q) ||
            wi.Id.ToString().Contains(q) ||
            wi.Fields.State.ToLowerInvariant().Contains(q) ||
            wi.Fields.WorkItemType.ToLowerInvariant().Contains(q))
            return true;
        if (wi.Fields.AssignedTo is not null &&
            wi.Fields.AssignedTo.DisplayName.ToLowerInvariant().Contains(q))
            return true;
        if (wi.Fields.Tags.ToLowerInvariant().Contains(q))
            return true;
        return false;
    }

    /// <summary>True if the work item matches the query including project name (≈ <c>filterWorkItemMulti</c>).</summary>
    public static bool FilterWorkItemMulti(WorkItem wi, string query)
    {
        if (query == "") return true;
        string q = query.ToLowerInvariant();
        if (wi.ProjectDisplayName.ToLowerInvariant().Contains(q) ||
            wi.ProjectName.ToLowerInvariant().Contains(q))
            return true;
        return FilterWorkItem(wi, query);
    }

    // ----- tag / state collectors and filters (list.go) -----

    /// <summary>All unique tags across the items, sorted alphabetically (≈ <c>collectUniqueTags</c>).</summary>
    public static List<string> CollectUniqueTags(IReadOnlyList<WorkItem> items)
    {
        var seen = new HashSet<string>();
        foreach (var wi in items)
            foreach (var tag in wi.TagList())
                seen.Add(tag);
        var tags = seen.ToList();
        tags.Sort(StringComparer.Ordinal);
        return tags;
    }

    /// <summary>All unique non-empty states across the items, sorted alphabetically (≈ <c>collectUniqueStates</c>).</summary>
    public static List<string> CollectUniqueStates(IReadOnlyList<WorkItem> items)
    {
        var seen = new HashSet<string>();
        foreach (var wi in items)
            if (wi.Fields.State != "")
                seen.Add(wi.Fields.State);
        var states = seen.ToList();
        states.Sort(StringComparer.Ordinal);
        return states;
    }

    /// <summary>Only items carrying the given tag; empty tag returns all (≈ <c>applyTagFilter</c>).</summary>
    public static List<WorkItem> ApplyTagFilter(IReadOnlyList<WorkItem> items, string tag)
    {
        if (tag == "") return items.ToList();
        var filtered = new List<WorkItem>();
        foreach (var wi in items)
            if (wi.TagList().Contains(tag))
                filtered.Add(wi);
        return filtered;
    }

    /// <summary>Only items with the given state; empty state returns all (≈ <c>applyStateFilter</c>).</summary>
    public static List<WorkItem> ApplyStateFilter(IReadOnlyList<WorkItem> items, string state)
    {
        if (state == "") return items.ToList();
        var filtered = new List<WorkItem>();
        foreach (var wi in items)
            if (wi.Fields.State == state)
                filtered.Add(wi);
        return filtered;
    }

    // ----- detail helpers (detail.go) -----

    [GeneratedRegex(@"(?i)</(p|div|br|li|tr)>")]
    private static partial Regex BlockTagsRegex();

    [GeneratedRegex(@"(?i)<br\s*/?>")]
    private static partial Regex BrTagsRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex AnyTagRegex();

    /// <summary>Removes HTML tags and decodes common entities (≈ <c>stripHTMLTags</c>).</summary>
    public static string StripHtmlTags(string s)
    {
        s = BlockTagsRegex().Replace(s, "\n");
        s = BrTagsRegex().Replace(s, "\n");
        s = AnyTagRegex().Replace(s, "");

        s = s.Replace("&nbsp;", " ")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&amp;", "&")
             .Replace("&quot;", "\"")
             .Replace("&#39;", "'");

        while (s.Contains("\n\n\n"))
            s = s.Replace("\n\n\n", "\n\n");

        var lines = s.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].Trim();
        s = string.Join("\n", lines);

        return s.Trim();
    }

    /// <summary>Shortens a long iteration path to its last two segments (≈ <c>shortenIterationPath</c>).</summary>
    public static string ShortenIterationPath(string path)
    {
        var parts = path.Split('\\');
        if (parts.Length <= 2) return path;
        return string.Join("\\", parts[^2..]);
    }

    /// <summary>Wraps text in an OSC 8 terminal hyperlink (≈ <c>hyperlink</c>).</summary>
    public static string Hyperlink(string text, string url)
    {
        if (url == "") return text;
        return $"\x1b]8;;{url}\x07{text}\x1b]8;;\x07";
    }

    /// <summary>Constructs the Azure DevOps URL to view a work item (≈ <c>buildWorkItemURL</c>).</summary>
    public static string BuildWorkItemUrl(string org, string project, int id)
    {
        if (org == "" || project == "") return "";
        return $"https://dev.azure.com/{org}/{project}/_workitems/edit/{id}";
    }
}
