# Port TODO — Go → C#/.NET 10 (Spectre.Console)

Tracks the port of the Azure DevOps TUI from Go (Bubble Tea / Lip Gloss) to
C#/.NET 10 (Spectre.Console). Source of truth for behaviour: [`reference/`](reference/).

Legend: `[ ]` todo · `[~]` in progress · `[x]` done

## 0. Project setup
- [x] Move Go sources to `reference/`
- [x] .NET 10 solution: `Azdo.Core`, `Azdo.Tui`, `Azdo.App`, `Azdo.Tests`
- [x] CLAUDE.md / TODO.md / README.md
- [x] Spectre.Console dependency wired

## 1. Rendering engine (`Azdo.Tui.Rendering`) — Lip Gloss equivalent
- [x] ANSI-aware text width / strip / truncate / wrap / pad
- [x] `Style`: fg/bg, bold/faint/italic/underline, padding, width/height, align
- [x] Borders (rounded / normal, per-side) + border foreground
- [x] `JoinHorizontal`, `JoinVertical`, `Place` / `PlaceHorizontal` / `PlaceVertical`
- [x] Tests

## 2. Elm runtime (`Azdo.Tui.Runtime`)
- [x] `Msg`, `Cmd`, `Batch`, `Tick`, `Quit`
- [x] `IModel` (Init / Update / View)
- [x] `Program`: alt-screen, raw key reader, resize polling, command scheduler
- [x] Key parsing (arrows, enter, esc, pgup/pgdn, ctrl+c, runes)
- [x] Tests for the message loop

## 3. Domain models (`Azdo.Core.AzureDevOps`)
- [x] Pipelines: `PipelineRun`, `Timeline`, `TimelineRecord`, `BuildLog`, helpers
- [x] Git: `PullRequest`, `Repository`, threads/comments, diffs
- [x] Work items: `WorkItem`, WIQL types
- [x] `Project`, `Links`, error types (`PartialError`)

## 4. API client (`Azdo.Core.AzureDevOps`)
- [x] `Client` (auth, GET/POST/PATCH/PUT, HTTP error classification)
- [x] `MultiClient` (concurrent multi-project fetch, merge, enrich)
- [x] Pipelines / builds / logs / timeline
- [x] Git: PRs, threads, votes, comments, diffs
- [x] Work items: WIQL, get by id, state change, comments
- [x] Metrics endpoints (`/updates`, state-change date)
- [x] Tests (mocked `HttpMessageHandler`)

## 5. Config / state / auth (`Azdo.Core.Configuration`, `Azdo.Core.State`)
- [x] `Config` load/save (YAML), projects + display names, disabled panes, metrics
- [x] PAT store (system credential store with `AZDO_PAT` env fallback)
- [x] Navigation `State` + debounced atomic `Store`
- [x] Tests

## 6. Styles & themes (`Azdo.Tui.Styles`)
- [x] `Theme`, `Styles` factory
- [x] Built-in themes (dark, light, dracula, catppuccin, …)
- [x] Custom theme loading from themes dir
- [x] Tests

## 7. Reusable components (`Azdo.Tui.Components`)
- [x] `ListView<T>` (generic list/detail/search) + `IDetailView`
- [x] `Table` (ANSI-aware truncation)
- [x] `StatusBar`, `Logo`, `Spinner`, `ContextItem`
- [x] `HelpModal`, `ErrorModal`
- [x] Pickers: theme, tag, state, vote, list
- [x] `CommentForm`
- [x] Tests

## 8. Tab views (`Azdo.Tui.Views`)
- [x] Pull Requests: list / detail / diff view
- [x] Work Items: list / detail
- [x] Pipelines: list / timeline detail / log viewer
- [x] Metrics core (`Azdo.Core.Metrics`) + dashboard (live + trends)
- [x] Tests

## 9. Cross-cutting
- [x] Polling (`Poller`, `ErrorHandler`, events)
- [x] Diff parsing/formatting
- [x] Version check / update notification
- [x] Browser open
- [x] Demo mode (mock server + data)
- [x] Setup wizard + PAT input (Spectre prompts)

## 10. App + CLI
- [x] Root `AppModel` (tabs, routing, overlays, theme switching)
- [x] `Program.cs` CLI dispatch (run / auth / demo / help / version)

## 11. Wrap-up
- [x] `dotnet build` clean
- [x] `dotnet test` green
- [x] README usage verified

## Known gaps / future work
- System keyring uses a 0600 file under the config dir on Linux rather than the
  SecretService D-Bus API; macOS/Windows native stores are TODO. `AZDO_PAT`
  always works as a fallback.
- `ntcharts` trend sparkline rendering is approximated with a block-based chart.
- Mouse / `bubblezone` hit-testing from the Go version is not ported (keyboard-only).
