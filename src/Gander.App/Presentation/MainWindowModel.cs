using System.Collections.Generic;
using Gander.Core.Controller;

namespace Gander.App.Presentation;

/// <summary>The pages of the Gander window. Home is where the window opens; the others have a Back button.</summary>
public enum GanderPage
{
    Home,
    Settings,
    Setup,
    Troubleshooting,
}

/// <summary>A switch on the home page: its name, what it does, and whether it is on.</summary>
public sealed record HomeSwitch(TrayCommand Command, string Title, string Help, bool IsOn, string? ShortcutText);

/// <summary>A button that goes somewhere: a page of the window, or (on Troubleshooting) a tool window.</summary>
public sealed record HomeLink(string Title, string Help, GanderPage? Page = null, TrayCommand Command = TrayCommand.None);

/// <summary>Everything the home page shows, from the same state the tray menu reads.</summary>
public sealed record HomeView(
    string StatusLine,
    StatusTone Tone,
    TrayBanner? Banner,
    IReadOnlyList<HomeSwitch> Switches,
    string LookText,
    IReadOnlyList<HomeLink> Links);

/// <summary>
/// The Gander window opened from the tray (Windows only; the Mac has a popover). Pure, so the words, the order and the
/// navigation are tested: the home page shows the status, any banner, the Tracking and Focus Lock switches with what
/// they do, the "Looking at" line and links to Calibrate, Settings, the Welcome Tour and Troubleshooting. Pages other
/// than Home have a Back button; Troubleshooting opens the debug tools, which stay windows of their own because they
/// show live camera data beside what you are testing.
/// </summary>
public static class MainWindowModel
{
    /// <summary>The window's title bar.</summary>
    public const string WindowTitle = "Gander";

    /// <summary>The Back button on every page but Home.</summary>
    public const string BackText = "Back";

    /// <summary>The home page, built from the tray menu's input.</summary>
    public static HomeView Home(TrayMenuInput input) => new(
        input.StatusLine,
        input.Tone,
        input.Banner,
        [
            new HomeSwitch(TrayCommand.ToggleTracking, "Tracking", TrayMenuModel.Help.Tracking, input.TrackingEnabled, null),
            new HomeSwitch(
                TrayCommand.ToggleFocusLock,
                TrayMenuModel.FocusLockText,
                TrayMenuModel.Help.FocusLock,
                input.FocusLocked,
                input.FocusLockShortcut is { IsValid: true } shortcut ? shortcut.DisplayString : null),
        ],
        input.LookText,
        [
            new HomeLink("Calibrate", TrayMenuModel.Help.Calibrate, GanderPage.Setup, TrayCommand.Calibrate),
            new HomeLink("Settings", TrayMenuModel.Help.Settings, GanderPage.Settings),
            new HomeLink(TrayMenuModel.WelcomeTourText, TrayMenuModel.Help.WelcomeTour, GanderPage.Setup, TrayCommand.WelcomeTour),
            new HomeLink("Troubleshooting", TroubleshootingHelp, GanderPage.Troubleshooting),
        ]);

    /// <summary>What the Troubleshooting link leads to.</summary>
    public const string TroubleshootingHelp = "Tools that show what Gander sees, and details to send with a problem report.";

    /// <summary>The Troubleshooting page: one row per tool, each opening its own window.</summary>
    public static IReadOnlyList<HomeLink> Troubleshooting { get; } =
    [
        new("Pose Debug", "Shows the head angles Gander measures, live, and can record them.", Command: TrayCommand.PoseDebug),
        new("Head-Pose Gate Test", "Checks that Gander can tell your displays apart by where you look.", Command: TrayCommand.GateTest),
        new("Focus Test", "Checks that Gander can move keyboard focus between your displays.", Command: TrayCommand.FocusTest),
        new("Diagnostics", TrayMenuModel.Help.Diagnostics, Command: TrayCommand.Diagnostics),
    ];

    /// <summary>The heading of a page.</summary>
    public static string PageTitle(GanderPage page) => page switch
    {
        GanderPage.Settings => "Settings",
        GanderPage.Setup => "Set up Gander",
        GanderPage.Troubleshooting => "Troubleshooting",
        _ => WindowTitle,
    };

    /// <summary>Home has no Back button; every other page does.</summary>
    public static bool HasBack(GanderPage page) => page != GanderPage.Home;
}

/// <summary>
/// Where the window has been, so Back returns to the page before (Settings → Calibrate → Back is Settings again).
/// Opening a page that is already on the way back pops to it instead of stacking a second copy. Pure.
/// </summary>
public sealed class PageHistory
{
    private readonly List<GanderPage> _pages = [GanderPage.Home];

    /// <summary>The page shown now.</summary>
    public GanderPage Current => _pages[^1];

    /// <summary>The pages from Home to the current one.</summary>
    public IReadOnlyList<GanderPage> Pages => _pages;

    /// <summary>Goes to <paramref name="page"/>: Home clears the history; a page already in it is popped back to.</summary>
    public void Open(GanderPage page)
    {
        var index = _pages.LastIndexOf(page);
        if (index >= 0)
        {
            _pages.RemoveRange(index + 1, _pages.Count - index - 1);
            return;
        }

        _pages.Add(page);
    }

    /// <summary>Returns to the page before; false on Home, where there is nothing to go back to.</summary>
    public bool Back()
    {
        if (_pages.Count <= 1)
        {
            return false;
        }

        _pages.RemoveAt(_pages.Count - 1);
        return true;
    }
}
