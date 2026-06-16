using Azdo.Core.AzureDevOps;

namespace Azdo.Core.Metrics;

/// <summary>
/// Configurable cut-offs that drive flagging and the overloaded marker on the
/// per-user table.
/// </summary>
public sealed class Thresholds
{
    /// <summary>Dwell in Active longer than this → active-stale flag.</summary>
    public int ActiveStaleDays { get; set; }

    /// <summary>Dwell in Ready for Test longer than this → rft-stale flag.</summary>
    public int RFTStaleDays { get; set; }

    /// <summary>Strictly more in-flight items than this → Overloaded.</summary>
    public int WIPLimit { get; set; }

    /// <summary>Canonical state names; matched case-insensitively.</summary>
    public StateConfig States { get; set; } = StateConfig.DefaultStates();
}

/// <summary>One row of the per-developer table.</summary>
public sealed class UserMetrics
{
    public string User { get; set; } = "";
    public int InFlight { get; set; }
    public int ActiveCount { get; set; }
    public int RFTCount { get; set; }
    public double PointsClosed { get; set; }
    public TimeSpan OldestActive { get; set; }
    public TimeSpan OldestRFT { get; set; }
    public int Stalled { get; set; }
    public bool Overloaded { get; set; }
}

/// <summary>One row of the worst-first stuck-items digest.</summary>
public sealed class ItemFlag
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public string User { get; set; } = "";
    public string State { get; set; } = "";
    public TimeSpan Dwell { get; set; }

    /// <summary>"active-stale" or "rft-stale".</summary>
    public string Reason { get; set; } = "";
}

/// <summary>Pure aggregation logic for the metrics tab.</summary>
public static class Aggregator
{
    public const string ReasonActiveStale = "active-stale";
    public const string ReasonRFTStale = "rft-stale";

    /// <summary>
    /// Rolls a flat slice of work items into per-developer rows and a flat list of
    /// flagged items, applying <paramref name="th"/> thresholds. Items in states
    /// other than Active / Ready for Test / Closed are ignored. Closed items
    /// contribute to PointsClosed only when their ClosedDate falls strictly after
    /// <paramref name="intervalStart"/>.
    /// </summary>
    public static (List<UserMetrics> Rows, List<ItemFlag> Flags) Aggregate(
        IReadOnlyList<WorkItem> items, DateTime intervalStart, DateTime now, Thresholds th)
    {
        var byUser = new Dictionary<string, UserMetrics>();
        UserMetrics Get(string u)
        {
            if (!byUser.TryGetValue(u, out var um))
            {
                um = new UserMetrics { User = u };
                byUser[u] = um;
            }
            return um;
        }

        var activeStale = TimeSpan.FromDays(th.ActiveStaleDays);
        var rftStale = TimeSpan.FromDays(th.RFTStaleDays);
        var states = th.States;

        var flags = new List<ItemFlag>();

        foreach (var wi in items)
        {
            var user = wi.AssignedToName();
            var dwell = wi.TimeInCurrentState(now);
            var s = wi.Fields.State;

            if (states.IsActive(s))
            {
                var um = Get(user);
                um.InFlight++;
                um.ActiveCount++;
                if (dwell > um.OldestActive) um.OldestActive = dwell;
                if (dwell > activeStale)
                {
                    um.Stalled++;
                    flags.Add(new ItemFlag
                    {
                        Id = wi.Id,
                        Title = wi.Fields.Title,
                        Project = wi.ProjectDisplayName,
                        User = user,
                        State = wi.Fields.State,
                        Dwell = dwell,
                        Reason = ReasonActiveStale,
                    });
                }
            }
            else if (states.IsRFT(s))
            {
                var um = Get(user);
                um.InFlight++;
                um.RFTCount++;
                if (dwell > um.OldestRFT) um.OldestRFT = dwell;
                if (dwell > rftStale)
                {
                    um.Stalled++;
                    flags.Add(new ItemFlag
                    {
                        Id = wi.Id,
                        Title = wi.Fields.Title,
                        Project = wi.ProjectDisplayName,
                        User = user,
                        State = wi.Fields.State,
                        Dwell = dwell,
                        Reason = ReasonRFTStale,
                    });
                }
            }
            else if (states.IsClosed(s))
            {
                if (wi.Fields.ClosedDate != default && wi.Fields.ClosedDate > intervalStart)
                {
                    var um = Get(user);
                    um.PointsClosed += wi.EffectivePoints();
                }
            }
        }

        var rows = new List<UserMetrics>(byUser.Count);
        foreach (var um in byUser.Values)
        {
            um.Overloaded = um.InFlight > th.WIPLimit;
            rows.Add(um);
        }

        rows.Sort((a, b) =>
        {
            if (a.Stalled != b.Stalled) return b.Stalled.CompareTo(a.Stalled);
            return b.InFlight.CompareTo(a.InFlight);
        });
        flags.Sort((a, b) => b.Dwell.CompareTo(a.Dwell));

        return (rows, flags);
    }
}
