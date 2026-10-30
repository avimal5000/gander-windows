using Sway.Core;

namespace Sway.Core.Tests;

public class HeadPoseTests
{
    [Fact]
    public void PosesWithTheSameAnglesAreEqual()
    {
        Assert.Equal(new HeadPose(10, -5, 1), new HeadPose(10, -5, 1));
    }

    [Fact]
    public void PosesWithDifferentAnglesAreNotEqual()
    {
        Assert.NotEqual(new HeadPose(10, -5, 1), new HeadPose(-10, -5, 1));
    }
}
