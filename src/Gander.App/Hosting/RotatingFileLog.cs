using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gander.App.Hosting;

/// <summary>
/// A text log in one folder, capped at <see cref="MaxBytes"/> with one rotation (gander.log and gander.log.1), so it never
/// grows past about twice the cap. Thread-safe. It never throws: logging must not be the thing that crashes the app.
/// Callers write status text and exceptions only, never window titles or image data.
/// </summary>
public sealed class RotatingFileLog
{
    /// <summary>The cap for the current file: 1 MB.</summary>
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates a log writing <c>gander.log</c> in <paramref name="directory"/>.</summary>
    public RotatingFileLog(string directory, long maxBytes = DefaultMaxBytes, Func<DateTimeOffset>? now = null)
    {
        _path = Path.Combine(directory, "gander.log");
        _maxBytes = maxBytes;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The folder under %LocalAppData%\Gander\Logs.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gander", "Logs");

    /// <summary>The file being written.</summary>
    public string CurrentPath => _path;

    /// <summary>Appends one timestamped line (newlines inside the text are flattened).</summary>
    public void Write(string line)
    {
        try
        {
            var flat = line.Replace("\r\n", " | ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
            var text = $"{_now().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {flat}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                RotateIfNeeded(bytes.Length);
                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception)
        {
            // Logging is best effort.
        }
    }

    private void RotateIfNeeded(int incoming)
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length + incoming <= _maxBytes)
        {
            return;
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }
}
