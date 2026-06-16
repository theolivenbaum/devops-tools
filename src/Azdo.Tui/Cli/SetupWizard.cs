using Azdo.Core.Configuration;
using Azdo.Tui.Components;
using Azdo.Tui.Styles;
using Spectre.Console;

namespace Azdo.Tui.Cli;

/// <summary>
/// Interactive first-run configuration wizard (≈ <c>setupwizard</c>), implemented
/// with Spectre.Console prompts rather than a Bubble Tea model.
/// </summary>
public static class SetupWizard
{
    /// <summary>Runs the wizard and saves the config. Returns null if cancelled.</summary>
    public static Config? Run()
    {
        AnsiConsole.MarkupLine($"[mediumpurple]{Markup.Escape(string.Join("\n", Logo.Art))}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Welcome to azdo[/] — let's set up your configuration.");
        AnsiConsole.WriteLine();

        try
        {
            var org = AnsiConsole.Prompt(
                new TextPrompt<string>("Azure DevOps [green]organization[/]:")
                    .Validate(v => string.IsNullOrWhiteSpace(v) ? ValidationResult.Error("Organization is required") : ValidationResult.Success()));

            var projectsRaw = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Projects[/] (comma-separated):")
                    .Validate(v => string.IsNullOrWhiteSpace(v) ? ValidationResult.Error("At least one project is required") : ValidationResult.Success()));
            var projects = projectsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var interval = AnsiConsole.Prompt(
                new TextPrompt<int>("[green]Polling interval[/] (seconds):")
                    .DefaultValue(Config.DefaultPollingInterval)
                    .Validate(v => v <= 0 ? ValidationResult.Error("Must be greater than 0") : ValidationResult.Success()));

            var theme = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [green]theme[/]:")
                    .AddChoices(Themes.ListAvailable()));

            var cfg = Config.NewWithPath(org.Trim(), projects, interval, theme, Config.GetPath());
            cfg.Save();
            AnsiConsole.MarkupLine("\n[green]Configuration saved successfully![/]");
            return cfg;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Setup failed: {Markup.Escape(ex.Message)}[/]");
            return null;
        }
    }
}
