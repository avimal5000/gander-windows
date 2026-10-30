using System.Text.Json;

namespace Sway.Core.Tests;

/// <summary>Port of the Mac CalibrationLibraryTests ("Calibration library" suite).</summary>
public class CalibrationLibraryTests
{
    private static DisplayInfo Display(string id, double x) =>
        new(new DisplayId(id), 1, id, new DisplayRect(x, 0, 1000, 600), false, false);

    private static DisplayLayout Layout(params string[] ids) =>
        new(ids.Select((id, index) => Display(id, index * 1000.0)));

    private static CalibrationProfile Profile(string[] ids, string? camera = "C922", double created = 0, int formatVersion = 1) =>
        new(
            PoseFeatureSet.VisionAngles,
            ids.Select((id, index) => new DisplayProfile(new DisplayId(id), index * 20.0, 5, 1, 2, 60)).ToList(),
            1.5,
            2.5,
            Layout(ids).Fingerprint,
            camera,
            DateTimeOffset.FromUnixTimeSeconds((long)created),
            formatVersion);

    private static double Created(CalibrationProfile profile) => profile.CreatedAt.ToUnixTimeSeconds();

    [Fact]
    public void KeyIgnoresDisplayOrder()
    {
        Assert.Equal(Profile(["A", "B"]).Key, Profile(["B", "A"]).Key);
        Assert.NotEqual(Profile(["A", "B"]).Key, Profile(["A", "B"], camera: "FaceTime").Key);
    }

    [Fact]
    public void RestrictedKeepsOrderScalesAndMetadata()
    {
        var full = Profile(["A", "B", "C"]);
        var restricted = full.RestrictedTo(new HashSet<DisplayId> { "C", "A" });
        Assert.Equal(new DisplayId[] { "A", "C" }, restricted.Displays.Select(d => d.DisplayId));
        Assert.Equal(full.YawScale, restricted.YawScale);
        Assert.Equal(full.PitchScale, restricted.PitchScale);
        Assert.Equal(full.CameraName, restricted.CameraName);
        Assert.Equal(full.CreatedAt, restricted.CreatedAt);
    }

    [Fact]
    public void SaveReplacesTheSameKeyAndPutsNewestFirst()
    {
        var library = new CalibrationLibrary();
        library.Save(Profile(["A", "B"], created: 1));
        library.Save(Profile(["A", "B", "C"], created: 2));
        library.Save(Profile(["B", "A"], created: 3));
        Assert.Equal([3.0, 2.0], library.Profiles.Select(Created));
    }

    [Fact]
    public void SaveIgnoresUnusableProfiles()
    {
        var library = new CalibrationLibrary();
        library.Save(Profile(["A"]));
        library.Save(Profile(["A", "B"], formatVersion: 99));
        Assert.Empty(library.Profiles);
    }

    [Fact]
    public void SaveTrimsToTheLimit()
    {
        var library = new CalibrationLibrary();
        for (var index = 0; index < CalibrationLibrary.MaxProfiles + 3; index++)
        {
            library.Save(Profile(["A", "B"], camera: $"Camera {index}", created: index));
        }

        Assert.Equal(CalibrationLibrary.MaxProfiles, library.Profiles.Count);
        Assert.Equal($"Camera {CalibrationLibrary.MaxProfiles + 2}", library.Profiles[0].CameraName);
        Assert.DoesNotContain(library.Profiles, p => p.CameraName == "Camera 0");
    }

