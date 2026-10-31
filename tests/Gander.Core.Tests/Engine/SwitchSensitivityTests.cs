using System.Text.Json;

namespace Gander.Core.Tests;

/// <summary>Port of the Mac SwitchSensitivityTests ("Switch sensitivity" suite).</summary>
public class SwitchSensitivityTests
{
    private static SwitchSensitivity Decoded(string json) => JsonSerializer.Deserialize<SwitchSensitivity>(json, EngineJson.Options);

    [Fact]
    public void DefaultsMatchTheEngineDefaults()
    {
        Assert.Equal(SwitchSettings.Default, SwitchSensitivity.Default.ToSwitchSettings());
        Assert.Equal(new SwitchSensitivity(0.40, 0.75, 2), SwitchSensitivity.Default);
    }

    [Fact]
    public void MapsDwellAndMarginsKeepingTheRest()
    {
        var settings = new SwitchSensitivity(0.8, 4).ToSwitchSettings();
        Assert.Equal(0.8, settings.Dwell);
        Assert.Equal(0.45, settings.EnterMargin);
        Assert.Equal(0.225, settings.StayMargin);
        Assert.Equal(SwitchSettings.Default.Cooldown, settings.Cooldown);
        Assert.Equal(SwitchSettings.Default.MaxGap, settings.MaxGap);
    }

    [Fact]
    public void SwitchSettingsCarriesTheCooldownAndKeepsTheOtherFields()
    {
        var settings = new SwitchSensitivity(0.6, 1.1, 1).ToSwitchSettings();
        Assert.Equal(0.6, settings.Dwell);
        Assert.Equal(1.1, settings.Cooldown);
        Assert.Equal(0.225, settings.EnterMargin);
        Assert.Equal(0.1125, settings.StayMargin);
        Assert.Equal(SwitchSettings.Default.MaxGap, settings.MaxGap);
        Assert.Equal(SwitchSettings.Default.MinFaceConfidence, settings.MinFaceConfidence);
        Assert.Equal(SwitchSettings.Default.ElsewhereYawMargin, settings.ElsewhereYawMargin);
        Assert.Equal(SwitchSettings.Default.ElsewherePitchMargin, settings.ElsewherePitchMargin);
    }

    [Fact]
    public void TheRangesAreTheDocumentedOnes()
    {
        Assert.Equal(new DoubleRange(0.05, 1.5), SwitchSensitivity.DwellRange);
        Assert.Equal(new DoubleRange(0.2, 1.5), SwitchSensitivity.CooldownRange);
    }

    [Fact]
    public void ClampsEveryFieldIntoItsRange()
    {
        var low = new SwitchSensitivity(0, 0, -3);
        Assert.Equal(SwitchSensitivity.DwellRange.Lower, low.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Lower, low.Cooldown);
        Assert.Equal(0, low.ConfidenceLevel);
        var high = new SwitchSensitivity(9, 9, 40);
        Assert.Equal(SwitchSensitivity.DwellRange.Upper, high.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Upper, high.Cooldown);
        Assert.Equal(SwitchSensitivity.ConfidenceMargins.Count - 1, high.ConfidenceLevel);
        var inside = new SwitchSensitivity(0.05, 0.2, 3);
        Assert.Equal(0.05, inside.Dwell);
        Assert.Equal(0.2, inside.Cooldown);
        Assert.Equal(3, inside.ConfidenceLevel);
    }

    [Fact]
    public void TheOldInitializerKeepsTheDefaultCooldownAndClamps()
    {
        var old = new SwitchSensitivity(0.8, 4);
        Assert.Equal(new SwitchSensitivity(0.8, 0.75, 4), old);
        Assert.Equal(SwitchSensitivity.DwellRange.Lower, new SwitchSensitivity(0, 9).Dwell);
    }

