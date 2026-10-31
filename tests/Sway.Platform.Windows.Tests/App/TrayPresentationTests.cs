using System;
using System.IO;
using System.Linq;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Tests.App;

public sealed class TrayPresentationTests
{
    [Fact]
    public void AppearanceMatchesTheFourStates()
    {
        Assert.Equal(new TrayIconAppearance(true, false, false, false), TrayIconAppearance.For(TrayIconState.Tracking));
        Assert.Equal(new TrayIconAppearance(false, true, false, false), TrayIconAppearance.For(TrayIconState.Paused));
        Assert.True(TrayIconAppearance.For(TrayIconState.FocusLock).LockBadge);
        Assert.True(TrayIconAppearance.For(TrayIconState.Attention).AttentionDot);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(18, 16)]
    [InlineData(22, 20)]
    [InlineData(30, 32)]
    [InlineData(64, 32)]
    public void NearestSizePicksADrawnSize(int requested, int expected) =>
        Assert.Equal(expected, TrayIconAppearance.NearestSize(requested));

    [Fact]
    public void TooltipIsCappedAt63Characters()
    {
        Assert.Equal("Sway: Tracking", TrayIconAppearance.Tooltip("Tracking"));
        Assert.Equal(63, TrayIconAppearance.Tooltip(new string('x', 200)).Length);
    }

    [Fact]
    public void PaletteFollowsTheTaskbarTheme()
    {
        Assert.Equal(TrayPalette.Forest, TrayPalette.For(true).Mark);
        Assert.Equal(TrayPalette.Ivory, TrayPalette.For(false).Mark);
        Assert.Equal(new Rgb(0x2F, 0x5D, 0x3A), TrayPalette.Forest);
        Assert.False(TrayPalette.IsLight(0));
        Assert.True(TrayPalette.IsLight(1));
        Assert.True(TrayPalette.IsLight(null));
    }

    private static TrayMenuInput Input(
        bool tracking = true,
        bool locked = false,
        string? message = null,
        LoginItemStatus login = LoginItemStatus.Disabled,
        CameraDevice[]? cameras = null) =>
        new(
            "Tracking · Cam",
            tracking,
            locked,
            HotKeyShortcut.FocusLockDefault,
            message,
            cameras ?? [new CameraDevice("a", "Built-in", true), new CameraDevice("b", "USB", false)],
            "b",
            login);

    [Fact]
    public void MenuHasTheItemsInOrder()
    {
        var menu = TrayMenuModel.Build(Input());
        var texts = menu.Where(e => !e.IsSeparator).Select(e => e.Text).ToList();
        Assert.Equal(
            ["Tracking · Cam", "Tracking", "Focus Lock (Ctrl+Alt+Shift+L)", "Camera", "Calibrate…", "Diagnostics…", "Open Sway at login", "Quit Sway"],
            texts);
        Assert.False(menu[0].Enabled);
    }

    [Fact]
    public void MenuChecksReflectState()
    {
        var menu = TrayMenuModel.Build(Input(tracking: false, locked: true, login: LoginItemStatus.Enabled));
        Assert.False(menu.Single(e => e.Command == TrayCommand.ToggleTracking).Checked);
        Assert.True(menu.Single(e => e.Command == TrayCommand.ToggleFocusLock).Checked);
        Assert.True(menu.Single(e => e.Command == TrayCommand.ToggleLoginItem).Checked);
    }

    [Fact]
    public void ShortcutMessageAppearsDisabledWhenPresent()
    {
        Assert.DoesNotContain(TrayMenuModel.Build(Input()), e => e.Text.Contains("in use", StringComparison.Ordinal));
        var menu = TrayMenuModel.Build(Input(message: "Shortcut in use"));
        var row = menu.Single(e => e.Text == "Shortcut in use");
        Assert.False(row.Enabled);
    }

    [Fact]
    public void CameraSubmenuChecksTheSelectedCamera()
    {
        var camera = TrayMenuModel.Build(Input()).Single(e => e.Text == "Camera");
        Assert.Equal(["Built-in", "USB"], camera.Children!.Select(c => c.Text).ToArray());
        Assert.Equal("b", camera.Children!.Single(c => c.Checked).Argument);
        var none = TrayMenuModel.Build(Input(cameras: [])).Single(e => e.Text == "Camera");
        Assert.False(none.Children!.Single().Enabled);
    }