    [Fact]
    public void RemoveDropsOnlyThatKey()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"]), Profile(["A", "B", "C"])]);
        library.Remove(Profile(["A", "B"]).Key);
        Assert.Equal([Profile(["A", "B", "C"]).Key], library.Profiles.Select(p => p.Key));
    }

    [Fact]
    public void InitKeepsTheGivenOrderNewestFirst()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"], created: 2), Profile(["A", "C"], created: 1)]);
        Assert.Equal([2.0, 1.0], library.Profiles.Select(Created));
    }

    [Fact]
    public void ExactDisplaySetResolves()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"])]);
        var match = library.Resolve(Layout("A", "B"), "C922");
        Assert.NotNull(match);
        Assert.Equal(match.Saved, match.Profile);
        Assert.False(match.IsPartial);
    }

    [Fact]
    public void AMovedDisplayStillResolves()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"])]);
        var moved = new DisplayLayout([Display("A", 0), Display("B", -1440)]);
        Assert.NotNull(library.Resolve(moved, "C922"));
    }

    [Fact]
    public void ASubsetOfALargerProfileResolvesRestricted()
    {
        var library = new CalibrationLibrary([Profile(["A", "B", "C"])]);
        var match = library.Resolve(Layout("A", "C"), "C922");
        Assert.NotNull(match);
        Assert.Equal(new DisplayId[] { "A", "C" }, match.Profile.Displays.Select(d => d.DisplayId));
        Assert.Equal(3, match.Saved.Displays.Count);
        Assert.True(match.IsPartial);
    }

    [Fact]
    public void TheFewestExtraDisplaysWinThenTheMostRecentlySaved()
    {
        // Newest first: the ABE profile was saved after the ABC one.
        var library = new CalibrationLibrary([
            Profile(["A", "B", "C", "D"], created: 5),
            Profile(["A", "B", "E"], created: 9),
            Profile(["A", "B", "C"], created: 1),
        ]);
        var match = library.Resolve(Layout("A", "B"), "C922");
        Assert.NotNull(match);
        Assert.Equal(9, Created(match.Saved));
        var exact = new CalibrationLibrary([Profile(["A", "B", "C"], created: 9), Profile(["A", "B"], created: 1)])
            .Resolve(Layout("A", "B"), "C922");
        Assert.NotNull(exact);
        Assert.False(exact.IsPartial);
    }

    [Fact]
    public void UnknownDisplaysOtherCamerasAndSingleDisplaysDoNotResolve()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"])]);
        Assert.Null(library.Resolve(Layout("A", "B", "C"), "C922"));
        Assert.Null(library.Resolve(Layout("A", "B"), "FaceTime HD Camera"));
        Assert.Null(library.Resolve(Layout("A", "B"), null));
        Assert.Null(library.Resolve(Layout("A"), "C922"));
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var library = new CalibrationLibrary([Profile(["A", "B"], created: 2), Profile(["A", "C"], created: 1)]);
        var json = JsonSerializer.Serialize(library, EngineJson.Options);
        var decoded = JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options);
        Assert.Equal(library, decoded);
    }

    [Fact]
    public void DecodingDropsBrokenAndOutdatedProfiles()
    {
        var good = JsonSerializer.Serialize(Profile(["A", "B"]), EngineJson.Options);
        var outdated = JsonSerializer.Serialize(Profile(["A", "C"], formatVersion: 7), EngineJson.Options);
        var json = $$"""{"formatVersion": 1, "profiles": [{{good}}, {"displays": 3}, {{outdated}}]}""";
        var library = JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options);
        Assert.NotNull(library);
        Assert.Equal([Profile(["A", "B"]).Key], library.Profiles.Select(p => p.Key));
    }

    [Fact]
    public void AnotherLibraryVersionDecodesEmpty()
    {
        const string json = """{"formatVersion": 2, "profiles": []}""";
        Assert.Empty(JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options)!.Profiles);
    }

    /// <summary>Not in the Swift suite: a profile missing a required field is dropped, not silently zeroed.</summary>
    [Fact]
    public void AProfileMissingARequiredFieldIsDropped()
    {
        var good = JsonSerializer.Serialize(Profile(["A", "B"]), EngineJson.Options);
        var broken = good.Replace("\"yawScale\":1.5,", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(good, broken);
        var json = $$"""{"formatVersion": 1, "profiles": [{{broken}}]}""";
        Assert.Empty(JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options)!.Profiles);
    }
}
