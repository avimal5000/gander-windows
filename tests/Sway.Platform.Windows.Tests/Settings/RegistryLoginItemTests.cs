using Sway.Core.Platform;
using Sway.Platform.Windows.Settings;

namespace Sway.Platform.Windows.Tests.Settings;

public sealed class RegistryLoginItemTests
{
    private const string Exe = @"C:\Program Files\Gander\Gander.exe";

    private sealed class FakeKey : IRunKeyAccess
    {
        public Dictionary<string, string> Values { get; } = [];

        public bool Throws { get; set; }

        public string? Get(string name) => Throws ? throw new UnauthorizedAccessException() : Values.GetValueOrDefault(name);

        public void Set(string name, string value)
        {
            if (Throws)
            {
                throw new UnauthorizedAccessException();
            }

            Values[name] = value;
        }

        public void Delete(string name)
        {
            if (Throws)
            {
                throw new UnauthorizedAccessException();
            }

            Values.Remove(name);
        }
    }

    [Fact]
    public void DisabledByDefault() =>
        Assert.Equal(LoginItemStatus.Disabled, new RegistryLoginItem(new FakeKey(), Exe).Status);

    [Fact]
    public void EnablingWritesTheQuotedPathUnderSway()
    {
        var key = new FakeKey();
        var item = new RegistryLoginItem(key, Exe);

        item.SetEnabled(true);

        Assert.Equal("\"" + Exe + "\"", key.Values["Gander"]);
        Assert.Equal(LoginItemStatus.Enabled, item.Status);
    }

    [Fact]
    public void DisablingRemovesTheValue()
    {
        var key = new FakeKey();
        var item = new RegistryLoginItem(key, Exe);
        item.SetEnabled(true);

        item.SetEnabled(false);

        Assert.Empty(key.Values);
        Assert.Equal(LoginItemStatus.Disabled, item.Status);
    }

    [Fact]
    public void DisablingWhenAbsentIsHarmless()
    {
        var item = new RegistryLoginItem(new FakeKey(), Exe);
        item.SetEnabled(false);
        Assert.Equal(LoginItemStatus.Disabled, item.Status);
    }

    [Fact]
    public void ValueForAnotherPathReadsDisabledAndEnablingRepairsIt()
    {
        var key = new FakeKey();
        key.Values["Gander"] = "\"D:\\old\\Gander.exe\"";
        var item = new RegistryLoginItem(key, Exe);

        Assert.Equal(LoginItemStatus.Disabled, item.Status);
        item.SetEnabled(true);
        Assert.Equal(LoginItemStatus.Enabled, item.Status);
    }

    [Fact]
    public void PathComparisonIgnoresCase()
    {
        var key = new FakeKey();
        key.Values["Gander"] = "\"" + Exe.ToUpperInvariant() + "\"";
        Assert.Equal(LoginItemStatus.Enabled, new RegistryLoginItem(key, Exe).Status);
    }

    [Fact]
    public void OtherRunValuesAreUntouched()
    {
        var key = new FakeKey();
        key.Values["Other"] = "x";
        var item = new RegistryLoginItem(key, Exe);
        item.SetEnabled(true);
        item.SetEnabled(false);
        Assert.Equal("x", key.Values["Other"]);
    }

    [Fact]
    public void RegistryFailureReportsUnavailableAndDoesNotThrow()
    {
        var item = new RegistryLoginItem(new FakeKey { Throws = true }, Exe);
        item.SetEnabled(true);
        Assert.Equal(LoginItemStatus.Unavailable, item.Status);
    }

    [Fact]
    public void MissingExePathIsUnavailable() =>
        Assert.Equal(LoginItemStatus.Unavailable, new RegistryLoginItem(new FakeKey(), string.Empty).Status);

    [Fact]
    public void CommandDoesNotDoubleQuote() =>
        Assert.Equal("\"" + Exe + "\"", RegistryLoginItem.Command("\"" + Exe + "\""));
}
