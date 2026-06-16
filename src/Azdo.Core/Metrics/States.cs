namespace Azdo.Core.Metrics;

/// <summary>
/// Carries the configured canonical names for the three workflow states the
/// metrics tab buckets on. All matching against snapshot or live state strings
/// is case-insensitive — different ADO projects and historical data often spell
/// the same state differently ("Ready for Test" vs "Ready for test").
/// </summary>
public readonly struct StateConfig
{
    public string Active { get; init; }
    public string ReadyForTest { get; init; }
    public string Closed { get; init; }

    public StateConfig(string active, string readyForTest, string closed)
    {
        Active = active;
        ReadyForTest = readyForTest;
        Closed = closed;
    }

    /// <summary>The historical hardcoded names. Used by tests and as a safety net.</summary>
    public static StateConfig DefaultStates() => new("Active", "Ready for Test", "Closed");

    public bool IsActive(string s) => EqState(s, Active);

    public bool IsRFT(string s) => EqState(s, ReadyForTest);

    public bool IsClosed(string s) => EqState(s, Closed);

    /// <summary>The three configured states in workflow order: Active → RFT → Closed.</summary>
    public string[] Order() => new[] { Active, ReadyForTest, Closed };

    /// <summary>
    /// Position of <paramref name="s"/> in <see cref="Order"/> (case-insensitive).
    /// ok=false when <paramref name="s"/> does not match any of the three states.
    /// </summary>
    public (int Index, bool Ok) IndexOf(string s)
    {
        if (IsActive(s)) return (0, true);
        if (IsRFT(s)) return (1, true);
        if (IsClosed(s)) return (2, true);
        return (-1, false);
    }

    private static bool EqState(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
