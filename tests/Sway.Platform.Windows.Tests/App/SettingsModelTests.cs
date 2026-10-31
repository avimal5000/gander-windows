using System;
using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Tests.App;

public sealed class SettingsModelTests
{
    [Fact]
    public void PresetsAreOrderedAndInsideTheAllowedRanges()
    {
        Assert.Equal(["Slow", "Default", "Quick", "Instant"], TimingPresets.All.Select(p => p.Name));
        Assert.True(TimingPresets.All.Zip(TimingPresets.All.Skip(1)).All(p => p.First.Dwell > p.Second.Dwell && p.First.Cooldown > p.Second.Cooldown));
        foreach (var preset in TimingPresets.All)
        {
            var clamped = new SwitchSensitivity(preset.Dwell, preset.Cooldown, 2);
            Assert.Equal(preset.Dwell, clamped.Dwell);
            Assert.Equal(preset.Cooldown, clamped.Cooldown);
        }
    }

    [Fact]
    public void DefaultAndInstantPresetsComeFromTheCore()
    {
        Assert.Equal("Default", TimingPresets.Match(SwitchSensitivity.Default)?.Name);
        Assert.Equal("Instant", TimingPresets.Match(SwitchSensitivity.Instant)?.Name);
        Assert.Null(TimingPresets.Match(new SwitchSensitivity(0.55, 0.9, 2)));
    }

    [Fact]
    public void ApplyingAPresetKeepsTheConfidence()
    {
        var applied = TimingPresets.Apply(TimingPresets.All[0], new SwitchSensitivity(0.4, 0.75, 4));
        Assert.Equal(new SwitchSensitivity(0.70, 1.00, 4), applied);
        Assert.Equal("Slow", TimingPresets.Match(applied)?.Name);
    }

    [Theory]
    [InlineData(0.42, 0.4)]
    [InlineData(0.43, 0.45)]
    [InlineData(1.0, 1.0)]
    public void SnapUsesTheFiftyMillisecondGrid(double input, double expected) =>
        Assert.Equal(expected, TimingPresets.Snap(input));

    [Fact]
    public void SecondsAreFormattedWithTwoDecimals() => Assert.Equal("0.40 s", TimingPresets.Seconds(0.4));

    [Fact]
    public void ModifierKeysAloneKeepWaiting()
    {
        Assert.Equal(ShortcutCaptureKind.Waiting, ShortcutCapture.From(0x11, HotKeyModifiers.Control).Kind);
        Assert.Equal(ShortcutCaptureKind.Waiting, ShortcutCapture.From(0x5B, HotKeyModifiers.Win).Kind);
    }

    [Fact]
    public void ALetterWithModifiersIsAccepted()
    {
        var result = ShortcutCapture.From(0x4C, HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift);
        Assert.Equal(ShortcutCaptureKind.Accepted, result.Kind);
        Assert.Equal(HotKeyShortcut.FocusLockDefault, result.Shortcut);
    }

    [Fact]
    public void ShiftAloneIsNotEnough()
    {
        var result = ShortcutCapture.From(0x4C, HotKeyModifiers.Shift);
        Assert.Equal(ShortcutCaptureKind.NeedsModifier, result.Kind);
        Assert.Null(result.Shortcut);
        Assert.Equal(ShortcutCaptureKind.NeedsModifier, ShortcutCapture.From(0x4C, HotKeyModifiers.None).Kind);
    }

    [Theory]
    [InlineData(0x1B)]
    [InlineData(0x09)]
    [InlineData(0x0D)]
    [InlineData(0x08)]
    public void KeysThatCannotBeShortcutsAreRefused(uint key) =>
        Assert.Equal(ShortcutCaptureKind.Unsupported, ShortcutCapture.From(key, HotKeyModifiers.Control).Kind);

    [Theory]
    [InlineData(0x41u, "A")]
    [InlineData(0x35u, "5")]
    [InlineData(0x70u, "F1")]
    [InlineData(0x7Bu, "F12")]
    [InlineData(0x20u, "Space")]
    [InlineData(0x25u, "Left")]
    public void KeyLabelsAreReadable(uint key, string label) => Assert.Equal(label, ShortcutCapture.KeyLabel(key));

    [Fact]
    public void CalibrationStatusCoversEachState()
    {
        var day = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Calibration is running.", SettingsText.Calibration(2, day, false, true));
        Assert.Contains("Not calibrated", SettingsText.Calibration(0, null, false, false));
        Assert.StartsWith("Calibrated for 3 displays on ", SettingsText.Calibration(3, day, false, false));
        Assert.Contains("not connected", SettingsText.Calibration(3, day, true, false));
    }

    [Fact]
    public void VersionDropsBuildMetadata()
    {
        Assert.Equal("Version 1.2.3", SettingsText.Version("1.2.3+abc123"));
        Assert.Equal("Version 1.0", SettingsText.Version("1.0"));
    }

    [Fact]
    public void ShortcutValueShowsNoneWhenUnset()
    {
        Assert.Equal("None", SettingsText.ShortcutValue(null));
        Assert.Equal("Ctrl+Alt+Shift+L", SettingsText.ShortcutValue(HotKeyShortcut.FocusLockDefault));
    }

    [Fact]
    public void MenuPaletteFollowsTheAppTheme()
    {
        Assert.Equal(Rgb.Hex("#F6F3EA"), MenuPalette.For(true).Background);
        Assert.Equal(Rgb.Hex("#2B2B2B"), MenuPalette.For(false).Background);
        Assert.Equal(TrayPalette.Forest, MenuPalette.For(true).Accent);
        Assert.Equal(TrayPalette.Sage, MenuPalette.For(false).Accent);
    }
}
