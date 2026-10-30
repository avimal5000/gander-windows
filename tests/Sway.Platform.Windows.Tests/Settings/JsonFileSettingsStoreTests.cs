using Sway.Platform.Windows.Settings;

namespace Sway.Platform.Windows.Tests.Settings;

public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sway-settings-" + Guid.NewGuid().ToString("N"));

    public JsonFileSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void MissingFileReadsNull()
    {
        var store = new JsonFileSettingsStore(FilePath);
        Assert.Null(store.Read("anything"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void WriteThenReadRoundTripsAcrossInstances()
    {
        new JsonFileSettingsStore(FilePath).Write("a", "1");

        Assert.Equal("1", new JsonFileSettingsStore(FilePath).Read("a"));
    }

    [Fact]
    public void WriteCreatesTheDirectory()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "settings.json");
        new JsonFileSettingsStore(path).Write("a", "1");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void OverwriteReplacesTheValue()
    {
        var store = new JsonFileSettingsStore(FilePath);
        store.Write("a", "1");
        store.Write("a", "2");
        Assert.Equal("2", new JsonFileSettingsStore(FilePath).Read("a"));
    }

    [Fact]
    public void RemoveDeletesOnlyThatKey()
    {
        var store = new JsonFileSettingsStore(FilePath);
        store.Write("a", "1");
        store.Write("b", "2");
        store.Remove("a");

        var reloaded = new JsonFileSettingsStore(FilePath);
        Assert.Null(reloaded.Read("a"));
        Assert.Equal("2", reloaded.Read("b"));
    }

    [Fact]
    public void RemovingMissingKeyDoesNotCreateTheFile()
    {
        new JsonFileSettingsStore(FilePath).Remove("nope");
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void UnknownKeysAndNonStringValuesSurviveWrites()
    {
        File.WriteAllText(FilePath, """{"future":{"x":[1,2,3]},"flag":true,"mine":"old"}""");
        var store = new JsonFileSettingsStore(FilePath);

        store.Write("mine", "new");

        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"future\"", text);
        Assert.Contains("[", text);
        Assert.Contains("\"flag\": true", text);
        Assert.Equal("new", new JsonFileSettingsStore(FilePath).Read("mine"));
        Assert.Equal("true", new JsonFileSettingsStore(FilePath).Read("flag"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"a\": ")]
    public void CorruptFileStartsEmptyAndKeepsBackup(string content)
    {
        File.WriteAllText(FilePath, content);
        var store = new JsonFileSettingsStore(FilePath);

        Assert.Null(store.Read("a"));
        Assert.Equal(content, File.ReadAllText(FilePath + ".bak"));

        store.Write("a", "1");
        Assert.Equal("1", new JsonFileSettingsStore(FilePath).Read("a"));
        Assert.Equal(content, File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void EmptyFileCountsAsCorrupt()
    {
        File.WriteAllText(FilePath, string.Empty);
        var store = new JsonFileSettingsStore(FilePath);
        Assert.Null(store.Read("a"));
        store.Write("a", "1");
        Assert.Equal("1", new JsonFileSettingsStore(FilePath).Read("a"));
    }

    [Fact]
    public void WriteLeavesNoTempFile()
    {
        var store = new JsonFileSettingsStore(FilePath);
        store.Write("a", "1");
        store.Write("a", "2");
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void FailedWriteKeepsValueInMemoryAndReportsError()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "a file where a directory is needed");
        var store = new JsonFileSettingsStore(Path.Combine(blocker, "settings.json"));

        store.Write("a", "1");

        Assert.Equal("1", store.Read("a"));
        Assert.NotNull(store.LastWriteError);
    }

    [Fact]
    public void ValuesWithSpecialCharactersRoundTrip()
    {
        const string Value = "quote \" backslash \\ newline \n unicode \u00e9\u4e2d tab \t";
        new JsonFileSettingsStore(FilePath).Write("k", Value);
        Assert.Equal(Value, new JsonFileSettingsStore(FilePath).Read("k"));
    }

    [Fact]
    public void ConcurrentWritersLoseNothing()
    {
        var store = new JsonFileSettingsStore(FilePath);

        Parallel.For(0, 64, i => store.Write("k" + i, "v" + i));

        var reloaded = new JsonFileSettingsStore(FilePath);
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal("v" + i, reloaded.Read("k" + i));
        }
    }

    [Fact]
    public void DefaultPathIsUnderAppDataSway()
    {
        Assert.EndsWith(Path.Combine("Sway", "settings.json"), JsonFileSettingsStore.DefaultPath);
    }
}
