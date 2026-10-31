using System;
using System.Reflection;
using System.Threading.Tasks;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.Core.Controller;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Gander.App.Tray;

/// <summary>
/// The NotifyIcon and its menu. Thin: the menu content comes from <see cref="TrayMenuModel"/>, the icon from
/// <see cref="TrayIconRenderer"/>, and both are refreshed when the controller says its state changed.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly TrackingController _controller;
    private readonly Action _openCalibration;
    private readonly Action _openSettings;
    private readonly Action _openDiagnostics;
    private readonly Action _openPoseDebug;
    private readonly Action _openGateTest;
    private readonly Action _openFocusTest;
    private readonly Action _quit;
    private readonly Action<string> _log;
    private readonly Action<bool> _menuOpen;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu = new();
    private Drawing.Icon? _icon;
    private TrayIconState? _shownState;
    private bool? _shownLight;

    /// <summary>Creates and shows the tray icon. <paramref name="menuOpen"/> is told when the menu opens and closes.</summary>
    public TrayHost(
        TrackingController controller,
        Action openCalibration,
        Action openSettings,
        Action openDiagnostics,
        Action openPoseDebug,
        Action openGateTest,
        Action openFocusTest,
        Action quit,
        Action<bool> menuOpen,
        Action<string> log)
    {
        _controller = controller;
        _openCalibration = openCalibration;
        _openSettings = openSettings;
        _openDiagnostics = openDiagnostics;
        _openPoseDebug = openPoseDebug;
        _openGateTest = openGateTest;
        _openFocusTest = openFocusTest;
        _quit = quit;
        _menuOpen = menuOpen;
        _log = log;

        _menu.Opening += (_, _) =>
        {
            _menuOpen(true);
            Rebuild();
        };
        _menu.Closed += (_, _) => _menuOpen(false);
        _menu.HandleCreated += (_, _) => ThemedMenu.Round(_menu.Handle);
        _tray = new Forms.NotifyIcon { ContextMenuStrip = _menu, Visible = false };
        _tray.MouseClick += OnMouseClick;
        _controller.StateChanged += Refresh;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        Refresh();
        _tray.Visible = true;
    }

    /// <summary>Updates the icon and tooltip from the controller.</summary>
    public void Refresh()
    {
        var light = SystemInfo.IsLightTaskbar();
        var state = _controller.TrayIcon;
        if (state != _shownState || light != _shownLight || _icon is null)
        {
            var size = TrayIconAppearance.NearestSize(Forms.SystemInformation.SmallIconSize.Width);
            var next = TrayIconRenderer.Render(state, size, light);
            _tray.Icon = next;
            _icon?.Dispose();
            _icon = next;
            _shownState = state;
            _shownLight = light;
        }

        _tray.Text = TrayIconAppearance.Tooltip(_controller.StatusLine);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _controller.StateChanged -= Refresh;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Refresh();
        }
    }

    // Left click opens the same menu as right click. NotifyIcon has no public call for this; the private one places the
    // menu at the icon and handles focus, and we never read the cursor ourselves.
    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left)
        {
            return;
        }

        try
        {
            typeof(Forms.NotifyIcon)
                .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_tray, null);
        }
        catch (Exception exception)
        {
            _log($"Could not open the tray menu on left click: {exception.Message}");
        }
    }

    private void Rebuild()
    {
        var status = _controller.TrackingStatus;
        var facts = new TrayBannerFacts(
            _controller.PermissionPrompt,
            _controller.Library.Profiles.Count == 0,
            _controller.TrackingEnabled,
            _controller.Layout.Displays.Count,
            _controller.ActiveCalibration is not null,
            _controller.MenuShortcutMessage);
        var input = new TrayMenuInput(
            _controller.StatusLine,
            status.Tone,
            _controller.TrackingEnabled,
            _controller.FocusLocked,
            _controller.FocusLockShortcut,
            TrayBanner.Current(facts),
            LookSummary.Text(_controller.IsCameraActive, _controller.LastDecision?.Classification, _controller.DisplayName),
            _controller.Devices,
            _controller.SelectedCameraId);
        ThemedMenu.Apply(_menu, AppTheme.Current);
        var scale = _menu.DeviceDpi / 96f;
        _menu.Items.Clear();
        foreach (var entry in TrayMenuModel.Build(input))
        {
            var item = ToItem(entry);
            ThemedMenu.StyleItem(item, entry, scale);
            _menu.Items.Add(item);
        }
    }

    private Forms.ToolStripItem ToItem(TrayMenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            return new Forms.ToolStripSeparator();
        }

        var item = new Forms.ToolStripMenuItem(entry.Text) { Enabled = entry.Enabled, Checked = entry.Checked };
        if (entry.ShortcutText is { } shortcut)
        {
            item.ShortcutKeyDisplayString = shortcut;
        }

        if (entry.Children is { } children)
        {
            foreach (var child in children)
            {
                if (item.DropDown is Forms.ToolStripDropDownMenu dropDown)
                {
                    dropDown.ShowImageMargin = false;
                    dropDown.ShowCheckMargin = true;
                    dropDown.HandleCreated += (_, _) => ThemedMenu.Round(dropDown.Handle);
                }

                var sub = ToItem(child);
                ThemedMenu.StyleItem(sub, child, _menu.DeviceDpi / 96f);
                item.DropDownItems.Add(sub);
            }
        }
        else if (entry.Command != TrayCommand.None)
        {
            item.Click += (_, _) => Run(entry);
        }

        return item;
    }

    private void Run(TrayMenuEntry entry)
    {
        switch (entry.Command)
        {
            case TrayCommand.ToggleTracking:
                Fire(_controller.SetTrackingEnabledAsync(!_controller.TrackingEnabled));
                break;
            case TrayCommand.ToggleFocusLock:
                _controller.ToggleFocusLock();
                break;
            case TrayCommand.SelectCamera:
                Fire(_controller.SelectCameraAsync(entry.Argument));
                break;
            case TrayCommand.Calibrate:
                _openCalibration();
                break;
            case TrayCommand.Settings:
                _openSettings();
                break;
            case TrayCommand.PoseDebug:
                _openPoseDebug();
                break;
            case TrayCommand.GateTest:
                _openGateTest();
                break;
            case TrayCommand.FocusTest:
                _openFocusTest();
                break;
            case TrayCommand.Diagnostics:
                _openDiagnostics();
                break;
            case TrayCommand.OpenCameraSettings:
                ShellLauncher.OpenCameraSettings();
                break;
            case TrayCommand.Quit:
                _quit();
                break;
        }
    }

    private void Fire(Task task) =>
        _ = task.ContinueWith(
            t => _log($"Menu action failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
