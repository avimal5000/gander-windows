using Sway.Core.Calibration;

namespace Sway.Core.Tests.Calibration;

/// <summary>Ports of the Mac FramingAdviceTests (same inputs and expected values).</summary>
public sealed class FramingAdviceTests
{
    private static FramingAdvice Advice(double height = 0.4, double cx = 0.5, double cy = 0.5) =>
        FramingAdvices.For(new HeadPose(0, 0, 0, 0.95, FaceBounds: new FaceBounds(cx - (height / 2), cy - (height / 2), height, height)));

    [Fact]
    public void NoPoseMeansNoFace()
    {
        Assert.Equal(FramingAdvice.NoFace, FramingAdvices.For(null));
        Assert.Equal(FramingAdvice.NoFace, FramingAdvices.For(new HeadPose(0, 0, 0)));
        Assert.Equal("Gander can't see your face yet.", FramingAdvice.NoFace.Text());
    }

    [Fact]
    public void ATooSmallFaceAsksToMoveCloser()
    {
        Assert.Equal(FramingAdvice.Closer, Advice(0.1));
        Assert.Equal(FramingAdvice.Closer, Advice(0.179));
        Assert.Equal("Move a little closer to the camera.", FramingAdvice.Closer.Text());
    }

    [Fact]
    public void ATooBigFaceAsksToMoveBack()
    {
        Assert.Equal(FramingAdvice.Back, Advice(0.601));
        Assert.Equal(FramingAdvice.Back, Advice(0.9));
        Assert.Equal("Move back a little.", FramingAdvice.Back.Text());
    }

    [Fact]
    public void TheHeightBoundariesThemselvesAreFine()
    {
        Assert.Equal(FramingAdvice.Good, Advice(0.18));
        Assert.Equal(FramingAdvice.Good, Advice(0.6));
    }

    [Fact]
    public void AFaceOffCenterOnEitherAxisAsksToCenter()
    {
        Assert.Equal(FramingAdvice.Center, Advice(cx: 0.19));
        Assert.Equal(FramingAdvice.Center, Advice(cx: 0.81));
        Assert.Equal(FramingAdvice.Center, Advice(cy: 0.19));
        Assert.Equal(FramingAdvice.Center, Advice(cy: 0.81));
        Assert.Equal("Center your face in the camera's view.", FramingAdvice.Center.Text());
    }

    [Fact]
    public void JustInsideTheCenterRangeIsFineAndJustOutsideIsNot()
    {
        Assert.Equal(FramingAdvice.Good, Advice(0.2, 0.2001, 0.5));
        Assert.Equal(FramingAdvice.Good, Advice(0.2, 0.5, 0.2001));
        Assert.Equal(FramingAdvice.Good, Advice(0.2, 0.7999, 0.7999));
        Assert.Equal(FramingAdvice.Center, Advice(0.2, 0.1999, 0.5));
        Assert.Equal(FramingAdvice.Center, Advice(0.2, 0.5, 0.8001));
        Assert.Equal(0.2, FramingAdvices.CenterRangeLow);
        Assert.Equal(0.8, FramingAdvices.CenterRangeHigh);
    }

    [Fact]
    public void AWellFramedFaceLooksGood()
    {
        Assert.Equal(FramingAdvice.Good, Advice());
        Assert.Equal("Looks good.", FramingAdvice.Good.Text());
    }

    [Fact]
    public void TheSizeRulesComeBeforeTheCenterRule()
    {
        Assert.Equal(FramingAdvice.Closer, Advice(0.1, 0.05, 0.5));
        Assert.Equal(FramingAdvice.Back, Advice(0.7, 0.9, 0.5));
    }
}
