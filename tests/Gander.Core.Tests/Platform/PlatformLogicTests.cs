using Gander.Core.Platform;

namespace Gander.Core.Tests.Platform;

public class CameraSelectionTests
{
    [Theory]
    [InlineData("Integrated Camera", false, true)]
    [InlineData("HP TrueVision Internal webcam", false, true)]
    [InlineData("Logitech C922 Pro Stream Webcam", false, false)]
    [InlineData("Logitech C922 Pro Stream Webcam", true, true)]
    [InlineData("USB Video Device", false, false)]
    [InlineData("Surface Camera Front", false, true)]
    public void BuiltInHeuristic(string name, bool panel, bool expected) =>
        Assert.Equal(expected, CameraSelection.IsBuiltIn(name, panel));

    private static readonly CameraDevice Usb = new("usb", "C922", false);
    private static readonly CameraDevice Inner = new("inner", "Integrated Camera", true);

    [Fact]
    public void ResolveSavedIdThenBuiltInThenFirst()
    {
        Assert.Equal(Usb, CameraSelection.Resolve([Inner, Usb], "USB"));
        Assert.Equal(Inner, CameraSelection.Resolve([Usb, Inner], "gone"));
        Assert.Equal(Inner, CameraSelection.Resolve([Usb, Inner], null));
        Assert.Equal(Usb, CameraSelection.Resolve([Usb], null));
        Assert.Null(CameraSelection.Resolve([], "usb"));
    }

    [Theory]
    [InlineData("Allow", "Allow", "Allow", CameraAuthorization.Authorized)]
    [InlineData(null, null, null, CameraAuthorization.Authorized)]
    [InlineData("Deny", "Allow", "Allow", CameraAuthorization.Denied)]
    [InlineData("Allow", "deny", null, CameraAuthorization.Denied)]
    [InlineData("Allow", "Allow", "Deny", CameraAuthorization.Denied)]
    [InlineData("Prompt", null, null, CameraAuthorization.Authorized)]
    public void ConsentValues(string? user, string? machine, string? desktop, CameraAuthorization expected) =>
        Assert.Equal(expected, CameraSelection.Interpret(user, machine, desktop));
}

public class CaptureSupervisorTests
{
    [Fact]
    public void StallIsReportedAfterThreeSecondsThenRebuiltAfterTenThirtySixty()
    {
        var supervisor = new CaptureSupervisor();
        supervisor.Opened(0);
        supervisor.FrameArrived(1);
        Assert.Equal(SupervisorAction.None, supervisor.Tick(3.9));
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(4.0));
        Assert.Equal(SupervisorAction.None, supervisor.Tick(13.9));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(14.0));

        supervisor.Opened(14);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(17));
        Assert.Equal(SupervisorAction.None, supervisor.Tick(46.9));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(47));

        supervisor.Opened(47);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(50));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(110));
        supervisor.Opened(110);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(113));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(173)); // stays at 60 s
    }

    [Fact]
    public void FramesResetTheStallDelays()
    {
        var supervisor = new CaptureSupervisor();
        supervisor.Opened(0);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(3));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(13));
        supervisor.Opened(13);
        supervisor.FrameArrived(13.1);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Tick(16.1));
        Assert.Equal(SupervisorAction.None, supervisor.Tick(25.9));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(26.1)); // back to 10 s
    }

    [Fact]
    public void OpenFailuresBackOffOneTwoFiveTen()
    {
        var supervisor = new CaptureSupervisor();
        Assert.Equal(1, supervisor.OpenFailed(0));
        Assert.Equal(SupervisorAction.None, supervisor.Tick(0.5));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(1));
        Assert.Equal(2, supervisor.OpenFailed(1));
        Assert.Equal(5, supervisor.OpenFailed(3));
        Assert.Equal(10, supervisor.OpenFailed(8));
        Assert.Equal(10, supervisor.OpenFailed(18));
        Assert.Equal(CaptureSupervisor.Phase.Waiting, supervisor.State);
    }

    [Fact]
    public void InterruptActsLikeAnImmediateStall()
    {
        var supervisor = new CaptureSupervisor();
        Assert.Equal(SupervisorAction.None, supervisor.Interrupt(0));
        supervisor.Opened(0);
        supervisor.FrameArrived(0.5);
        Assert.Equal(SupervisorAction.ReportInterrupted, supervisor.Interrupt(1));
        Assert.Equal(SupervisorAction.None, supervisor.Interrupt(1.1));
        Assert.Equal(SupervisorAction.Rebuild, supervisor.Tick(11));
    }

    [Fact]
    public void NothingHappensWhileIdle()
    {
        var supervisor = new CaptureSupervisor();
        Assert.Equal(SupervisorAction.None, supervisor.Tick(100));
    }
}

