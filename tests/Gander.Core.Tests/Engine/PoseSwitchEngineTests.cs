namespace Gander.Core.Tests;

/// <summary>Port of the Mac PoseSwitchEngineTests: the facade must do what the pieces do and share no state between deciders.</summary>
public class PoseSwitchEngineTests
{
    private readonly PoseSwitchEngine _engine = new();

    [Fact]
    public void FitProfileMatchesTheFitter()
    {
        var trace = ScenarioWorld.CalibrationTrace(5);
        var rounds = new HashSet<int> { 1 };
        var viaEngine = _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, rounds);
        var viaFitter = new ProfileFitter().Fit(trace, PoseFeatureSet.VisionAngles, rounds);
        Assert.Equal(viaFitter, viaEngine);
    }

    [Fact]
    public void ClassifyMatchesTheClassifier()
    {
        var pose = Poses.Make(51, 8);
        var viaEngine = _engine.Classify(pose, HandProfile.Profile, HandProfile.Settings);
        var viaClassifier = new PoseClassifier(HandProfile.Profile, HandProfile.Settings).Classify(pose);
        Assert.Equal(viaClassifier, viaEngine);
        Assert.Equal(PoseClassification.NoFace, _engine.Classify(null, HandProfile.Profile, HandProfile.Settings));
    }

    [Fact]
    public void ANewDeciderIsIdle()
    {
        var decider = _engine.MakeDecider(HandProfile.Profile, HandProfile.Settings);
        var decision = decider.Process(Poses.Sample(0, null), new SwitchContext(null));
        Assert.Equal(SwitchPhase.Idle, decision.Phase);
        Assert.Equal(SwitchHoldReason.NoFace, decision.Reason);
        Assert.Null(decision.SwitchTo);
    }

    [Fact]
    public void DecidersFromOneEngineDoNotShareState()
    {
        var first = _engine.MakeDecider(HandProfile.Profile, HandProfile.Settings);
        var second = _engine.MakeDecider(HandProfile.Profile, HandProfile.Settings);
        var context = new SwitchContext(HandProfile.B);
        first.Process(Poses.Sample(0, HandProfile.YawA), context);
        first.Process(Poses.Sample(0.25, HandProfile.YawA), context);
        // The second decider starts its own dwell at 0.375; the first one would be at progress 0.75.
        var decision = second.Process(Poses.Sample(0.375, HandProfile.YawA), context);
        Assert.Equal(SwitchPhase.Dwelling(HandProfile.A, 0), decision.Phase);
    }

    [Fact]
    public void ADeciderUsesTheSettingsItWasMadeWith()
    {
        var slow = HandProfile.Settings with { Dwell = 1 };
        var decider = _engine.MakeDecider(HandProfile.Profile, slow);
        var context = new SwitchContext(HandProfile.B);
        var fired = new List<double>();
        for (var step = 0; step <= 8; step++)
        {
            var t = step * 0.125;
            if (decider.Process(Poses.Sample(t, HandProfile.YawA), context).SwitchTo is not null)
            {
                fired.Add(t);
            }
        }

        Assert.Equal([1.0], fired);
    }

    [Fact]
    public void ResetThroughTheInterfaceDropsTheDwell()
    {
        var decider = _engine.MakeDecider(HandProfile.Profile, HandProfile.Settings);
        var context = new SwitchContext(HandProfile.B);
        decider.Process(Poses.Sample(0, HandProfile.YawA), context);
        decider.Reset();
        var decision = decider.Process(Poses.Sample(0.25, HandProfile.YawA), context);
        Assert.Equal(SwitchPhase.Dwelling(HandProfile.A, 0), decision.Phase);
    }

    [Fact]
    public async Task TheEngineCanBeSharedAcrossTasks()
    {
        var shared = _engine;
        var one = Task.Run(() => shared.Classify(Poses.Make(60, 8), HandProfile.Profile, SwitchSettings.Default).Best);
        var two = Task.Run(() => shared.Classify(Poses.Make(0, 8), HandProfile.Profile, SwitchSettings.Default).Best);
        Assert.Equal(HandProfile.A, await one);
        Assert.Equal(HandProfile.C, await two);
    }
}
