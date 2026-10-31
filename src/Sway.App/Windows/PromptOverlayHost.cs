using System;
using System.Collections.Generic;
using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Calibration;

namespace Sway.App.Windows;

/// <summary>
/// Shows the "Look here" marker on the monitor the calibration asks about, and nowhere else. One overlay window per
/// monitor, reused; all hidden when no look is in progress.
/// </summary>
public sealed class PromptOverlayHost : IDisposable
{
    private readonly CalibrationSession? _calibration;
    private readonly Dictionary<DisplayId, PromptOverlayWindow> _windows = [];

    /// <summary>
    /// Starts following <paramref name="calibration"/>. With none, the owner calls <see cref="Show"/> itself (the Gate Test
    /// window does).
    /// </summary>
    public PromptOverlayHost(CalibrationSession? calibration = null)
    {
        _calibration = calibration;
        if (_calibration is not null)
        {
            _calibration.Changed += Update;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_calibration is not null)
        {
            _calibration.Changed -= Update;
        }

        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void Update() =>
        Show(_calibration is { State: CalibrationState.Running } calibration ? calibration.Progress : null);

    /// <summary>Shows the marker for <paramref name="progress"/> on its monitor and hides every other; null hides all.</summary>
    public void Show(GateProgress? progress)
    {
        foreach (var (id, window) in _windows)
        {
            if (progress is null || id != progress.Display.Id)
            {
                window.Hide();
            }
        }

        if (progress is null)
        {
            return;
        }

        var display = progress.Display;
        if (!_windows.TryGetValue(display.Id, out var overlay))
        {
            overlay = new PromptOverlayWindow();
            _windows[display.Id] = overlay;
        }

        overlay.Place(display.Frame.X, display.Frame.Y, display.Frame.Width, display.Frame.Height);
        overlay.Update(progress.StepNumber, PromptOverlayMath.Opacity(progress.Phase, progress.PhaseElapsed, progress.PhaseDuration));
    }
}
