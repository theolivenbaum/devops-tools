using Azdo.Core.AzureDevOps;
using Azdo.Tui.Polling;
using Xunit;

namespace Azdo.Tests.Polling;

public class ErrorHandlerTests
{
    private static PipelineRunsUpdated Ok(params int[] ids)
        => new(ids.Select(i => new PipelineRun { Id = i }).ToList(), null);

    [Fact]
    public void Success_StoresGoodData_NoError()
    {
        var h = new ErrorHandler();
        var (runs, hasError) = h.ProcessUpdate(Ok(1, 2));
        Assert.False(hasError);
        Assert.Equal(2, runs.Count);
        Assert.False(h.HasError());
    }

    [Fact]
    public void FullError_ReturnsLastKnownGood()
    {
        var h = new ErrorHandler();
        h.ProcessUpdate(Ok(1, 2)); // seed good data
        var (runs, hasError) = h.ProcessUpdate(new PipelineRunsUpdated(new(), new Exception("boom")));
        Assert.True(hasError);
        Assert.Equal(2, runs.Count); // stale data retained
        Assert.True(h.HasError());
    }

    [Fact]
    public void PartialError_TreatsDataAsValid_WithWarning()
    {
        var h = new ErrorHandler();
        var pe = new PartialException(1, 3, new[] { new Exception("p3 failed") });
        var (runs, hasError) = h.ProcessUpdate(new PipelineRunsUpdated(Ok(1).Runs, pe));
        Assert.False(hasError);
        Assert.Single(runs);
        Assert.False(h.HasError());
        Assert.NotEqual("", h.PartialWarning());
    }

    [Fact]
    public void Recovery_EscalatesAfterThreshold()
    {
        var h = new ErrorHandler();
        for (int i = 0; i <= ErrorHandler.MaxRecoverableErrors; i++)
            h.ProcessUpdate(new PipelineRunsUpdated(new(), new Exception("x")));
        Assert.False(h.IsRecoverable());
        Assert.Contains("Check your network", h.RecoveryMessage());
    }
}

public class DemoServerTests
{
    [Fact]
    public async Task DemoClient_ServesPipelinesPrsAndWorkItems()
    {
        var client = Azdo.Core.Demo.DemoServer.CreateClient();
        var runs = await client.ListPipelineRunsAsync(30);
        var prs = await client.ListPullRequestsAsync(25);
        var items = await client.ListWorkItemsAsync(50);

        Assert.NotEmpty(runs);   // merged across 2 demo projects
        Assert.NotEmpty(prs);
        Assert.NotEmpty(items);
        Assert.All(prs, p => Assert.False(string.IsNullOrEmpty(p.ProjectDisplayName)));
    }
}
