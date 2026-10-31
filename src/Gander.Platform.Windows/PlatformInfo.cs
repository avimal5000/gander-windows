namespace Gander.Platform.Windows;

/// <summary>What the running system is, for the checks every Windows adapter makes before it starts.</summary>
public static class PlatformInfo
{
    /// <summary>Gander targets Windows 10 version 2004 (build 19041) and later.</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
}
