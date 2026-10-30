using System.Text.Json;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.Core.Tests.Controller;

/// <summary>Port of the Mac AppModelFocusLockTests ("AppModel Focus Lock and its shortcut").</summary>
public class FocusLockTests
{
    private static readonly HotKeyShortcut Other = new(0x4B, HotKeyModifiers.Control | HotKeyModifiers.Alt, "K");
    private static readonly HotKeyShortcut Another = new(0x4A, HotKeyModifiers.Win | HotKeyModifiers.Shift, "J");

    private static HotKeyShortcut? StoredShortcut(Harness h) =>
        h.Settings.Values.TryGetValue(SettingsKeys.FocusLockShortcut, out var json) && json.Length > 0
            ? JsonSerializer.Deserialize<HotKeyShortcut>(json, EngineJson.Options)
            : null;

    private static void Press(Harness h, FakeHotKeys hotKeys)
    {
        hotKeys.Press();
        h.Drain();
    }

    // MARK: The lock

    [Fact]
    public void FocusLockIsOffAtLaunchAndTogglesBothWays()
    {
        using var h = Harness.Make();
        Assert.False(h.Controller.FocusLocked);

        h.Controller.ToggleFocusLock();
        Assert.True(h.Controller.FocusLocked);
        h.Controller.ToggleFocusLock();
        Assert.False(h.Controller.FocusLocked);

        h.Controller.SetFocusLock(true);
        h.Controller.SetFocusLock(true);
        Assert.True(h.Controller.FocusLocked);
    }

    [Fact]
    public void FocusLockIsNotSaved()
    {
        using var h = Harness.Make();
        h.Controller.SetFocusLock(true);

        using var relaunched = h.Relaunch();
        Assert.False(relaunched.Controller.FocusLocked);
    }

