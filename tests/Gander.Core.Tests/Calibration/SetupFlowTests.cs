using Gander.Core.Calibration;

namespace Gander.Core.Tests.Calibration;

/// <summary>Ports of the Mac SetupFlowTests; the Accessibility cases are omitted (no such step on Windows).</summary>
public sealed class SetupFlowTests
{
    private static readonly SetupStep[] AllSteps = [SetupStep.Welcome, SetupStep.NeedsYou, SetupStep.CameraCheck, SetupStep.Calibrating, SetupStep.Done];

    private static SetupFlow At(SetupStep step)
    {
        var flow = step switch
        {
            SetupStep.Welcome => new SetupFlow(true, showsWelcome: true),
            SetupStep.NeedsYou => new SetupFlow(false),
            _ => new SetupFlow(true),
        };
        if (step is SetupStep.Calibrating or SetupStep.Done)
        {
            flow.StartCalibration();
        }

        if (step == SetupStep.Done)
        {
            flow.CalibrationFinished();
        }

        return flow;
    }

    [Fact]
    public void OpensAtNeedsYouUnlessTheCameraIsAllowed()
    {
        Assert.Equal(SetupStep.NeedsYou, new SetupFlow(false).Step);
        Assert.Equal(SetupStep.CameraCheck, new SetupFlow(true).Step);
    }

    [Fact]
    public void AllowingTheCameraMovesOnAndBlockingItSendsTheCheckBack()
    {
        var flow = new SetupFlow(false);
        flow.Update(false);
        Assert.Equal(SetupStep.NeedsYou, flow.Step);
        flow.Update(true);
        Assert.Equal(SetupStep.CameraCheck, flow.Step);

        var onCheck = new SetupFlow(true);
        onCheck.Update(false);
        Assert.Equal(SetupStep.NeedsYou, onCheck.Step);
    }

    [Fact]
    public void TheCalibrationAndTheResultIgnoreChangedPermissions()
    {
        var flow = new SetupFlow(true);
        flow.StartCalibration();
        flow.Update(false);
        Assert.Equal(SetupStep.Calibrating, flow.Step);
        flow.CalibrationFinished();
        flow.Update(false);
        Assert.Equal(SetupStep.Done, flow.Step);
    }

    [Fact]
    public void StartingCalibratingFinishingAndCalibratingAgain()
    {
        var flow = new SetupFlow(true);
        flow.StartCalibration();
        Assert.Equal(SetupStep.Calibrating, flow.Step);
        flow.CalibrationFinished();
        Assert.Equal(SetupStep.Done, flow.Step);
        flow.CalibrateAgain();
        Assert.Equal(SetupStep.CameraCheck, flow.Step);
    }

    [Fact]
    public void CancellingReturnsToTheCameraCheck()
    {
        var flow = new SetupFlow(true);
        flow.StartCalibration();
        flow.CalibrationCancelled();
        Assert.Equal(SetupStep.CameraCheck, flow.Step);
    }

    [Fact]
    public void AFailedFitStillShowsTheResultStepAndCalibratingAgainWorks()
    {
        var flow = new SetupFlow(true);
        flow.StartCalibration();
        flow.CalibrationFinished();
        Assert.Equal(SetupStep.Done, flow.Step);
        flow.CalibrateAgain();
        flow.StartCalibration();
        Assert.Equal(SetupStep.Calibrating, flow.Step);
    }

    [Fact]
    public void MovesOutOfTurnDoNothing()
    {
        var onCheck = new SetupFlow(true);
        onCheck.CalibrationFinished();
        onCheck.CalibrationCancelled();
        onCheck.CalibrateAgain();
        Assert.Equal(SetupStep.CameraCheck, onCheck.Step);

        var onPermission = new SetupFlow(false);
        onPermission.StartCalibration();
        Assert.Equal(SetupStep.NeedsYou, onPermission.Step);

        var calibrating = new SetupFlow(true);
        calibrating.StartCalibration();
        calibrating.StartCalibration();
        calibrating.CalibrateAgain();
        Assert.Equal(SetupStep.Calibrating, calibrating.Step);

        var onResult = new SetupFlow(true);
        onResult.StartCalibration();
        onResult.CalibrationFinished();
        onResult.CalibrationCancelled();
        onResult.StartCalibration();
        Assert.Equal(SetupStep.Done, onResult.Step);
    }

