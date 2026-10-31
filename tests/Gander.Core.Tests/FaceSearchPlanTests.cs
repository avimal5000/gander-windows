using Gander.Core.Pose;

namespace Gander.Core.Tests;

public class FaceSearchPlanTests
{
    private static void AssertInside(SquareBox box, int width, int height)
    {
        Assert.True(box.Left >= -1e-9);
        Assert.True(box.Top >= -1e-9);
        Assert.True(box.Left + box.Side <= width + 1e-9);
        Assert.True(box.Top + box.Side <= height + 1e-9);
    }

    [Fact]
    public void CandidatesAreAllInsideTheFrame()
    {
        foreach (var last in new SquareBox?[] { null, new SquareBox(5, 5, 300), new SquareBox(1270, 710, 900), new SquareBox(-50, 900, 100) })
        {
            var all = FaceSearchPlan.Candidates(last, 1280, 720);
            Assert.Equal(5, all.Count);
            foreach (var box in all)
            {
                AssertInside(box, 1280, 720);
            }
        }
    }

    [Fact]
    public void WithoutALastBoxTheSearchStartsAtTheFrameCenter()
    {
        var all = FaceSearchPlan.Candidates(null, 1000, 600);
        Assert.Equal(500, all[0].CenterX, 6);
        Assert.Equal(300, all[0].CenterY, 6);
        Assert.Equal(360, all[0].Side, 6);
        Assert.True(all[1].Side > all[0].Side);
    }

    [Fact]
    public void ShiftedBoxesSitHalfABoxToEachSide()
    {
        var all = FaceSearchPlan.Candidates(new SquareBox(640, 360, 200), 1280, 720);
        Assert.Equal(540, all[2].CenterX, 6);
        Assert.Equal(740, all[3].CenterX, 6);
    }

    [Fact]
    public void WideBoxCoversSeventyPercentOfFrameHeight()
    {
        var wide = FaceSearchPlan.Candidates(new SquareBox(100, 100, 200), 1280, 720)[4];
        Assert.Equal(504, wide.Side, 6);
        Assert.Equal(640, wide.CenterX, 6);
    }

    [Fact]
    public void NextRotatesAcrossFramesAndWraps()
    {
        var plan = new FaceSearchPlan();
        var all = FaceSearchPlan.Candidates(null, 1280, 720);
        var first = plan.Next(null, 1280, 720, 2);
        var second = plan.Next(null, 1280, 720, 2);
        var third = plan.Next(null, 1280, 720, 2);
        Assert.Equal(all[0], first[0]);
        Assert.Equal(all[1], first[1]);
        Assert.Equal(all[2], second[0]);
        Assert.Equal(all[3], second[1]);
        Assert.Equal(all[4], third[0]);
        Assert.Equal(all[0], third[1]);
    }

    [Fact]
    public void ResetRestartsTheRotation()
    {
        var plan = new FaceSearchPlan();
        var first = plan.Next(null, 640, 480, 2);
        plan.Next(null, 640, 480, 2);
        plan.Reset();
        Assert.Equal(first, plan.Next(null, 640, 480, 2));
    }

    [Fact]
    public void CountIsCappedAtOneLapAndBadInputGivesNothing()
    {
        var plan = new FaceSearchPlan();
        Assert.Equal(5, plan.Next(null, 640, 480, 50).Count);
        Assert.Empty(plan.Next(null, 640, 480, 0));
        Assert.Empty(plan.Next(null, 0, 480));
        Assert.Empty(FaceSearchPlan.Candidates(null, 640, 0));
    }

    [Fact]
    public void NonFiniteLastBoxFallsBackToTheFrameCenter()
    {
        var all = FaceSearchPlan.Candidates(new SquareBox(double.NaN, 10, 50), 640, 480);
        Assert.Equal(320, all[0].CenterX, 6);
    }
}
