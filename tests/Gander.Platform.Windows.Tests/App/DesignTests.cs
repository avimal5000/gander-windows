using System;
using System.Collections.Generic;
using System.Linq;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.Platform.Windows.Tests.App;

public sealed class DesignTests
{
    [Fact]
    public void PaletteIsTheMacPaletteExactly()
    {
        Assert.Equal(new Rgb(0x2F, 0x5D, 0x3A), GanderDesign.Forest);
        Assert.Equal(new Rgb(0x9C, 0xAF, 0x88), GanderDesign.Sage);
        Assert.Equal(new Rgb(0xF6, 0xF3, 0xEA), GanderDesign.Ivory);
        Assert.Equal(new Rgb(0x2B, 0x2B, 0x2B), GanderDesign.Charcoal);
        Assert.Equal(new Rgb(0x6F, 0xAF, 0x84), GanderDesign.GreenDark);
        // Amber (0.78, 0.52, 0.10) and brick (0.69, 0.23, 0.20) as bytes.
        Assert.Equal(new Rgb((byte)Math.Round(0.78 * 255), (byte)Math.Round(0.52 * 255), (byte)Math.Round(0.10 * 255)), GanderDesign.Amber);
        Assert.Equal(new Rgb((byte)Math.Round(0.69 * 255), (byte)Math.Round(0.23 * 255), (byte)Math.Round(0.20 * 255)), GanderDesign.Brick);
        Assert.Equal(6, GanderDesign.Series.Count);
        Assert.Equal(GanderDesign.Forest, GanderDesign.SeriesColor(0));
        Assert.Equal(GanderDesign.SeriesColor(1), GanderDesign.SeriesColor(7));
        Assert.Equal(GanderDesign.SeriesColor(5), GanderDesign.SeriesColor(-1));
    }

    [Fact]
    public void GreenAndAccentChangeWithTheAppearance()
    {
        Assert.Equal(GanderDesign.Forest, GanderDesign.Green(true));
        Assert.Equal(GanderDesign.GreenDark, GanderDesign.Green(false));
        Assert.Equal(GanderDesign.Forest, GanderDesign.TextAccent(true));
        Assert.Equal(GanderDesign.Sage, GanderDesign.TextAccent(false));
    }

    [Fact]
    public void ThemesFollowTheMacCards()
    {
        var light = GanderTheme.For(true);
        var dark = GanderTheme.For(false);
        Assert.Equal(Rgba.Of(GanderDesign.Ivory), light.Card);
        Assert.Equal(0x0F, dark.Card.A); // white at 6%
        Assert.Equal(GanderDesign.Charcoal, light.Text);
        Assert.Equal(GanderDesign.Ivory, dark.Text);
        Assert.Equal(GanderDesign.Ivory, light.MenuSurface);
        Assert.Equal(GanderDesign.Charcoal, dark.MenuSurface);
        Assert.Equal(GanderDesign.Forest, light.Accent);
        Assert.Equal(GanderDesign.Sage, dark.Accent);
        Assert.Equal(new Rgb(1, 2, 3), GanderTheme.For(true, new Rgb(1, 2, 3)).Accent);
    }

    [Fact]
    public void TranslucentTokensFlattenOverTheSurface()
    {
        var flat = new Rgba(0, 0, 0, 128).Over(new Rgb(200, 100, 50));
        Assert.Equal(new Rgb(100, 50, 25), flat);
        Assert.Equal(new Rgb(10, 20, 30), new Rgba(10, 20, 30).Over(new Rgb(200, 100, 50)));
        var hover = GanderTheme.For(true).Flat(GanderTheme.For(true).Hover);
        Assert.True(hover.R < GanderDesign.Ivory.R);
    }

