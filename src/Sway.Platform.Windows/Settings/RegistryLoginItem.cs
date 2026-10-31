using System.Security;
using Microsoft.Win32;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Settings;

/// <summary>Reads and writes one value under a Run key. A seam so the login item is testable without the registry.</summary>
public interface IRunKeyAccess
{
    /// <summary>The value, or null when absent.</summary>
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>The real thing: <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.</summary>
public sealed class CurrentUserRunKey : IRunKeyAccess
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc />
    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    /// <inheritdoc />
    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Start Gander at login through the per-user Run key: value <c>Gander</c> holds the quoted exe path. Off unless the user
/// turns it on. <see cref="Status"/> is Enabled only when the value equals this exe's command (so a moved exe reads as
/// Disabled and turning it on repairs it); Unavailable when the registry cannot be read (policy, permissions).
/// <see cref="SetEnabled"/> does not throw: check <see cref="Status"/> afterwards.
/// </summary>
public sealed class RegistryLoginItem : ILoginItem
{
    /// <summary>The Run value name.</summary>
    public const string ValueName = "Gander";

    private readonly IRunKeyAccess _key;
    private readonly string _command;

    /// <summary>The login item for this process's exe.</summary>
    public RegistryLoginItem()
        : this(new CurrentUserRunKey(), Environment.ProcessPath ?? string.Empty)
    {
    }

    /// <summary>The login item for <paramref name="exePath"/> using <paramref name="key"/>.</summary>
    public RegistryLoginItem(IRunKeyAccess key, string exePath)
    {
        _key = key;
        _command = Command(exePath);
    }

    /// <summary>The Run value for an exe path: the path in double quotes.</summary>
    public static string Command(string exePath) => "\"" + exePath.Trim('"') + "\"";

    /// <inheritdoc />
    public LoginItemStatus Status
    {
        get
        {
            if (_command == "\"\"")
            {
                return LoginItemStatus.Unavailable;
            }

            try
            {
                return string.Equals(_key.Get(ValueName), _command, StringComparison.OrdinalIgnoreCase)
                    ? LoginItemStatus.Enabled
                    : LoginItemStatus.Disabled;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                return LoginItemStatus.Unavailable;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _key.Set(ValueName, _command);
            }
            else
            {
                _key.Delete(ValueName);
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Status reports Unavailable or the unchanged value.
        }
    }
}
