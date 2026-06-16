namespace Azdo.Tui.Runtime;

/// <summary>
/// A unit in the Elm architecture (≈ <c>tea.Model</c>). Implementations are
/// typically immutable: <see cref="Update"/> returns a (possibly new) model.
/// </summary>
public interface IModel
{
    /// <summary>Optional command to run when the program starts.</summary>
    Cmd? Init();

    /// <summary>Handles a message, returning the next model and an optional command.</summary>
    (IModel Model, Cmd? Cmd) Update(IMsg msg);

    /// <summary>Renders the current view as a (possibly multi-line, ANSI-styled) string.</summary>
    string View();
}
