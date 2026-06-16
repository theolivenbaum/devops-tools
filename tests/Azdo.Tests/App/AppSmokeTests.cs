using Azdo.Core.Configuration;
using Azdo.Core.Demo;
using Azdo.Tui.App;
using Azdo.Tui.Runtime;
using Xunit;

namespace Azdo.Tests.App;

/// <summary>
/// End-to-end smoke tests that drive the real message loop against the demo
/// client: AppModel routing → view → API client → rendering, with no TTY.
/// </summary>
public class AppSmokeTests
{
    private static AppModel NewDemoApp()
    {
        var client = DemoServer.CreateClient();
        var cfg = Config.NewWithPath(DemoServer.Org,
            new List<string> { DemoServer.ProjectNexus, DemoServer.ProjectHorizon },
            3600, "dracula", Path.Combine(Path.GetTempPath(), $"azdo-{Guid.NewGuid():N}.yaml"));
        cfg.DisplayNames = new Dictionary<string, string>
        {
            [DemoServer.ProjectNexus] = DemoServer.DisplayNexus,
            [DemoServer.ProjectHorizon] = DemoServer.DisplayHorizon,
        };
        return new AppModel(client, cfg, "test", "none");
    }

    /// <summary>
    /// Runs commands and feeds their resulting messages back, simulating the
    /// runtime loop. Commands that don't resolve quickly (the 3600s demo poll
    /// tick, the GitHub version check) are dropped so the loop stays bounded.
    /// </summary>
    private static async Task Drive(AppModel model, Cmd? cmd, int maxRounds = 40)
    {
        var queue = new Queue<Cmd>();
        if (cmd is not null) queue.Enqueue(cmd);
        while (maxRounds-- > 0 && queue.Count > 0)
        {
            var c = queue.Dequeue();
            var task = c();
            var done = await Task.WhenAny(task, Task.Delay(1500));
            if (done != task) continue; // slow tick / network — skip
            var msg = task.Result;
            if (msg is null) continue;
            if (msg is BatchMsg batch) { foreach (var bc in batch.Commands) queue.Enqueue(bc); continue; }
            if (msg is QuitMsg or TickMsg) continue;
            var (_, next) = model.Update(msg);
            if (next is not null) queue.Enqueue(next);
        }
    }

    [Fact]
    public void Renders_TabBar_AfterResize()
    {
        var model = NewDemoApp();
        model.Update(new WindowSizeMsg(120, 40));
        var view = model.View();
        Assert.Contains("Pull Requests", view);
        Assert.Contains("Work Items", view);
        Assert.Contains("Pipelines", view);
    }

    [Fact]
    public async Task LoadsPullRequests_FromDemoClient()
    {
        var model = NewDemoApp();
        model.Update(new WindowSizeMsg(140, 40));
        await Drive(model, model.Init());
        var view = model.View();
        // Demo PR titles should appear once the async fetch resolves.
        Assert.Contains("OAuth", view);
    }

    [Fact]
    public async Task SwitchToPipelines_ShowsRuns()
    {
        var model = NewDemoApp();
        model.Update(new WindowSizeMsg(140, 40));
        await Drive(model, model.Init());
        // Feed a poller-style pipeline update directly.
        var (_, cmd) = model.Update(new Azdo.Tui.Polling.PipelineRunsUpdated(
            (await DemoServer.CreateClient().ListPipelineRunsAsync(30)), null));
        await Drive(model, cmd);
        // Switch to the Pipelines tab (key "3").
        model.Update(KeyMsg.Rune_('3'));
        var view = model.View();
        Assert.Contains("CI", view); // demo build definition name
    }

    [Fact]
    public async Task PipelinePermissionError_DoesNotBlockOtherFeatures()
    {
        var model = NewDemoApp();
        model.Update(new WindowSizeMsg(140, 40));
        await Drive(model, model.Init());

        // Simulate the background poller receiving a 403 because the PAT lacks
        // the Build (Read) scope.
        var permissionErr = Azdo.Core.AzureDevOps.Client.FormatHttpError(403);
        var (_, cmd) = model.Update(new Azdo.Tui.Polling.PipelineRunsUpdated(
            new List<Azdo.Core.AzureDevOps.PipelineRun>(), permissionErr));
        await Drive(model, cmd);

        var view = model.View();
        // The blocking error modal must NOT take over the whole screen.
        Assert.DoesNotContain("Press esc to dismiss", view);
        // Pull Requests (Code scope present) keep working.
        Assert.Contains("OAuth", view);

        // The Pipelines tab itself surfaces the missing-scope error inline.
        model.Update(KeyMsg.Rune_('3'));
        Assert.Contains("Error loading pipeline runs", model.View());
    }

    [Fact]
    public void HelpOverlay_Toggles()
    {
        var model = NewDemoApp();
        model.Update(new WindowSizeMsg(120, 40));
        model.Update(KeyMsg.Rune_('?'));
        var view = model.View();
        Assert.Contains("Navigation", view); // help section title
    }
}
