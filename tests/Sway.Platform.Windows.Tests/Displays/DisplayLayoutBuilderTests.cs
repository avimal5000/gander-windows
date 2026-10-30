using Sway.Core;
using Sway.Platform.Windows.Displays;

namespace Sway.Platform.Windows.Tests.Displays;

public sealed class DisplayLayoutBuilderTests
{
    private const string PathA = @"\\?\DISPLAY#GSM5B09#5&2a5e3c3f&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathB = @"\\?\DISPLAY#DEL4100#5&1b2c3d4e&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static MonitorSnapshot Monitor(long handle, string device, double x, double y, double w, double h, bool primary = false) =>
        new(handle, device, new DisplayRect(x, y, w, h), primary);

    [Fact]
    public void IdIsDevicePathWithoutInterfaceGuidLowerCased() =>
        Assert.Equal(@"\\?\display#gsm5b09#5&2a5e3c3f&0&uid4352", DisplayLayoutBuilder.StableId(PathA, @"\\.\DISPLAY1"));

    [Fact]
    public void IdIgnoresTheGdiName() =>
        Assert.Equal(DisplayLayoutBuilder.StableId(PathA, @"\\.\DISPLAY1"), DisplayLayoutBuilder.StableId(PathA, @"\\.\DISPLAY7"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPathFallsBackToGdiName(string? path) =>
        Assert.Equal(@"gdi:\\.\display2", DisplayLayoutBuilder.StableId(path, @"\\.\DISPLAY2"));

    [Fact]
    public void BuildsTwoDisplaysFromTargets()
    {
        var monitors = new[]
        {
            Monitor(222, @"\\.\DISPLAY2", 0, 0, 2560, 1440, primary: true),
            Monitor(111, @"\\.\DISPLAY1", -1920, 0, 1920, 1080),
        };
        var targets = new Dictionary<string, DisplayTarget>(StringComparer.OrdinalIgnoreCase)
        {
            [@"\\.\DISPLAY1"] = new(PathB, "DELL U2415", 10),
            [@"\\.\DISPLAY2"] = new(PathA, "LG ULTRAGEAR", 10),
        };

        var layout = DisplayLayoutBuilder.Build(monitors, targets);

        Assert.Equal(2, layout.Displays.Count);
        var left = layout.Displays[0];
        Assert.Equal("DELL U2415", left.Name);
        Assert.Equal(-1920, left.Frame.X);
        Assert.False(left.IsMain);
        Assert.Equal(111u, left.NativeId);
        var right = layout.Displays[1];
        Assert.Equal("LG ULTRAGEAR", right.Name);
        Assert.True(right.IsMain);
        Assert.Equal(new DisplayRect(0, 0, 2560, 1440), right.Frame);
    }

    [Fact]
    public void GdiNameChangeKeepsTheSameIdsAndFingerprint()
    {
        var first = DisplayLayoutBuilder.Build(
            [Monitor(1, @"\\.\DISPLAY1", 0, 0, 1920, 1080, true), Monitor(2, @"\\.\DISPLAY2", 1920, 0, 1920, 1080)],
            new Dictionary<string, DisplayTarget> { [@"\\.\DISPLAY1"] = new(PathA, "A", 0), [@"\\.\DISPLAY2"] = new(PathB, "B", 0) });
        var second = DisplayLayoutBuilder.Build(
            [Monitor(7, @"\\.\DISPLAY5", 0, 0, 1920, 1080, true), Monitor(8, @"\\.\DISPLAY3", 1920, 0, 1920, 1080)],
            new Dictionary<string, DisplayTarget> { [@"\\.\DISPLAY5"] = new(PathA, "A", 0), [@"\\.\DISPLAY3"] = new(PathB, "B", 0) });

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void MissingTargetsFallBackToGdiNamesAndGenericName()
    {
        var layout = DisplayLayoutBuilder.Build([Monitor(1, @"\\.\DISPLAY1", 0, 0, 100, 100, true)], new Dictionary<string, DisplayTarget>());

        var display = Assert.Single(layout.Displays);
        Assert.Equal(@"gdi:\\.\display1", display.Id.RawValue);
        Assert.Equal("Display", display.Name);
        Assert.False(display.IsBuiltIn);
    }

    [Fact]
    public void TargetLookupIgnoresCase()
    {
        var targets = new Dictionary<string, DisplayTarget> { [@"\\.\display1"] = new(PathA, "A", 0) };
        var layout = DisplayLayoutBuilder.Build([Monitor(1, @"\\.\DISPLAY1", 0, 0, 100, 100)], targets);
        Assert.Equal("A", Assert.Single(layout.Displays).Name);
    }

    [Fact]
    public void DuplicateIdsGetSuffixesInLeftToRightOrder()
    {
        var monitors = new[] { Monitor(2, "b", 100, 0, 100, 100), Monitor(1, "a", 0, 0, 100, 100) };
        var targets = new Dictionary<string, DisplayTarget> { ["a"] = new(PathA, "M", 0), ["b"] = new(PathA, "M", 0) };

        var layout = DisplayLayoutBuilder.Build(monitors, targets);

        Assert.Equal(2, layout.Displays.Select(d => d.Id).Distinct().Count());
        Assert.EndsWith("uid4352", layout.Displays[0].Id.RawValue);
        Assert.EndsWith("uid4352#2", layout.Displays[1].Id.RawValue);
    }

    [Theory]
    [InlineData(0x80000000u, true)]
    [InlineData(11u, true)]
    [InlineData(13u, true)]
    [InlineData(5u, false)]
    [InlineData(10u, false)]
    [InlineData(0xFFFFFFFFu, false)]
    public void BuiltInOutputTechnologies(uint technology, bool builtIn) =>
        Assert.Equal(builtIn, DisplayLayoutBuilder.IsBuiltIn(technology));

    [Fact]
    public void InternalPanelWithoutFriendlyNameIsNamedBuiltIn()
    {
        var targets = new Dictionary<string, DisplayTarget> { ["a"] = new(PathA, string.Empty, 0x80000000u) };
        var display = Assert.Single(DisplayLayoutBuilder.Build([Monitor(1, "a", 0, 0, 100, 100, true)], targets).Displays);
        Assert.True(display.IsBuiltIn);
        Assert.Equal("Built-in display", display.Name);
    }

    [Fact]
    public void NoMonitorsGivesEmptyLayout() =>
        Assert.Empty(DisplayLayoutBuilder.Build([], new Dictionary<string, DisplayTarget>()).Displays);

    [Fact]
    public void CatalogReturnsLayoutAndRaisesChangeNotifications()
    {
        var native = new FakeDisplayNative(Monitor(1, "a", 0, 0, 100, 100, true));
        var catalog = new WindowsDisplayCatalog(native, listen: false);
        var raised = 0;
        catalog.LayoutChanged += () => raised++;

        Assert.Single(catalog.Current().Displays);
        catalog.NotifyChanged();
        catalog.NotifyChanged();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void CatalogReturnsEmptyLayoutWhenNativeThrows()
    {
        var catalog = new WindowsDisplayCatalog(new FakeDisplayNative { Throw = true }, listen: false);
        Assert.Empty(catalog.Current().Displays);
    }

    private sealed class FakeDisplayNative(params MonitorSnapshot[] monitors) : IDisplayNative
    {
        public bool Throw { get; init; }

        public IReadOnlyList<MonitorSnapshot> Monitors() => Throw ? throw new InvalidOperationException() : monitors;

        public IReadOnlyDictionary<string, DisplayTarget> Targets() => new Dictionary<string, DisplayTarget>();
    }
}
