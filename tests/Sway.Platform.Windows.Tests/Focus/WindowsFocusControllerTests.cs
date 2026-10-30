using Sway.Core;
using Sway.Core.Platform;
using Sway.Platform.Windows.Focus;
using Sway.Platform.Windows.Tests.Windowing;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Tests.Focus;

public sealed class WindowsFocusControllerTests : IDisposable
{
    private const int Own = 100;

    private static readonly DisplayLayout Layout = new(
    [
        new DisplayInfo("left", 10, "L", new DisplayRect(-1920, 0, 1920, 1080), false, false),
        new DisplayInfo("right", 20, "R", new DisplayRect(0, 0, 2560, 1440), false, true),
    ]);

    private readonly FakeNative _native = new();
    private readonly WindowsFocusController _controller;

    public WindowsFocusControllerTests()
    {
        _controller = new WindowsFocusController(_native, Own, new SerialWorker("test"));
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public async Task LastUsedWindowsPicksTopmostEligiblePerDisplay()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10, name: "code"));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 2, monitor: 20, name: "chrome"));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 3, monitor: 20, name: "old"));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 4, monitor: 10, pid: Own));

        var result = await _controller.LastUsedWindowsAsync(Layout);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[new DisplayId("left")].Handle);
        Assert.Equal("code", result[new DisplayId("left")].AppName);
        Assert.Equal(new DisplayId("left"), result[new DisplayId("left")].DisplayId);
        Assert.Equal(2, result[new DisplayId("right")].Handle);
    }

    [Fact]
    public async Task FocusedDisplayIsTheForegroundWindowsDisplay()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 2, monitor: 20));
        _native.Foreground = 2;

        Assert.Equal(new DisplayId("right"), await _controller.FocusedDisplayAsync(Layout));
    }

    [Fact]
    public async Task FocusedDisplayIsNullForOwnOrShellOrNoForeground()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10, pid: Own));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 2, monitor: 10, cls: "Shell_TrayWnd"));

        Assert.Null(await _controller.FocusedDisplayAsync(Layout));
        _native.Foreground = 1;
        Assert.Null(await _controller.FocusedDisplayAsync(Layout));
        _native.Foreground = 2;
        Assert.Null(await _controller.FocusedDisplayAsync(Layout));
        _native.Foreground = 777;
        Assert.Null(await _controller.FocusedDisplayAsync(Layout));
    }

    [Fact]
    public async Task FocusUsesTheAttachRecipeInOrderAndDetaches()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 2, monitor: 20));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;
        _native.SetForegroundWorks = true;

        var result = await _controller.FocusAsync("left", Layout);

        var focused = Assert.IsType<FocusResult.Focused>(result);
        Assert.Equal(FocusMethod.AttachInput, focused.Method);
        Assert.Equal(1, focused.Window.Handle);
        Assert.Equal(["attach 22", "top 1", "foreground 1", "detach 22"], _native.Calls.Where(c => !c.StartsWith("sleep", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task FocusDetachesEvenWhenBringToTopThrows()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;
        _native.ThrowOnTop = true;

        var result = await _controller.FocusAsync("left", Layout);

        Assert.IsType<FocusResult.Failed>(result);
        Assert.Contains("detach 22", _native.Calls);
    }

    [Fact]
    public async Task FocusRestoresAMinimizedTargetFirst()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;
        _native.Iconic.Add(1);
        _native.SetForegroundWorks = true;

        await _controller.FocusAsync("left", Layout);

        Assert.Equal("restore 1", _native.Calls[0]);
    }

    [Fact]
    public async Task FocusUsesPlainWhenTargetThreadAlreadyOwnsForeground()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 11;
        _native.SetForegroundWorks = true;

        var focused = Assert.IsType<FocusResult.Focused>(await _controller.FocusAsync("left", Layout));

        Assert.Equal(FocusMethod.Plain, focused.Method);
        Assert.DoesNotContain(_native.Calls, c => c.StartsWith("attach", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FocusAlreadyThereReturnsImmediately()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Foreground = 1;

        var focused = Assert.IsType<FocusResult.Focused>(await _controller.FocusAsync("left", Layout));

        Assert.Equal(0, focused.Milliseconds);
        Assert.Empty(_native.Calls);
    }

    [Fact]
    public async Task FocusReportsHowLongItWaited()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;
        _native.ForegroundChangesAfterSleeps = 3;

        var focused = Assert.IsType<FocusResult.Focused>(await _controller.FocusAsync("left", Layout));

        Assert.Equal(150, focused.Milliseconds);
    }

    [Fact]
    public async Task FocusGivesUpAfterSixHundredMilliseconds()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10, name: "stubborn"));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;

        var failed = Assert.IsType<FocusResult.Failed>(await _controller.FocusAsync("left", Layout));

        Assert.Equal(12, _native.Calls.Count(c => c.StartsWith("sleep", StringComparison.Ordinal)));
        Assert.Equal(1, failed.Window!.Handle);
        Assert.Contains("stubborn", failed.Reason);
        Assert.Contains("600", failed.Reason);
    }

    [Fact]
    public async Task ElevatedTargetFailureSaysSo()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10, name: "regedit", elevated: true));
        _native.Foreground = 2;
        _native.Threads[1] = 11;
        _native.Threads[2] = 22;

        var failed = Assert.IsType<FocusResult.Failed>(await _controller.FocusAsync("left", Layout));

        Assert.Contains("administrator", failed.Reason);
        Assert.Contains("regedit", failed.Reason);
    }

    [Fact]
    public async Task ElevatedTargetWhenSwayIsElevatedGivesTheGenericReason()
    {
        _native.SelfElevated = true;
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10, elevated: true));
        _native.Foreground = 2;

        var failed = Assert.IsType<FocusResult.Failed>(await _controller.FocusAsync("left", Layout));

        Assert.DoesNotContain("administrator", failed.Reason);
    }

    [Fact]
    public async Task FocusWithNoWindowOnTheDisplayIsNoWindow()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 2, monitor: 20));
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 3, monitor: 10, iconic: true));

        var result = await _controller.FocusAsync("left", Layout);

        Assert.Equal(new FocusResult.NoWindow("left"), result);
    }

    [Fact]
    public async Task NativeFailureBecomesFailedNotAnException()
    {
        _native.ThrowOnEnumerate = true;

        var result = await _controller.FocusAsync("left", Layout);

        Assert.IsType<FocusResult.Failed>(result);
        Assert.Empty(await _controller.LastUsedWindowsAsync(Layout));
        Assert.Null(await _controller.FocusedDisplayAsync(Layout));
    }

    [Fact]
    public async Task NativeWorkRunsOnOneThreadThatIsNotTheCallers()
    {
        _native.Windows.Add(WindowEligibilityTests.Window(handle: 1, monitor: 10));

        await Task.WhenAll(
            _controller.LastUsedWindowsAsync(Layout),
            _controller.LastUsedWindowsAsync(Layout),
            _controller.FocusedDisplayAsync(Layout));

        Assert.Single(_native.ThreadIds);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _native.ThreadIds.Single());
    }

    private sealed class FakeNative : IWindowNativeCalls
    {
        private int _sleeps;

        public List<WindowSnapshot> Windows { get; } = [];

        public long Foreground { get; set; }

        public Dictionary<long, uint> Threads { get; } = [];

        public HashSet<long> Iconic { get; } = [];

        public List<string> Calls { get; } = [];

        public HashSet<int> ThreadIds { get; } = [];

        public bool SetForegroundWorks { get; set; }

        public int ForegroundChangesAfterSleeps { get; set; } = -1;

        public bool ThrowOnTop { get; set; }

        public bool ThrowOnEnumerate { get; set; }

        public bool SelfElevated { get; set; }

        private long TargetOfLastSetForeground { get; set; }

        bool IWindowNativeCalls.IsSelfElevated => SelfElevated;

        public IReadOnlyList<WindowSnapshot> EnumerateWindows()
        {
            Touch();
            return ThrowOnEnumerate ? throw new InvalidOperationException("boom") : Windows.ToList();
        }

        public WindowSnapshot? Describe(long handle)
        {
            Touch();
            return Windows.FirstOrDefault(w => w.Handle == handle);
        }

        public long ForegroundWindow()
        {
            Touch();
            if (ForegroundChangesAfterSleeps >= 0 && _sleeps >= ForegroundChangesAfterSleeps && TargetOfLastSetForeground != 0)
            {
                return TargetOfLastSetForeground;
            }

            return Foreground;
        }

        public bool IsIconic(long handle) => Iconic.Contains(handle);

        public void Restore(long handle) => Calls.Add("restore " + handle);

        public uint WindowThread(long handle) => Threads.GetValueOrDefault(handle);

        public bool AttachInput(uint otherThread, bool attach)
        {
            Calls.Add((attach ? "attach " : "detach ") + otherThread);
            return true;
        }

        public void BringToTop(long handle)
        {
            Calls.Add("top " + handle);
            if (ThrowOnTop)
            {
                throw new InvalidOperationException("boom");
            }
        }

        public bool SetForeground(long handle)
        {
            Calls.Add("foreground " + handle);
            TargetOfLastSetForeground = handle;
            if (SetForegroundWorks)
            {
                Foreground = handle;
            }

            return SetForegroundWorks;
        }

        public DisplayRect? MonitorBounds(long monitorHandle) => null;

        public void Sleep(int milliseconds)
        {
            Assert.Equal(50, milliseconds);
            _sleeps++;
            Calls.Add("sleep");
        }

        private void Touch()
        {
            lock (ThreadIds)
            {
                ThreadIds.Add(Environment.CurrentManagedThreadId);
            }
        }
    }
}
