using Microsoft.Win32;
using Sway.Core.Platform;
using Windows.Devices.Enumeration;

namespace Sway.Platform.Windows;

/// <summary>
/// The cameras Windows offers (Windows.Devices.Enumeration, VideoCapture) and whether privacy settings allow them.
/// The device id is the system's device-interface id, which stays the same across runs. A device watcher raises
/// <see cref="DevicesChanged"/> whenever a camera is added, removed or updated (often several times for one change;
/// the tracking controller coalesces). Dispose stops the watcher.
/// </summary>
public sealed class WindowsCameraCatalog : ICameraCatalog, IDisposable
{
    private const string ConsentKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    private readonly object _gate = new();
    private DeviceWatcher? _watcher;
    private bool _enumerationCompleted;
    private bool _disposed;

    public event Action? DevicesChanged
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
            }

            EnsureWatching();
        }

        remove
        {
            lock (_gate)
            {
                _changed -= value;
            }
        }
    }

    private event Action? _changed;

    /// <summary>
    /// Reads the consent store: the user's switch, the machine's switch and the one for desktop (non-packaged) apps.
    /// "Deny" in any of them is <see cref="CameraAuthorization.Denied"/>. Windows shows no prompt to desktop apps.
    /// </summary>
    public CameraAuthorization Authorization()
    {
        try
        {
            return CameraSelection.Interpret(
                ReadConsent(Registry.CurrentUser, string.Empty),
                ReadConsent(Registry.LocalMachine, string.Empty),
                ReadConsent(Registry.CurrentUser, @"\NonPackaged"));
        }
        catch (Exception)
        {
            return CameraAuthorization.Authorized; // unreadable settings: let the open attempt report a real denial
        }
    }

    public IReadOnlyList<CameraDevice> AvailableDevices()
    {
        try
        {
            var found = Task.Run(async () => await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)).GetAwaiter().GetResult();
            return found
                .Where(device => device.IsEnabled)
                .Select(ToCameraDevice)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public CameraDevice? Resolve(string? deviceId) => CameraSelection.Resolve(AvailableDevices(), deviceId);

    public void Dispose()
    {
        DeviceWatcher? watcher;
        lock (_gate)
        {
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.Added -= OnChange;
        watcher.Removed -= OnChange;
        watcher.Updated -= OnChange;
        watcher.EnumerationCompleted -= OnEnumerationCompleted;
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception)
        {
            // Already stopped or the system is shutting down.
        }
    }

    internal static CameraDevice ToCameraDevice(DeviceInformation device)
    {
        var panel = device.EnclosureLocation?.Panel;
        return new CameraDevice(device.Id, device.Name, CameraSelection.IsBuiltIn(device.Name, panel is Panel.Front or Panel.Back));
    }

    private static string? ReadConsent(RegistryKey hive, string subKey)
    {
        using var key = hive.OpenSubKey(ConsentKey + subKey);
        return key?.GetValue("Value")?.ToString();
    }

    private void EnsureWatching()
    {
        lock (_gate)
        {
            if (_watcher is not null || _disposed)
            {
                return;
            }

            try
            {
                var watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
                watcher.Added += OnChange;
                watcher.Removed += OnChange;
                watcher.Updated += OnChange;
                watcher.EnumerationCompleted += OnEnumerationCompleted;
                _watcher = watcher;
                watcher.Start();
            }
            catch (Exception)
            {
                _watcher = null; // no change notifications; the controller still re-reads on start
            }
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            _enumerationCompleted = true;
        }
    }

    private void OnChange(DeviceWatcher sender, object args)
    {
        Action? handlers;
        lock (_gate)
        {
            // Added fires once per existing camera during the first enumeration: that is not a change.
            if (!_enumerationCompleted)
            {
                return;
            }

            handlers = _changed;
        }

        handlers?.Invoke(); // outside the lock: handlers may call back in
    }
}
