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
    private readonly CalibrationSession _calibration;
    private readonly Dictionary<DisplayId, PromptOverlayWindow> _windows = [];

    /// <summary>Starts following <paramref name="calibration"/>.</summary>
    public PromptOverlayHost(CalibrationSession calibration)
    {
        _calibration = calibration;
        _calibration.Changed += Update;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _calibration.Changed -= Update;
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void Update()
    {
        var progress = _calibration.State == CalibrationState.Running ? _calibration.Progress : null;
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
