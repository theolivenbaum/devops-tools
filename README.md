# azdo — Azure DevOps TUI (C# / .NET 10)

A fast, keyboard-driven terminal UI for Azure DevOps: browse **Pull Requests**,
**Work Items**, and **Pipelines**, review diffs, vote, change work-item state,
tail build logs, and track delivery **Metrics** — all without leaving the
terminal.

This is a **C#/.NET 10 port** of the original Go implementation, rebuilt on
[Spectre.Console](https://github.com/spectreconsole/spectre.console). The
original Go source is preserved under [`reference/`](reference/).

> Built with an Elm-style runtime layered over Spectre.Console — see
> [CLAUDE.md](CLAUDE.md) and [Architecture](reference/Architecture.md).

## Features

- **Pull Requests** — list, filter (mine / as reviewer), detail with description
  and comment threads, vote/approve, and a file **diff viewer** with inline
  comments.
- **Work Items** — WIQL-backed list with search, "my items", tag and state
  filters; detail view with state changes and comments.
- **Pipelines** — recent runs, an expandable **timeline tree** (stages → jobs →
  tasks), and a scrollable, searchable **log viewer**.
- **Metrics** (opt-in) — per-user WIP / stuck / closed roll-up plus sprint-on-
  sprint **trends**, backed by a local 90-day snapshot file.
- **Multi-project** — fetches concurrently across all configured projects with
  graceful partial-failure handling.
- **Theming** — built-in themes (dark, light, dracula, catppuccin, …) plus
  custom themes from a themes directory.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure DevOps **Personal Access Token (PAT)** with:
  - **Build** (Read) — pipelines & build logs
  - **Code** (Read & Write) — pull requests, voting, comments
  - **Work Items** (Read & Write) — queries, comments, state changes

## Build & run

```bash
dotnet build AzdoTui.slnx
dotnet run --project src/Azdo.App              # start the TUI
dotnet run --project src/Azdo.App -- auth      # set/update your PAT
dotnet run --project src/Azdo.App -- demo      # mock data, no network
dotnet run --project src/Azdo.App -- --help    # usage
dotnet run --project src/Azdo.App -- --version
```

On first run, an interactive setup wizard creates the config file.

## Configuration

Config lives at `~/.config/azdo-tui/config.yaml`:

```yaml
organization: your-org
projects:
  - your-project
  # or, with friendly display names:
  # - name: api-name
  #   display_name: Friendly Name
polling_interval: 60
theme: dark
# disabled_panes: pipelines,workitems   # optional
metrics:
  enabled: false
```

**Auth resolution order:** system credential store (service `azdo-tui`) →
`AZDO_PAT` environment variable. See [TODO.md](TODO.md) for platform notes.

## Keyboard shortcuts

```
Navigation:  ↑/k ↓/j  move    pgup/pgdn  page    enter  details    esc  back
Tabs:        1/2/3/4  switch  ←/→  prev/next tab
Actions:     f search  m my items  A as reviewer  T tags  r refresh
             v vote (PR)  s state (work item)  c comment  o open in browser
             t theme  ? help  q quit
Diff:        c comment  p reply  x resolve  n/N next/prev comment
Logs:        g top  G bottom
```

## Development

```bash
dotnet test AzdoTui.slnx        # run all tests
dotnet format                   # format
```

TDD is the working style — see [CLAUDE.md](CLAUDE.md). Progress is tracked in
[TODO.md](TODO.md).

## License

MIT — see [LICENSE](LICENSE).
