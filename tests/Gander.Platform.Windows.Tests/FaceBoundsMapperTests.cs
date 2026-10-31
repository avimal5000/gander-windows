using Gander.Core;
using Gander.Platform.Windows.Pose;

namespace Gander.Platform.Windows.Tests;

public sealed class FaceBoundsMapperTests
{
    [Fact]
    public void LandmarkBoxBecomesFractionsOfTheFrame()
    {
        Landmark[] landmarks = [new(160, 120, 0), new(480, 360, 0), new(300, 200, 5)];
        var bounds = FaceBoundsMapper.FromLandmarks(landmarks, 640, 480);
        Assert.NotNull(bounds);
        Assert.Equal(0.25, bounds.Value.X, 6);
        Assert.Equal(0.25, bounds.Value.Y, 6);
        Assert.Equal(0.5, bounds.Value.Width, 6);
        Assert.Equal(0.5, bounds.Value.Height, 6);
        Assert.Equal(0.5, bounds.Value.MidX, 6);
    }

    [Fact]
    public void BoxIsClampedToTheFrame()
    {
        Landmark[] landmarks = [new(-50, -10, 0), new(700, 500, 0)];
        var bounds = FaceBoundsMapper.FromLandmarks(landmarks, 640, 480)!.Value;
        Assert.Equal(new FaceBounds(0, 0, 1, 1), bounds);
    }

    [Fact]
    public void BadInputGivesNull()
    {
        Assert.Null(FaceBoundsMapper.FromLandmarks([], 640, 480));
        Assert.Null(FaceBoundsMapper.FromLandmarks([new Landmark(1, 1, 0), new Landmark(2, 2, 0)], 0, 480));
        Assert.Null(FaceBoundsMapper.FromLandmarks([new Landmark(double.NaN, 1, 0)], 640, 480));
        Assert.Null(FaceBoundsMapper.FromLandmarks([new Landmark(5, 5, 0)], 640, 480));
    }
}
