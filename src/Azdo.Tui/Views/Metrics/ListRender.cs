using System.Globalization;
using System.Text;
using Azdo.Core.Metrics;
using Azdo.Tui.Rendering;

namespace Azdo.Tui.Views.Metrics;

public sealed partial class Model
{
    // Flags-pane columns.
    private const int FlagCursorW = 2;
    private const int FlagIdW = 8;
    private const int FlagStateW = 7;
    private const int FlagDwellW = 6;
    private const int FlagUserW = 14;
    private const int FlagProjectW = 18;
    private const int FlagTitleW = 50;

    // Users-pane columns.
    private const int UserCursorW = 2;
    private const int UserNameW = 18;
    private const int UserInFlightW = 10;
    private const int UserActiveW = 7;
    private const int UserRFTW = 5;
    private const int UserOldActiveW = 11;
    private const int UserOldRFTW = 9;
    private const int UserClosedPtsW = 11;
    private const int UserStalledW = 3;

    /// <summary>Pads or truncates s so its display width equals n. ANSI-aware.</summary>
    internal static string PadCol(string s, int n)
    {
        int w = Ansi.Width(s);
        if (w == n) return s;
        if (w > n) return Ansi.Truncate(s, n);
        return s + new string(' ', n - w);
    }

    /// <summary>Resolves the three column-header labels (active, rft, closed).</summary>
    private (string Active, string Rft, string Closed) StateLabels()
    {
        var sc = StateConfigOf();
        var lbl = _config?.Metrics.StateLabels;
        return (
            Labels.LabelFor(sc.Active, lbl?.Active ?? ""),
            Labels.LabelFor(sc.ReadyForTest, lbl?.ReadyForTest ?? ""),
            Labels.LabelFor(sc.Closed, lbl?.Closed ?? ""));
    }

    private string RenderHeader()
    {
        var mc = _config?.Metrics;
        var parts = new List<string> { "Metrics" };
        if (_mode == ViewMode.TrendsChart)
        {
            parts.Add("Trends (chart)");
        }
        else if (_mode == ViewMode.Trends)
        {
            parts.Add("Trends");
        }
        else
        {
            parts.Add("Live");
            if (_activeTag != "") parts.Add("Tag: " + _activeTag);
            var (_, rftLbl, _) = StateLabels();
            parts.Add($"Interval {IntervalDays()}d");
            parts.Add($"Active-stale >{mc?.ActiveStaleDays ?? 0}d");
            parts.Add($"{Labels.TitleCase(rftLbl)}-stale >{mc?.RFTStaleDays ?? 0}d");
        }
        parts.Add("Updated " + LastUpdatedLabel());
        if (_mode == ViewMode.Live)
        {
            var (_, rftLbl, _) = StateLabels();
            if (_flagFilter == FlagFilter.ActiveStale) parts.Add("Filter: Active-stale");
            else if (_flagFilter == FlagFilter.RFTStale) parts.Add("Filter: " + Labels.TitleCase(rftLbl) + "-stale");
        }
        var line = string.Join(" · ", parts);
        return _styles is not null ? _styles.Header.Render(line) : line;
    }

