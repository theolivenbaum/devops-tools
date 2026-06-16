# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## Project Overview

**azdo** is a TUI (Terminal User Interface) for working with Azure DevOps in the
terminal. This repository is a **C# / .NET 10 port** of the original Go
implementation (which used Bubble Tea + Lip Gloss). The port is built on
[Spectre.Console](https://github.com/spectreconsole/spectre.console).

- **Language**: C# (.NET 10)
- **UI foundation**: Spectre.Console
- **Purpose**: Interact with Azure DevOps (Pull Requests, Work Items, Pipelines,
  Metrics) from the command line.

The original Go source is preserved verbatim under [`reference/`](reference/) and
is the source of truth for behaviour. When in doubt about how a feature should
behave, read the corresponding Go file.

## The Port

The Go app uses the **Elm Architecture** (Bubble Tea): every component is a
`Model` with `Init() -> Cmd`, `Update(Msg) -> (Model, Cmd)`, and `View() -> string`.
Lip Gloss provides ANSI styling and box layout.

Spectre.Console is not an Elm-style event-loop framework, so the port provides:

- **`Azdo.Tui.Runtime`** — a small Elm-style runtime (`IModel`, `Msg`, `Cmd`,
  `Program`) that owns the render loop, raw key input, window-resize detection,
  and an async command scheduler. This mirrors Bubble Tea's `tea.Program`.
- **`Azdo.Tui.Rendering`** — a Lip Gloss-equivalent styling/layout engine
  (`Style`, borders, alignment, `JoinHorizontal`/`JoinVertical`, ANSI-aware
  width/truncate/wrap). Colors are resolved through Spectre.Console's `Color`
  type, and the runtime renders through `IAnsiConsole`.
- Spectre.Console is used directly for color handling, capability detection, the
  console abstraction, and rich one-shot prompts (setup wizard, PAT entry).

### Project layout

```
src/
  Azdo.Core/   Domain models, Azure DevOps API client, config, state,
               metrics core, diff, version check, polling — no UI.
  Azdo.Tui/    Runtime, rendering engine, styles/themes, reusable components,
               tab views, and the root application model.
  Azdo.App/    Executable entry point (CLI dispatch: run / auth / demo / help / version).
tests/
  Azdo.Tests/  xUnit tests mirroring the Go *_test.go files.
reference/     The original Go implementation (read-only reference).
```

Namespaces mirror folders: `Azdo.Core.AzureDevOps`, `Azdo.Core.Configuration`,
`Azdo.Tui.Components`, `Azdo.Tui.Views.PullRequests`, etc.

## Common Commands

```bash
dotnet build AzdoTui.slnx            # build everything
dotnet test  AzdoTui.slnx            # run all tests
dotnet run --project src/Azdo.App    # run the TUI
dotnet run --project src/Azdo.App -- demo   # run with mock data
dotnet format                        # format code
```

## Conventions

- Idiomatic, modern C#: nullable reference types, records for immutable data,
  `sealed` classes by default, file-scoped namespaces, expression-bodied members,
  pattern matching, `System.Text.Json` for (de)serialization.
- Mirror the Go architecture: nested model hierarchy, generic `ListView<T>`
  configured by callbacks, `IDetailView` polymorphism, two-tier
  `Client`/`MultiClient`, graceful degradation via `PartialError`.
- **Accept interfaces, return concrete types.** Inject clients/styles/config via
  constructors; no global state.

## TDD

!!! IMPORTANT !!! Use a TDD approach: write the test, watch it fail, make it
green. Tests are table-driven where the Go originals were, and they mirror the
`*_test.go` files under `reference/`.
