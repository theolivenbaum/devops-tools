using Azdo.Tui.Components;
using Azdo.Tui.Runtime;

namespace Azdo.Tui.Views;

/// <summary>
/// Common contract for the four tab views (Pull Requests, Work Items,
/// Pipelines, Metrics), letting the root <c>AppModel</c> route messages and
/// compose chrome uniformly. Mutable: <see cref="Update"/> mutates and returns
/// an optional follow-up command.
/// </summary>
public interface ITabView
{
    /// <summary>Command to run when the tab is first activated.</summary>
    Cmd? Init();

    /// <summary>
    /// Handles a message. <see cref="WindowSizeMsg"/> carries the CONTENT size
    /// (terminal minus tab bar, content-box border, and footer).
    /// </summary>
    Cmd? Update(IMsg msg);

    /// <summary>Renders the tab body, including any open modal/picker overlay.</summary>
    string View();

    /// <summary>True when an inline search/filter input is active (suppresses global keys).</summary>
    bool IsSearching();

    /// <summary>True when a modal/picker/form is capturing input (suppresses global keys).</summary>
    bool IsCapturingInput();

    /// <summary>Whether the footer should show context keybindings instead of defaults.</summary>
    bool HasContextBar();

    /// <summary>Context keybindings for the footer when <see cref="HasContextBar"/> is true.</summary>
    IReadOnlyList<ContextItem> GetContextItems();

    /// <summary>Scroll percentage (0–100) for the footer, or 0 to hide.</summary>
    double GetScrollPercent();

    /// <summary>Transient status message for the footer, or "".</summary>
    string GetStatusMessage();

    /// <summary>The filter badge text for the footer, or "" when no filter is active.</summary>
    string FilterLabel();

    /// <summary>Default footer keybindings string (used when <see cref="HasContextBar"/> is false).</summary>
    string DefaultKeybindings();
}

/// <summary>
/// Implemented by tabs whose open detail view should be restored across runs
/// (Pull Requests, Work Items). Pipelines/Metrics do not participate.
/// </summary>
public interface IRestorableTab
{
    /// <summary>ID of the currently open detail item, or 0 when on the list.</summary>
    int DetailItemId();

    /// <summary>Queues a one-shot detail restore for the given item ID on first populate.</summary>
    void SetPendingDetailRestore(int id);
}
