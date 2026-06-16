using Azdo.Core.AzureDevOps;
using Azdo.Core.Configuration;
using Azdo.Core.Demo;
using Azdo.Core.State;
using Azdo.Core.Util;
using Azdo.Tui.App;
using Azdo.Tui.Cli;
using Azdo.Tui.Components;
using TuiProgram = Azdo.Tui.Runtime.Program;

namespace Azdo.App;

/// <summary>Entry point and CLI dispatch (≈ <c>cmd/azdo-tui/main.go</c>).</summary>
internal static class Program
{
    // Build-time values (overridable via assembly metadata / env in a real release).
    private const string Version = "dev";
    private const string Commit = "none";
    private const string BuildDate = "unknown";

    private static int Main(string[] args)
    {
        WireBrowser();
        try
        {
            return ParseAction(args) switch
            {
                CliAction.Help => RunHelp(),
                CliAction.Version => RunVersion(),
                CliAction.Auth => RunAuth(),
                CliAction.Demo => RunDemo(),
                _ => RunTui(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private enum CliAction { Run, Auth, Help, Version, Demo }

    private static CliAction ParseAction(string[] args)
    {
        if (args.Length == 0) return CliAction.Run;
        return args[0] switch
        {
            "auth" => CliAction.Auth,
            "--help" or "-h" or "help" => CliAction.Help,
            "--version" or "-v" or "version" => CliAction.Version,
            "demo" => CliAction.Demo,
            _ => CliAction.Run,
        };
    }

    /// <summary>Routes the in-app "open in browser" seams to the real OS launcher.</summary>
    private static void WireBrowser()
    {
        static Exception? Open(string url)
        {
            try { Browser.Open(url); return null; }
            catch (Exception e) { return e; }
        }
        Azdo.Tui.Views.PullRequests.PullRequestDetailView.OpenUrl = Open;
        Azdo.Tui.Views.WorkItems.DetailModel.OpenUrl = Open;
        Azdo.Tui.Views.Metrics.Model.OpenUrl = Open;
    }

    private static int RunHelp()
    {
        string configPath; try { configPath = Config.GetPath(); } catch { configPath = "~/.config/azdo-tui/config.yaml"; }
        Console.WriteLine(string.Join("\n", Logo.Art));
        Console.WriteLine($"""

            azdo - A TUI for Azure DevOps ({Version})

            Usage:
              azdo              Start the TUI application
              azdo auth         Set or update your Personal Access Token (PAT)
              azdo demo         Launch with mock data (for screenshots/demos)
              azdo --help       Show this help message
              azdo --version    Show version information

            Configuration:
              Config file: {configPath}
              PAT storage: file (~/.config/azdo-tui/.pat) with AZDO_PAT env fallback

            Required PAT permissions:
              Build        (Read)         - pipelines, build logs
              Code         (Read & Write) - pull requests, voting, comments
              Work Items   (Read & Write) - queries, comments, state changes

            Keyboard shortcuts (in TUI):
              1/2/3/4 tabs · left/right prev/next · up/down move · enter details · esc back
              f search · m my items · A as reviewer · T tags · r refresh
              v vote · w work-item state · c comment · o open in browser · t theme · ? help · q quit

            For more information, visit: https://github.com/Elpulgo/azdo
            """);
        return 0;
    }

    private static int RunVersion()
    {
        Console.WriteLine($"azdo version {Version} (commit: {Commit}, built: {BuildDate})");
        return 0;
    }

    private static int RunAuth()
    {
        var store = new PatStore();
        bool isUpdate;
        try { store.GetPat(); isUpdate = true; } catch (PatNotFoundException) { isUpdate = false; }

        var pat = PatPrompt.Run(isUpdate);
        if (string.IsNullOrEmpty(pat))
        {
            Console.Error.WriteLine("PAT input cancelled or empty");
            return 1;
        }
        store.SetPat(pat);
        Console.WriteLine("\nPAT saved successfully.");
        return 0;
    }

    private static int RunDemo()
    {
        // Redirect metrics/config persistence into a temp dir so demo mode never
        // touches the user's real config.
        var tmp = Directory.CreateTempSubdirectory("azdo-demo-");
        Environment.SetEnvironmentVariable("AZDO_CONFIG_DIR", Path.Combine(tmp.FullName, "azdo-tui"));
        try
        {
            var client = DemoServer.CreateClient();
            var cfg = Config.NewWithPath(DemoServer.Org,
                new List<string> { DemoServer.ProjectNexus, DemoServer.ProjectHorizon },
                3600, "dracula", Path.Combine(tmp.FullName, "config.yaml"));
            cfg.DisplayNames = new Dictionary<string, string>
            {
                [DemoServer.ProjectNexus] = DemoServer.DisplayNexus,
                [DemoServer.ProjectHorizon] = DemoServer.DisplayHorizon,
            };
            var model = new AppModel(client, cfg, Version + " (demo)", Commit);
            new TuiProgram(model, altScreen: true).Run();
            return 0;
        }
        finally { try { tmp.Delete(true); } catch { } }
    }

    private static int RunTui()
    {
        Config cfg;
        try { cfg = Config.Load(); }
        catch (ConfigNotFoundException)
        {
            var created = SetupWizard.Run();
            if (created is null) { Console.Error.WriteLine("setup cancelled"); return 1; }
            cfg = created;
        }

        var store = new PatStore();
        string pat;
        try { pat = store.GetPat(); }
        catch (PatNotFoundException)
        {
            var entered = PatPrompt.Run(isUpdate: false);
            if (string.IsNullOrEmpty(entered)) { Console.Error.WriteLine("failed to set PAT"); return 1; }
            store.SetPat(entered);
            pat = entered;
        }

        var client = new MultiClient(cfg.Organization, cfg.Projects, pat, cfg.DisplayNames);

        var stateStore = Store.Create(AppState.Path());
        var model = new AppModel(client, cfg, Version, Commit);
        model.SetStateStore(stateStore);
        model.ApplyState(stateStore.State());

        try
        {
            new TuiProgram(model, altScreen: true).Run();
        }
        finally
        {
            try { stateStore.Flush(); }
            catch (Exception e) { Console.Error.WriteLine($"Warning: failed to persist state: {e.Message}"); }
        }
        return 0;
    }
}
