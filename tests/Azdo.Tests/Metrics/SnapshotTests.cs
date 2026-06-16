using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class SnapshotTests
{
    private static readonly DateTime SnapNow = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Ret = TimeSpan.FromDays(90);

    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name);

    [Fact]
    public void ReadSnapshots_MissingFile()
    {
        var rows = Snapshots.ReadSnapshots(TempFile("does-not-exist.jsonl"));
        Assert.Empty(rows);
    }

    [Fact]
    public void ReadSnapshots_RoundTrip()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        var input = new[]
        {
            new Snapshot { TS = "2026-05-28", Id = 1, State = "Active", AssignedTo = "Alice", Source = Snapshots.SourceSnapshot },
            new Snapshot { TS = "2026-05-29", Id = 1, State = "Ready for Test", AssignedTo = "Alice", Source = Snapshots.SourceSnapshot },
        };
        Snapshots.AppendSnapshots(path, input, Ret, SnapNow);
        var output = Snapshots.ReadSnapshots(path);
        Assert.Equal(input.Length, output.Count);
        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(input[i].TS, output[i].TS);
            Assert.Equal(input[i].Id, output[i].Id);
            Assert.Equal(input[i].State, output[i].State);
        }
    }

    [Fact]
    public void ReadSnapshots_SkipsMalformed()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        var good = JsonSerializer.Serialize(new Snapshot { TS = "2026-05-28", Id = 1, State = "Active" });
        File.WriteAllText(path, good + "\n{this is not json}\n" + good + "\n");
        var rows = Snapshots.ReadSnapshots(path);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void AppendSnapshots_DedupLatestWins()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        Snapshots.AppendSnapshots(path,
            new[] { new Snapshot { TS = "2026-05-28", Id = 1, State = "Active", AssignedTo = "Alice" } }, Ret, SnapNow);
        Snapshots.AppendSnapshots(path,
            new[] { new Snapshot { TS = "2026-05-28", Id = 1, State = "Ready for Test", AssignedTo = "Alice" } }, Ret, SnapNow);
        var rows = Snapshots.ReadSnapshots(path);
        Assert.Single(rows);
        Assert.Equal("Ready for Test", rows[0].State);
    }

    [Fact]
    public void AppendSnapshots_PrunesOldRows()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        var combined = new[]
        {
            new Snapshot { TS = "2026-02-21", Id = 1, State = "Active" },
            new Snapshot { TS = "2026-05-31", Id = 2, State = "Active" },
        };
        Snapshots.AppendSnapshots(path, combined, Ret, SnapNow);
        var rows = Snapshots.ReadSnapshots(path);
        Assert.Single(rows);
        Assert.Equal(2, rows[0].Id);
    }

    [Fact]
    public void AppendSnapshots_AtomicLeavesNoTempFile()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        Snapshots.AppendSnapshots(path,
            new[] { new Snapshot { TS = "2026-05-29", Id = 1, State = "Active" } }, Ret, SnapNow);
        var dir = Path.GetDirectoryName(path)!;
        foreach (var e in Directory.GetFiles(dir))
            Assert.NotEqual(".tmp", Path.GetExtension(e));
    }

    [Fact]
    public void AppendSnapshots_SortsDeterministically()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        var input = new[]
        {
            new Snapshot { TS = "2026-05-29", Id = 5, State = "Active" },
            new Snapshot { TS = "2026-05-28", Id = 2, State = "Active" },
            new Snapshot { TS = "2026-05-28", Id = 1, State = "Active" },
            new Snapshot { TS = "2026-05-29", Id = 1, State = "Active" },
        };
        Snapshots.AppendSnapshots(path, input, Ret, SnapNow);
        var rows = Snapshots.ReadSnapshots(path);
        for (int i = 1; i < rows.Count; i++)
        {
            var prev = rows[i - 1];
            var cur = rows[i];
            bool sorted = string.CompareOrdinal(prev.TS, cur.TS) < 0
                || (prev.TS == cur.TS && prev.Id <= cur.Id);
            Assert.True(sorted, $"rows not sorted at {i}");
        }
    }

    private static WorkItem MkSnapItem(int id, string state, string user, string project, double points, string tags)
    {
        var wi = new WorkItem { Id = id, ProjectName = project, ProjectDisplayName = project };
        wi.Fields.State = state;
        wi.Fields.StoryPoints = points;
        wi.Fields.Tags = tags;
        wi.Fields.StateChangeDate = SnapNow.AddDays(-2);
        if (state == "Closed") wi.Fields.ClosedDate = SnapNow.AddDays(-1);
        if (user != "") wi.Fields.AssignedTo = new Identity { DisplayName = user };
        return wi;
    }

    [Fact]
    public void BuildSnapshots_FromWorkItems()
    {
        var items = new[]
        {
            MkSnapItem(1, "Active", "Alice", "proj-a", 3, "sprint-42;mobile"),
            MkSnapItem(2, "Closed", "Bob", "proj-b", 5, ""),
        };
        var rows = Snapshots.BuildSnapshots(items, SnapNow);
        Assert.Equal(2, rows.Count);
        foreach (var r in rows)
        {
            Assert.Equal("2026-06-01", r.TS);
            Assert.Equal(Snapshots.SourceSnapshot, r.Source);
        }
        Assert.Equal(new[] { "sprint-42", "mobile" }, rows[0].Tags);
        Assert.Equal("Alice", rows[0].AssignedTo);
        Assert.Equal(3, rows[0].Points);
    }

    [Fact]
    public async Task AppendSnapshots_ConcurrentCallsPreserveAllRows()
    {
        var path = TempFile("snap.jsonl");
        Snapshots.EnsureSnapshotDir(path);
        const int perWriter = 50;
        var a = new List<Snapshot>();
        var b = new List<Snapshot>();
        for (int i = 0; i < perWriter; i++)
        {
            a.Add(new Snapshot { TS = "2026-05-28", Id = 1000 + i, State = "Active", Source = Snapshots.SourceSnapshot });
            b.Add(new Snapshot { TS = "2026-05-28", Id = 2000 + i, State = "Active", Source = Snapshots.SourceUpdates });
        }
        var t1 = Task.Run(() => Snapshots.AppendSnapshots(path, a, Ret, SnapNow));
        var t2 = Task.Run(() => Snapshots.AppendSnapshots(path, b, Ret, SnapNow));
        await Task.WhenAll(t1, t2);

        var got = Snapshots.ReadSnapshots(path);
        var ids = got.Select(s => s.Id).ToHashSet();
        for (int i = 0; i < perWriter; i++)
        {
            Assert.Contains(1000 + i, ids);
            Assert.Contains(2000 + i, ids);
        }
    }

    [Fact]
    public void LatestPerItem_AndHasSnapshotForToday()
    {
        var snaps = new[]
        {
            new Snapshot { TS = "2026-05-28", Id = 1, State = "Active" },
            new Snapshot { TS = "2026-05-30", Id = 1, State = "Closed" },
            new Snapshot { TS = "2026-05-29", Id = 2, State = "Active" },
        };
        var latest = Snapshots.LatestPerItem(snaps);
        Assert.Equal("Closed", latest[1].State);
        Assert.Equal("2026-05-29", latest[2].TS);
        Assert.True(Snapshots.HasSnapshotForToday(snaps, "2026-05-30"));
        Assert.False(Snapshots.HasSnapshotForToday(snaps, "2026-06-01"));
    }
}
