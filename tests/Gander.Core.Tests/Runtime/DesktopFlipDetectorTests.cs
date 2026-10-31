using Gander.Core.Runtime;

namespace Gander.Core.Tests.Runtime;

public class DesktopFlipDetectorTests
{
    private static KeyValuePair<long, bool>[] Flags(params (long Handle, bool On)[] windows) =>
        windows.Select(w => KeyValuePair.Create(w.Handle, w.On)).ToArray();

    [Fact]
    public void TheFirstUpdateOnlyRecords()
    {
        var detector = new DesktopFlipDetector();

        Assert.False(detector.Update(Flags((1, true), (2, false))));
    }

    [Fact]
    public void SwappingWhichWindowsAreOnTheCurrentDesktopIsAChange()
    {
        var detector = new DesktopFlipDetector();
        detector.Update(Flags((1, true), (2, true), (3, false)));

        Assert.True(detector.Update(Flags((1, false), (2, false), (3, true))));
        Assert.False(detector.Update(Flags((1, false), (2, false), (3, true))));
    }

    [Fact]
    public void WindowsOpeningAndClosingAreNotAChange()
    {
        var detector = new DesktopFlipDetector();
        detector.Update(Flags((1, true), (2, true)));

        Assert.False(detector.Update(Flags((1, true), (3, false), (4, true))));
        Assert.False(detector.Update(Flags((3, false))));
    }

    [Fact]
    public void AWindowPinnedToEveryDesktopNeverFlips()
    {
        var detector = new DesktopFlipDetector();
        detector.Update(Flags((9, true), (1, true)));

        Assert.False(detector.Update(Flags((9, true), (1, true))));
    }

    [Fact]
    public void ResetForgetsWhatWasSeen()
    {
        var detector = new DesktopFlipDetector();
        detector.Update(Flags((1, true)));
        detector.Reset();

        Assert.False(detector.Update(Flags((1, false))));
    }
}
