using System.Security;

namespace Gander.Platform.Windows.Settings;

/// <summary>
/// Carries a user's data over from the app's earlier name (Sway): the settings file, and the "start with Windows" entry.
/// Runs at start-up, copies rather than moves, and never overwrites anything already under the new name. It does not
/// throw: a failure leaves the user with fresh defaults, as a first run would.
/// </summary>
public static class EarlierNameMigration
{
    /// <summary>The earlier name, used for folder and Run value names.</summary>
    public const string EarlierName = "Sway";

    /// <summary>The Run value name under the current name.</summary>
    private const string CurrentRunValue = RegistryLoginItem.ValueName;

    /// <summary>Runs the migration for this user and this exe.</summary>
    public static void Run()
    {
        Run(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            new CurrentUserRunKey(),
            Environment.ProcessPath ?? string.Empty);
    }

    /// <summary>Runs the migration with the roaming data folder, Run key and exe path given.</summary>
    public static void Run(string appData, IRunKeyAccess runKey, string exePath)
    {
        CopySettings(appData);
        MoveStartupEntry(runKey, exePath);
    }

    private static void CopySettings(string appData)
    {
        try
        {
            var earlier = Path.Combine(appData, EarlierName, "settings.json");
            var current = Path.Combine(appData, "Gander", "settings.json");
            if (File.Exists(earlier) && !File.Exists(current))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                File.Copy(earlier, current, overwrite: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Fresh defaults.
        }
    }

    private static void MoveStartupEntry(IRunKeyAccess runKey, string exePath)
    {
        try
        {
            if (runKey.Get(EarlierName) is null)
            {
                return;
            }

            // The earlier entry pointed at the old exe, which an upgrade no longer has: point the new one at this exe.
            runKey.Delete(EarlierName);
            if (runKey.Get(CurrentRunValue) is null && exePath.Length > 0)
            {
                runKey.Set(CurrentRunValue, RegistryLoginItem.Command(exePath));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // The entry stays as it was.
        }
    }
}
