using System;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Microsoft.Win32;

namespace Gander.App.Hosting;

/// <summary>
/// The theme every window reads: light or dark from the Windows apps setting, and the accent the user chose in
/// Settings. Windows asks for it each time it draws, so a change in Windows or in Settings shows at once.
/// </summary>
public static class AppTheme
{
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    /// <summary>The preferences that hold the accent choice; set once at startup.</summary>
    public static AppPreferences? Preferences { get; set; }

    /// <summary>The theme for the current Windows appearance and accent choice.</summary>
    public static GanderTheme Current => GanderTheme.For(SystemInfo.IsLightApps(), AccentOverride());

    /// <summary>Parses a DWM colorization value (0xAARRGGBB) into a color.</summary>
    public static Rgb FromColorization(int argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Rgb? AccentOverride()
    {
        if (Preferences?.Accent != AccentChoice.System)
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
            return key?.GetValue("ColorizationColor") is int value ? FromColorization(value) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
