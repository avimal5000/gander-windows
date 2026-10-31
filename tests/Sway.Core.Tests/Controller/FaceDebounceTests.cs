using Sway.Core.Controller;

namespace Sway.Core.Tests.Controller;

/// <summary>Port of the Mac FaceDebounceTests ("Face debounce", 7 tests).</summary>
public class FaceDebounceTests
{
    [Fact]
    public void AFaceShowsAtOnce()
    {
        var debounce = new FaceDebounce();
        Assert.False(debounce.IsPresent);
        Assert.True(debounce.Update(true, 10));
        Assert.True(debounce.IsPresent);
    }

    [Fact]
    public void NoFaceShowsOnlyAfterHalfASecondWithoutOne()
    {
        var debounce = new FaceDebounce();
        debounce.Update(true, 10);
        Assert.True(debounce.Update(false, 10.2));
        Assert.True(debounce.Update(false, 10.49));
        Assert.False(debounce.Update(false, 10.5));
        Assert.False(debounce.Update(false, 11));
    }

    [Fact]
    public void AFaceInBetweenStartsTheWaitAgain()
    {
        var debounce = new FaceDebounce();
        debounce.Update(true, 10);
        debounce.Update(false, 10.4);
        debounce.Update(true, 10.45);
        Assert.True(debounce.Update(false, 10.9));
        Assert.False(debounce.Update(false, 10.95));
    }

    [Fact]
    public void AFaceComingBackAfterNoFaceShowsAtOnce()
    {
        var debounce = new FaceDebounce();
        debounce.Update(true, 1);
        debounce.Update(false, 2);
        Assert.False(debounce.IsPresent);
        Assert.True(debounce.Update(true, 2.07));
    }

    [Fact]
    public void NoFaceFromTheStartStaysAbsent()
    {
        var debounce = new FaceDebounce();
        Assert.False(debounce.Update(false, 1));
        Assert.False(debounce.Update(false, 5));
    }

    [Fact]
    public void AClockThatRestartsLowerDoesNotPinTheFace()
    {
        var debounce = new FaceDebounce();
        debounce.Update(true, 500);
        Assert.True(debounce.Update(false, 3));
        Assert.False(debounce.Update(false, 3.5));
    }

    [Fact]
    public void ResetForgetsTheFace()
    {
        var debounce = new FaceDebounce();
        debounce.Update(true, 10);
        debounce.Reset();
        Assert.False(debounce.Update(false, 10.1));
        Assert.False(debounce.IsPresent);
    }
}