    [Fact]
    public void TheCameraRunsOnlyForTheCameraCheckAndTheCalibration()
    {
        foreach (var step in AllSteps)
        {
            Assert.Equal(step is SetupStep.CameraCheck or SetupStep.Calibrating, At(step).NeedsCamera);
        }
    }

    [Fact]
    public void TrackingHoldsOnTheCameraCheckAndTheCalibrationOnly()
    {
        foreach (var step in AllSteps)
        {
            Assert.Equal(step is SetupStep.CameraCheck or SetupStep.Calibrating, At(step).HoldsTracking);
        }
    }

    [Fact]
    public void OnlyNeedsYouWaitsForAPermission()
    {
        foreach (var step in AllSteps)
        {
            Assert.Equal(step == SetupStep.NeedsYou, At(step).IsPermissionStep);
        }
    }

    [Fact]
    public void StepsAreNumberedOneToFourAndTheWelcomeIsNotCounted()
    {
        Assert.Equal(4, SetupFlow.StepCount);
        Assert.Equal(1, (int)At(SetupStep.NeedsYou).Step);
        Assert.Equal(4, (int)At(SetupStep.Done).Step);
        Assert.Equal(0, (int)At(SetupStep.Welcome).Step);
        Assert.Equal(SetupFlow.StepCount, AllSteps.Count(s => (int)s >= 1));
        Assert.Null(SetupPresentation.StepLine(SetupStep.Welcome));
        Assert.Equal("Step 2 of 4", SetupPresentation.StepLine(SetupStep.CameraCheck));
    }

    [Fact]
    public void TheWelcomeOpensFirstOnlyWhenAskedFor()
    {
        Assert.Equal(SetupStep.Welcome, new SetupFlow(true, true).Step);
        Assert.Equal(SetupStep.Welcome, new SetupFlow(false, true).Step);
        Assert.Equal(SetupStep.CameraCheck, new SetupFlow(true, false).Step);
        Assert.Equal(SetupStep.CameraCheck, new SetupFlow(true).Step);
    }

    [Fact]
    public void GetStartedMovesToTheFirstStepThatAppliesUsingTheFactsOfThatMoment()
    {
        var blocked = new SetupFlow(false, true);
        blocked.GetStarted(false);
        Assert.Equal(SetupStep.NeedsYou, blocked.Step);

        var allowed = new SetupFlow(true, true);
        allowed.GetStarted(true);
        Assert.Equal(SetupStep.CameraCheck, allowed.Step);

        var later = new SetupFlow(false, true);
        later.GetStarted(true);
        Assert.Equal(SetupStep.CameraCheck, later.Step);
    }

    [Fact]
    public void TheWelcomeStaysUntilGetStartedWhateverThePermissionsDo()
    {
        var flow = new SetupFlow(false, true);
        flow.Update(true);
        flow.Update(false);
        Assert.Equal(SetupStep.Welcome, flow.Step);
    }

    [Fact]
    public void TheWelcomeAsksNothingOfTheApp()
    {
        var welcome = At(SetupStep.Welcome);
        Assert.False(welcome.NeedsCamera);
        Assert.False(welcome.HoldsTracking);
        Assert.False(welcome.IsPermissionStep);
    }

    [Fact]
    public void MovesThatBelongToOtherStepsDoNothingOnTheWelcome()
    {
        var flow = new SetupFlow(true, true);
        flow.StartCalibration();
        flow.CalibrationFinished();
        flow.CalibrationCancelled();
        flow.CalibrateAgain();
        Assert.Equal(SetupStep.Welcome, flow.Step);
    }

    [Fact]
    public void GetStartedDoesNothingOnAnyOtherStep()
    {
        foreach (var step in AllSteps.Where(s => s != SetupStep.Welcome))
        {
            var flow = At(step);
            flow.GetStarted(false);
            Assert.Equal(step, flow.Step);
        }
    }
}
