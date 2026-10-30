using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Controller;

/// <summary>The cases of <see cref="TrackingStatus"/>, in priority order (the first that matches wins).</summary>
public enum TrackingStatusKind
{
    /// <summary>Tracking is on but the camera permission is denied or restricted.</summary>
    CameraBlocked,

    /// <summary>The camera reported a failure; shown until the next start.</summary>
    CameraFailed,

    /// <summary>Tracking is off and no calibration runs.</summary>
    Paused,

    /// <summary>A calibration runs.</summary>
    Calibrating,

    /// <summary>Fewer than two displays are connected.</summary>
    OneDisplay,

    /// <summary>No saved calibration fits the connected displays and camera.</summary>
    NeedsCalibration,

    /// <summary>Sleep, displays asleep, the screen is locked, or another user's session is active.</summary>
    Suspended,

    /// <summary>The camera is starting.</summary>
    Starting,

    /// <summary>The camera is interrupted; carries the interruption text.</summary>
    CameraBusy,

    /// <summary>The chosen camera is unplugged; carries its name.</summary>
    CameraDisconnected,

    /// <summary>Focus Lock is on.</summary>
    FocusLock,

    /// <summary>Tracking, and a face is seen.</summary>
    Tracking,

    /// <summary>Tracking, but no face is seen.</summary>
    NoFace,
}

/// <summary>How alarming the status is, for the dot in the menu.</summary>
public enum StatusTone
{
    /// <summary>Nothing is running, by choice.</summary>
    Idle,

    /// <summary>Working normally.</summary>
    Active,

    /// <summary>Waiting or needs something from the user.</summary>
    Attention,

    /// <summary>Something is broken.</summary>
    Problem,
}

/// <summary>The four states of the tray icon (Mac MenuBarGlyph, SPEC F25).</summary>
public enum TrayIconState
{
    /// <summary>Solid mark: Tracking, No face, Calibrating, Starting.</summary>
    Tracking,

    /// <summary>Dimmed mark with a hollow end point: Paused, One display, Suspended.</summary>
    Paused,

    /// <summary>Mark with a lock badge: Focus Lock.</summary>
    FocusLock,

    /// <summary>Mark with a dot: the statuses that need the user ("Needs you").</summary>
    Attention,
}

/// <summary>Everything the status depends on, so the rules can be tested without a controller.</summary>
public sealed record TrackingStatusSnapshot(
    CameraAuthorization Authorization,
    CaptureStatus Capture,
    bool TrackingEnabled,
    bool CalibrationRunning,
    int DisplayCount,
    bool HasCalibration,
    bool FocusLocked,
    bool HasFace,
    string? DisconnectedCamera = null);

