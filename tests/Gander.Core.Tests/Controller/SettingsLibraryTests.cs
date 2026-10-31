using System.Text.Json;
using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>
/// Port of the controller-side parts of the Mac AppModelSettingsTests: the sensitivity sliders and the calibration list.
/// The login item, shortcut recording, label formatting and list presentation tests are UI and are not ported.
/// </summary>
public class SettingsLibraryTests
{
    private static Harness Three()
    {
        var settings = new FakeSettingsStore();
        Harness.SeedLibrary(
            settings,
            Fixtures.Profile(),
            Fixtures.Profile(cameraName: Fixtures.ExternalCamera.Name),
            Fixtures.Profile(cameraName: "Other USB"));
        return Harness.Make(settingsStore: settings);
    }

    // MARK: Sensitivity sliders

    [Fact]
    public void ChangingTheHoldTimeSavesItAndRebuildsTheDeciderAtOnce()
    {
        using var h = Harness.Make(calibrated: true);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.SetHoldTime(0.8);

        Assert.Equal(new SwitchSensitivity(0.8, 2), h.Controller.Sensitivity);
        Assert.Equal(recorders + 1, h.Engine.Recorders.Count);
        Assert.Equal(0.8, h.Engine.LastRecorder!.Settings.Dwell);
        Assert.Equal(SwitchSensitivity.Default.ToSwitchSettings().EnterMargin, h.Engine.LastRecorder.Settings.EnterMargin);
        using var relaunched = h.Relaunch();
        Assert.Equal(new SwitchSensitivity(0.8, 2), relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void TheHoldTimeSnapsToTheSlidersSteps()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetHoldTime(0.62);
        Assert.True(Math.Abs(h.Controller.Sensitivity.Dwell - 0.6) < 1e-9);
    }

    [Fact]
    public void ChangingTheConfidenceMapsToTheEnterAndStayMargins()
    {
        using var h = Harness.Make(calibrated: true);
        var levels = new[] { (0, 0.15), (1, 0.225), (2, 0.30), (3, 0.375), (4, 0.45) };
        foreach (var (level, enter) in levels)
        {
            h.Controller.SetSwitchConfidence(level);
            var settings = h.Engine.LastRecorder!.Settings;
            Assert.Equal(enter, settings.EnterMargin);
            Assert.Equal(enter / 2, settings.StayMargin);
            Assert.Equal(0.4, settings.Dwell); // The hold time is untouched.
        }

        Assert.Equal(4, h.Controller.Sensitivity.ConfidenceLevel);
    }

    [Fact]
    public void AValueOutsideTheRangeIsClamped()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetSwitchConfidence(12);
        h.Controller.SetHoldTime(30);
        Assert.Equal(new SwitchSensitivity(1.5, 4), h.Controller.Sensitivity);

        h.Controller.SetHoldTime(-1);
        h.Controller.SetCooldown(30);
        Assert.Equal(new SwitchSensitivity(0.05, 1.5, 4), h.Controller.Sensitivity);

        h.Controller.SetCooldown(-30);
        Assert.Equal(0.2, h.Controller.Sensitivity.Cooldown);
    }

