namespace Azdo.Core.Diff;

public enum LineType { Context, Added, Removed }

/// <summary>A single line in a diff (≈ <c>diff.Line</c>).</summary>
public sealed record DiffLine(LineType Type, string Content, int OldNum, int NewNum);

/// <summary>A contiguous group of changes with surrounding context (≈ <c>diff.Hunk</c>).</summary>
public sealed class Hunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public List<DiffLine> Lines { get; } = new();
}

/// <summary>Diff result for a single file (≈ <c>diff.FileDiff</c>).</summary>
public sealed class FileDiff
{
    public string Path { get; set; } = "";
    public string ChangeType { get; set; } = ""; // add, edit, delete, rename
    public string OldPath { get; set; } = "";
    public List<Hunk> Hunks { get; set; } = new();
}

/// <summary>LCS-based line diff (≈ <c>diff.ComputeDiff</c>).</summary>
public static class DiffEngine
{
    private readonly record struct EditOp(LineType Type, string Content, int OldNum, int NewNum);

    public static List<Hunk> ComputeDiff(string oldContent, string newContent, int contextLines)
    {
        var oldLines = SplitLines(oldContent);
        var newLines = SplitLines(newContent);
        var ops = ComputeEditScript(oldLines, newLines);
        return BuildHunks(ops, contextLines);
    }

    private static List<string> SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return new List<string>();
        var lines = content.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static List<EditOp> ComputeEditScript(List<string> oldLines, List<string> newLines)
    {
        int m = oldLines.Count, n = newLines.Count;
        var lcs = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                lcs[i, j] = oldLines[i - 1] == newLines[j - 1]
                    ? lcs[i - 1, j - 1] + 1
                    : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);

        var ops = new List<EditOp>();
        int x = m, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && oldLines[x - 1] == newLines[y - 1])
            {
                ops.Add(new EditOp(LineType.Context, oldLines[x - 1], x, y));
                x--; y--;
            }
            else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
            {
                ops.Add(new EditOp(LineType.Added, newLines[y - 1], 0, y));
                y--;
            }
            else
            {
                ops.Add(new EditOp(LineType.Removed, oldLines[x - 1], x, 0));
                x--;
            }
        }
        ops.Reverse();
        return ops;
    }

    private static List<Hunk> BuildHunks(List<EditOp> ops, int contextLines)
    {
        var hunks = new List<Hunk>();
        if (ops.Count == 0) return hunks;

        var changeIndices = new List<int>();
        for (int i = 0; i < ops.Count; i++)
            if (ops[i].Type != LineType.Context) changeIndices.Add(i);
        if (changeIndices.Count == 0) return hunks;

        var ranges = new List<(int start, int end)>();
        foreach (var idx in changeIndices)
        {
            int start = Math.Max(0, idx - contextLines);
            int end = Math.Min(ops.Count - 1, idx + contextLines);
            ranges.Add((start, end));
        }

        var merged = new List<(int start, int end)>();
        var current = ranges[0];
        for (int i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].start <= current.end + 1)
            {
                if (ranges[i].end > current.end) current = (current.start, ranges[i].end);
            }
            else { merged.Add(current); current = ranges[i]; }
        }
        merged.Add(current);

        foreach (var r in merged)
        {
            var hunk = new Hunk();
            for (int i = r.start; i <= r.end; i++)
            {
                var op = ops[i];
                hunk.Lines.Add(new DiffLine(op.Type, op.Content, op.OldNum, op.NewNum));
            }
            if (hunk.Lines.Count > 0)
            {
                hunk.OldStart = hunk.Lines.FirstOrDefault(l => l.OldNum > 0)?.OldNum ?? 0;
                hunk.NewStart = hunk.Lines.FirstOrDefault(l => l.NewNum > 0)?.NewNum ?? 0;
                foreach (var line in hunk.Lines)
                {
                    if (line.Type != LineType.Added) hunk.OldCount++;
                    if (line.Type != LineType.Removed) hunk.NewCount++;
                }
            }
            hunks.Add(hunk);
        }
        return hunks;
    }
}
