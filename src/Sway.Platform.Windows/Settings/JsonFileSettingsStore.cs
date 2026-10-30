using System.Text.Json;
using System.Text.Json.Nodes;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Settings;

/// <summary>
/// Settings in one JSON object file (default <c>%AppData%\Sway\settings.json</c>). Values are strings. Reads come from
/// memory after the first load; every write rewrites the file atomically (temp file, then replace). A missing file is an
/// empty store. A corrupt file (not JSON, or not an object) is copied to <c>settings.json.bak</c> and the store starts
/// empty; the next write replaces the corrupt file. Keys this class did not write, including non-string values, are kept
/// when the file is rewritten. Thread-safe. A write that fails (disk full, locked file) keeps the value in memory and is
/// reported in <see cref="LastWriteError"/> instead of throwing.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _path;
    private JsonObject? _root;

    /// <summary>The store at <c>%AppData%\Sway\settings.json</c>.</summary>
    public JsonFileSettingsStore()
        : this(DefaultPath)
    {
    }

    /// <summary>The store at <paramref name="path"/>.</summary>
    public JsonFileSettingsStore(string path)
    {
        _path = path;
    }

    /// <summary>The default settings file location.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sway", "settings.json");

    /// <summary>The last write failure, or null when the last write worked.</summary>
    public Exception? LastWriteError { get; private set; }

    /// <inheritdoc />
    public string? Read(string key)
    {
        lock (_gate)
        {
            var node = Load()[key];
            return node switch
            {
                null => null,
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                _ => node.ToJsonString(),
            };
        }
    }

    /// <inheritdoc />
    public void Write(string key, string value)
    {
        lock (_gate)
        {
            Load()[key] = JsonValue.Create(value);
            Save();
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        lock (_gate)
        {
            if (Load().Remove(key))
            {
                Save();
            }
        }
    }

    private JsonObject Load()
    {
        if (_root is not null)
        {
            return _root;
        }

        _root = [];
        string text;
        try
        {
            if (!File.Exists(_path))
            {
                return _root;
            }

            text = File.ReadAllText(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _root;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject parsed)
            {
                _root = parsed;
                return _root;
            }
        }
        catch (JsonException)
        {
            // Falls through to the backup below.
        }

        KeepBackup();
        return _root;
    }

    private void KeepBackup()
    {
        try
        {
            File.Copy(_path, _path + ".bak", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The backup is best effort.
        }
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temp, _root!.ToJsonString(WriteOptions));
            if (File.Exists(_path))
            {
                File.Replace(temp, _path, null);
            }
            else
            {
                File.Move(temp, _path);
            }

            LastWriteError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastWriteError = exception;
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leave the temp file; the next write overwrites it.
            }
        }
    }
}