    [Fact]
    public void ChangingThePauseSavesItAndRebuildsTheDeciderAtOnce()
    {
        using var h = Harness.Make(calibrated: true);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.SetCooldown(0.5);

        Assert.Equal(new SwitchSensitivity(0.4, 0.5, 2), h.Controller.Sensitivity);
        Assert.Equal(recorders + 1, h.Engine.Recorders.Count);
        var settings = h.Engine.LastRecorder!.Settings;
        Assert.Equal(0.5, settings.Cooldown);
        Assert.Equal(0.4, settings.Dwell);
        using var saved = JsonDocument.Parse(h.Settings.Values[SettingsKeys.SwitchSensitivity]);
        Assert.Equal(0.5, saved.RootElement.GetProperty("cooldown").GetDouble());
        using var relaunched = h.Relaunch();
        Assert.Equal(new SwitchSensitivity(0.4, 0.5, 2), relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void ThePauseSnapsToTheSlidersSteps()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetCooldown(0.62);
        Assert.Equal(0.6, h.Controller.Sensitivity.Cooldown, 9);
        h.Controller.SetCooldown(0.3);
        Assert.Equal(0.3, h.Controller.Sensitivity.Cooldown, 9);
    }

    [Fact]
    public void EachSliderLeavesTheOtherTwoAlone()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetSensitivity(new SwitchSensitivity(0.6, 1.1, 3));

        h.Controller.SetHoldTime(0.9);
        Assert.Equal(new SwitchSensitivity(0.9, 1.1, 3), h.Controller.Sensitivity);
        h.Controller.SetCooldown(0.45);
        Assert.Equal(new SwitchSensitivity(0.9, 0.45, 3), h.Controller.Sensitivity);
        h.Controller.SetSwitchConfidence(0);
        Assert.Equal(new SwitchSensitivity(0.9, 0.45, 0), h.Controller.Sensitivity);

        var settings = h.Engine.LastRecorder!.Settings;
        Assert.Equal(0.9, settings.Dwell);
        Assert.Equal(0.45, settings.Cooldown);
        Assert.Equal(0.15, settings.EnterMargin);
    }

    [Fact]
    public async Task ChangingThePauseResetsTheDeciderAndTheLoopAtOnce()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Engine.Script.Rule = (_, _) => Fixtures.Decision(phase: SwitchPhase.Dwelling(Fixtures.RightId, 0.5));
        h.FeedBurst(0.1, 0.2);
        Assert.NotNull(h.Controller.LastDecision);
        var before = h.Engine.Recorders.Count;

        h.Controller.SetCooldown(1.2);

