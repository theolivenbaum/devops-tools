using Azdo.Tui.Runtime;

namespace Azdo.Tui.Components;

/// <summary>
/// A drill-down detail view managed uniformly by <see cref="ListView{T}"/>
/// (≈ <c>listview.DetailView</c>). Mutable: <see cref="Update"/> mutates and
/// returns an optional follow-up command.
/// </summary>
public interface IDetailView
{
    Cmd? Update(IMsg msg);
    string View();
    void SetSize(int width, int height);
    IReadOnlyList<ContextItem> GetContextItems();
    double GetScrollPercent();
    string GetStatusMessage();
}
