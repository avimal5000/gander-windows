using Gander.App.Hosting;

namespace Gander.Platform.Windows.Tests.App;

public sealed class CameraDemandTests
{
    private bool _trackingWants;
    private bool _otherHolds;
    private readonly List<bool> _calls = [];

    private CameraDemand Make() => new(
        on =>
        {
            _calls.Add(on);
            return Task.CompletedTask;
        },
        () => _trackingWants,
        () => _otherHolds);

    [Fact]
    public async Task FirstOwnerTurnsTheHoldOnAndTheLastTurnsItOff()
    {
        var demand = Make();
        await demand.AcquireAsync("debug");
        await demand.AcquireAsync("gate");
        Assert.Equal([true], _calls);
        Assert.True(demand.Holding);
        await demand.ReleaseAsync("debug");
        Assert.True(demand.Holding);
        await demand.ReleaseAsync("gate");
        Assert.Equal([true, false], _calls);
        Assert.False(demand.Holding);
        Assert.Equal(0, demand.OwnerCount);
    }

    [Fact]
    public async Task ReleasingAnOwnerThatNeverAcquiredDoesNothing()
    {
        var demand = Make();
        await demand.ReleaseAsync("debug");
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task DebugDoesNotHoldWhileTrackingRunsTheCameraButTheGateTestAlwaysDoes()
    {
        _trackingWants = true;
        var demand = Make();
        await demand.AcquireAsync("debug");
        Assert.Empty(_calls);
        await demand.AcquireAsync("gate", exclusive: true);
        Assert.Equal([true], _calls);
        await demand.ReleaseAsync("gate");
        Assert.Equal([true, false], _calls);
        Assert.Equal(1, demand.OwnerCount);
    }

    [Fact]
    public async Task HoldFollowsTrackingWhileADebugWindowIsOpen()
    {
        var demand = Make();
        await demand.AcquireAsync("debug");
        Assert.True(demand.Holding);
        _trackingWants = true;
        await demand.ReevaluateAsync();
        Assert.False(demand.Holding);
        _trackingWants = false;
        await demand.ReevaluateAsync();
        Assert.True(demand.Holding);
        Assert.Equal([true, false, true], _calls);
    }

    [Fact]
    public async Task LeavesAHoldAnotherPartOfTheAppTookAlone()
    {
        _otherHolds = true;
        var demand = Make();
        await demand.AcquireAsync("debug", exclusive: true);
        Assert.Empty(_calls);
        Assert.False(demand.Holding);
        await demand.ReleaseAsync("debug");
        Assert.Empty(_calls);
    }
}
