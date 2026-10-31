using System.Text.Json;
using Sway.Core.Platform;

namespace Sway.Core.Controller;

// The calibration library (SPEC D24, D25): every saved calibration per display set and camera. The active calibration
// is the one that fits the connected displays (by layout fingerprint and display ids) and the camera; tracking waits
// when none does. Fitting from a trace belongs to the calibration UI (out of scope); a fitted profile is saved here.
public sealed partial class TrackingController
{
    /// <summary>Saves <paramref name="profile"/> as the newest in the library, persists it, and re-resolves the active calibration.</summary>
    public void SaveToLibrary(CalibrationProfile profile)
    {
        var updated = CloneLibrary();
        updated.Save(profile);
        if (updated.Equals(Library))
        {
            return;
        }

        Library = updated;
        PersistLibrary();
        RefreshActiveCalibration();
        SyncCameraFallback();
        Publish();
    }

    /// <summary>Removes the calibration saved under <paramref name="key"/>. Calibrating again restores it.</summary>
    public void RemoveFromLibrary(CalibrationKey key)
    {
        var updated = CloneLibrary();
        updated.Remove(key);
        if (updated.Equals(Library))
        {
            return;
        }

        Library = updated;
        PersistLibrary();
        RefreshActiveCalibration();
        SyncCameraFallback();
        Publish();
    }

    private CalibrationLibrary CloneLibrary() => new(Library.Profiles);

    /// <summary>
    /// The name of the camera the calibration is looked up for: the running camera, else the one the session is waiting
    /// for or has been moved to (SPEC D46), else the one capture would pick. While the chosen camera is unplugged,
    /// capture's own rule would name the built-in camera, but the session still belongs to the chosen camera.
    /// </summary>
    private string? TrackingCameraName
    {
        get
        {
            if ((Status as CaptureStatus.Running)?.Device?.Name is { } running)
            {
                return running;
            }

            // Also while the source restarts for it: no stream then, but the stand-in is still the camera in use.
            if (FallbackCamera is { } fallback)
            {
                return fallback.Name;
            }

            return UnpluggedChosenCamera?.Name ?? PredictedCameraName;
        }
    }

    /// <summary>Re-asks the camera catalog which device capture would open, assigning only on a change.</summary>
    private void RefreshPredictedCamera()
    {
        var name = _env.Cameras.Resolve(SelectedCameraId)?.Name;
        if (name != PredictedCameraName)
        {
            PredictedCameraName = name;
        }
    }

    /// <summary>
    /// Re-resolves the active calibration for the current layout, camera, and library. When it changes, the decider
    /// is rebuilt, and the camera is reconciled if a calibration appeared or disappeared while Tracking is on.
    /// </summary>
    /// <param name="reconcile">False at launch, where the launch reconcile follows anyway.</param>
    /// <returns>True if the active calibration changed.</returns>
    private bool RefreshActiveCalibration(bool reconcile = true)
    {
        var resolved = Library.Resolve(Layout, TrackingCameraName);
        if (Equals(resolved, ActiveCalibration))
        {
            return false;
        }

        var appearedOrDisappeared = (resolved is null) != (ActiveCalibration is null);
        ActiveCalibration = resolved;
        RebuildDecider();
        SyncTrackingActivity();
        if (reconcile && TrackingEnabled && appearedOrDisappeared)
        {
            _ = ReconcileCamera();
        }

        return true;
    }

    private void PersistLibrary()
    {
        try
        {
            _env.Settings.Write(SettingsKeys.CalibrationLibrary, JsonSerializer.Serialize(Library, EngineJson.Options));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Log("Saving the calibrations failed");
        }
    }

    /// <summary>The stored library; data that does not decode, or has another format version, gives an empty library.</summary>
    private CalibrationLibrary StoredLibrary()
    {
        if (_env.Settings.Read(SettingsKeys.CalibrationLibrary) is not { } json)
        {
            return new CalibrationLibrary();
        }

        try
        {
            return JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options) ?? new CalibrationLibrary();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Corrupt or hostile data (including JSON null in a required field) is treated as no library.
            return new CalibrationLibrary();
        }
    }
}
