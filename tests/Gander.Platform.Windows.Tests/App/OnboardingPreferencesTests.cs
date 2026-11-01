using Gander.App.Presentation;
using Gander.Core.Calibration;
using Gander.Platform.Windows.Settings;

namespace Gander.Platform.Windows.Tests.App;

public sealed class OnboardingPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gander-onboarding-" + Guid.NewGuid().ToString("N"));

    public OnboardingPreferencesTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AFileWithoutTheFieldReadsAsNotSeen()
    {
        File.WriteAllText(FilePath, "{\"gander.edgeGlowEnabled\":\"false\"}");
        Assert.False(new AppPreferences(new JsonFileSettingsStore(FilePath)).OnboardingSeen);
        Assert.False(new AppPreferences(new JsonFileSettingsStore(Path.Combine(_directory, "none.json"))).OnboardingSeen);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("True", false)]
    [InlineData("1", false)]
    [InlineData("true", true)]
    public void OnlyTheExactWordTrueMeansSeen(string? stored, bool expected) =>
        Assert.Equal(expected, AppPreferences.ParseSeen(stored));

    [Fact]
    public void MarkingItSeenRoundTripsThroughTheFileAndKeepsOtherKeys()
    {
        File.WriteAllText(FilePath, "{\"gander.edgeGlowEnabled\":\"false\"}");
        var preferences = new AppPreferences(new JsonFileSettingsStore(FilePath));
        var changes = 0;
        preferences.Changed += () => changes++;

        preferences.MarkOnboardingSeen();
        preferences.MarkOnboardingSeen();

        Assert.True(preferences.OnboardingSeen);
        Assert.Equal(0, changes);
        var reread = new AppPreferences(new JsonFileSettingsStore(FilePath));
        Assert.True(reread.OnboardingSeen);
        Assert.False(reread.EdgeGlowEnabled);
        Assert.Equal("true", new JsonFileSettingsStore(FilePath).Read(AppPreferences.OnboardingSeenKey));
    }

    [Fact]
    public void AMigratedFileCarriesNoFlagSoOnlyUsersWithoutCalibrationsSeeTheWalkthrough()
    {
        var appData = Path.Combine(_directory, "AppData");
        Directory.CreateDirectory(Path.Combine(appData, "Sway"));
        File.WriteAllText(Path.Combine(appData, "Sway", "settings.json"), "{\"gander.trackingEnabled\":\"true\"}");

        EarlierNameMigration.Run(appData, new NoRunKey(), @"C:\Gander\Gander.exe");

        var migrated = new AppPreferences(new JsonFileSettingsStore(Path.Combine(appData, "Gander", "settings.json")));
        Assert.False(migrated.OnboardingSeen);
        Assert.False(FirstRunRule.ShouldOpenSetup(migrated.OnboardingSeen, calibrationCount: 2));
        Assert.True(FirstRunRule.ShouldOpenSetup(migrated.OnboardingSeen, calibrationCount: 0));
    }

    private sealed class NoRunKey : IRunKeyAccess
    {
        public string? Get(string name) => null;

        public void Set(string name, string value)
        {
        }

        public void Delete(string name)
        {
        }
    }
}
