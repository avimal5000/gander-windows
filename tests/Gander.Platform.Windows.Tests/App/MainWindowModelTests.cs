using System;
using System.Linq;
using Gander.App.Presentation;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.Platform.Windows.Tests.App;

public sealed class MainWindowModelTests
{
    private static TrayMenuInput Input(bool tracking = true, bool locked = false, HotKeyShortcut? shortcut = null) =>
        new("Tracking · Cam", StatusTone.Active, tracking, locked, shortcut ?? HotKeyShortcut.FocusLockDefault, null, "Looking at Left", [], null);

    [Fact]
    public void HomeShowsTheTwoSwitchesWithWhatTheyDo()
    {
        var home = MainWindowModel.Home(Input(tracking: true, locked: true));
        Assert.Equal([TrayCommand.ToggleTracking, TrayCommand.ToggleFocusLock], home.Switches.Select(s => s.Command));
        Assert.All(home.Switches, s => Assert.True(s.IsOn));
        Assert.All(home.Switches, s => Assert.False(string.IsNullOrWhiteSpace(s.Help)));
        Assert.Equal("Ctrl+Alt+Shift+L", home.Switches[1].ShortcutText);
        Assert.Null(home.Switches[0].ShortcutText);
        Assert.Equal("Tracking · Cam", home.StatusLine);
        Assert.Equal("Looking at Left", home.LookText);
    }

    [Fact]
    public void HomeLinksGoToCalibrateSettingsTourAndTroubleshooting()
    {
        var links = MainWindowModel.Home(Input()).Links;
        Assert.Equal(["Calibrate", "Settings", "Welcome Tour", "Troubleshooting"], links.Select(l => l.Title));
        Assert.Equal(TrayCommand.Calibrate, links[0].Command);
        Assert.Equal(GanderPage.Settings, links[1].Page);
        Assert.Equal(TrayCommand.WelcomeTour, links[2].Command);
        Assert.Equal(GanderPage.Troubleshooting, links[3].Page);
        Assert.Equal(TrayCommand.None, links[3].Command);
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Help)));
    }

    [Fact]
    public void TroubleshootingOpensTheFourTools()
    {
        Assert.Equal(
            [TrayCommand.PoseDebug, TrayCommand.GateTest, TrayCommand.FocusTest, TrayCommand.Diagnostics],
            MainWindowModel.Troubleshooting.Select(t => t.Command));
        Assert.All(MainWindowModel.Troubleshooting, t => Assert.Null(t.Page));
    }

    [Theory]
    [InlineData(GanderPage.Home, false)]
    [InlineData(GanderPage.Settings, true)]
    [InlineData(GanderPage.Setup, true)]
    [InlineData(GanderPage.Troubleshooting, true)]
    public void EveryPageButHomeHasBack(GanderPage page, bool back) => Assert.Equal(back, MainWindowModel.HasBack(page));

    [Fact]
    public void BackReturnsToThePageBefore()
    {
        var history = new PageHistory();
        history.Open(GanderPage.Settings);
        history.Open(GanderPage.Setup);
        Assert.Equal(GanderPage.Setup, history.Current);
        Assert.True(history.Back());
        Assert.Equal(GanderPage.Settings, history.Current);
        Assert.True(history.Back());
        Assert.Equal(GanderPage.Home, history.Current);
        Assert.False(history.Back());
        Assert.Equal(GanderPage.Home, history.Current);
    }

    [Fact]
    public void BackSaysWhereItGoes()
    {
        var history = new PageHistory();
        history.Open(GanderPage.Settings);
        Assert.Equal("Home", MainWindowModel.BackTo(history.Previous));
        history.Open(GanderPage.Setup);
        Assert.Equal("Settings", MainWindowModel.BackTo(history.Previous));
    }

    [Fact]
    public void OpeningAPageOnTheWayBackPopsToItInsteadOfStackingIt()
    {
        var history = new PageHistory();
        history.Open(GanderPage.Settings);
        history.Open(GanderPage.Setup);
        history.Open(GanderPage.Settings);
        Assert.Equal([GanderPage.Home, GanderPage.Settings], history.Pages);
        history.Open(GanderPage.Home);
        Assert.Equal([GanderPage.Home], history.Pages);
    }

    [Fact]
    public void TheMenuOffersToOpenTheWindowFirst()
    {
        var menu = TrayMenuModel.Build(Input()).Where(e => !e.IsSeparator).ToList();
        var open = menu.FindIndex(e => e.Command == TrayCommand.OpenMain);
        Assert.Equal(open, menu.FindIndex(e => e.Role == TrayEntryRole.Normal));
        Assert.Equal(TrayMenuModel.OpenMainText, menu[open].Text);
        Assert.False(TrayMenuModel.KeepsMenuOpen(TrayCommand.OpenMain));
    }
}
