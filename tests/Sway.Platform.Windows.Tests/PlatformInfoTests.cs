using Sway.Platform.Windows;

namespace Sway.Platform.Windows.Tests;

public class PlatformInfoTests
{
    [Fact]
    public void TheRunnerIsASupportedWindows()
    {
        Assert.True(PlatformInfo.IsSupported);
    }
}
