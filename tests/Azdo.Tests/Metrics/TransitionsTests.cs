using System.Globalization;
using Azdo.Core.Metrics;
using Xunit;

namespace Azdo.Tests.Metrics;

public class TransitionsTests
{
    [Theory]
    [InlineData("", "Active", GapAction.Write)]
    [InlineData("", "Closed", GapAction.Write)]
    [InlineData("Active", "Active", GapAction.Write)]
    [InlineData("Ready for Test", "Ready for Test", GapAction.Write)]
    [InlineData("Active", "Ready for Test", GapAction.Write)]
    [InlineData("Ready for Test", "Closed", GapAction.Write)]
    [InlineData("Active", "Closed", GapAction.NeedsFallback)]
    [InlineData("Closed", "Active", GapAction.NeedsFallback)]
    [InlineData("Closed", "Ready for Test", GapAction.NeedsFallback)]
    [InlineData("Ready for Test", "Active", GapAction.NeedsFallback)]
    [InlineData("ACTIVE", "ready for test", GapAction.Write)]
    [InlineData("Active", "Resolved", GapAction.NeedsFallback)]
    [InlineData("New", "Active", GapAction.NeedsFallback)]
    public void ClassifyTransition(string prev, string curr, GapAction want)
        => Assert.Equal(want, Transitions.ClassifyTransition(prev, curr, StateConfig.DefaultStates()));

    [Theory]
    [InlineData("In Progress", "RFT", GapAction.Write)]
    [InlineData("RFT", "Done", GapAction.Write)]
    [InlineData("In Progress", "Done", GapAction.NeedsFallback)]
    [InlineData("Done", "In Progress", GapAction.NeedsFallback)]
    [InlineData("In Progress", "In Progress", GapAction.Write)]
    [InlineData("in progress", "RFT", GapAction.Write)]
    public void ClassifyTransition_CustomWorkflow(string prev, string curr, GapAction want)
    {
        var sc = new StateConfig("In Progress", "RFT", "Done");
        Assert.Equal(want, Transitions.ClassifyTransition(prev, curr, sc));
    }

    private static DateTime Day(int d) => new(2026, 5, d, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SynthesizeGapRows_FillsIntermediateDays()
    {
        var transitions = new[]
        {
            new StateTransition("Active", Day(1).AddHours(10)),
            new StateTransition("Ready for Test", Day(3).AddHours(10)),
            new StateTransition("Closed", Day(5).AddHours(10)),
        };
        var template = new Snapshot { Id = 42, Project = "p", AssignedTo = "Alice", Points = 3 };
        var rows = Transitions.SynthesizeGapRows(transitions, Day(1), Day(5), template);

        Assert.Equal(3, rows.Count);
        var want = new[]
        {
            ("2026-05-02", "Active"),
            ("2026-05-03", "Ready for Test"),
            ("2026-05-04", "Ready for Test"),
        };
        for (int i = 0; i < want.Length; i++)
        {
            Assert.Equal(want[i].Item1, rows[i].TS);
            Assert.Equal(want[i].Item2, rows[i].State);
            Assert.Equal(Snapshots.SourceUpdates, rows[i].Source);
            Assert.Equal(template.Id, rows[i].Id);
            Assert.Equal(template.AssignedTo, rows[i].AssignedTo);
        }
    }

    [Fact]
    public void SynthesizeGapRows_HandlesUnsortedInput()
    {
        var transitions = new[]
        {
            new StateTransition("Closed", Day(5).AddHours(10)),
            new StateTransition("Active", Day(1).AddHours(10)),
            new StateTransition("Ready for Test", Day(3).AddHours(10)),
        };
        var rows = Transitions.SynthesizeGapRows(transitions, Day(1), Day(5), new Snapshot { Id = 1 });
        Assert.Equal(3, rows.Count);
        Assert.Equal("Ready for Test", rows[1].State);
    }

    [Fact]
    public void SynthesizeGapRows_NoGap()
    {
        var transitions = new[] { new StateTransition("Active", Day(1)) };
        var rows = Transitions.SynthesizeGapRows(transitions, Day(1), Day(2), new Snapshot { Id = 1 });
        Assert.Empty(rows);
    }

    [Fact]
    public void SynthesizeGapRows_EmptyTransitions()
    {
        var day = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Transitions.SynthesizeGapRows(Array.Empty<StateTransition>(), day, day.AddDays(5), new Snapshot());
        Assert.Empty(rows);
    }

    [Fact]
    public void SynthesizeGapRows_SkipsDaysBeforeFirstTransition()
    {
        var transitions = new[] { new StateTransition("Active", Day(10).AddHours(10)) };
        var rows = Transitions.SynthesizeGapRows(transitions, Day(5), Day(15), new Snapshot { Id = 1 });
        Assert.Equal(5, rows.Count);
        Assert.Equal("2026-05-10", rows[0].TS);
        Assert.Equal("2026-05-14", rows[^1].TS);
    }
}