    [Fact]
    public void ScalesMatchTheMacLayout()
    {
        Assert.Equal(12, GanderDesign.Space.Card);
        Assert.Equal(16, GanderDesign.Space.Section);
        Assert.Equal(20, GanderDesign.Space.SettingsPadding);
        Assert.Equal(24, GanderDesign.Space.SetupPadding);
        Assert.Equal(10, GanderDesign.Radius.Card);
        Assert.Equal(6, GanderDesign.Radius.Chip);
        Assert.Equal(5, GanderDesign.Radius.Row);
        Assert.Equal(0.225, GanderDesign.Radius.TileRatio);
        Assert.Equal(520, GanderDesign.Size.SettingsWidth);
        Assert.Equal(300, GanderDesign.Size.MenuWidth);
    }

    [Fact]
    public void FontsPreferSegoeUiVariableAndFallBackToSegoeUi()
    {
        Assert.Equal("Segoe UI Variable Text, Segoe UI", GanderDesign.Text.Wpf);
        Assert.Equal("Segoe UI Variable Display, Segoe UI", GanderDesign.Display.Wpf);
        Assert.Equal("Segoe UI Variable Text", GanderDesign.Text.Resolve(_ => true));
        Assert.Equal("Segoe UI", GanderDesign.Text.Resolve(name => name == "Segoe UI"));
        Assert.Same(GanderDesign.Display, GanderDesign.Type.StackFor(GanderDesign.Type.Title));
        Assert.Same(GanderDesign.Text, GanderDesign.Type.StackFor(GanderDesign.Type.Body));
    }

    [Fact]
    public void TypeRampKeepsTheMacHierarchy()
    {
        var sizes = new[] { GanderDesign.Type.Caption, GanderDesign.Type.Callout, GanderDesign.Type.Body, GanderDesign.Type.Title3, GanderDesign.Type.Title2, GanderDesign.Type.Title }
            .Select(t => t.Size).ToList();
        Assert.Equal(sizes.OrderBy(x => x), sizes);
        Assert.Equal(600, GanderDesign.Type.Headline.Weight);
        Assert.Equal(sizes[2], GanderDesign.Type.Headline.Size);
        Assert.True(GanderDesign.Type.Title.Display);
    }

    [Fact]
    public void MotionNumbersAreTheMacs()
    {
        Assert.Equal(new FeedbackTiming(0.1, 0.05, 0.2, 0.35), GanderDesign.Motion.Glow);
        Assert.Equal(new FeedbackTiming(0.15, 1, 0.3, 1.3), GanderDesign.Motion.Notice);
        Assert.Equal(0.35, GanderDesign.Motion.Glow.TotalDuration(true), 3);
        Assert.Equal(0.35, GanderDesign.Motion.Glow.TotalDuration(false), 3);
        Assert.Equal(1.45, GanderDesign.Motion.Notice.TotalDuration(true), 3);
        Assert.Equal(1.3, GanderDesign.Motion.Notice.TotalDuration(false), 3);
        Assert.Equal(0.2, GanderDesign.Motion.Fade);
        Assert.Equal((0.42, 0.0, 0.58, 1.0), GanderDesign.Motion.EaseInOut);
    }

    // ----- Glyph -----

    [Theory]
    [InlineData(TrayIconState.Tracking, 1.0, false, false, false)]
    [InlineData(TrayIconState.Paused, 0.5, true, false, false)]
    [InlineData(TrayIconState.FocusLock, 1.0, false, true, false)]
    [InlineData(TrayIconState.Attention, 1.0, false, false, true)]
    public void GlyphStylesAreTheMacsTable(TrayIconState state, double opacity, bool hollow, bool lockBadge, bool dot)
    {
        var look = TrayIconAppearance.For(state);
        Assert.Equal(opacity, look.Opacity);
        Assert.Equal(hollow, look.HollowEnd);
        Assert.Equal(lockBadge, look.LockBadge);
        Assert.Equal(dot, look.AttentionDot);
    }

