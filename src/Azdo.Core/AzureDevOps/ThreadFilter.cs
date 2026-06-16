namespace Azdo.Core.AzureDevOps;

/// <summary>Helpers for filtering out system-generated PR comment threads.</summary>
public static class ThreadFilter
{
    /// <summary>
    /// Filters out threads that are system-generated comments (e.g. threads whose
    /// comments start with "Microsoft.VisualStudio", policy updates, or vote notices).
    /// </summary>
    public static List<Thread> FilterSystemThreads(IEnumerable<Thread> threads)
    {
        var filtered = new List<Thread>();
        foreach (var thread in threads)
        {
            if (!IsSystemThread(thread))
                filtered.Add(thread);
        }
        return filtered;
    }

    /// <summary>True if the thread is a system-generated thread.</summary>
    public static bool IsSystemThread(Thread thread)
    {
        if (thread.Comments.Count == 0)
            return false;

        foreach (var comment in thread.Comments)
        {
            // Filter by author name (e.g. "Microsoft.VisualStudio.Services.TFS").
            if (comment.Author.DisplayName.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal))
                return true;

            var content = comment.Content.Trim();
            // Filter by content starting with "Microsoft.VisualStudio".
            if (content.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal))
                return true;
            // Filter "Policy status has been updated" comments.
            if (content.Contains("Policy status has been updated", StringComparison.Ordinal))
                return true;
            // Filter "voted" comments (e.g. "John Doe voted -5").
            if (IsVotedComment(content))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the content is a vote notification comment, e.g. "John Doe voted -5",
    /// "Jane Smith voted 10", "Bob voted 0".
    /// </summary>
    internal static bool IsVotedComment(string content)
    {
        int idx = content.IndexOf("voted", StringComparison.Ordinal);
        if (idx == -1)
            return false;
        var after = content[(idx + 5)..].Trim();
        if (after.Length == 0)
            return false;
        if (after[0] == '-' && after.Length > 1)
            after = after[1..];
        return after.Length > 0 && after[0] >= '0' && after[0] <= '9';
    }
}
