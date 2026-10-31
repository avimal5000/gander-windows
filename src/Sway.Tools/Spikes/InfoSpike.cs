using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Enumeration;

namespace Sway.Tools.Spikes;

/// <summary>
/// Spike S3: Windows version, cameras, monitors (order, size, scale), and the windows on each monitor in z-order.
/// Prints process and class names only, never window titles.
/// </summary>
internal static class InfoSpike
{
    internal static void Run()
    {
        using var report = new Report("sway-info.txt");
        report.Line($"SwayTools info, {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.Line();

        Computer(report);
        Cameras(report);
        MonitorsAndWindows(report);

        report.Line();
        report.Line($"Also saved to: {report.Path}");
    }

    private static void Computer(Report report)
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        using var current = Registry.LocalMachine.OpenSubKey(key);
        report.Line("== Computer ==");
        report.Line($"Windows: {current?.GetValue("ProductName")} {current?.GetValue("DisplayVersion")}, build {current?.GetValue("CurrentBuildNumber")}.{current?.GetValue("UBR")}");
        report.Line($"Processor architecture: {RuntimeInformation.OSArchitecture} (this program runs as {RuntimeInformation.ProcessArchitecture})");
        report.Line($"Logical processors: {Environment.ProcessorCount}");
        report.Line();
    }

    private static void Cameras(Report report)
    {
        report.Line("== Cameras ==");
        try
        {
            var cameras = DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask().GetAwaiter().GetResult();
            foreach (var camera in cameras)
            {
                report.Line($"- {camera.Name} (enabled: {camera.IsEnabled})");
            }

            if (cameras.Count == 0)
            {
                report.Line("(none found)");
            }
        }
        catch (Exception exception)
        {
            report.Line($"Could not list cameras: {exception.Message}");
        }

        // Windows keeps one consent switch per user and one for desktop apps; "Deny" blocks the camera.
        const string consent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
        foreach (var (name, hive) in new[] { ("this user", Registry.CurrentUser), ("this machine", Registry.LocalMachine) })
        {
            using var webcam = hive.OpenSubKey(consent);
            report.Line($"Camera access setting ({name}): {webcam?.GetValue("Value") ?? "not set"}");
        }

        using var desktop = Registry.CurrentUser.OpenSubKey(consent + @"\NonPackaged");
        report.Line($"Camera access for desktop apps (this user): {desktop?.GetValue("Value") ?? "not set"}");
        report.Line();
    }

    private static void MonitorsAndWindows(Report report)
    {
        var monitors = Survey.Monitors();
        var (windows, skipped) = Survey.Windows();

        report.Line($"== Monitors ({monitors.Count}), left to right ==");
        foreach (var (monitor, index) in monitors.Select((monitor, index) => (monitor, index)))
        {
            var scale = monitor.Dpi * 100 / 96;
            report.Line($"{index + 1}. {monitor.Device}: {Survey.Describe(monitor.Bounds)}, scale {scale}%{(monitor.Primary ? ", primary" : string.Empty)}");
        }

        report.Line();
        report.Line("== Windows Gander would consider, topmost first, per monitor ==");
        foreach (var (monitor, index) in monitors.Select((monitor, index) => (monitor, index)))
        {
            report.Line($"Monitor {index + 1} ({monitor.Device}):");
            var onThis = windows.Where(window => window.Monitor == monitor.Handle).ToList();
            foreach (var window in onThis)
            {
                var elevated = window.Elevated switch { true => ", ELEVATED", null => ", elevation unknown", _ => string.Empty };
                report.Line($"  - {window.Process} [{window.ClassName}] {Survey.Describe(window.Bounds)}{elevated}");
            }

            if (onThis.Count == 0)
            {
                report.Line("  (none)");
            }
        }

        report.Line();
        report.Line("== Left out, and why ==");
        foreach (var (reason, count) in skipped)
        {
            report.Line($"  {count} x {reason}");
        }

        var foreground = Native.GetForegroundWindow();
        report.Line();
        report.Line($"Foreground window when this ran: {Native.ClassName(foreground)} (probably this console)");
    }
}