    [Theory]
    [InlineData(TrackingStatusKind.Tracking, TrayIconState.Tracking)]
    [InlineData(TrackingStatusKind.NoFace, TrayIconState.Tracking)]
    [InlineData(TrackingStatusKind.Calibrating, TrayIconState.Tracking)]
    [InlineData(TrackingStatusKind.Starting, TrayIconState.Tracking)]
    [InlineData(TrackingStatusKind.Paused, TrayIconState.Paused)]
    [InlineData(TrackingStatusKind.OneDisplay, TrayIconState.Paused)]
    [InlineData(TrackingStatusKind.Suspended, TrayIconState.Paused)]
    [InlineData(TrackingStatusKind.FocusLock, TrayIconState.FocusLock)]
    [InlineData(TrackingStatusKind.CameraBlocked, TrayIconState.Attention)]
    [InlineData(TrackingStatusKind.CameraFailed, TrayIconState.Attention)]
    [InlineData(TrackingStatusKind.CameraBusy, TrayIconState.Attention)]
    [InlineData(TrackingStatusKind.CameraDisconnected, TrayIconState.Attention)]
    [InlineData(TrackingStatusKind.NeedsCalibration, TrayIconState.Attention)]
    public void StatusMapsToTheMacGlyph(TrackingStatusKind kind, TrayIconState expected) =>
        Assert.Equal(expected, new TrackingStatus(kind).Icon);

    [Fact]
    public void MarkGeometryIsTheMacsGoose()
    {
        var bounds = GanderMarkGeometry.VisualBounds;
        Assert.Equal(new MarkRect(14, 10.9, 68.3, 75.8), bounds);
        Assert.Equal(MarkStepKind.Move, GanderMarkGeometry.Silhouette[0].Kind);
        Assert.Equal(MarkStepKind.Close, GanderMarkGeometry.Silhouette[^1].Kind);
        Assert.Equal(new MarkPoint(60.8, 17.1), GanderMarkGeometry.EyeCenter);
        Assert.Equal(1.6, GanderMarkGeometry.EyeRadius);

        // Every outline point lies inside the visual bounds (with room for the curve's overshoot).
        foreach (var step in GanderMarkGeometry.Silhouette)
        {
            var end = step.Kind == MarkStepKind.Cubic ? step.C : step.A;
            if (step.Kind != MarkStepKind.Close)
            {
                Assert.InRange(end.X, bounds.X - 1e-9, bounds.Right + 1e-9);
                Assert.InRange(end.Y, bounds.Y - 1e-9, bounds.Bottom + 1e-9);
            }
        }
    }

    [Fact]
    public void MarkFitsItsAreaAndCentersIt()
    {
        var area = TrayGlyphSpec.MarkArea;
        var placement = TrayGlyphSpec.Placement;
        var bounds = GanderMarkGeometry.VisualBounds;
        var left = placement.Point(new MarkPoint(bounds.X, 0)).X;
        var right = placement.Point(new MarkPoint(bounds.Right, 0)).X;
        var top = placement.Point(new MarkPoint(0, bounds.Y)).Y;
        var bottom = placement.Point(new MarkPoint(0, bounds.Bottom)).Y;
        Assert.Equal(area.MidX, (left + right) / 2, 6);
        Assert.Equal(area.MidY, (top + bottom) / 2, 6);
        Assert.True(right - left <= area.Width + 1e-9);
        Assert.True(bottom - top <= area.Height + 1e-9);
        Assert.True(Math.Abs((right - left) - area.Width) < 1e-9 || Math.Abs((bottom - top) - area.Height) < 1e-9);
        Assert.Equal(15 / 75.8, placement.Scale, 6);
    }

