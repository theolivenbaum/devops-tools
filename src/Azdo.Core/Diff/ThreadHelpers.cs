using Azdo.Core.AzureDevOps;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Core.Diff;

/// <summary>
/// Helpers mapping PR comment threads to files/lines (≈ the thread functions in
/// <c>diff/diff.go</c>).
/// </summary>
public static class ThreadHelpers
{
    /// <summary>Total comments per file path across all file-anchored threads.</summary>
    public static Dictionary<string, int> CountCommentsPerFile(IEnumerable<Thread> threads)
    {
        var result = new Dictionary<string, int>();
        foreach (var t in threads)
        {
            if (t.ThreadContext is null || string.IsNullOrEmpty(t.ThreadContext.FilePath)) continue;
            result.TryGetValue(t.ThreadContext.FilePath, out var c);
            result[t.ThreadContext.FilePath] = c + t.Comments.Count;
        }
        return result;
    }

    /// <summary>Threads without a file context (general PR comments).</summary>
    public static List<Thread> FilterGeneralThreads(IEnumerable<Thread> threads)
        => threads.Where(t => t.ThreadContext is null || string.IsNullOrEmpty(t.ThreadContext.FilePath)).ToList();

    /// <summary>Total comments across all general (non-file) threads.</summary>
    public static int CountGeneralComments(IEnumerable<Thread> threads)
        => threads.Where(t => t.ThreadContext is null || string.IsNullOrEmpty(t.ThreadContext.FilePath))
                  .Sum(t => t.Comments.Count);

    /// <summary>Maps threads for a file to their (right-side) line numbers.</summary>
    public static Dictionary<int, List<Thread>> MapThreadsToLines(IEnumerable<Thread> threads, string filePath)
    {
        var result = new Dictionary<int, List<Thread>>();
        foreach (var t in threads)
        {
            if (t.ThreadContext is null) continue;
            if (t.ThreadContext.FilePath != filePath) continue;
            if (t.ThreadContext.RightFileStart is null) continue;
            var line = t.ThreadContext.RightFileStart.Line;
            if (!result.TryGetValue(line, out var list)) { list = new List<Thread>(); result[line] = list; }
            list.Add(t);
        }
        return result;
    }
}
