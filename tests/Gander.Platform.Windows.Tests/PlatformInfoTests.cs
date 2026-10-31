using Gander.Platform.Windows;

namespace Gander.Platform.Windows.Tests;

public class PlatformInfoTests
{
    [Fact]
    public void TheRunnerIsASupportedWindows()
    {
        Assert.True(PlatformInfo.IsSupported);
    }
}