    [Fact]
    public void BadgesStayInsideTheGlyphAndOutOfTheMark()
    {
        // The badges themselves are inside the 18 point image; only the clear ring around them may reach past an edge.
        var body = TrayGlyphSpec.LockBody;
        var shackle = TrayGlyphSpec.Shackle;
        Assert.True(body.Right <= TrayGlyphSpec.Side && body.Bottom <= TrayGlyphSpec.Side && shackle.Y >= 0);
        var lockOutline = TrayGlyphSpec.LockOutline;
        Assert.Equal(body.X - TrayGlyphSpec.BadgeGap, lockOutline.X, 9);
        Assert.Equal(shackle.Y - TrayGlyphSpec.BadgeGap, lockOutline.Y, 9);
        Assert.Equal(body.Right + TrayGlyphSpec.BadgeGap, lockOutline.Right, 9);
        var dot = TrayGlyphSpec.AlertCenter;
        Assert.True(dot.X + TrayGlyphSpec.AlertRadius <= TrayGlyphSpec.Side && dot.Y - TrayGlyphSpec.AlertRadius >= 0);
        Assert.Equal(0.55, TrayGlyphSpec.EyeRadius);
        Assert.Equal(1.2, TrayGlyphSpec.OutlineWidth);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void EveryTraySizeIsOffered(int size) => Assert.Contains(size, TrayIconAppearance.Sizes);

    // ----- Edge glow -----

    [Fact]
    public void GlowConstantsAreTheMacs()
    {
        Assert.Equal(4, GanderDesign.Glow.BorderWidth);
        Assert.Equal(10, GanderDesign.Glow.GlowWidth);
        Assert.Equal(0.4, GanderDesign.Glow.GlowOpacity);
        Assert.Equal(0.4 * 0.95 * 0.95, GanderDesign.Glow.RingOpacity(0), 9);
        Assert.Equal(0.4 * 0.05 * 0.05, GanderDesign.Glow.RingOpacity(9), 9);
        var stops = GanderDesign.Glow.Stops();
        Assert.Equal(11, stops.Count);
        Assert.Equal((0.0, GanderDesign.Glow.RingOpacity(0)), stops[0]);
        Assert.Equal((1.0, 0.0), stops[^1]);
        Assert.Equal(stops.OrderByDescending(s => s.Opacity).ToList(), stops);
    }

    [Fact]
    public void GlowStripsRunAlongTheEdgesAndLeaveTheMiddleEmpty()
    {
        var strips = EdgeGlowLayout.Strips(1920, 1080);
        Assert.Equal([GlowEdge.Left, GlowEdge.Right, GlowEdge.Bottom, GlowEdge.Top], strips.Select(s => s.Edge).ToArray());
        var left = strips[0];
        Assert.Equal(new MarkRect(0, 0, 4, 1080), left.Border);
        Assert.Equal(new MarkRect(4, 4, 10, 1072), left.Glow);
        var right = strips[1];
        Assert.Equal(new MarkRect(1916, 0, 4, 1080), right.Border);
        Assert.Equal(new MarkRect(1906, 4, 10, 1072), right.Glow);
        var bottom = strips[2];
        Assert.Equal(new MarkRect(4, 1076, 1912, 4), bottom.Border);
        Assert.Equal(new MarkRect(4, 1066, 1912, 10), bottom.Glow);
        var top = strips[3];
        Assert.Equal(new MarkRect(4, 0, 1912, 4), top.Border);
        Assert.Equal(new MarkRect(4, 4, 1912, 10), top.Glow);
        // Nothing reaches past 14 px from any edge, so the middle has no layer.
        foreach (var strip in strips)
        {
            foreach (var rect in new[] { strip.Border, strip.Glow })
            {
                Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= 1920 && rect.Bottom <= 1080);
                Assert.True(rect.Width <= 14 || rect.Height <= 14);
            }
        }
    }

    [Fact]
    public void GlowGradientsFadeInwardFromTheirEdge()
    {
        var strips = EdgeGlowLayout.Strips(800, 600);
        Assert.Equal((new MarkPoint(0, 0.5), new MarkPoint(1, 0.5)), (strips[0].GradientStart, strips[0].GradientEnd));
        Assert.Equal((new MarkPoint(1, 0.5), new MarkPoint(0, 0.5)), (strips[1].GradientStart, strips[1].GradientEnd));
        Assert.Equal((new MarkPoint(0.5, 1), new MarkPoint(0.5, 0)), (strips[2].GradientStart, strips[2].GradientEnd));
        Assert.Equal((new MarkPoint(0.5, 0), new MarkPoint(0.5, 1)), (strips[3].GradientStart, strips[3].GradientEnd));
    }

