using Azdo.Tui.Components;
using Spectre.Console;

namespace Azdo.Tui.Cli;

/// <summary>Interactive PAT entry (≈ <c>patinput</c>), via a Spectre.Console secret prompt.</summary>
public static class PatPrompt
{
    /// <summary>Required PAT permissions as plain text (≈ <c>PermissionInfoPlain</c>).</summary>
    public const string PermissionInfoPlain =
        "Required PAT permissions:\n" +
        "  Build        (Read)         - pipelines, build logs\n" +
        "  Code         (Read & Write) - pull requests, voting, comments\n" +
        "  Work Items   (Read & Write) - queries, state changes";

    /// <summary>Prompts for a PAT. Returns the entered token, or null if empty/cancelled.</summary>
    public static string? Run(bool isUpdate)
    {
        AnsiConsole.MarkupLine($"[mediumpurple]{Markup.Escape(string.Join("\n", Logo.Art))}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(isUpdate
            ? "[bold]Azure DevOps PAT Update[/]\nThis will replace your existing Personal Access Token."
            : "[bold]Azure DevOps PAT Setup[/]\nThis will store your Personal Access Token.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(PermissionInfoPlain);
        AnsiConsole.WriteLine();

        var pat = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter your [green]Personal Access Token[/]:")
                .Secret()
                .AllowEmpty());

        return string.IsNullOrWhiteSpace(pat) ? null : pat.Trim();
    }
}
