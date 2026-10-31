using System.Globalization;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Controller;

// The automatic tracking loop (SPEC F16, D28, D29): every pose sample goes through the switch decider; when it fires,
// Gander focuses the last-used window on that display. TrackingLoop holds the pure bookkeeping; this file feeds it and
// does the I/O. The focused display is polled once a second and on every foreground change, and only while the camera
// runs for tracking.
public sealed partial class TrackingController
{
    /// <summary>Most switches kept in the log.</summary>
    public const int SwitchLogLimit = 10;

    private ISwitchDecider? _decider;
    private IReadOnlySet<DisplayId>? _trackedTargets;
    private double _enterMargin;
    private bool _isPollingFocus;
    private IScheduledAction? _pollTimer;
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _switchCts;
    private (DisplayId Display, double Timestamp)? _dwellStart;
    private int _nextLogId;

    // Repeat guard. When Windows (or the app that had focus) hands focus straight back after a switch that verified,
    // the poll shows the old display again and the look is still held, so the decider would ask again and again for the
    // same window. A repeat request for the display Gander last focused waits 2 s, then 4 s, 8 s, up to 10 s.
    private const double RepeatWaitBase = 2;
    private const double RepeatWaitCap = 10;
    private const double RepeatForgetAfter = 30;
    private (DisplayId Display, double At, int Repeats)? _lastFocused;
    private bool _repeatLogged;
    private bool _takenBackLogged;

    /// <summary>
    /// The Tracking switch. The choice is saved, so the next launch restores it. Completes when the camera has been
    /// started or stopped to match.
    /// </summary>
    public async Task SetTrackingEnabledAsync(bool enabled)
    {
        if (enabled == TrackingEnabled)
        {
            return;
        }

        _env.Settings.Write(SettingsKeys.TrackingEnabled, enabled ? "true" : "false");
        TrackingEnabled = enabled;
        if (enabled)
        {
            _diagnostics.ResetCounts();
        }

        // The user's own switch ends a stop for a lost camera: the normal rules apply from here.
        LostCameraId = null;
        ResetTracking();
        SyncTrackingActivity();
        Publish();
        await ReconcileCamera();
        Publish();
    }

    /// <summary>Makes a fresh decider for the active calibration and clears the loop.</summary>
    private void RebuildDecider()
    {
        var settings = Sensitivity.ToSwitchSettings();
        _enterMargin = settings.EnterMargin;
        _decider = ActiveCalibration is { } match ? _env.Engine.MakeDecider(match.Profile, settings) : null;
        UpdateTrackedTargets();
        ClearTrackingState();
        if (_isPollingFocus)
        {
            PollFocusedDisplay();
        }
    }

    /// <summary>The first <see cref="GanderPolicy.MaxTrackedDisplays"/> calibrated displays of the layout (SPEC D22), or null for all.</summary>
    private void UpdateTrackedTargets() =>
        _trackedTargets = ActiveCalibration is { } match
            ? TrackingLoop.Targets(Layout, match.Profile, GanderPolicy.MaxTrackedDisplays)
            : null;

    /// <summary>Drops any dwell or cooldown and forgets the focus bookkeeping.</summary>
    private void ResetTracking()
    {
        _decider?.Reset();
        ClearTrackingState();
    }

    private void ClearTrackingState()
    {
        _loop.Reset();
        _lastFocused = null;
        _switchCts?.Cancel();
        _switchCts = null;
        CancelFocusPoll();
        _dwellStart = null;
        LastDecision = null;
    }

    /// <summary>Saves and applies new switching settings (SPEC D31, D49).</summary>
    public void SetSensitivity(SwitchSensitivity newValue)
    {
        var clamped = new SwitchSensitivity(newValue.Dwell, newValue.Cooldown, newValue.ConfidenceLevel);
        if (clamped == Sensitivity)
        {
            return;
        }

        Sensitivity = clamped;
        _env.Settings.Write(SettingsKeys.SwitchSensitivity, System.Text.Json.JsonSerializer.Serialize(clamped, EngineJson.Options));
        Log(SensitivityLogLine(clamped));
        RebuildDecider();
        Publish();
    }

    /// <summary>The Hold time slider: seconds on the 0.05 s grid, inside the allowed range.</summary>
    public void SetHoldTime(double seconds) => SetSensitivity(Sensitivity with { Dwell = Snapped(seconds) });

    /// <summary>The Pause after a switch slider: seconds on the 0.05 s grid, inside the allowed range (SPEC F35).</summary>
    public void SetCooldown(double seconds) => SetSensitivity(Sensitivity with { Cooldown = Snapped(seconds) });

