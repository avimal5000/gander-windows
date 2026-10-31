using Gander.Core.Pose;

namespace Gander.Core.Tests;

public class FaceRecoveryTests
{
    [Fact]
    public void FollowedFacesNeedALowerScoreThanColdOnes()
    {
        Assert.True(FaceRecovery.Threshold(true) < FaceRecovery.Threshold(false));
        Assert.Equal(0.5, FaceRecovery.ColdThreshold);
    }

    [Fact]
    public void PlausibleAcceptsSmallChangesAndRejectsJumpsAndSizeFlips()
    {
        var before = new SquareBox(300, 240, 200);
        Assert.True(FaceRecovery.Plausible(new SquareBox(330, 240, 170), before));
        Assert.False(FaceRecovery.Plausible(new SquareBox(300 + 250, 240, 200), before));
        Assert.False(FaceRecovery.Plausible(new SquareBox(300, 240, 60), before));
        Assert.False(FaceRecovery.Plausible(new SquareBox(300, 240, 500), before));
        Assert.False(FaceRecovery.Plausible(new SquareBox(300, 240, 0), before));
    }

    [Fact]
    public void NeighbourhoodGrowsEachLostFrameAndStaysInsideTheFrame()
    {
        var last = new SquareBox(500, 240, 200);
        var first = FaceRecovery.Neighbourhood(last, null, 1, 640, 480);
        var later = FaceRecovery.Neighbourhood(last, null, 6, 640, 480);
        Assert.Equal(3, first.Count);
        Assert.True(later[0].Side > first[0].Side);
        foreach (var box in first.Concat(later))
        {
            Assert.True(box.Left >= -1e-9 && box.Top >= -1e-9 && box.Left + box.Side <= 640 + 1e-9 && box.Top + box.Side <= 480 + 1e-9);
        }
    }

    [Fact]
    public void NeighbourhoodEndsAfterTheWindowAndWithoutALastBox()
    {
        Assert.Empty(FaceRecovery.Neighbourhood(null, null, 1, 640, 480));
        Assert.Empty(FaceRecovery.Neighbourhood(new SquareBox(300, 240, 200), null, FaceRecovery.NeighbourhoodFrames + 1, 640, 480));
        Assert.Empty(FaceRecovery.Neighbourhood(new SquareBox(300, 240, 200), null, 0, 640, 480));
    }

    [Fact]
    public void NeighbourhoodLeansTheWayTheFaceWasMoving()
    {
        var last = new SquareBox(320, 240, 100);
        var left = FaceRecovery.Neighbourhood(last, (-12, 0), 1, 640, 480);
        var right = FaceRecovery.Neighbourhood(last, (12, 0), 1, 640, 480);
        Assert.True(left[1].CenterX < last.CenterX);
        Assert.True(right[1].CenterX > last.CenterX);
    }

    [Fact]
    public void WithoutDriftTheLeanIsTowardTheNearerFrameSide()
    {
        var nearRight = FaceRecovery.Neighbourhood(new SquareBox(520, 240, 100), null, 1, 640, 480);
        var nearLeft = FaceRecovery.Neighbourhood(new SquareBox(120, 240, 100), null, 1, 640, 480);
        Assert.True(nearRight[1].CenterX > 520);
        Assert.True(nearLeft[1].CenterX < 120);
    }

    // A face that turns away moves toward the image edge and its landmark box shrinks. The search only has the last
    // good box. A box "finds" the face when it holds the face center and is 0.8 to 2.5 times the face size.
    private static bool Finds(SquareBox box, SquareBox face) =>
        Math.Abs(box.CenterX - face.CenterX) < box.Side / 2 * 0.6
        && Math.Abs(box.CenterY - face.CenterY) < box.Side / 2 * 0.6
        && box.Side / face.Side is >= 0.8 and <= 2.5;

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void AFaceMovingTowardTheEdgeAndShrinkingIsReacquiredWithinAFewFrames(int direction)
    {
        const int width = 640, height = 480;
        var last = new SquareBox(320, 240, 220);
        (double X, double Y) drift = (direction * 14, 0);
        var plan = new FaceSearchPlan();

        // The face keeps drifting while unseen: 25 px per lost frame toward the edge, and 5 percent smaller.
        for (var lost = 1; lost <= 6; lost++)
        {
            var face = new SquareBox(320 + (direction * 25 * lost), 240, 150 * Math.Pow(0.95, lost));
            var tried = FaceRecovery.NeighbourhoodForFrame(last, drift, lost, width, height).Concat(plan.Next(last, width, height));
            if (tried.Any(box => Finds(box, face)))
            {
                return;
            }
        }

        Assert.Fail("the face was not re-acquired in 6 lost frames");
    }
}
