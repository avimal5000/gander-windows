using Sway.Core;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Tests.Windowing;

public sealed class WindowEligibilityTests
{
    private const int Own = 100;

    internal static WindowSnapshot Window(
        long handle = 1,
        int pid = 5,
        string cls = "Chrome_WidgetWin_1",
        long exStyle = 0,
        long owner = 0,
        bool visible = true,
        bool iconic = false,
        bool cloaked = false,
        bool onDesktop = true,
        DisplayRect? rect = null,
        long monitor = 10,
        string name = "app",
        bool? elevated = false) =>
        new(handle, pid, cls, exStyle, owner, visible, iconic, cloaked, onDesktop, rect ?? new DisplayRect(0, 0, 800, 600), monitor, name, elevated);

    [Fact]
    public void PlainWindowIsEligible()
    {
        Assert.Equal(WindowRejection.None, WindowEligibility.Check(Window(), Own));
        Assert.True(WindowEligibility.IsEligible(Window(), Own));
    }

    [Fact]
    public void InvisibleWindowIsRejected() =>
        Assert.Equal(WindowRejection.NotVisible, WindowEligibility.Check(Window(visible: false), Own));

    [Fact]
    public void MinimizedWindowIsRejected() =>
        Assert.Equal(WindowRejection.Minimized, WindowEligibility.Check(Window(iconic: true), Own));

    [Fact]
    public void CloakedWindowIsRejected() =>
        Assert.Equal(WindowRejection.Cloaked, WindowEligibility.Check(Window(cloaked: true), Own));

    [Fact]
    public void ToolWindowIsRejected() =>
        Assert.Equal(WindowRejection.ToolWindowOrOwnedPopup, WindowEligibility.Check(Window(exStyle: WindowEligibility.ExToolWindow), Own));

    [Fact]
    public void OwnedPopupIsRejected() =>
        Assert.Equal(WindowRejection.ToolWindowOrOwnedPopup, WindowEligibility.Check(Window(owner: 7), Own));

    [Fact]
    public void AppWindowFlagKeepsToolWindowEligible() =>
        Assert.Equal(WindowRejection.None, WindowEligibility.Check(Window(exStyle: WindowEligibility.ExToolWindow | WindowEligibility.ExAppWindow), Own));

    [Fact]
    public void AppWindowFlagKeepsOwnedWindowEligible() =>
        Assert.Equal(WindowRejection.None, WindowEligibility.Check(Window(owner: 7, exStyle: WindowEligibility.ExAppWindow), Own));

    [Fact]
    public void NoActivateWindowIsRejected() =>
        Assert.Equal(WindowRejection.NeverActivates, WindowEligibility.Check(Window(exStyle: WindowEligibility.ExNoActivate), Own));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    public void ShellClassesAreRejected(string className) =>
        Assert.Equal(WindowRejection.ShellChrome, WindowEligibility.Check(Window(cls: className), Own));

    [Fact]
    public void ShellClassMatchIsCaseSensitiveAndExact()
    {
        Assert.False(WindowEligibility.IsShellChrome("progman"));
        Assert.False(WindowEligibility.IsShellChrome("Progman2"));
    }

    [Fact]
    public void OwnProcessWindowIsRejected() =>
        Assert.Equal(WindowRejection.OwnWindow, WindowEligibility.Check(Window(pid: Own), Own));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    public void EmptyRectangleIsRejected(double width, double height) =>
        Assert.Equal(WindowRejection.EmptyRectangle, WindowEligibility.Check(Window(rect: new DisplayRect(0, 0, width, height)), Own));

    [Fact]
    public void OtherVirtualDesktopIsRejected() =>
        Assert.Equal(WindowRejection.OtherVirtualDesktop, WindowEligibility.Check(Window(onDesktop: false), Own));

    [Fact]
    public void RuleOrderIsStable()
    {
        // Invisible wins over everything; a minimized own window is "minimized", not "own".
        Assert.Equal(WindowRejection.NotVisible, WindowEligibility.Check(Window(visible: false, iconic: true, pid: Own), Own));
        Assert.Equal(WindowRejection.Minimized, WindowEligibility.Check(Window(iconic: true, pid: Own), Own));
    }

    [Fact]
    public void ElevationDoesNotAffectEligibility()
    {
        Assert.True(WindowEligibility.IsEligible(Window(elevated: true), Own));
        Assert.True(WindowEligibility.IsEligible(Window(elevated: null), Own));
    }

    [Fact]
    public void OwnOrShellDetection()
    {
        Assert.True(WindowEligibility.IsOwnOrShell(Window(pid: Own), Own));
        Assert.True(WindowEligibility.IsOwnOrShell(Window(cls: "Shell_TrayWnd"), Own));
        Assert.False(WindowEligibility.IsOwnOrShell(Window(), Own));
    }

    private static DisplayId? Map(long monitor) => monitor switch
    {
        10 => (DisplayId?)new DisplayId("left"),
        20 => new DisplayId("right"),
        _ => (DisplayId?)null,
    };

    [Fact]
    public void TopmostEligibleWindowWinsPerDisplay()
    {
        var windows = new[]
        {
            Window(handle: 1, monitor: 10),
            Window(handle: 2, monitor: 20),
            Window(handle: 3, monitor: 10),
            Window(handle: 4, monitor: 20),
        };

        var result = WindowEligibility.LastUsedByDisplay(windows, Own, Map);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[new DisplayId("left")].Handle);
        Assert.Equal(2, result[new DisplayId("right")].Handle);
    }

    [Fact]
    public void IneligibleWindowsAreSkippedInOrder()
    {
        var windows = new[]
        {
            Window(handle: 1, monitor: 10, pid: Own),
            Window(handle: 2, monitor: 10, cloaked: true),
            Window(handle: 3, monitor: 10, iconic: true),
            Window(handle: 4, monitor: 10, cls: "Shell_TrayWnd"),
            Window(handle: 5, monitor: 10),
        };

        var result = WindowEligibility.LastUsedByDisplay(windows, Own, Map);

        Assert.Equal(5, Assert.Single(result).Value.Handle);
    }

    [Fact]
    public void DisplayWithNoEligibleWindowIsAbsent()
    {
        var windows = new[] { Window(handle: 1, monitor: 10), Window(handle: 2, monitor: 20, visible: false) };

        var result = WindowEligibility.LastUsedByDisplay(windows, Own, Map);

        Assert.True(result.ContainsKey(new DisplayId("left")));
        Assert.False(result.ContainsKey(new DisplayId("right")));
    }

    [Fact]
    public void WindowOnUnknownMonitorIsIgnored()
    {
        var windows = new[] { Window(handle: 1, monitor: 99), Window(handle: 2, monitor: 10) };

        var result = WindowEligibility.LastUsedByDisplay(windows, Own, Map);

        Assert.Equal(2, Assert.Single(result).Value.Handle);
    }

    [Fact]
    public void EmptyInputGivesEmptyResult() =>
        Assert.Empty(WindowEligibility.LastUsedByDisplay([], Own, Map));
}
