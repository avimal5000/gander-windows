using Sway.Core.Platform;

namespace Sway.Core.Controller;

/// <summary>The keys the controller saves in <see cref="ISettingsStore"/> (the Mac UserDefaults keys, unchanged).</summary>
public static class SettingsKeys
{
    /// <summary>Preferred camera's id; absent means "default (built-in)".</summary>
    public const string CameraDeviceId = "sway.cameraDeviceID";

    /// <summary>The calibration library as JSON (SPEC D25).</summary>
    public const string CalibrationLibrary = "sway.calibrationLibrary";

    /// <summary>"true" or "false": whether the user last left Tracking on (SPEC D23). Written only by the user's own choices.</summary>
    public const string TrackingEnabled = "sway.trackingEnabled";

    /// <summary>The switch sensitivity as JSON (SPEC D31). Values saved before D49 have no cooldown.</summary>
    public const string SwitchSensitivity = "sway.switchSensitivity";

    /// <summary>The Focus Lock shortcut as JSON; an empty string means no shortcut, an absent key means the default.</summary>
    public const string FocusLockShortcut = "sway.focusLockShortcut";
}

/// <summary>Why the camera cannot be used, for the menu banner.</summary>
public enum CameraPermissionPrompt
{
    /// <summary>The user denied access (Windows Settings > Privacy &amp; security > Camera): show how to enable it.</summary>
    Denied,

    /// <summary>A policy forbids access.</summary>
    Restricted,
}

/// <summary>One row of the recent switches list. Names the app and the display, never a window title.</summary>
public sealed record SwitchLogEntry(int Id, DateTimeOffset Time, string Text);

/// <summary>What the USB camera fallback's own restart is doing (Mac FallbackRestart).</summary>
public sealed record FallbackRestart
{
    private FallbackRestart(bool onChosen, CameraDevice? replacing)
    {
        OnChosen = onChosen;
        Replacing = replacing;
    }

    /// <summary>On a stand-in, because the chosen camera has been unplugged for long enough.</summary>
    public static FallbackRestart StandIn { get; } = new(false, null);

    /// <summary>On the chosen camera, which is connected again; carries the stand-in it replaces (null when nothing ran).</summary>
    public static FallbackRestart Chosen(CameraDevice? replacing) => new(true, replacing);

    /// <summary>True for a restart on the chosen camera.</summary>
    public bool OnChosen { get; }

    /// <summary>The stand-in a restart on the chosen camera replaces.</summary>
    public CameraDevice? Replacing { get; }
}
