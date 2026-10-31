using System;
using System.Globalization;
using System.IO;
using Sway.Core;

namespace Sway.App.Hosting;

/// <summary>
/// Writes recorded pose traces (numbers only, JSON) to %LocalAppData%\Sway\Traces. A trace never holds a frame; the files
/// stay on this PC. Sway writes one only when the user records: Stop and save, or the recording ends because the camera
/// stopped, the window closed or the sample limit was reached.
/// </summary>
public sealed class TraceStore
{
    private readonly string _directory;

    /// <summary>Creates a store in <paramref name="directory"/> (the default folder when null).</summary>
    public TraceStore(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
    }

    /// <summary>The folder under %LocalAppData%\Sway\Traces.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sway", "Traces");

    /// <summary>The file name for a trace taken at <paramref name="now"/>: "pose-trace-20261031-142530.json".</summary>
    public static string FileName(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"pose-trace-{now.LocalDateTime:yyyyMMdd-HHmmss}.json");

    /// <summary>Writes the trace and returns the full path. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the folder cannot be written.</summary>
    public string Write(PoseTrace trace, DateTimeOffset now)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, FileName(now));
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"pose-trace-{now.LocalDateTime:yyyyMMdd-HHmmss}-{suffix}.json"));
            suffix++;
        }

        File.WriteAllText(path, trace.ToJson());
        return path;
    }
}
