using Sway.Core;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Tests.Windowing;

public sealed class DisplayMapperTests
{
    private static DisplayLayout Layout() => new(
    [
        new DisplayInfo("a", 111, "A", new DisplayRect(-1920, 0, 1920, 1080), false, false),
        new DisplayInfo("b", 222, "B", new DisplayRect(0, 0, 2560, 1440), false, true),
    ]);

    [Fact]
    public void MatchesByNativeId()
    {
        var map = DisplayMapper.Create(Layout(), _ => null);
        Assert.Equal(new DisplayId("a"), map(111));
        Assert.Equal(new DisplayId("b"), map(222));
    }

    [Fact]
    public void FallsBackToFrameMatch()
    {
        var map = DisplayMapper.Create(Layout(), handle => handle == 999 ? new DisplayRect(0, 0, 2560, 1440) : null);
        Assert.Equal(new DisplayId("b"), map(999));
    }

    [Fact]
    public void UnknownMonitorMapsToNull()
    {
        var map = DisplayMapper.Create(Layout(), _ => new DisplayRect(5, 5, 5, 5));
        Assert.Null(map(12345));
    }

    [Fact]
    public void HandleUsesLow32Bits()
    {
        var map = DisplayMapper.Create(Layout(), _ => null);
        Assert.Equal(new DisplayId("a"), map(0x1_0000_0000L + 111));
    }

    [Fact]
    public void BoundsLookupHappensOncePerHandle()
    {
        var calls = 0;
        var map = DisplayMapper.Create(Layout(), _ =>
        {
            calls++;
            return null;
        });
        map(5);
        map(5);
        Assert.Equal(1, calls);
    }
}