    [Fact]
    public async Task FocusLockShowsInTheStatusAndKeepsTheCameraOn()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Feed(1);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);

        h.Controller.SetFocusLock(true);

        Assert.Equal(TrackingStatus.FocusLock, h.Controller.TrackingStatus);
        Assert.Equal(TrayIconState.FocusLock, h.Controller.TrayIcon);
        Assert.Equal("Focus Lock on · Integrated Camera", h.Controller.StatusLine);
        Assert.True(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task TheDeciderIsToldTheLockIsOn()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();

        h.Feed(1);
        h.Controller.SetFocusLock(true);
        h.Feed(1.1);

        Assert.Equal([false, true], h.Engine.LastRecorder!.Contexts.Select(c => c.IsFocusLocked));
    }

    // MARK: Shortcut at launch

    [Fact]
    public void TheDefaultShortcutIsRegisteredAtLaunchAndPressingItTogglesTheLock()
    {
        var hotKeys = new FakeHotKeys();
        using var h = Harness.Make(hotKeys: hotKeys);

        Assert.Equal(HotKeyShortcut.FocusLockDefault, h.Controller.FocusLockShortcut);
        Assert.Equal(HotKeyShortcut.FocusLockDefault, hotKeys.Registered[HotKeyAction.ToggleFocusLock]);
        Assert.Null(h.Controller.ShortcutMessage);

        Press(h, hotKeys);
        Assert.True(h.Controller.FocusLocked);
        Press(h, hotKeys);
        Assert.False(h.Controller.FocusLocked);
    }

    [Fact]
    public void PressingTheShortcutTellsTheUiSoItCanShowTheNotice()
    {
        var hotKeys = new FakeHotKeys();
        using var h = Harness.Make(hotKeys: hotKeys);
        var notices = new List<bool>();
        h.Controller.FocusLockToggledByShortcut += notices.Add;

        Press(h, hotKeys);
        h.Controller.ToggleFocusLock(); // The menu switch shows nothing.
        Press(h, hotKeys);

        Assert.Equal([true, true], notices);
    }

    [Fact]
    public void ASavedShortcutIsRestoredAndRegistered()
    {
        using var first = Harness.Make();
        Assert.True(first.Controller.SetFocusLockShortcut(Other));

        var hotKeys = new FakeHotKeys();
        using var relaunched = first.Relaunch(hotKeys: hotKeys);

        Assert.Equal(Other, relaunched.Controller.FocusLockShortcut);
        Assert.Equal(Other, hotKeys.Registered[HotKeyAction.ToggleFocusLock]);
    }

    [Fact]
    public void ASavedClearMeansNoShortcutNotTheDefault()
    {
        using var first = Harness.Make();
        Assert.True(first.Controller.SetFocusLockShortcut(null));
        Assert.Equal(string.Empty, first.Settings.Values[SettingsKeys.FocusLockShortcut]);

        var hotKeys = new FakeHotKeys();
        using var relaunched = first.Relaunch(hotKeys: hotKeys);

        Assert.Null(relaunched.Controller.FocusLockShortcut);
        Assert.Empty(hotKeys.Registered);
        Assert.Empty(hotKeys.RegisterCalls);
    }

    [Fact]
    public void AShortcutThatDoesNotDecodeFallsBackToTheDefault()
    {
        using var h = Harness.Make(seed: s => s.Values[SettingsKeys.FocusLockShortcut] = "not json");

        Assert.Equal(HotKeyShortcut.FocusLockDefault, h.Controller.FocusLockShortcut);
    }

    // MARK: Registration failures

    [Fact]
    public void AShortcutAnotherAppHasShowsThatMessageAndNamesTheShortcut()
    {
        var hotKeys = new FakeHotKeys();
        hotKeys.Failures.Add((HotKeyShortcut.FocusLockDefault, new HotKeyException(HotKeyErrorKind.ShortcutInUse, "in use")));
        using var h = Harness.Make(calibrated: true, hotKeys: hotKeys);

        Assert.Equal(
            "Ctrl+Alt+Shift+L is already used by another app. Choose another shortcut in Settings.",
            h.Controller.ShortcutMessage);
        Assert.Empty(hotKeys.Registered);
        Assert.Equal(HotKeyShortcut.FocusLockDefault, h.Controller.FocusLockShortcut); // Still the chosen shortcut, just not working.
        Assert.Equal(h.Controller.ShortcutMessage, h.Controller.MenuShortcutMessage);
        Assert.False(h.Controller.IsShortcutRegistered);
    }

    [Fact]
    public void AnyOtherFailureSaysTheShortcutCouldNotBeSetUp()
    {
        foreach (var kind in new[] { HotKeyErrorKind.RegistrationFailed, HotKeyErrorKind.InvalidShortcut })
        {
            var hotKeys = new FakeHotKeys();
            hotKeys.Failures.Add((HotKeyShortcut.FocusLockDefault, new HotKeyException(kind, "x")));
            using var h = Harness.Make(hotKeys: hotKeys);

            Assert.Equal("The Focus Lock shortcut could not be set up.", h.Controller.ShortcutMessage);
        }
    }

    // MARK: Changing the shortcut

    [Fact]
    public void ANewShortcutIsRegisteredSavedAndClearsTheMessage()
    {
        var hotKeys = new FakeHotKeys();
        hotKeys.Failures.Add((HotKeyShortcut.FocusLockDefault, new HotKeyException(HotKeyErrorKind.ShortcutInUse, "in use")));
        using var h = Harness.Make(hotKeys: hotKeys);
        Assert.NotNull(h.Controller.ShortcutMessage);

        var accepted = h.Controller.SetFocusLockShortcut(Other);

        Assert.True(accepted);
        Assert.Equal(Other, h.Controller.FocusLockShortcut);
        Assert.Equal(Other, hotKeys.Registered[HotKeyAction.ToggleFocusLock]);
        Assert.Equal(Other, StoredShortcut(h));
        Assert.Null(h.Controller.ShortcutMessage);
        Press(h, hotKeys);
        Assert.True(h.Controller.FocusLocked); // The new shortcut works.
    }

    [Fact]
    public void AFailedChangeKeepsThePreviousShortcutRegisteredAndSaved()
    {
        var hotKeys = new FakeHotKeys();
        hotKeys.Failures.Add((Other, new HotKeyException(HotKeyErrorKind.ShortcutInUse, "in use")));
        using var h = Harness.Make(hotKeys: hotKeys);
        Assert.True(h.Controller.SetFocusLockShortcut(Another));

        var accepted = h.Controller.SetFocusLockShortcut(Other);

        Assert.False(accepted);
        Assert.Equal(Another, h.Controller.FocusLockShortcut);
        Assert.Equal(Another, hotKeys.Registered[HotKeyAction.ToggleFocusLock]); // The old shortcut is registered again.
        Assert.Equal(Another, StoredShortcut(h));
        Assert.Contains("already used by another app", h.Controller.ShortcutMessage);
        Assert.Null(h.Controller.MenuShortcutMessage); // Settings says it; the menu needn't.
        Press(h, hotKeys);
        Assert.True(h.Controller.FocusLocked); // The old shortcut still works.
    }

    [Fact]
    public void AFailedChangeWhenTheUserHadNoShortcutLeavesNone()
    {
        var hotKeys = new FakeHotKeys();
        hotKeys.Failures.Add((Other, new HotKeyException(HotKeyErrorKind.RegistrationFailed, "x")));
        using var h = Harness.Make(hotKeys: hotKeys);
        Assert.True(h.Controller.SetFocusLockShortcut(null));

        Assert.False(h.Controller.SetFocusLockShortcut(Other));

        Assert.Null(h.Controller.FocusLockShortcut);
        Assert.Empty(hotKeys.Registered);
        Assert.Equal("The Focus Lock shortcut could not be set up.", h.Controller.ShortcutMessage);
        Assert.Null(h.Controller.MenuShortcutMessage); // The user chose no shortcut, so the menu has nothing to warn about.
    }

    [Fact]
    public void ClearingRemovesTheShortcutAndTheMessage()
    {
        var hotKeys = new FakeHotKeys();
        hotKeys.Failures.Add((HotKeyShortcut.FocusLockDefault, new HotKeyException(HotKeyErrorKind.ShortcutInUse, "in use")));
        using var h = Harness.Make(hotKeys: hotKeys);

        Assert.True(h.Controller.SetFocusLockShortcut(null));

        Assert.Null(h.Controller.FocusLockShortcut);
        Assert.Equal([HotKeyAction.ToggleFocusLock], hotKeys.UnregisterCalls);
        Assert.Null(h.Controller.ShortcutMessage);
        Assert.Null(StoredShortcut(h));
        Assert.Equal(string.Empty, h.Settings.Values[SettingsKeys.FocusLockShortcut]);
    }

    [Fact]
    public void ResettingToTheDefaultRegistersItAgain()
    {
        using var h = Harness.Make();
        Assert.True(h.Controller.SetFocusLockShortcut(Other));

        Assert.True(h.Controller.ResetFocusLockShortcut());

        Assert.Equal(HotKeyShortcut.FocusLockDefault, h.HotKeys.Registered[HotKeyAction.ToggleFocusLock]);
        Assert.Equal(HotKeyShortcut.FocusLockDefault, StoredShortcut(h));
    }

    [Fact]
    public void TheShortcutsRegisteredCountsMatchTheCalls()
    {
        using var h = Harness.Make();
        Assert.Equal([HotKeyShortcut.FocusLockDefault], h.HotKeys.RegisterCalls); // Once at launch, nothing else.
    }
}
