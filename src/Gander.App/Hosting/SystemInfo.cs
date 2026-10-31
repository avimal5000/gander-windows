using System;
using System.Runtime.InteropServices;
using Gander.App.Presentation;
using Microsoft.Win32;

namespace Gander.App.Hosting;

/// <summary>Reads the machine facts the diagnostics show, and the taskbar theme.</summary>
public static class SystemInfo
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The Windows version line, from the registry as the info spike does.</summary>
    public static string WindowsVersion()
    {
        try
        {
            using var current = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return WindowsVersionText.Format(
                current?.GetValue("ProductName")?.ToString(),
                current?.GetValue("DisplayVersion")?.ToString(),
                current?.GetValue("CurrentBuildNumber")?.ToString(),
                current?.GetValue("UBR")?.ToString());
        }
        catch (Exception)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    /// <summary>OS and process architecture.</summary>
    public static string Architecture() => $"{RuntimeInformation.OSArchitecture} (process {RuntimeInformation.ProcessArchitecture})";

    /// <summary>Gander's version.</summary>
    public static string GanderVersion()
    {
        var version = typeof(SystemInfo).Assembly.GetName().Version;
        var informational = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            typeof(SystemInfo).Assembly,
            typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        return informational?.InformationalVersion ?? version?.ToString() ?? "unknown";
    }

    /// <summary>True when the taskbar uses the light theme (registry SystemUsesLightTheme).</summary>
    public static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return TrayPalette.IsLight(key?.GetValue("SystemUsesLightTheme"));
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>True when apps should use the light theme (registry AppsUseLightTheme).</summary>
    public static bool IsLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return TrayPalette.IsLight(key?.GetValue("AppsUseLightTheme"));
        }
        catch (Exception)
        {
            return true;
        }
    }
}
