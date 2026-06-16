using Azdo.Tui.Runtime;

namespace Azdo.Tests.Views.WorkItems;

internal static class TestHelpers
{
    /// <summary>Synchronously resolves a command to its produced message (or null).</summary>
    public static IMsg? Run(Cmd? cmd) => cmd?.Invoke().GetAwaiter().GetResult();

    public static KeyMsg Key(string name) => KeyMsg.Named(name);
    public static KeyMsg Rune(char c) => KeyMsg.Rune_(c);
}
