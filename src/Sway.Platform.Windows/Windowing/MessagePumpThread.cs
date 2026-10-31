using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sway.Platform.Windows.Windowing;

/// <summary>
/// A dedicated thread with a hidden window and a message loop. The window is a hidden top-level window rather than a
/// message-only one on purpose: message-only windows do not receive broadcast messages such as WM_DISPLAYCHANGE.
/// The handler runs on the pump thread; work can be marshalled onto the thread with <see cref="Invoke{T}"/>.
/// </summary>
internal sealed class MessagePumpThread : IDisposable
{
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmApp = 0x8000;

    private readonly Func<uint, nint, nint, bool> _handler;
    private readonly ConcurrentQueue<(Action Run, Action<Exception> Fail)> _work = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private readonly WindowProc _windowProc;
    private readonly string _className = "SwayPump_" + Guid.NewGuid().ToString("N");
    private nint _window;
    private int _threadId;
    private volatile bool _disposed;
    private volatile bool _stopped; // the message loop has ended: queued work will never run

    /// <summary>Starts the thread and waits until its window exists.</summary>
    /// <param name="name">Thread name.</param>
    /// <param name="handler">Called on the pump thread with (message, wParam, lParam); return true when handled.</param>
    public MessagePumpThread(string name, Func<uint, nint, nint, bool> handler)
    {
        _handler = handler;
        _windowProc = WndProc;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
        _ready.Wait();
        if (_window == 0)
        {
            throw new InvalidOperationException("Could not create the message window.");
        }
    }

    private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    /// <summary>The hidden window's handle (for RegisterHotKey).</summary>
    public nint Window => _window;

    /// <summary>Runs <paramref name="work"/> on the pump thread and returns its result (or rethrows its exception). Blocks the caller briefly.</summary>
    public T Invoke<T>(Func<T> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return work();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue((
            () =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            exception => completion.TrySetException(exception)));
        if (_stopped || !PostMessageW(_window, WmApp, 0, 0))
        {
            // The window is gone (or went while this was queued): nothing will drain the queue, so fail it here.
            FailPending();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window != 0)
        {
            PostMessageW(_window, WmClose, 0, 0);
        }

        if (Environment.CurrentManagedThreadId != _threadId)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var instance = GetModuleHandleW(null);
        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            Instance = instance,
            ClassName = _className,
        };

        if (RegisterClassExW(ref windowClass) != 0)
        {
            _window = CreateWindowExW(0, _className, "Sway", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        }

        _ready.Set();
        if (_window == 0)
        {
            return;
        }

        try
        {
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException))
        {
            // A stray exception here would end the process; the window procedure already guards its own work.
            System.Diagnostics.Trace.TraceError($"Sway message pump ended: {exception.GetType().Name}");
        }

        _stopped = true;
        FailPending();
        UnregisterClassW(_className, instance);
    }

    /// <summary>Faults every queued call so a caller blocked in <see cref="Invoke{T}"/> wakes up when the pump has stopped.</summary>
    private void FailPending()
    {
        while (_work.TryDequeue(out var item))
        {
            item.Fail(new ObjectDisposedException(nameof(MessagePumpThread)));
        }
    }

    private nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == WmApp)
            {
                while (_work.TryDequeue(out var item))
                {
                    item.Run();
                }

                return 0;
            }

            if (_handler(message, wParam, lParam))
            {
                return 0;
            }
        }
        catch (Exception)
        {
            // A handler fault must not unwind into native code. Handlers report their own errors.
            return 0;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint IconSmall;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out Msg message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}
