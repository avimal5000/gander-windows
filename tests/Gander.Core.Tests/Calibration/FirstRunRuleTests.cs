using Gander.Core.Calibration;

namespace Gander.Core.Tests.Calibration;

public sealed class FirstRunRuleTests
{
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 2, false)]
    public void OpensOnlyWhenUnseenAndNothingIsCalibrated(bool seen, int calibrations, bool expected) =>
        Assert.Equal(expected, FirstRunRule.ShouldOpenSetup(seen, calibrations));
}
