using System.Collections.Concurrent;
using System.Text;

namespace Azdo.Tui.Runtime;

/// <summary>
/// The Elm-style program runner — the C# equivalent of <c>tea.Program</c>. Owns
/// the message loop, a background raw-key reader, window-resize polling, an async
/// command scheduler, and incremental frame rendering. Renders through stdout
/// (Spectre.Console drives color capability detection at the app boundary).
/// </summary>
public sealed class Program
{
    private readonly bool _altScreen;
    private readonly BlockingCollection<IMsg> _queue = new(new ConcurrentQueue<IMsg>());
    private volatile bool _quitting;
    private IModel _model;
    private string[] _lastFrame = Array.Empty<string>();
    private int _width, _height;
    private readonly TextWriter _out;

    public Program(IModel model, bool altScreen = true, TextWriter? output = null)
    {
        _model = model;
        _altScreen = altScreen;
        _out = output ?? Console.Out;
    }

    /// <summary>Posts a message into the loop from outside (e.g. signal handlers).</summary>
    public void Send(IMsg msg)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(msg);
    }

    public IModel Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.TreatControlCAsInput = true; } catch { /* not a tty */ }
        if (_altScreen) Write("\x1b[?1049h\x1b[2J\x1b[H\x1b[?25l");

        (_width, _height) = ReadSize();
        var keyThread = new Thread(KeyLoop) { IsBackground = true };
        keyThread.Start();
        var sizeThread = new Thread(SizeLoop) { IsBackground = true };
        sizeThread.Start();

        try
        {
            // Seed initial size + Init command.
            Send(new WindowSizeMsg(_width, _height));
            Schedule(_model.Init());
            Render();

            foreach (var msg in _queue.GetConsumingEnumerable())
            {
                if (msg is QuitMsg) break;
                if (msg is WindowSizeMsg ws) { _width = ws.Width; _height = ws.Height; }

                if (msg is BatchMsg batch)
                {
                    foreach (var c in batch.Commands) Schedule(c);
                    continue;
                }

                var (next, cmd) = _model.Update(msg);
                _model = next;
                Schedule(cmd);
                Render();
            }
        }
        finally
        {
            _quitting = true;
            _queue.CompleteAdding();
            if (_altScreen) Write("\x1b[?25h\x1b[2J\x1b[H\x1b[?1049l");
            else Write("\x1b[?25h\n");
            _out.Flush();
        }
        return _model;
    }

    private void Schedule(Cmd? cmd)
    {
        if (cmd is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await cmd().ConfigureAwait(false);
                if (result is not null && !_queue.IsAddingCompleted) _queue.Add(result);
            }
            catch
            {
                // A failing command must never crash the loop; views surface
                // errors through their own error messages.
            }
        });
    }

    private void KeyLoop()
    {
        while (!_quitting)
        {
            try
            {
                if (!Console.KeyAvailable) { Thread.Sleep(8); continue; }
                var k = Console.ReadKey(intercept: true);
                Send(KeyMsg.FromConsole(k));
            }
            catch (InvalidOperationException) { return; } // input redirected / not a tty
            catch { Thread.Sleep(20); }
        }
    }

    private void SizeLoop()
    {
        while (!_quitting)
        {
            Thread.Sleep(200);
            var (w, h) = ReadSize();
            if (w != _width || h != _height) Send(new WindowSizeMsg(w, h));
        }
    }

    private static (int, int) ReadSize()
    {
        try
        {
            int w = Console.WindowWidth, h = Console.WindowHeight;
            return (w > 0 ? w : 80, h > 0 ? h : 24);
        }
        catch { return (80, 24); }
    }

    private void Render()
    {
        var frame = _model.View().Split('\n');
        var sb = new StringBuilder();
        sb.Append("\x1b[H");
        int max = Math.Max(frame.Length, _lastFrame.Length);
        for (int i = 0; i < max; i++)
        {
            if (i < frame.Length) sb.Append(frame[i]);
            sb.Append("\x1b[K"); // clear to end of line
            if (i < max - 1) sb.Append("\r\n");
        }
        Write(sb.ToString());
        _out.Flush();
        _lastFrame = frame;
    }

    private void Write(string s) => _out.Write(s);
}