    [Fact]
    public void TinyDisplayDoesNotProduceNegativeStrips()
    {
        foreach (var strip in EdgeGlowLayout.Strips(6, 6))
        {
            Assert.True(strip.Border.Width >= 0 && strip.Glow.Width >= 0 && strip.Glow.Height >= 0 && strip.Border.Height >= 0);
        }
    }

    [Fact]
    public void FeedbackKeyframesFadeInHoldAndOut()
    {
        var frames = EdgeGlowLayout.Keyframes(GanderDesign.Motion.Glow, true);
        Assert.Equal(4, frames.Count);
        Assert.Equal((0.0, 0.0), frames[0]);
        Assert.Equal(0.1, frames[1].Seconds, 9);
        Assert.Equal(1.0, frames[1].Opacity);
        Assert.Equal(0.15, frames[2].Seconds, 9);
        Assert.Equal(1.0, frames[2].Opacity);
        Assert.Equal(0.35, frames[3].Seconds, 9);
        Assert.Equal(0.0, frames[3].Opacity);
        Assert.Equal(0.35, EdgeGlowLayout.HideAfter(GanderDesign.Motion.Glow, true), 9);
    }

    [Fact]
    public void ReducedMotionShowsPlainlyThenHides()
    {
        var frames = EdgeGlowLayout.Keyframes(GanderDesign.Motion.Glow, false);
        Assert.Equal([(0.0, 1.0), (0.35, 1.0)], frames.ToArray());
        Assert.Equal(1.3, EdgeGlowLayout.HideAfter(GanderDesign.Motion.Notice, false), 9);
        Assert.Equal(1.45, EdgeGlowLayout.HideAfter(GanderDesign.Motion.Notice, true), 9);
    }

    // ----- Settings -----

    [Fact]
    public void SettingsSectionsFollowTheMacOrderWithAboutLast() =>
        Assert.Equal(
            ["Appearance", "General", "Switching", "Focus Lock shortcut", "Camera", "Calibrations", "About"],
            SettingsWording.SectionOrder.ToArray());

    [Fact]
    public void SettingsWordsAreTheMacsInWindowsTerms()
    {
        Assert.Equal("Glow on the display that gets focus", SettingsWording.GlowToggle);
        Assert.Equal("Show a notice when the shortcut toggles Focus Lock", SettingsWording.NoticeToggle);
        Assert.Equal("Start with Windows", SettingsWording.StartWithWindows);
        Assert.Equal("How long you look at a display before Gander switches to it.", SettingsWording.HoldTimeHelp);
        Assert.StartsWith("How long Gander waits after switching", SettingsWording.PauseHelp, StringComparison.Ordinal);
        Assert.Equal("A stricter setting needs a clearer look at one display before Gander switches.", SettingsWording.ConfidenceHelp);
        Assert.Equal("Works from any app. A shortcut needs Ctrl, Alt, or Win. Esc cancels recording.", SettingsWording.ShortcutHelp);
        Assert.DoesNotContain("Command", SettingsWording.ShortcutHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("Option", SettingsWording.ShortcutHelp, StringComparison.Ordinal);
        Assert.Equal("Match Windows", AccentChoices.Title(AccentChoice.System));
        Assert.Equal("Gander green", AccentChoices.Title(AccentChoice.Gander));
        Assert.Equal("Reset to Ctrl+Alt+Shift+L", SettingsWording.ResetTo(HotKeyShortcut.FocusLockDefault));
        Assert.Equal("Hold time 0.10 s and pause 0.30 s. Switch confidence stays as it is.", SettingsWording.InstantHelp);
    }

    [Fact]
    public void ConfidenceNamesRunFromRelaxedToStrict()
    {
        Assert.Equal(["Relaxed", "Easy", "Balanced", "Firm", "Strict"], SettingsWording.ConfidenceNames.ToArray());
        Assert.Equal(SwitchSensitivity.ConfidenceMargins.Count, SettingsWording.ConfidenceNames.Count);
        Assert.Equal("Relaxed", SettingsWording.ConfidenceName(-3));
        Assert.Equal("Strict", SettingsWording.ConfidenceName(99));
        Assert.Equal("Balanced", SettingsWording.ConfidenceName(SwitchSensitivity.Default.ConfidenceLevel));
    }

    [Fact]
    public void NoticeTextIsTheMacs()
    {
        Assert.Equal("Focus Lock on", SettingsWording.NoticeText(true));
        Assert.Equal("Focus Lock off", SettingsWording.NoticeText(false));
    }

    [Fact]
    public void AccentChoiceRoundTripsAndDefaultsToGanderGreen()
    {
        Assert.Equal(AccentChoice.Gander, AccentChoices.Parse(null));
        Assert.Equal(AccentChoice.Gander, AccentChoices.Parse("garbage"));
        Assert.Equal(AccentChoice.System, AccentChoices.Parse(AccentChoices.Store(AccentChoice.System)));
        Assert.Equal(AccentChoice.Gander, AccentChoices.Parse(AccentChoices.Store(AccentChoice.Gander)));
        Assert.Equal("gander.accentChoice", AccentChoices.Key);
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value) => Values[key] = value;

        public void Remove(string key) => Values.Remove(key);
    }

