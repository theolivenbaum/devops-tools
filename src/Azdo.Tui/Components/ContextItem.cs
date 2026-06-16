namespace Azdo.Tui.Components;

/// <summary>A keybinding/action shown in the status bar (≈ <c>components.ContextItem</c>).</summary>
public readonly record struct ContextItem(string Key, string Description);