    [Fact]
    public void LoginItemIsDisabledWhenUnavailable() =>
        Assert.False(TrayMenuModel.Build(Input(login: LoginItemStatus.Unavailable)).Single(e => e.Command == TrayCommand.ToggleLoginItem).Enabled);

    [Fact]
    public void FocusLockTextWithoutShortcut() => Assert.Equal("Focus Lock", TrayMenuModel.FocusLockText(null));

    [Fact]
    public void InFrontTrackerIsTrueWhileAnySourceIsSet()
    {
        var tracker = new InFrontTracker();
        Assert.True(tracker.Set("menu", true));
        Assert.True(tracker.Set("diagnostics", true));
        Assert.True(tracker.Set("menu", false));
        Assert.False(tracker.Set("diagnostics", false));
    }

    [Fact]
    public void DiagnosticsTextCoversTheRequiredFacts()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new DiagnosticsSnapshot
        {
            SwayVersion = "0.1.0",
            WindowsVersion = "Windows 11 25H2, build 26200.1",
            Architecture = "X64",
            StatusLine = "Tracking · C922",
            TrackingEnabled = true,
            CaptureText = "Running",
            HoldText = "Focus Lock on",
            CurrentCamera = "C922",
            Cameras = [new DiagnosticsCamera("C922", false, true, true)],
            Displays = [new DiagnosticsDisplay("LG 27", 2560, 1440, 100, true, true), new DiagnosticsDisplay("Dell", 1920, 1080, null, false, false)],
            HasCalibration = true,
            CalibrationCreatedAt = now - TimeSpan.FromDays(3),
            Now = now,
            LibraryCount = 2,
            DwellSeconds = 0.4,
            CooldownSeconds = 0.75,
            ConfidenceLevel = 2,
            Settings = [new("sway.trackingEnabled", "true"), new("sway.calibrationLibrary", new string('x', 500))],
            LogLines = ["line one"],
        };

        var text = DiagnosticsText.Build(snapshot);
        Assert.Contains("Sway version: 0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("Windows: Windows 11 25H2, build 26200.1", text, StringComparison.Ordinal);
        Assert.Contains("- C922 (selected, running)", text, StringComparison.Ordinal);
        Assert.Contains("1. LG 27: 2560x1440, scale 100%, primary, has focus", text, StringComparison.Ordinal);
        Assert.Contains("2. Dell: 1920x1080, scale unknown", text, StringComparison.Ordinal);
        Assert.Contains("present, made 3 days ago", text, StringComparison.Ordinal);
        Assert.Contains("Holds: Focus Lock on", text, StringComparison.Ordinal);
        Assert.Contains("Hold time: 0.40 s, cooldown: 0.75 s, confidence level: 2", text, StringComparison.Ordinal);
        Assert.Contains("sway.trackingEnabled = true", text, StringComparison.Ordinal);
        Assert.Contains("sway.calibrationLibrary = (500 characters)", text, StringComparison.Ordinal);
        Assert.Contains("Live pose (yaw, pitch): not available yet", text, StringComparison.Ordinal);
        Assert.EndsWith("line one", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsTextHandlesAnEmptySnapshot()
    {
        var text = DiagnosticsText.Build(new DiagnosticsSnapshot());
        Assert.Contains("Holds: none", text, StringComparison.Ordinal);
        Assert.Contains("(none found)", text, StringComparison.Ordinal);
        Assert.Contains("Calibration for these monitors and this camera: none", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Windows 10 Pro", "25H2", "26200", "1", "Windows 11 Pro 25H2, build 26200.1")]
    [InlineData("Windows 10 Pro", "22H2", "19045", "9", "Windows 10 Pro 22H2, build 19045.9")]
    [InlineData(null, null, null, null, "Windows, build unknown")]
    public void WindowsVersionNamesWindows11(string? product, string? display, string? build, string? revision, string expected) =>
        Assert.Equal(expected, WindowsVersionText.Format(product, display, build, revision));

    [Fact]
    public void LogRotatesOnceAtTheCap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sway-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new RotatingFileLog(directory, maxBytes: 200);
            for (var i = 0; i < 30; i++)
            {
                log.Write($"line {i:00} " + new string('a', 20));
            }

            Assert.True(File.Exists(log.CurrentPath + ".1"));
            Assert.True(new FileInfo(log.CurrentPath).Length <= 200);
            Assert.False(File.Exists(log.CurrentPath + ".2"));
            Assert.Contains("line 29", File.ReadAllText(log.CurrentPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