/// <summary>
/// The one status the tray icon and menu show (SPEC F19). Pure: a function of a snapshot. Windows wording differs from
/// the Mac only by dropping the Accessibility row (Windows needs no permission to focus windows).
/// </summary>
public readonly record struct TrackingStatus(TrackingStatusKind Kind, string? Text = null, SuspendReason? Reason = null)
{
    /// <summary>Tracking off.</summary>
    public static TrackingStatus Paused => new(TrackingStatusKind.Paused);

    /// <summary>Tracking and a face.</summary>
    public static TrackingStatus Tracking => new(TrackingStatusKind.Tracking);

    /// <summary>Tracking, no face.</summary>
    public static TrackingStatus NoFace => new(TrackingStatusKind.NoFace);

    /// <summary>Focus Lock on.</summary>
    public static TrackingStatus FocusLock => new(TrackingStatusKind.FocusLock);

    /// <summary>The camera is starting.</summary>
    public static TrackingStatus Starting => new(TrackingStatusKind.Starting);

    /// <summary>A suspension.</summary>
    public static TrackingStatus Suspended(SuspendReason reason) => new(TrackingStatusKind.Suspended, null, reason);

    /// <summary>An interruption with the camera service's text.</summary>
    public static TrackingStatus CameraBusy(string text) => new(TrackingStatusKind.CameraBusy, text);

    /// <summary>The named camera is unplugged.</summary>
    public static TrackingStatus CameraDisconnected(string name) => new(TrackingStatusKind.CameraDisconnected, name);

    /// <summary>Computes the status for a snapshot.</summary>
    public static TrackingStatus Evaluate(TrackingStatusSnapshot snapshot) =>
        SettledStatus(snapshot) ?? CameraStatus(snapshot);

    // Rows 1 to 6 of the table: what the settings and the displays say, before the camera is considered.
    private static TrackingStatus? SettledStatus(TrackingStatusSnapshot s)
    {
        var denied = s.Authorization is CameraAuthorization.Denied or CameraAuthorization.Restricted;
        if (s.TrackingEnabled && denied)
        {
            return new TrackingStatus(TrackingStatusKind.CameraBlocked);
        }

        if (s.Capture is CaptureStatus.Failed)
        {
            return new TrackingStatus(TrackingStatusKind.CameraFailed);
        }

        if (s.CalibrationRunning)
        {
            return new TrackingStatus(TrackingStatusKind.Calibrating);
        }

        if (!s.TrackingEnabled)
        {
            return s.DisconnectedCamera is { } name ? CameraDisconnected(name) : Paused;
        }

        if (s.DisplayCount < 2)
        {
            return new TrackingStatus(TrackingStatusKind.OneDisplay);
        }

        return s.HasCalibration ? null : new TrackingStatus(TrackingStatusKind.NeedsCalibration);
    }

    // Rows 7 to 12: Tracking is on and calibrated, so the camera decides.
    private static TrackingStatus CameraStatus(TrackingStatusSnapshot s)
    {
        switch (s.Capture)
        {
            case CaptureStatus.Suspended suspended:
                return Suspended(suspended.Reason);
            case CaptureStatus.Interrupted interrupted:
                return s.DisconnectedCamera is { } name ? CameraDisconnected(name) : CameraBusy(interrupted.Detail);
            case CaptureStatus.Running:
                if (s.FocusLocked)
                {
                    return FocusLock;
                }

                return s.HasFace ? Tracking : NoFace;
            default:
                // Stopped with Tracking on and a calibration is the moment before the camera starts.
                return Starting;
        }
    }

    /// <summary>The status line, without the camera name.</summary>
    public string Line => Kind switch
    {
        TrackingStatusKind.CameraBlocked => "Camera access needed",
        TrackingStatusKind.CameraFailed => "Camera unavailable",
        TrackingStatusKind.Paused => "Paused",
        TrackingStatusKind.Calibrating => "Calibrating",
        TrackingStatusKind.OneDisplay => "Only one display",
        TrackingStatusKind.NeedsCalibration => "Needs calibration",
        TrackingStatusKind.Suspended => CaptureText.Summary(new CaptureStatus.Suspended(Reason ?? SuspendReason.SystemSleep)),
        TrackingStatusKind.Starting => "Starting camera",
        TrackingStatusKind.CameraBusy => IsUsedByAnotherApp(Text ?? string.Empty)
            ? "Camera busy"
            : CaptureText.Summary(new CaptureStatus.Interrupted(Text ?? string.Empty)),
        TrackingStatusKind.CameraDisconnected => $"{Text} disconnected",
        TrackingStatusKind.FocusLock => "Focus Lock on",
        TrackingStatusKind.Tracking => "Tracking",
        _ => "No face",
    };

    /// <summary>How alarming the status is.</summary>
    public StatusTone Tone => Kind switch
    {
        TrackingStatusKind.CameraBlocked or TrackingStatusKind.CameraFailed or TrackingStatusKind.CameraBusy
            or TrackingStatusKind.CameraDisconnected => StatusTone.Problem,
        TrackingStatusKind.Paused or TrackingStatusKind.OneDisplay => StatusTone.Idle,
        TrackingStatusKind.Calibrating or TrackingStatusKind.NeedsCalibration or TrackingStatusKind.Suspended
            or TrackingStatusKind.Starting => StatusTone.Attention,
        _ => StatusTone.Active,
    };

    /// <summary>The tray icon for this status, exactly as SPEC F25's table says.</summary>
    public TrayIconState Icon => Kind switch
    {
        TrackingStatusKind.Tracking or TrackingStatusKind.NoFace or TrackingStatusKind.Calibrating
            or TrackingStatusKind.Starting => TrayIconState.Tracking,
        TrackingStatusKind.Paused or TrackingStatusKind.OneDisplay or TrackingStatusKind.Suspended => TrayIconState.Paused,
        TrackingStatusKind.FocusLock => TrayIconState.FocusLock,
        _ => TrayIconState.Attention,
    };

    /// <summary>True when the camera service worded an interruption as another app using the camera.</summary>
    public static bool IsUsedByAnotherApp(string interruption) =>
        interruption.Contains("in use by another", StringComparison.OrdinalIgnoreCase)
        || interruption.Contains("used by another", StringComparison.OrdinalIgnoreCase);
}

/// <summary>User-facing wording for camera state (Mac CaptureStatusPresentation).</summary>
public static class CaptureText
{
    /// <summary>One-line state text, for example "Running" or "Suspended: screen locked".</summary>
    public static string Summary(CaptureStatus status) => status switch
    {
        CaptureStatus.Stopped => "Camera off",
        CaptureStatus.Starting => "Starting camera…",
        CaptureStatus.Running => "Running",
        CaptureStatus.Suspended s => $"Suspended: {Describe(s.Reason)}",
        CaptureStatus.Interrupted i => $"Interrupted: {i.Detail}",
        CaptureStatus.Failed f => $"Failed: {UserMessage(f.Error)}",
        _ => string.Empty,
    };

    /// <summary>Lowercase phrase for the status line.</summary>
    public static string Describe(SuspendReason reason) => reason switch
    {
        SuspendReason.SystemSleep => "system asleep",
        SuspendReason.DisplaysAsleep => "displays asleep",
        SuspendReason.ScreenLocked => "screen locked",
        _ => "another user's session",
    };

    /// <summary>Plain-English description of a camera error.</summary>
    public static string UserMessage(CameraError error) => error.Kind switch
    {
        CameraErrorKind.NotAuthorized => "camera access was not granted",
        CameraErrorKind.NoDevice => "no camera was found",
        _ => error.Detail.Length > 0 ? error.Detail : error.Kind.ToString(),
    };

    /// <summary>The camera could not be opened right now but may be able to shortly: just plugged in, or still starting.</summary>
    public static bool IsCameraNotReady(this CameraError error) =>
        error.Kind is CameraErrorKind.DeviceUnavailable or CameraErrorKind.NoDevice;
}
