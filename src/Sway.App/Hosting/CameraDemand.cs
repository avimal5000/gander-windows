using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sway.App.Hosting;

/// <summary>
/// Shares the camera between the debug windows (Pose Debug, Gate Test). The controller has one switch for "a window needs
/// the camera and switching must wait" (<c>SetCalibrationRunningAsync</c>), so this keeps a list of who wants it and turns the
/// switch on for the first owner and off after the last. The Pose Debug window does not need the hold while Tracking
/// already runs the camera; the Gate Test always does, so no switch can happen while the user looks around. When another
/// part of the app (the Set up window) already holds the camera, nothing is taken and nothing is released.
/// </summary>
public sealed class CameraDemand
{
    private readonly Func<bool, Task> _setHold;
    private readonly Func<bool> _trackingWantsCamera;
    private readonly Func<bool> _someoneElseHolds;
    private readonly Dictionary<string, bool> _owners = [];
    private bool _weHold;
    private bool _busy;
    private bool _again;

    /// <summary>Creates the shared demand.</summary>
    /// <param name="setHold">Turns the controller's hold on or off.</param>
    /// <param name="trackingWantsCamera">True when Tracking is on with a calibration that fits, so the camera runs anyway.</param>
    /// <param name="someoneElseHolds">True when the hold is on and it is not ours.</param>
    public CameraDemand(Func<bool, Task> setHold, Func<bool> trackingWantsCamera, Func<bool> someoneElseHolds)
    {
        _setHold = setHold;
        _trackingWantsCamera = trackingWantsCamera;
        _someoneElseHolds = someoneElseHolds;
    }

    /// <summary>True while this object holds the camera.</summary>
    public bool Holding => _weHold;

    /// <summary>How many owners want the camera.</summary>
    public int OwnerCount => _owners.Count;

    /// <summary>Adds an owner. <paramref name="exclusive"/> asks for the hold even while Tracking runs the camera. Completes when the controller has acted.</summary>
    public Task AcquireAsync(string owner, bool exclusive = false)
    {
        _owners[owner] = exclusive;
        return ReevaluateAsync();
    }

    /// <summary>Removes an owner. Completes when the controller has acted.</summary>
    public Task ReleaseAsync(string owner)
    {
        _owners.Remove(owner);
        return ReevaluateAsync();
    }

    /// <summary>Checks the wish again after the state changed (Tracking turned on or off, a calibration appeared).</summary>
    public async Task ReevaluateAsync()
    {
        if (_busy)
        {
            _again = true;
            return;
        }

        _busy = true;
        try
        {
            do
            {
                _again = false;
                var wanted = Wanted();
                if (wanted && !_weHold)
                {
                    _weHold = true;
                    await _setHold(true);
                }
                else if (!wanted && _weHold)
                {
                    _weHold = false;
                    await _setHold(false);
                }
            }
            while (_again);
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Wanted()
    {
        if (_owners.Count == 0)
        {
            return false;
        }

        if (!_weHold && _someoneElseHolds())
        {
            return false;
        }

        return _owners.ContainsValue(true) || !_trackingWantsCamera();
    }
}
