namespace Sway.Core.Platform;

/// <summary>
/// Pure rules a camera catalog applies to what the system reports: which device is built in, which one to use, and
/// whether the privacy settings allow it. No system calls, so the rules are tested on any OS.
/// </summary>
public static class CameraSelection
{
    private static readonly string[] BuiltInNameHints =
    [
        "integrated", "internal", "built-in", "builtin", "built in", "facetime", "front camera", "front-facing",
        "user facing", "user-facing", "surface camera",
    ];

    /// <summary>
    /// Built in when the device sits on the front or back panel of the machine's enclosure (the system's own statement),
    /// or its name says internal or integrated. A USB webcam such as a C922 is neither.
    /// </summary>
    public static bool IsBuiltIn(string name, bool onFrontOrBackPanel)
    {
        if (onFrontOrBackPanel)
        {
            return true;
        }

        foreach (var hint in BuiltInNameHints)
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The device for a saved id, else the first built-in one, else the first one; null when there are none.</summary>
    public static CameraDevice? Resolve(IReadOnlyList<CameraDevice> devices, string? deviceId)
    {
        if (devices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(deviceId))
        {
            foreach (var device in devices)
            {
                if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
        }

        foreach (var device in devices)
        {
            if (device.IsBuiltIn)
            {
                return device;
            }
        }

        return devices[0];
    }

    /// <summary>
    /// Reads the three Windows consent-store values (CapabilityAccessManager, webcam): the user's switch, the
    /// machine's switch and the one for desktop apps. "Deny" in any of them blocks the camera; "Allow", a missing
    /// value or anything else does not (a missing value is the Windows default, which allows).
    /// </summary>
    public static CameraAuthorization Interpret(string? userValue, string? machineValue, string? desktopAppsValue) =>
        IsDeny(userValue) || IsDeny(machineValue) || IsDeny(desktopAppsValue)
            ? CameraAuthorization.Denied
            : CameraAuthorization.Authorized;

    private static bool IsDeny(string? value) => string.Equals(value?.Trim(), "Deny", StringComparison.OrdinalIgnoreCase);
}