    [Fact]
    public void GlowAndNoticeAreOnByDefaultLikeTheMac()
    {
        var prefs = new AppPreferences(new MemoryStore());
        Assert.True(prefs.EdgeGlowEnabled);
        Assert.True(prefs.FocusLockNoticeEnabled);
        Assert.Equal(AccentChoice.Gander, prefs.Accent);
        Assert.True(AppPreferences.ParseFlag("unreadable"));
        Assert.False(AppPreferences.ParseFlag("false"));
    }

    [Fact]
    public void PreferencesSaveUnderTheMacKeysAndRaiseChanged()
    {
        var store = new MemoryStore();
        var prefs = new AppPreferences(store);
        var changes = 0;
        prefs.Changed += () => changes++;
        prefs.SetEdgeGlow(false);
        prefs.SetEdgeGlow(false);
        prefs.SetFocusLockNotice(false);
        prefs.SetAccent(AccentChoice.System);
        Assert.Equal(3, changes);
        Assert.Equal("false", store.Values["gander.edgeGlowEnabled"]);
        Assert.Equal("false", store.Values["gander.focusLockNoticeEnabled"]);
        Assert.Equal("system", store.Values["gander.accentChoice"]);

        var again = new AppPreferences(store);
        Assert.False(again.EdgeGlowEnabled);
        Assert.False(again.FocusLockNoticeEnabled);
        Assert.Equal(AccentChoice.System, again.Accent);
    }

    [Fact]
    public void CalibrationRowsListEveryProfileAndMarkTheOneInUse()
    {
        // The library is empty here: rows come out empty and nothing throws.
        var rows = SettingsWording.Rows(new CalibrationLibrary(), null, id => id.RawValue);
        Assert.Empty(rows);
        Assert.Equal("No calibrations yet.", SettingsWording.NoCalibrations);
        var row = new CalibrationRow(new CalibrationKey([new DisplayId("a")], "Cam"), "Left, Right", "Cam", new DateTimeOffset(2026, 10, 12, 14, 30, 0, TimeSpan.Zero), true);
        Assert.StartsWith("Cam · ", row.Detail, StringComparison.Ordinal);
        Assert.Contains("2026", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FromColorizationReadsTheWindowsAccent() =>
        Assert.Equal(new Rgb(0x11, 0x22, 0x33), Gander.App.Hosting.AppTheme.FromColorization(unchecked((int)0xC4112233)));
}
