using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.Platform.Windows.Tests.App;

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
        Assert.Equal("Gander: Tracking", TrayIconAppearance.Tooltip("Tracking"));
        Assert.Equal(63, TrayIconAppearance.Tooltip(new string('x', 200)).Length);
    }

    [Fact]
    public void PaletteFollowsTheTaskbarTheme()
    {
        Assert.Equal(GanderDesign.Charcoal, TrayPalette.For(true).Mark);
        Assert.Equal(GanderDesign.Ivory, TrayPalette.For(false).Mark);
        Assert.Equal(new Rgb(0x2F, 0x5D, 0x3A), TrayPalette.Forest);
        Assert.False(TrayPalette.IsLight(0));
        Assert.True(TrayPalette.IsLight(1));
        Assert.True(TrayPalette.IsLight(null));
    }

    private static TrayMenuInput Input(
        bool tracking = true,
        bool locked = false,
        TrayBanner? banner = null,
        CameraDevice[]? cameras = null,
        string? selected = "b",
        HotKeyShortcut? shortcut = null) =>
        new(
            "Tracking · Cam",
            StatusTone.Active,
            tracking,
            locked,
            shortcut ?? HotKeyShortcut.FocusLockDefault,
            banner,
            "Looking at Left",
            cameras ?? [new CameraDevice("a", "Built-in", true), new CameraDevice("b", "USB", false)],
            selected);

    [Fact]
    public void MenuHasTheMacPopoverOrder()
    {
        var menu = TrayMenuModel.Build(Input());
        var texts = menu.Where(e => !e.IsSeparator).Select(e => e.Text).ToList();
        Assert.Equal(
            ["Gander", "Tracking · Cam", "Tracking", "Focus Lock", "Looking at Left", "Camera", "Calibrate…", "Settings…", "Welcome Tour", "Pose Debug Window…", "Head-Pose Gate Test…", "Focus Test Window…", "Diagnostics…", "Quit Gander"],
            texts);
        Assert.Equal(TrayEntryRole.Header, menu[0].Role);
        Assert.Equal(TrayEntryRole.Status, menu[1].Role);
        Assert.False(menu[1].Enabled);
        Assert.Equal(StatusTone.Active, menu[1].Tone);
    }

    [Fact]
    public void DebugWindowsAreReachableFromTheMenuInTheMacOrder()
    {
        var menu = TrayMenuModel.Build(Input()).Where(e => !e.IsSeparator).ToList();
        var commands = menu.Select(e => e.Command).ToList();
        var calibrate = commands.IndexOf(TrayCommand.Calibrate);
        Assert.Equal(
            [TrayCommand.Calibrate, TrayCommand.Settings, TrayCommand.WelcomeTour, TrayCommand.PoseDebug, TrayCommand.GateTest, TrayCommand.FocusTest, TrayCommand.Diagnostics, TrayCommand.Quit],
            commands.Skip(calibrate).ToList());
        Assert.All(
            menu.Where(e => e.Command is TrayCommand.PoseDebug or TrayCommand.GateTest or TrayCommand.FocusTest),
            e =>
            {
                Assert.True(e.Enabled);
                Assert.EndsWith("…", e.Text, StringComparison.Ordinal);
            });
        Assert.Equal("Pose Debug Window…", TrayMenuModel.PoseDebugText);
        Assert.Equal("Head-Pose Gate Test…", TrayMenuModel.GateTestText);
        Assert.Equal("Focus Test Window…", TrayMenuModel.FocusTestText);
    }

    [Fact]
    public void FocusLockShowsItsShortcutBesideIt()
    {
        Assert.Equal("Ctrl+Alt+Shift+L", TrayMenuModel.Build(Input()).Single(e => e.Command == TrayCommand.ToggleFocusLock).ShortcutText);
        var plain = TrayMenuModel.Build(new TrayMenuInput("s", StatusTone.Idle, true, false, null, null, "x", [], null));
        Assert.Null(plain.Single(e => e.Command == TrayCommand.ToggleFocusLock).ShortcutText);
        Assert.Equal("Focus Lock", TrayMenuModel.FocusLockText);
    }

    [Fact]
    public void MenuHasNoLoginItemBecauseSettingsOwnsIt() =>
        Assert.DoesNotContain(TrayMenuModel.Build(Input()), e => e.Text.Contains("login", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void MenuChecksReflectState()
    {
        var menu = TrayMenuModel.Build(Input(tracking: false, locked: true));
        Assert.False(menu.Single(e => e.Command == TrayCommand.ToggleTracking).Checked);
        Assert.True(menu.Single(e => e.Command == TrayCommand.ToggleFocusLock).Checked);
    }

    [Fact]
    public void BannerLinesAppearBeforeTheSwitchesAndAreNeverClickable()
    {
        var banner = new TrayBanner(TrayBanner.NeedsCalibrationText, TrayBanner.CalibrateButton, TrayCommand.Calibrate);
        var menu = TrayMenuModel.Build(Input(banner: banner));
        var lines = menu.Where(e => e.Role == TrayEntryRole.Banner).ToList();
        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => Assert.False(l.Enabled));
        Assert.All(lines, l => Assert.True(l.Text.Length <= TrayMenuModel.BannerWidth));
        Assert.Equal(TrayBanner.NeedsCalibrationText, string.Join(" ", lines.Select(l => l.Text)));
        var button = menu.Single(e => e.Text == "Calibrate (about 20 s)…");
        Assert.Equal(TrayCommand.Calibrate, button.Command);
        var list = menu.ToList();
        Assert.True(list.IndexOf(button) < list.IndexOf(list.Single(e => e.Command == TrayCommand.ToggleTracking)));
    }

    [Fact]
    public void BannersFollowTheMacOrder()
    {
        var all = new TrayBannerFacts(CameraPermissionPrompt.Denied, true, true, 2, false, "Shortcut in use");
        Assert.StartsWith("Camera access is off.", TrayBanner.Current(all)!.Text, StringComparison.Ordinal);
        Assert.Equal(TrayCommand.OpenCameraSettings, TrayBanner.Current(all)!.Command);
        Assert.StartsWith("Camera access is restricted.", TrayBanner.Current(all with { PermissionPrompt = CameraPermissionPrompt.Restricted })!.Text, StringComparison.Ordinal);
        Assert.Equal(TrayBanner.FirstSetupText, TrayBanner.Current(all with { PermissionPrompt = null })!.Text);
        Assert.Equal(TrayBanner.NeedsCalibrationText, TrayBanner.Current(all with { PermissionPrompt = null, LibraryIsEmpty = false })!.Text);
        Assert.Equal("No calibration for C922 yet.", TrayBanner.Current(all with { PermissionPrompt = null, LibraryIsEmpty = false, CameraWithoutCalibration = "C922" })!.Text);
        Assert.Equal("Shortcut in use", TrayBanner.Current(all with { PermissionPrompt = null, LibraryIsEmpty = false, TrackingEnabled = false })!.Text);
        Assert.Null(TrayBanner.Current(all with { PermissionPrompt = null, LibraryIsEmpty = false, HasCalibration = true, ShortcutMessage = null }));
        Assert.Null(TrayBanner.Current(all with { PermissionPrompt = null, LibraryIsEmpty = false, DisplayCount = 1, ShortcutMessage = null }));
    }

    [Fact]
    public void WrapKeepsEveryWordAndRespectsTheWidth()
    {
        var lines = TrayMenuModel.Wrap("one two three four five six seven", 12);
        Assert.Equal("one two three four five six seven", string.Join(" ", lines));
        Assert.All(lines, l => Assert.True(l.Length <= 12));
        Assert.Empty(TrayMenuModel.Wrap(string.Empty, 12));
    }

    [Fact]
    public void LookLineFollowsTheMac()
    {
        string Label(DisplayId id) => id.RawValue;
        Assert.Equal("\u2014", LookSummary.Text(false, PoseClassification.NoFace, Label));
        Assert.Equal("\u2014", LookSummary.Text(true, null, Label));
        Assert.Equal("No face", LookSummary.Text(true, PoseClassification.NoFace, Label));
        var elsewhere = new PoseClassification(PoseClassification.ClassKind.Elsewhere, new DisplayId("L"), null, 0, new Dictionary<DisplayId, double>());
        Assert.Equal("Looking elsewhere", LookSummary.Text(true, elsewhere, Label));
        var left = new PoseClassification(PoseClassification.ClassKind.Display, new DisplayId("Left"), null, 0.5, new Dictionary<DisplayId, double>());
        Assert.Equal("Looking at Left", LookSummary.Text(true, left, Label));
    }

    [Fact]
    public void CameraSubmenuStartsWithDefaultAndChecksTheSelectedCamera()
    {
        var camera = TrayMenuModel.Build(Input()).Single(e => e.Text == "Camera");
        Assert.Equal(["Default camera", "Built-in", "USB"], camera.Children!.Select(c => c.Text).ToArray());
        Assert.Equal("b", camera.Children!.Single(c => c.Checked).Argument);
        Assert.True(TrayMenuModel.Build(Input(selected: null)).Single(e => e.Text == "Camera").Children![0].Checked);
        var gone = TrayMenuModel.Build(Input(selected: "zz")).Single(e => e.Text == "Camera").Children!;
        Assert.Equal("Unavailable camera (uses default)", gone[^1].Text);
        Assert.True(gone[^1].Checked);
        var none = TrayMenuModel.Build(Input(cameras: [])).Single(e => e.Text == "Camera");
        Assert.False(none.Children!.Single().Enabled);
    }

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
            GanderVersion = "0.1.0",
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
            Settings = [new("gander.trackingEnabled", "true"), new("gander.calibrationLibrary", new string('x', 500))],
            LogLines = ["line one"],
            Switching = new DiagnosticsSwitching
            {
                IsTrackingActive = true,
                TrackingEnabled = true,
                HasActiveCalibration = true,
                CalibrationCamera = "C922",
                CalibrationFingerprint = "fp-1",
                LiveFingerprint = "fp-1",
                CaptureText = "Running",
                GanderInFront = true,
                IsCameraActive = true,
                BelievedFocus = "d1 (LG 27)",
                Targets = ["d1 (LG 27)", "d2 (Dell)"],
                HasDecision = true,
                DecisionText = "Display, best Dell, margin 0.35",
                PhaseName = "Dwelling(0.50)",
                ReasonText = "Dwelling",
                EnterMargin = 0.3,
                HasPose = true,
                Yaw = -12.5,
                Pitch = 3,
                FaceConfidence = 0.9,
                PoseAgeSeconds = 1.5,
                LastSampleHadFace = true,
                SamplesReceived = 100,
                SamplesWithFace = 90,
                SamplesProcessed = 80,
                SwitchRequests = 3,
                Focused = 1,
                NoWindow = 1,
                Failed = 1,
                SkippedByHold = 4,
                SkippedInFlight = 2,
            },
        };

        var text = DiagnosticsText.Build(snapshot);
        Assert.Contains("Gander version: 0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("Windows: Windows 11 25H2, build 26200.1", text, StringComparison.Ordinal);
        Assert.Contains("- C922 (selected, running)", text, StringComparison.Ordinal);
        Assert.Contains("1. LG 27: 2560x1440, scale 100%, primary, has focus", text, StringComparison.Ordinal);
        Assert.Contains("2. Dell: 1920x1080, scale unknown", text, StringComparison.Ordinal);
        Assert.Contains("present, made 3 days ago", text, StringComparison.Ordinal);
        Assert.Contains("Holds: Focus Lock on", text, StringComparison.Ordinal);
        Assert.Contains("Hold time: 0.40 s, cooldown: 0.75 s, confidence level: 2", text, StringComparison.Ordinal);
        Assert.Contains("gander.trackingEnabled = true", text, StringComparison.Ordinal);
        Assert.Contains("gander.calibrationLibrary = (500 characters)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not available yet", text, StringComparison.Ordinal);
        Assert.Contains("== Switching ==", text, StringComparison.Ordinal);
        Assert.Contains("IsTrackingActive: yes (TrackingEnabled: yes, CalibrationRunning: no)", text, StringComparison.Ordinal);
        Assert.Contains("ActiveCalibration: yes, camera C922, layout fingerprint fp-1", text, StringComparison.Ordinal);
        Assert.Contains("Hold inputs: focus lock no, Gander in front yes, displays changing no, desktop changing no, switching in flight no", text, StringComparison.Ordinal);
        Assert.Contains("Display believed to have focus: d1 (LG 27)", text, StringComparison.Ordinal);
        Assert.Contains("Tracked targets: d1 (LG 27), d2 (Dell)", text, StringComparison.Ordinal);
        Assert.Contains("Last decision: Display, best Dell, margin 0.35; phase Dwelling(0.50) (Dwelling); enter margin 0.30; SwitchTo not set", text, StringComparison.Ordinal);
        Assert.Contains("Live pose: yaw -12.5, pitch 3.0, roll 0.0, confidence 0.90, 1.5 s ago (face found)", text, StringComparison.Ordinal);
        Assert.Contains("Counts: samples 100, with a face 90, processed by the decider 80, switch requests 3", text, StringComparison.Ordinal);
        Assert.Contains("Results: focused 1, no window 1, failed 1; skipped: hold active 4, attempt in flight 2", text, StringComparison.Ordinal);
        Assert.EndsWith("line one", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsTextHandlesAnEmptySnapshot()
    {
        var text = DiagnosticsText.Build(new DiagnosticsSnapshot());
        Assert.Contains("Holds: none", text, StringComparison.Ordinal);
        Assert.Contains("(none found)", text, StringComparison.Ordinal);
        Assert.Contains("ActiveCalibration: none", text, StringComparison.Ordinal);
        Assert.Contains("Live pose: none (no sample has arrived)", text, StringComparison.Ordinal);
        Assert.Contains("Last decision: none yet", text, StringComparison.Ordinal);
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
        var directory = Path.Combine(Path.GetTempPath(), "gander-log-" + Guid.NewGuid().ToString("N"));
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