    /// <summary>The Switch confidence slider: 0 (Relaxed) to 4 (Strict).</summary>
    public void SetSwitchConfidence(int level) => SetSensitivity(Sensitivity with { ConfidenceLevel = level });

    /// <summary>"Use Instant timing": the shortest hold time and pause the preset sets, keeping the confidence (SPEC D49).</summary>
    public void UseInstantTiming() => SetSensitivity(Sensitivity.ApplyingInstantTiming());

    /// <summary>"Restore Defaults": hold time, pause, and confidence.</summary>
    public void RestoreSensitivityDefaults() => SetSensitivity(SwitchSensitivity.Default);

    private static double Snapped(double seconds) => Math.Round(Math.Round(seconds / 0.05) * 0.05, 2);

    /// <summary>The enter margin of the current sensitivity, read per sample (for the tests and the Pose Debug view).</summary>
    public double EnterMargin => _enterMargin;

    private SwitchSensitivity StoredSensitivity()
    {
        if (_env.Settings.Read(SettingsKeys.SwitchSensitivity) is not { } json)
        {
            return SwitchSensitivity.Default;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<SwitchSensitivity>(json, EngineJson.Options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SwitchSensitivity.Default;
        }
    }

    internal static string SensitivityLogLine(SwitchSensitivity s) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Switching: hold {s.Dwell:F2} s, pause {s.Cooldown:F2} s, confidence level {s.ConfidenceLevel}");

    // MARK: Per sample

    /// <summary>
    /// Runs one tracked sample through the decider with the loop's focus and holds, and acts on its decision. Only a
    /// camera that reports running switches: a sample that arrives after the status left running is stale.
    /// </summary>
    private void ProcessTracking(TimedPose sample)
    {
        if (Status is not CaptureStatus.Running || !IsTrackingActive)
        {
            return;
        }

        // Gander's own request in flight is not a lock: a lock cancels the decider's cooldown, which must run from the
        // switch. The loop already reports the target as focused and refuses a second request.
        _diagnostics.RecordProcessed();
        var hold = TrackingHold;
        var context = new SwitchContext(
            _loop.FocusedDisplay, hold is not null && hold != Runtime.TrackingHold.Switching, _trackedTargets);
        if (_decider?.Process(sample, context) is not { } decision)
        {
            return;
        }

        TrackDwell(decision, sample.Timestamp);
        _loop.LookChanged(decision.Classification, _enterMargin);
        if (decision.SwitchTo is { } target)
        {
            RequestSwitch(target, sample.Timestamp);
        }

        LastDecision = decision;
    }

    // A dwell starts at progress 0, which also marks a dwell begun again on the same display without an idle phase in
    // between, so the latency counts from that sample.
    private void TrackDwell(SwitchDecision decision, double timestamp)
    {
        switch (decision.Phase)
        {
            case DwellingPhase dwelling:
                if (_dwellStart?.Display != dwelling.Display || dwelling.Progress == 0)
                {
                    _dwellStart = (dwelling.Display, timestamp);
                }

                break;
            case IdlePhase or LockedPhase:
                _dwellStart = null;
                break;
        }
    }

    // MARK: Focus requests

    private void RequestSwitch(DisplayId target, double timestamp)
    {
        // With a dwell of zero a switch fires without a dwelling phase ever being seen: no hold time then.
        var start = _dwellStart is { } d && d.Display == target ? d.Timestamp : timestamp;
        _dwellStart = null;
        var held = Math.Max(0, timestamp - start);
        _diagnostics.RecordRequest();
        if (TrackingHold is { } hold && hold != Runtime.TrackingHold.Switching)
        {
            _diagnostics.RecordSkippedByHold();
            return;
        }

        var wait = RepeatWait(target, _env.Clock());
        if (wait > 0)
        {
            if (!_repeatLogged)
            {
                _repeatLogged = true;
                Log(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Not asking again for {DisplayName(target)}: Gander focused it a moment ago and focus did not stay; waiting {wait:F1} s"));
            }

            return;
        }

        if (!_loop.BeginAttempt(target))
        {
            // The Switching hold is Gander's own request in flight, so it counts here and not as a hold.
            _diagnostics.RecordSkippedInFlight();
            return;
        }

        var epoch = _loop.Epoch;
        var layout = Layout;
        var cts = new CancellationTokenSource();
        _switchCts = cts;
        _ = RunSwitchAsync(target, layout, held, epoch, cts);
    }

    private async Task RunSwitchAsync(DisplayId target, DisplayLayout layout, double held, int epoch, CancellationTokenSource cts)
    {
        FocusResult result;
        try
        {
            result = await _env.Focus.FocusAsync(target, layout, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Not wanted any more (Focus Lock, a hold, a reset): nothing was focused, and no failure is recorded.
            Post(() => AbortSwitch(target, epoch, cts));
            return;
        }
        catch (Exception ex)
        {
            result = new FocusResult.Failed(null, ex.Message);
        }

        Post(() => FinishSwitch(target, result, held, epoch, cts));
    }

    /// <summary>A focus request was cancelled before it acted: frees the loop without recording an outcome.</summary>
    private void AbortSwitch(DisplayId target, int epoch, CancellationTokenSource cts)
    {
        if (ReferenceEquals(_switchCts, cts))
        {
            _switchCts = null;
        }

        if (epoch == _loop.Epoch && _loop.InFlight == target)
        {
            _loop.CancelAttempt();
        }
    }

    /// <summary>Tells a focus request that is queued or running not to act (it may still be waiting behind a slow window).</summary>
    private void CancelPendingSwitch() => _switchCts?.Cancel();

    /// <summary>A focus request ended. Ignored when tracking stopped or the loop was reset since it began.</summary>
    private void FinishSwitch(DisplayId target, FocusResult result, double held, int epoch, CancellationTokenSource cts)
    {
        if (epoch != _loop.Epoch || _loop.InFlight != target)
        {
            return;
        }

        if (ReferenceEquals(_switchCts, cts))
        {
            _switchCts = null;
        }

        _loop.FinishAttempt(target, result);
        _diagnostics.RecordResult(result);
        var display = DisplayName(target);
        var text = SwitchLogText(result, display, held);
        var entry = new SwitchLogEntry(++_nextLogId, _env.Now(), text);
        SwitchLog = SwitchLog.Prepend(entry).Take(SwitchLogLimit).ToList();
        LogSwitch(result, display);
        if (result is FocusResult.Focused)
        {
            NoteFocused(target);
            FocusSwitched?.Invoke(target);
        }
    }

    /// <summary>Seconds a request for <paramref name="target"/> must still wait because Gander already focused it recently; 0 for none.</summary>
    private double RepeatWait(DisplayId target, double now)
    {
        if (_lastFocused is not { } last || last.Display != target)
        {
            return 0;
        }

        var wait = Math.Min(RepeatWaitCap, RepeatWaitBase * Math.Pow(2, last.Repeats));
        return Math.Max(0, last.At + wait - now);
    }

    private void NoteFocused(DisplayId target)
    {
        var now = _env.Clock();
        var repeats = _lastFocused is { } last && last.Display == target && now - last.At < RepeatForgetAfter
            ? last.Repeats + 1
            : 0;
        _lastFocused = (target, now, repeats);
        _repeatLogged = false;
        _takenBackLogged = false;
    }

    // Evidence for the repeat guard: the first poll that finds focus elsewhere shortly after Gander focused a display.
    private void LogFocusTakenBack(DisplayId? polled)
    {
        if (_lastFocused is not { } last || _takenBackLogged || polled == last.Display)
        {
            return;
        }

        var age = _env.Clock() - last.At;
        if (age > RepeatWaitCap)
        {
            return;
        }

        _takenBackLogged = true;
        Log(string.Create(
            CultureInfo.InvariantCulture,
            $"Focus left {DisplayName(last.Display)} {age:F1} s after Gander focused it; the front window is now on {(polled is { } d ? DisplayName(d) : "no known display")}"));
    }

    /// <summary>A log line for a focus request's result. Names the app and the display, never a window title.</summary>
    public static string SwitchLogText(FocusResult result, string display, double held) => result switch
    {
        FocusResult.Focused f => string.Create(
            CultureInfo.InvariantCulture, $"Focused {f.Window.AppName} on {display} in {f.Milliseconds} ms, held {held:F2} s"),
        FocusResult.NoWindow => $"No window on {display}",
        FocusResult.Failed failed => $"Failed: {failed.Reason}",
        _ => string.Empty,
    };

    private void LogSwitch(FocusResult result, string display)
    {
        switch (result)
        {
            case FocusResult.Focused f:
                Log($"Focused {f.Window.AppName} on {display} via {f.Method} in {f.Milliseconds} ms");
                break;
            case FocusResult.NoWindow:
                Log($"No window on {display}");
                break;
            case FocusResult.Failed failed:
                Log($"Switch to {display} failed ({failed.Window?.AppName ?? "no app"}): {failed.Reason}");
                break;
        }
    }

    // MARK: Focused-display poll

    /// <summary>
    /// Starts or stops the focused-display poll to match whether the camera runs for tracking, and keeps the pose
    /// demand and the camera fallback in step with the state. Cheap to call.
    /// </summary>
    private void SyncTrackingActivity()
    {
        SyncFocusedDisplayPoll();
        SyncPoseDemand();
        SyncCameraFallback();
    }

    private void SyncFocusedDisplayPoll()
    {
        var shouldPoll = IsTrackingActive && Status is CaptureStatus.Running;
        if (shouldPoll == _isPollingFocus)
        {
            return;
        }

        _isPollingFocus = shouldPoll;
        if (shouldPoll)
        {
            ScheduleNextTick();
            TrackingTick();
        }
        else
        {
            _pollTimer?.Cancel();
            _pollTimer = null;
            CancelFocusPoll();
        }
    }

    private void ScheduleNextTick()
    {
        _pollTimer?.Cancel();
        _pollTimer = null;
        if (_env.FocusPollInterval is not { } interval)
        {
            return;
        }

        _pollTimer = _scheduler.Schedule(interval, () =>
        {
            _pollTimer = null;
            if (_isPollingFocus)
            {
                ScheduleNextTick();
                TrackingTick();
                MaybeLogDiagnostics();
            }
        });
    }

    // One log line every 5 s while tracking runs, so the log file alone shows why nothing switches.
    private const double DiagnosticsLogInterval = 5;
    private double _nextDiagnosticsLog;

    private void MaybeLogDiagnostics()
    {
        if (!IsTrackingActive || !IsCameraActive)
        {
            return;
        }

        var now = _env.Clock();
        if (now < _nextDiagnosticsLog)
        {
            return;
        }

        _nextDiagnosticsLog = now + DiagnosticsLogInterval;
        var inputs = HoldInputs();
        var hold = TrackingHold;
        var decision = LastDecision;
        var look = decision is null
            ? "no decision yet"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{decision.Classification.Kind} best {(decision.Classification.Best is { } best ? DisplayName(best) : "none")} margin {decision.Classification.Margin:F2}, phase {PhaseName(decision.Phase)}{(decision.Reason is { } reason ? $" ({reason})" : string.Empty)}");
        Log(
            $"Switch diagnostics: {_diagnostics.CountsLine()}; {look}; focus {(TrackingFocus is { } focus ? DisplayName(focus) : "unknown")}; "
            + $"hold {(hold is { } h ? h.ToString() : "none")} [lock {inputs.FocusLocked}, gander in front {inputs.GanderInFront}, displays changing {inputs.DisplaysChanging}, "
            + $"desktop changing {inputs.SpaceChanging}, switching {inputs.Switching}]");
    }

    /// <summary>The phase in words: Idle, Dwelling (progress), Cooling, or Locked.</summary>
    public static string PhaseName(SwitchPhase phase) => phase switch
    {
        DwellingPhase d => string.Create(CultureInfo.InvariantCulture, $"Dwelling({d.Progress:F2})"),
        CoolingDownPhase => "Cooling",
        LockedPhase => "Locked",
        _ => "Idle",
    };

    /// <summary>One poll step: ask where focus is, unless the last question is still running. Does nothing unless tracking runs.</summary>
    public void TrackingTick()
    {
        if (_isPollingFocus)
        {
            PollFocusedDisplay();
        }
    }

    /// <summary>The foreground window changed (the platform's foreground hook): ask where focus is now.</summary>
    public void ForegroundChanged()
    {
        PollFocusedDisplay();
        Publish();
    }

    private void CancelFocusPoll()
    {
        _pollCts?.Cancel();
        _pollCts = null;
    }

    /// <summary>
    /// Asks which display holds the focused window. An answer is ignored if a request began or ended, or the loop was
    /// reset, while the question was running.
    /// </summary>
    private void PollFocusedDisplay()
    {
        if (!_isPollingFocus || _pollCts is not null)
        {
            return;
        }

        var epoch = _loop.PollStarted();
        var layout = Layout;
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _ = RunPollAsync(layout, epoch, cts);
    }

    private async Task RunPollAsync(DisplayLayout layout, int epoch, CancellationTokenSource cts)
    {
        DisplayId? display;
        try
        {
            display = await _env.Focus.FocusedDisplayAsync(layout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            display = null;
        }

        Post(() =>
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _pollCts = null;
            _loop.PollFinished(display, epoch);
            if (_loop.Polled == display && _loop.Override is null && !_loop.IsFocusing)
            {
                LogFocusTakenBack(display);
            }
        });
    }
}