public class SessionMappingTests
{
    [Theory]
    [InlineData(7, SessionEvent.ScreenLocked)]
    [InlineData(8, SessionEvent.ScreenUnlocked)]
    [InlineData(2, SessionEvent.SessionInactive)]
    [InlineData(1, SessionEvent.SessionActive)]
    public void SessionChange(int code, SessionEvent expected) =>
        Assert.Equal(expected, SessionMapping.FromSessionChange(code));

    [Theory]
    [InlineData(3)] // remote connect
    [InlineData(5)] // logon
    [InlineData(6)] // logoff
    public void UnhandledSessionCodesAreIgnored(int code) => Assert.Null(SessionMapping.FromSessionChange(code));

    [Theory]
    [InlineData(0x4, SessionEvent.SystemSleeping)]
    [InlineData(0x12, SessionEvent.SystemWoke)]
    [InlineData(0x7, SessionEvent.SystemWoke)]
    public void PowerBroadcast(int code, SessionEvent expected) =>
        Assert.Equal(expected, SessionMapping.FromPowerBroadcast(code));

    [Fact]
    public void PowerBroadcastIgnoresBatteryAndSettingCodes()
    {
        Assert.Null(SessionMapping.FromPowerBroadcast(0xA)); // PBT_APMPOWERSTATUSCHANGE
        Assert.Null(SessionMapping.FromPowerBroadcast(0x8013)); // PBT_POWERSETTINGCHANGE
    }

    [Theory]
    [InlineData(0, SessionEvent.DisplaysSleeping)]
    [InlineData(2, SessionEvent.DisplaysSleeping)]
    [InlineData(1, SessionEvent.DisplaysWoke)]
    public void ConsoleDisplayState(int value, SessionEvent expected)
    {
        Assert.Equal(expected, SessionMapping.FromPowerSetting(SessionMapping.ConsoleDisplayState, BitConverter.GetBytes(value)));
        Assert.Equal(expected, SessionMapping.FromPowerSetting(SessionMapping.ConsoleDisplayState, new[] { (byte)value }));
    }

    [Fact]
    public void OtherSettingsAndBadDataAreIgnored()
    {
        Assert.Null(SessionMapping.FromPowerSetting(Guid.NewGuid(), BitConverter.GetBytes(1)));
        Assert.Null(SessionMapping.FromPowerSetting(SessionMapping.ConsoleDisplayState, ReadOnlySpan<byte>.Empty));
        Assert.Null(SessionMapping.FromPowerSetting(SessionMapping.ConsoleDisplayState, BitConverter.GetBytes(7)));
    }

    [Theory]
    [InlineData("Default", false)]
    [InlineData("default", false)]
    [InlineData("Winlogon", true)]
    [InlineData(null, true)]
    public void LockedDesktop(string? name, bool locked) => Assert.Equal(locked, SessionMapping.IsLockedDesktop(name));

    [Fact]
    public void ActiveConnectState()
    {
        Assert.True(SessionMapping.IsActiveConnectState(0));
        Assert.False(SessionMapping.IsActiveConnectState(4)); // WTSDisconnected
    }
}
