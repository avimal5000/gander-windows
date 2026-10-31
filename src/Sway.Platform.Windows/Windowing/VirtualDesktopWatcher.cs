using System.Runtime.InteropServices;
using Sway.Core.Runtime;

namespace Sway.Platform.Windows.Windowing;

/// <summary>
/// Raises <see cref="Changed"/> when the user switches virtual desktop. Windows offers no in-box notification to a
/// normal app, so a dedicated thread asks IVirtualDesktopManager every <see cref="PollMilliseconds"/> which top-level
/// windows are on the current desktop, and <see cref="DesktopFlipDetector"/> reports when some window flipped. It reads
/// handles and flags only, never a title or any window content. <see cref="Changed"/> is raised on that thread.
/// </summary>
public sealed class VirtualDesktopWatcher : IDisposable
{
    /// <summary>How often the windows are checked.</summary>
    public const int PollMilliseconds = 400;

    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _thread;
    private readonly Action<string>? _log;

    /// <summary>Starts watching.</summary>
    /// <param name="log">Receives one line when a poll or a <see cref="Changed"/> subscriber fails unexpectedly. Null for none.</param>
    public VirtualDesktopWatcher(Action<string>? log = null)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "Sway desktop watcher" };
        _thread.Start();
    }

    /// <summary>The active virtual desktop probably changed.</summary>
    public event Action? Changed;

    /// <summary>Stops the thread.</summary>
    public void Dispose()
    {
        _stop.Set();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(1000);
        }
    }

    private void Run()
    {
        var detector = new DesktopFlipDetector();
        var failing = false;
        while (!_stop.Wait(PollMilliseconds))
        {
            try
            {
                if (detector.Update(VirtualDesktops.VisibleWindowFlags()))
                {
                    Changed?.Invoke();
                }

                failing = false;
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                // Windows could not say this round; try the next.
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException))
            {
                // A stray exception on this background thread would end the process. Keep watching; log once per run of failures.
                if (!failing)
                {
                    failing = true;
                    _log?.Invoke($"The desktop watcher hit an unexpected {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
    }
}