    [Fact]
    public void DecodedOutOfRangeValuesAreClamped()
    {
        var value = Decoded("""{"dwell": 7, "cooldown": 40, "confidenceLevel": 12}""");
        Assert.Equal(SwitchSensitivity.DwellRange.Upper, value.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Upper, value.Cooldown);
        Assert.Equal(SwitchSensitivity.ConfidenceMargins.Count - 1, value.ConfidenceLevel);
        var settings = value.ToSwitchSettings();
        Assert.Equal(SwitchSensitivity.DwellRange.Upper, settings.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Upper, settings.Cooldown);
        Assert.Equal(SwitchSensitivity.ConfidenceMargins[^1], settings.EnterMargin);

        var low = Decoded("""{"dwell": -1, "cooldown": 0.01, "confidenceLevel": -5}""");
        Assert.Equal(SwitchSensitivity.DwellRange.Lower, low.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Lower, low.Cooldown);
        Assert.Equal(0, low.ConfidenceLevel);
    }

    [Fact]
    public void SavedValuesWithoutACooldownDecodeWithTheDefaultOne()
    {
        var value = Decoded("""{"dwell":0.2,"confidenceLevel":2}""");
        Assert.Equal(new SwitchSensitivity(0.2, 0.75, 2), value);
        Assert.Equal(SwitchSettings.Default.Cooldown, value.ToSwitchSettings().Cooldown);
        Assert.Equal(SwitchSensitivity.Default, Decoded("""{"dwell":0.4,"confidenceLevel":2}"""));
    }

    [Fact]
    public void ASavedValueMissingTheHoldTimeOrConfidenceStaysUnreadable()
    {
        Assert.Throws<JsonException>(() => Decoded("""{"cooldown":0.5,"confidenceLevel":2}"""));
        Assert.Throws<JsonException>(() => Decoded("""{"dwell":0.4,"cooldown":0.5}"""));
    }

    [Fact]
    public void EncodingWritesAllThreeValuesAndRoundTrips()
    {
        var value = new SwitchSensitivity(0.35, 0.5, 3);
        var json = JsonSerializer.Serialize(value, EngineJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(["dwell", "cooldown", "confidenceLevel"], root.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(0.35, root.GetProperty("dwell").GetDouble());
        Assert.Equal(0.5, root.GetProperty("cooldown").GetDouble());
        Assert.Equal(3, root.GetProperty("confidenceLevel").GetInt32());
        Assert.Equal(value, Decoded(json));
        Assert.Equal(SwitchSensitivity.Default, Decoded(JsonSerializer.Serialize(SwitchSensitivity.Default, EngineJson.Options)));
    }

    [Fact]
    public void TheInstantPresetIsAShortHoldAndPause()
    {
        Assert.Equal(0.10, SwitchSensitivity.Instant.Dwell);
        Assert.Equal(0.30, SwitchSensitivity.Instant.Cooldown);
        Assert.True(SwitchSensitivity.Instant.Dwell >= SwitchSensitivity.DwellRange.Lower);
        Assert.True(SwitchSensitivity.Instant.Cooldown >= SwitchSensitivity.CooldownRange.Lower);
        var settings = SwitchSensitivity.Instant.ToSwitchSettings();
        Assert.Equal(0.10, settings.Dwell);
        Assert.Equal(0.30, settings.Cooldown);
    }

    [Fact]
    public void ApplyingInstantTimingKeepsTheConfidence()
    {
        for (var level = 0; level < SwitchSensitivity.ConfidenceMargins.Count; level++)
        {
            var before = new SwitchSensitivity(1.2, 1.4, level);
            var after = before.ApplyingInstantTiming();
            Assert.Equal(SwitchSensitivity.Instant.Dwell, after.Dwell);
            Assert.Equal(SwitchSensitivity.Instant.Cooldown, after.Cooldown);
            Assert.Equal(level, after.ConfidenceLevel);
        }

        Assert.Equal(SwitchSensitivity.Instant, SwitchSensitivity.Default.ApplyingInstantTiming());
        Assert.Equal(SwitchSensitivity.Instant, SwitchSensitivity.Instant.ApplyingInstantTiming());
    }
}