        Assert.Equal(before + 1, h.Engine.Recorders.Count); // A new decider, so a cooldown in progress ends.
        Assert.Null(h.Controller.LastDecision);
    }

    [Fact]
    public void TheInstantButtonSetsTheShortHoldAndPauseAndKeepsTheConfidence()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetSwitchConfidence(4);
        h.Controller.SetHoldTime(1.0);
        h.Controller.SetCooldown(1.0);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.UseInstantTiming();

        Assert.Equal(new SwitchSensitivity(0.10, 0.30, 4), h.Controller.Sensitivity);
        var settings = h.Engine.LastRecorder!.Settings;
        Assert.Equal(recorders + 1, h.Engine.Recorders.Count);
        Assert.Equal(0.10, settings.Dwell);
        Assert.Equal(0.30, settings.Cooldown);
        Assert.Equal(0.45, settings.EnterMargin);
        using var relaunched = h.Relaunch();
        Assert.Equal(new SwitchSensitivity(0.10, 0.30, 4), relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void TheInstantButtonFromTheDefaultsIsThePreset()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.UseInstantTiming();
        Assert.Equal(SwitchSensitivity.Instant, h.Controller.Sensitivity);
    }

    [Fact]
    public void TheInstantButtonAgainRebuildsNothing()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.UseInstantTiming();
        var recorders = h.Engine.Recorders.Count;

        h.Controller.UseInstantTiming();

        Assert.Equal(recorders, h.Engine.Recorders.Count);
    }

    [Fact]
    public void DraggingTheSlidersToTheInstantValuesGivesTheSameSettingsAsTheButton()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetHoldTime(0.1);
        h.Controller.SetCooldown(0.3);
        Assert.Equal(SwitchSensitivity.Instant, h.Controller.Sensitivity);
    }

    [Fact]
    public async Task ChangingTheTimingDoesNotMakeTheSettingsChangeAtTheFrameRate()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Controller.UseInstantTiming();
        var recorders = h.Engine.Recorders.Count;

        for (var i = 1; i <= 30; i++)
        {
            h.Feed(i * 0.07);
        }

        Assert.Equal(recorders, h.Engine.Recorders.Count); // Only the user's own change touches the settings.
    }

    [Fact]
    public void SettingTheSameValueRebuildsNothing()
    {
        using var h = Harness.Make(calibrated: true);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.SetHoldTime(0.4);
        h.Controller.SetSwitchConfidence(2);

        Assert.Equal(recorders, h.Engine.Recorders.Count);
        Assert.False(h.Settings.Values.ContainsKey(SettingsKeys.SwitchSensitivity));
    }

    [Fact]
    public void RestoreDefaultsPutsAllThreeBackAndRebuildsTheDecider()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetHoldTime(1.2);
        h.Controller.SetCooldown(0.5);
        h.Controller.SetSwitchConfidence(4);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.RestoreSensitivityDefaults();

        Assert.Equal(SwitchSensitivity.Default, h.Controller.Sensitivity);
        Assert.Equal(recorders + 1, h.Engine.Recorders.Count);
        Assert.Equal(SwitchSettings.Default, h.Engine.LastRecorder!.Settings);
        using var relaunched = h.Relaunch();
        Assert.Equal(SwitchSensitivity.Default, relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void RestoreDefaultsUndoesTheInstantButton()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.UseInstantTiming();
        h.Controller.RestoreSensitivityDefaults();
        Assert.Equal(new SwitchSensitivity(0.40, 0.75, 2), h.Controller.Sensitivity);
    }

    // MARK: Calibration library

    [Fact]
    public void NoCalibrationsGivesAnEmptyLibrary()
    {
        using var h = Harness.Make();
        Assert.Empty(h.Controller.Library.Profiles);
        Assert.Null(h.Controller.ActiveCalibration);
    }

    [Fact]
    public void DeletingTheOneInUseRemovesItAndTrackingHasNoCalibration()
    {
        using var h = Three();
        var key = Fixtures.Profile().Key;
        Assert.NotNull(h.Controller.ActiveCalibration);

        h.Controller.RemoveFromLibrary(key);

        Assert.Equal(2, h.Controller.Library.Profiles.Count);
        Assert.Null(h.Controller.ActiveCalibration);
        Assert.Equal(2, h.StoredLibrary()!.Profiles.Count);
        Assert.DoesNotContain(h.Controller.Library.Profiles, p => p.Key == key);
    }

    [Fact]
    public void DeletingAnotherRowKeepsTheActiveCalibration()
    {
        using var h = Three();
        var external = Fixtures.Profile(cameraName: Fixtures.ExternalCamera.Name).Key;

        h.Controller.RemoveFromLibrary(external);

        Assert.Equal(2, h.Controller.Library.Profiles.Count);
        Assert.Equal(Fixtures.Profile().Key, h.Controller.ActiveCalibration!.Saved.Key);
    }

    [Fact]
    public async Task DeletingTheActiveCalibrationWhileTrackingStopsTheCamera()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);

        h.Controller.RemoveFromLibrary(Fixtures.Profile().Key);
        h.Drain();

        Assert.False(h.Controller.IsCameraActive);
        Assert.Equal(TrackingStatusKind.NeedsCalibration, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public async Task CalibratingAgainBringsADeletedCalibrationBack()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.RemoveFromLibrary(Fixtures.Profile().Key);
        Assert.Empty(h.Controller.Library.Profiles);

        await h.Controller.SetCalibrationRunningAsync(true);
        h.Controller.SaveToLibrary(Fixtures.Profile());
        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();

        Assert.Single(h.Controller.Library.Profiles);
        Assert.NotNull(h.Controller.ActiveCalibration);
    }

    [Fact]
    public void DeletingAKeyThatIsNotThereChangesNothing()
    {
        using var h = Harness.Make(calibrated: true);
        var before = h.Controller.Library;
        var changes = h.StateChangedCount;

        h.Controller.RemoveFromLibrary(new CalibrationKey([Fixtures.FarId, Fixtures.LeftId], "x"));

        Assert.Equal(before, h.Controller.Library);
        Assert.Equal(changes, h.StateChangedCount);
    }

    [Fact]
    public void ACalibrationThatDoesNotFitTheConnectedDisplaysIsNotActive()
    {
        var settings = new FakeSettingsStore();
        Harness.SeedLibrary(settings, Fixtures.Profile(new DisplayLayout([Fixtures.Far, Fixtures.Extra])));

        using var h = Harness.Make(settingsStore: settings);

        Assert.Single(h.Controller.Library.Profiles);
        Assert.Null(h.Controller.ActiveCalibration);
    }
}
