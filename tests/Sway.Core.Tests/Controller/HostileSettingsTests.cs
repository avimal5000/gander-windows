using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.Core.Tests.Controller;

/// <summary>Hand-edited or corrupt stored settings must fall back to defaults, never stop the controller from starting.</summary>
public class HostileSettingsTests
{
    private const string NullDisplays =
        """{"formatVersion":1,"profiles":[{"features":0,"displays":null,"yawScale":1,"pitchScale":1,"layoutFingerprint":"x","createdAt":"2024-01-01T00:00:00Z","formatVersion":1}]}""";

    private const string NullFingerprint =
        """{"formatVersion":1,"profiles":[{"features":0,"displays":[{"displayId":"A","yaw":0,"pitch":0,"yawStd":1,"pitchStd":1,"sampleCount":5},{"displayId":"B","yaw":9,"pitch":0,"yawStd":1,"pitchStd":1,"sampleCount":5}],"yawScale":1,"pitchScale":1,"layoutFingerprint":null,"createdAt":"2024-01-01T00:00:00Z","formatVersion":1}]}""";

    private const string NullElement = """{"formatVersion":1,"profiles":[null,5,"x"]}""";

    private const string NullDisplayEntry =
        """{"formatVersion":1,"profiles":[{"features":0,"displays":[null,null],"yawScale":1,"pitchScale":1,"layoutFingerprint":"x","createdAt":"2024-01-01T00:00:00Z","formatVersion":1}]}""";

    private const string ZeroScale =
        """{"formatVersion":1,"profiles":[{"features":0,"displays":[{"displayId":"A","yaw":0,"pitch":0,"yawStd":1,"pitchStd":1,"sampleCount":5},{"displayId":"B","yaw":9,"pitch":0,"yawStd":1,"pitchStd":1,"sampleCount":5}],"yawScale":0,"pitchScale":-1,"layoutFingerprint":"x","createdAt":"2024-01-01T00:00:00Z","formatVersion":1}]}""";

    private static Harness WithSetting(string key, string value)
    {
        var settings = new FakeSettingsStore();
        settings.Values[key] = value;
        return Harness.Make(settingsStore: settings);
    }

    [Theory]
    [InlineData(NullDisplays)]
    [InlineData(NullFingerprint)]
    [InlineData(NullElement)]
    [InlineData(NullDisplayEntry)]
    [InlineData(ZeroScale)]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("")]
    [InlineData("{")]
    public void ALibraryWithNullsOrNonsenseStartsEmpty(string json)
    {
        using var h = WithSetting(SettingsKeys.CalibrationLibrary, json);
        Assert.Empty(h.Controller.Library.Profiles);
    }

    [Fact]
    public void OneBadProfileDoesNotDropTheGoodOnes()
    {
        var good = System.Text.Json.JsonSerializer.Serialize(new CalibrationLibrary([Fixtures.Profile()]), EngineJson.Options);
        var withBad = good.Replace("\"profiles\":[", "\"profiles\":[{\"displays\":null},", StringComparison.Ordinal);
        using var h = WithSetting(SettingsKeys.CalibrationLibrary, withBad);
        Assert.Single(h.Controller.Library.Profiles);
    }

    [Theory]
    [InlineData("""{"virtualKey":76,"modifiers":7}""")]
    [InlineData("""{"virtualKey":76,"modifiers":7,"keyLabel":null}""")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("{")]
    public void AShortcutWithNullsFallsBackToTheDefault(string json)
    {
        using var h = WithSetting(SettingsKeys.FocusLockShortcut, json);
        Assert.Equal(HotKeyShortcut.FocusLockDefault, h.Controller.FocusLockShortcut);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"dwell":null,"confidenceLevel":2}""")]
    [InlineData("""{"dwell":"x","confidenceLevel":2}""")]
    public void HostileSensitivityFallsBackToTheDefault(string json)
    {
        using var h = WithSetting(SettingsKeys.SwitchSensitivity, json);
        Assert.Equal(SwitchSensitivity.Default, h.Controller.Sensitivity);
    }

    [Fact]
    public void ANullKeyLabelIsInvalidNotAnError()
    {
        Assert.False(new HotKeyShortcut(0x4C, HotKeyModifiers.Control, null!).IsValid);
    }
}
