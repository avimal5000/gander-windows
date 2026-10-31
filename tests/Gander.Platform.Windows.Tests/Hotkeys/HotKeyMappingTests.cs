using Gander.Core.Platform;
using Gander.Platform.Windows.Hotkeys;

namespace Gander.Platform.Windows.Tests.Hotkeys;

public sealed class HotKeyMappingTests
{
    [Fact]
    public void ControlAndAltUseWin32Bits()
    {
        Assert.Equal(0x2u, HotKeyMapping.ToNative(HotKeyModifiers.Control));
        Assert.Equal(0x1u, HotKeyMapping.ToNative(HotKeyModifiers.Alt));
        Assert.Equal(0x4u, HotKeyMapping.ToNative(HotKeyModifiers.Shift));
        Assert.Equal(0x8u, HotKeyMapping.ToNative(HotKeyModifiers.Win));
    }

    [Fact]
    public void DefaultShortcutIsCtrlAltShift() =>
        Assert.Equal(0x7u, HotKeyMapping.ToNative(HotKeyShortcut.FocusLockDefault.Modifiers));

    [Fact]
    public void NoModifiersIsZero() =>
        Assert.Equal(0u, HotKeyMapping.ToNative(HotKeyModifiers.None));

    [Fact]
    public void AllModifiers() =>
        Assert.Equal(0xFu, HotKeyMapping.ToNative(HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift | HotKeyModifiers.Win));

    [Fact]
    public void NoRepeatDoesNotOverlapModifiers() =>
        Assert.Equal(0u, HotKeyMapping.ModNoRepeat & 0xFu);

    [Theory]
    [InlineData(1409, HotKeyErrorKind.ShortcutInUse)]
    [InlineData(87, HotKeyErrorKind.InvalidShortcut)]
    [InlineData(5, HotKeyErrorKind.RegistrationFailed)]
    [InlineData(0, HotKeyErrorKind.RegistrationFailed)]
    public void Win32ErrorsMapToKinds(int error, HotKeyErrorKind kind)
    {
        var exception = HotKeyMapping.ErrorFor(HotKeyShortcut.FocusLockDefault, error);
        Assert.Equal(kind, exception.Kind);
        Assert.Contains("Ctrl+Alt+Shift+L", exception.Message);
    }
}
