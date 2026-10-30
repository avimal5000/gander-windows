namespace Sway.Core.Tests;

/// <summary>
/// Display id and layout fingerprint rules. Not a port: the Mac tests for this live in the DisplayTracker and app
/// suites, so these pin the documented rules (sorted left to right, fingerprint from ids and integral frames).
/// </summary>
public class DisplayLayoutTests
{
    private static DisplayInfo Display(string id, double x, double y = 0, double w = 1920, double h = 1080, uint native = 1) =>
        new(new DisplayId(id), native, id, new DisplayRect(x, y, w, h), false, false);

    [Fact]
    public void DisplaysAreSortedLeftToRightThenTopThenId()
    {
        var layout = new DisplayLayout([Display("c", 1920), Display("b", 0, 100), Display("a", 0, 100), Display("z", -1920)]);
        Assert.Equal(["z", "a", "b", "c"], layout.Displays.Select(d => d.Id.RawValue));
    }

    [Fact]
    public void TheFingerprintIsBuiltFromIdsAndFramesNotNativeIds()
    {
        var one = new DisplayLayout([Display("A", 0, native: 1), Display("B", 1920, w: 2560, h: 1440, native: 2)]);
        var other = new DisplayLayout([Display("B", 1920, w: 2560, h: 1440, native: 9), Display("A", 0, native: 7)]);
        Assert.Equal("A@0,0,1920x1080|B@1920,0,2560x1440", one.Fingerprint);
        Assert.Equal(one.Fingerprint, other.Fingerprint);
    }

    [Fact]
    public void ChangingAFrameChangesTheFingerprint()
    {
        var one = new DisplayLayout([Display("A", 0), Display("B", 1920)]);
        var moved = new DisplayLayout([Display("A", 0), Display("B", 1921)]);
        Assert.NotEqual(one.Fingerprint, moved.Fingerprint);
    }

    [Fact]
    public void FractionalFramesUseTheIntegralRectangle()
    {
        // Origin rounds down, far edges round up: 0.5..1920.25 becomes 0..1921.
        var layout = new DisplayLayout([Display("A", 0.5, 0, 1919.75, 1080)]);
        Assert.Equal("A@0,0,1921x1080", layout.Fingerprint);
    }

    [Fact]
    public void LookupByIdAndTheEmptyLayout()
    {
        var layout = new DisplayLayout([Display("A", 0), Display("B", 1920)]);
        Assert.Equal("B", layout.Display("B")?.Name);
        Assert.Null(layout.Display("Q"));
        Assert.Equal(string.Empty, DisplayLayout.Empty.Fingerprint);
    }

    [Fact]
    public void ADisplayIdConvertsFromAString()
    {
        DisplayId id = "abc";
        Assert.Equal("abc", id.RawValue);
        Assert.Equal("abc", id.ToString());
    }
}