    private string LastUpdatedLabel()
    {
        if (_lastUpdated == default) return "never";
        var d = _now() - _lastUpdated;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}m ago";
        if (d < TimeSpan.FromHours(24)) return $"{(int)d.TotalHours}h ago";
        return $"{(int)(d.TotalHours / 24)}d ago";
    }

    internal string RenderFlagsPane()
    {
        var vis = VisibleFlags();
        var title = $"⚠  Stuck items ({vis.Count})";
        if (_styles is not null && _focusedPane == FocusedPane.Flags)
            title = _styles.Warning.Render(title) + _styles.Muted.Render("  [focused]");
        else if (_styles is not null)
            title = _styles.Warning.Render(title);

        int totalW = FlagCursorW + FlagIdW + 1 + FlagStateW + 1 + FlagDwellW + 1 + FlagUserW + 1 + FlagProjectW + 1 + FlagTitleW;

        if (vis.Count == 0)
        {
            var body = PadCol("  (no flagged items)", totalW);
            if (_styles is not null) body = _styles.Muted.Render(body);
            return title + "\n" + body;
        }

        var b = new StringBuilder();
        b.Append(title).Append('\n');
        for (int i = 0; i < vis.Count; i++)
        {
            var f = vis[i];
            var cursor = PadCol("  ", FlagCursorW);
            if (_focusedPane == FocusedPane.Flags && i == _flagCursor)
                cursor = PadCol("> ", FlagCursorW);
            var row = cursor +
                PadCol($"#{f.Id}", FlagIdW) + " " +
                PadCol(ShortenState(f.State), FlagStateW) + " " +
                PadCol(FmtDwell(f.Dwell), FlagDwellW) + " " +
                PadCol(f.User, FlagUserW) + " " +
                PadCol(f.Project, FlagProjectW) + " " +
                PadCol(f.Title, FlagTitleW);
            if (_styles is not null && _focusedPane == FocusedPane.Flags && i == _flagCursor)
                row = _styles.Selected.Render(row);
            else if (_styles is not null)
                row = _styles.Error.Render(row);
            b.Append(row).Append('\n');
        }
        return b.ToString().TrimEnd('\n');
    }

    internal string RenderUsersPane()
    {
        var title = $"Per developer (sorted by stalled, then in-flight)  —  {_userRows.Count}";
        if (_styles is not null && _focusedPane == FocusedPane.Users)
            title = _styles.Header.Render(title) + _styles.Muted.Render("  [focused]");
        else if (_styles is not null)
            title = _styles.Header.Render(title);

        int totalW = UserCursorW + UserNameW + 1 + UserInFlightW + 1 + UserActiveW + 1 + UserRFTW + 1 +
            UserOldActiveW + 1 + UserOldRFTW + 1 + UserClosedPtsW + 1 + UserStalledW;

        if (_userRows.Count == 0)
        {
            var body = PadCol("  (no in-flight items)", totalW);
            if (_styles is not null) body = _styles.Muted.Render(body);
            return title + "\n" + body;
        }

        var (activeLbl, rftLbl, _) = StateLabels();
        var activeTitle = Labels.TitleCase(activeLbl);
        var rftTitle = Labels.TitleCase(rftLbl);
        var header = PadCol("  ", UserCursorW) +
            PadCol("User", UserNameW) + " " +
            PadCol("In-flight", UserInFlightW) + " " +
            PadCol(activeTitle, UserActiveW) + " " +
            PadCol(rftTitle, UserRFTW) + " " +
            PadCol("Old-" + activeTitle, UserOldActiveW) + " " +
            PadCol("Old-" + rftTitle, UserOldRFTW) + " " +
            PadCol("Closed-pts", UserClosedPtsW) + " " +
            PadCol("⚠", UserStalledW);
        if (_styles is not null) header = _styles.Muted.Render(header);

        var b = new StringBuilder();
        b.Append(title).Append('\n');
        b.Append(header).Append('\n');

        for (int i = 0; i < _userRows.Count; i++)
        {
            var r = _userRows[i];
            var cursor = PadCol("  ", UserCursorW);
            if (_focusedPane == FocusedPane.Users && i == _userCursor)
                cursor = PadCol("> ", UserCursorW);
            var inFlight = r.InFlight.ToString(CultureInfo.InvariantCulture);
            if (r.Overloaded) inFlight += " ⚠";
            var row = cursor +
                PadCol(r.User, UserNameW) + " " +
                PadCol(inFlight, UserInFlightW) + " " +
                PadCol(r.ActiveCount.ToString(CultureInfo.InvariantCulture), UserActiveW) + " " +
                PadCol(r.RFTCount.ToString(CultureInfo.InvariantCulture), UserRFTW) + " " +
                PadCol(FmtDwell(r.OldestActive), UserOldActiveW) + " " +
                PadCol(FmtDwell(r.OldestRFT), UserOldRFTW) + " " +
                PadCol(FmtPoints(r.PointsClosed), UserClosedPtsW) + " " +
                PadCol(r.Stalled.ToString(CultureInfo.InvariantCulture), UserStalledW);
            if (_styles is not null && _focusedPane == FocusedPane.Users && i == _userCursor)
                row = _styles.Selected.Render(row);
            else if (_styles is not null && r.Stalled > 0)
                row = _styles.Warning.Render(row);
            else if (_styles is not null)
                row = _styles.Value.Render(row);
            b.Append(row).Append('\n');
        }
        return b.ToString().TrimEnd('\n');
    }

    private string ShortenState(string s)
    {
        var sc = StateConfigOf();
        var (activeLbl, rftLbl, closedLbl) = StateLabels();
        if (sc.IsActive(s)) return Labels.TitleCase(activeLbl);
        if (sc.IsRFT(s)) return Labels.TitleCase(rftLbl);
        if (sc.IsClosed(s)) return Labels.TitleCase(closedLbl);
        return s;
    }

    internal static string FmtDwell(TimeSpan d)
    {
        if (d <= TimeSpan.Zero) return "—";
        if (d >= TimeSpan.FromHours(24)) return $"{(int)(d.TotalHours / 24)}d";
        if (d >= TimeSpan.FromHours(1)) return $"{(int)d.TotalHours}h";
        return $"{(int)d.TotalMinutes}m";
    }

    internal static string FmtPoints(double p)
    {
        if (p == 0) return "—";
        if (p == Math.Floor(p)) return ((long)p).ToString(CultureInfo.InvariantCulture);
        return p.ToString("F1", CultureInfo.InvariantCulture);
    }
}
