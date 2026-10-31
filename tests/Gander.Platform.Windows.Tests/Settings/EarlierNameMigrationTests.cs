using Gander.Platform.Windows.Settings;

namespace Gander.Platform.Windows.Tests.Settings;

public sealed class EarlierNameMigrationTests : IDisposable
{
    private const string Exe = @"C:\Users\me\AppData\Local\Programs\Gander\Gander.exe";

    private readonly string _appData = Path.Combine(Path.GetTempPath(), "gander-migrate-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeKey : IRunKeyAccess
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string value) => Values[name] = value;

        public void Delete(string name) => Values.Remove(name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_appData))
        {
            Directory.Delete(_appData, recursive: true);
        }
    }

    private string Earlier => Path.Combine(_appData, "Sway", "settings.json");

    private string Current => Path.Combine(_appData, "Gander", "settings.json");

    private void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void CopiesTheEarlierSettingsWhenThereAreNoneUnderTheNewName()
    {
        Write(Earlier, "{\"a\":1}");

        EarlierNameMigration.Run(_appData, new FakeKey(), Exe);

        Assert.Equal("{\"a\":1}", File.ReadAllText(Current));
        Assert.True(File.Exists(Earlier), "The earlier file is left alone.");
    }

    [Fact]
    public void NeverOverwritesSettingsUnderTheNewName()
    {
        Write(Earlier, "old");
        Write(Current, "new");

        EarlierNameMigration.Run(_appData, new FakeKey(), Exe);

        Assert.Equal("new", File.ReadAllText(Current));
    }

    [Fact]
    public void DoesNothingWhenThereIsNothingToCarryOver()
    {
        var key = new FakeKey();

        EarlierNameMigration.Run(_appData, key, Exe);

        Assert.False(File.Exists(Current));
        Assert.Empty(key.Values);
    }

    [Fact]
    public void MovesTheStartupEntryToTheNewNameAndThisExe()
    {
        var key = new FakeKey();
        key.Values["Sway"] = "\"C:\\old\\Sway.exe\"";

        EarlierNameMigration.Run(_appData, key, Exe);

        Assert.False(key.Values.ContainsKey("Sway"));
        Assert.Equal("\"" + Exe + "\"", key.Values["Gander"]);
    }

    [Fact]
    public void KeepsAnExistingNewStartupEntry()
    {
        var key = new FakeKey();
        key.Values["Sway"] = "\"C:\\old\\Sway.exe\"";
        key.Values["Gander"] = "\"D:\\elsewhere\\Gander.exe\"";

        EarlierNameMigration.Run(_appData, key, Exe);

        Assert.False(key.Values.ContainsKey("Sway"));
        Assert.Equal("\"D:\\elsewhere\\Gander.exe\"", key.Values["Gander"]);
    }
}
