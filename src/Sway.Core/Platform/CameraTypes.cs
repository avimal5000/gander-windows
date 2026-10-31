using Sway.Core.Runtime;

namespace Sway.Core.Platform;

/// <summary>A camera the system offers. <see cref="Id"/> is stable across runs (the system's device id).</summary>
public sealed record CameraDevice(string Id, string Name, bool IsBuiltIn);

/// <summary>Whether Gander may use the camera. Windows has no prompt for desktop apps, so only the last two occur there.</summary>
public enum CameraAuthorization
{
    NotDetermined,
    Authorized,
    Denied,
    Restricted,
}

public enum CameraErrorKind
{
    NotAuthorized,
    NoDevice,
    DeviceUnavailable,
    ConfigurationFailed,
    RuntimeFailure,
}

/// <summary>Why capture failed, with a short human-readable detail for the status line and the log.</summary>
public sealed record CameraError(CameraErrorKind Kind, string Detail = "");

/// <summary>What the capture session is doing (Mac: CaptureStatus).</summary>
public abstract record CaptureStatus
{
    public sealed record Stopped : CaptureStatus;

    public sealed record Starting : CaptureStatus;

    public sealed record Running(CameraDevice? Device) : CaptureStatus;

    public sealed record Suspended(SuspendReason Reason) : CaptureStatus;

    public sealed record Interrupted(string Detail) : CaptureStatus;

    public sealed record Failed(CameraError Error) : CaptureStatus;
}

/// <summary>Cameras the system offers and whether Gander may use them.</summary>
public interface ICameraCatalog
{
    CameraAuthorization Authorization();

    IReadOnlyList<CameraDevice> AvailableDevices();

    /// <summary>The device for a saved id, else the built-in camera, else the first one (Mac: resolvedDevice).</summary>
    CameraDevice? Resolve(string? deviceId);

    /// <summary>A camera was plugged in or removed. Raised on any thread; may be raised several times for one change.</summary>
    event Action? DevicesChanged;
}
