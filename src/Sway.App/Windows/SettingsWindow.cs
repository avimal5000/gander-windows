using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.App.Windows;

/// <summary>
/// The Settings window. Thin: the controller owns the settings, <see cref="TimingPresets"/>, <see cref="ShortcutCapture"/>
/// and <see cref="SettingsText"/> decide numbers and words, and this lays out controls and copies state onto them.
/// The shortcut is captured by this window's own key handler (never a keyboard hook) and only while "Change" is active.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static readonly Color Forest = Color.FromRgb(0x2F, 0x5D, 0x3A);
    private static readonly Color Sage = Color.FromRgb(0x9C, 0xAF, 0x88);

    private readonly TrackingController _controller;
    private readonly ILoginItem _loginItem;
    private readonly Action _calibrateAgain;
    private readonly Brush _muted;
    private readonly Brush _accent;
    private readonly List<(TimingPreset Preset, RadioButton Button)> _presets = [];
    private readonly Slider _hold = new() { Minimum = SwitchSensitivity.DwellRange.Lower, Maximum = SwitchSensitivity.DwellRange.Upper, TickFrequency = 0.05, IsSnapToTickEnabled = true, SmallChange = 0.05, LargeChange = 0.25, MinWidth = 240 };
    private readonly Slider _pause = new() { Minimum = SwitchSensitivity.CooldownRange.Lower, Maximum = SwitchSensitivity.CooldownRange.Upper, TickFrequency = 0.05, IsSnapToTickEnabled = true, SmallChange = 0.05, LargeChange = 0.25, MinWidth = 240 };
    private readonly TextBlock _holdValue = new() { MinWidth = 56, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _pauseValue = new() { MinWidth = 56, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _shortcutValue = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _shortcutHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _change = new() { Content = "Change", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(12, 0, 8, 0) };
    private readonly Button _reset = new() { Content = "Reset", Padding = new Thickness(14, 5, 14, 5) };
    private readonly CheckBox _login = new() { Content = "Open Sway when I sign in to Windows" };
    private readonly ComboBox _cameras = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _cameraHint = new();
    private readonly TextBlock _calibration = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _calibrate = new() { Content = "Calibrate again", Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    private bool _updating;
    private bool _capturing;

    /// <summary>Creates the window. <paramref name="calibrateAgain"/> opens the Set up Sway window.</summary>
    public SettingsWindow(TrackingController controller, ILoginItem loginItem, Action calibrateAgain)
    {
        _controller = controller;
        _loginItem = loginItem;
        _calibrateAgain = calibrateAgain;

        Title = "Sway Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height * 0.92);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = SystemFonts.MessageFontFamily;
        FontSize = 14;

        var light = SystemInfo.IsLightApps();
        var surface = light ? Color.FromRgb(0xF6, 0xF3, 0xEA) : Color.FromRgb(0x2B, 0x2B, 0x2B);
        var ink = light ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF6, 0xF3, 0xEA);
        Background = new SolidColorBrush(surface);
        Foreground = new SolidColorBrush(ink);
        _muted = new SolidColorBrush(Color.FromArgb(0xB3, ink.R, ink.G, ink.B));
        _accent = new SolidColorBrush(light ? Forest : Sage);

        BuildContent();
        WireEvents();

        Loaded += (_, _) =>
        {
            _controller.StateChanged += Apply;
            Apply();
            _hold.Focus();
        };
        Closed += (_, _) => _controller.StateChanged -= Apply;
    }

    private void BuildContent()
    {
        var stack = new StackPanel { Margin = new Thickness(32, 24, 32, 28) };
        stack.Children.Add(new TextBlock { Text = "Settings", FontSize = 24, FontWeight = FontWeights.SemiBold });

        // Switching timing
        stack.Children.Add(Header("Switching timing"));
        stack.Children.Add(Note("How long you look at a screen before Sway switches to it, and how long it waits afterwards."));
        var presetRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        foreach (var preset in TimingPresets.All)
        {
            var button = new RadioButton { Content = preset.Name, GroupName = "timing", Margin = new Thickness(0, 0, 18, 0) };
            AutomationProperties.SetName(button, $"{preset.Name} timing");
            var chosen = preset;
            button.Checked += (_, _) =>
            {
                if (!_updating)
                {
                    _controller.SetSensitivity(TimingPresets.Apply(chosen, _controller.Sensitivity));
                }
            };
            _presets.Add((preset, button));
            presetRow.Children.Add(button);
        }

        stack.Children.Add(presetRow);
        stack.Children.Add(SliderRow("Hold time", _hold, _holdValue, "Hold time in seconds"));
        stack.Children.Add(SliderRow("Pause after a switch", _pause, _pauseValue, "Pause after a switch in seconds"));

        // Focus Lock shortcut
        stack.Children.Add(Header("Focus Lock shortcut"));
        var shortcutRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        shortcutRow.Children.Add(_shortcutValue);
        shortcutRow.Children.Add(_change);
        shortcutRow.Children.Add(_reset);
        stack.Children.Add(shortcutRow);
        stack.Children.Add(_shortcutHint);
        AutomationProperties.SetName(_shortcutValue, "Focus Lock shortcut");
        AutomationProperties.SetName(_change, "Change Focus Lock shortcut");
        AutomationProperties.SetName(_reset, "Reset Focus Lock shortcut to Ctrl+Alt+Shift+L");
        AutomationProperties.SetLiveSetting(_shortcutHint, AutomationLiveSetting.Polite);

        // Start with Windows
        stack.Children.Add(Header("Start with Windows"));
        _login.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(_login, "Open Sway when I sign in to Windows");
        stack.Children.Add(_login);

        // Camera
        stack.Children.Add(Header("Camera"));
        _cameras.Margin = new Thickness(0, 8, 0, 6);
        AutomationProperties.SetName(_cameras, "Camera");
        _cameraHint.Foreground = _muted;
        stack.Children.Add(_cameras);
        stack.Children.Add(_cameraHint);

        // Calibration
        stack.Children.Add(Header("Calibration"));
        _calibration.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(_calibrate, "Calibrate again");
        stack.Children.Add(_calibration);
        stack.Children.Add(_calibrate);

        // About
        stack.Children.Add(Header("About"));
        stack.Children.Add(new TextBlock { Text = $"Sway for Windows. {SettingsText.Version(SystemInfo.SwayVersion())}", Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(new TextBlock { Text = SettingsText.PrivacyLine, Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = SettingsText.NoticesSummary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = _muted });

        foreach (var button in new[] { _change, _reset, _calibrate })
        {
            StyleButton(button);
        }

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Background,
            Focusable = false,
        };
    }

    private void StyleButton(Button button) => button.Foreground = Foreground;

    private TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = _accent,
        Margin = new Thickness(0, 24, 0, 0),
    };

    private TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = _muted, Margin = new Thickness(0, 4, 0, 0) };

    private static Grid SliderRow(string label, Slider slider, TextBlock value, string automationName)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        slider.VerticalAlignment = VerticalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(slider, automationName);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(text);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid;
    }

    private void WireEvents()
    {
        _hold.ValueChanged += (_, e) =>
        {
            _holdValue.Text = TimingPresets.Seconds(TimingPresets.Snap(e.NewValue));
            if (!_updating)
            {
                _controller.SetHoldTime(e.NewValue);
            }
        };
        _pause.ValueChanged += (_, e) =>
        {
            _pauseValue.Text = TimingPresets.Seconds(TimingPresets.Snap(e.NewValue));
            if (!_updating)
            {
                _controller.SetCooldown(e.NewValue);
            }
        };
        _change.Click += (_, _) => SetCapturing(!_capturing);
        _reset.Click += (_, _) =>
        {
            SetCapturing(false);
            _controller.ResetFocusLockShortcut();
            Apply();
        };
        _login.Click += (_, _) =>
        {
            _loginItem.SetEnabled(_login.IsChecked == true);
            Apply();
        };
        _cameras.SelectionChanged += (_, _) =>
        {
            if (!_updating && _cameras.SelectedItem is ComboBoxItem { Tag: string id })
            {
                _ = _controller.SelectCameraAsync(id);
            }
        };
        _calibrate.Click += (_, _) => _calibrateAgain();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => SetCapturing(false);
    }

    private void SetCapturing(bool on)
    {
        _capturing = on;
        _change.Content = on ? "Cancel" : "Change";
        if (on)
        {
            _shortcutHint.Text = "Press the new shortcut now, for example Ctrl+Alt+K. Esc cancels.";
        }
        else if (_controller.ShortcutMessage is null)
        {
            _shortcutHint.Text = string.Empty;
        }
    }

    // Capture only happens here, in this window, while "Change" is active. There is no keyboard hook.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            SetCapturing(false);
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var capture = ShortcutCapture.From((uint)KeyInterop.VirtualKeyFromKey(key), CurrentModifiers());
        switch (capture.Kind)
        {
            case ShortcutCaptureKind.Accepted:
                SetCapturing(false);
                _controller.SetFocusLockShortcut(capture.Shortcut);
                Apply();
                break;
            default:
                _shortcutHint.Text = capture.Hint;
                break;
        }
    }

    private static HotKeyModifiers CurrentModifiers()
    {
        var held = Keyboard.Modifiers;
        var result = HotKeyModifiers.None;
        if (held.HasFlag(ModifierKeys.Control))
        {
            result |= HotKeyModifiers.Control;
        }

        if (held.HasFlag(ModifierKeys.Alt))
        {
            result |= HotKeyModifiers.Alt;
        }

        if (held.HasFlag(ModifierKeys.Shift))
        {
            result |= HotKeyModifiers.Shift;
        }

        if (held.HasFlag(ModifierKeys.Windows))
        {
            result |= HotKeyModifiers.Win;
        }

        return result;
    }

    private void Apply()
    {
        _updating = true;
        try
        {
            var sensitivity = _controller.Sensitivity;
            _hold.Value = sensitivity.Dwell;
            _pause.Value = sensitivity.Cooldown;
            _holdValue.Text = TimingPresets.Seconds(sensitivity.Dwell);
            _pauseValue.Text = TimingPresets.Seconds(sensitivity.Cooldown);
            var match = TimingPresets.Match(sensitivity);
            foreach (var (preset, button) in _presets)
            {
                button.IsChecked = preset == match;
            }

            _shortcutValue.Text = SettingsText.ShortcutValue(_controller.FocusLockShortcut);
            if (!_capturing)
            {
                _shortcutHint.Text = _controller.ShortcutMessage ?? string.Empty;
            }

            _shortcutHint.Visibility = string.IsNullOrEmpty(_shortcutHint.Text) ? Visibility.Collapsed : Visibility.Visible;
            _reset.IsEnabled = _controller.FocusLockShortcut != HotKeyShortcut.FocusLockDefault;

            _login.IsEnabled = _loginItem.Status != LoginItemStatus.Unavailable;
            _login.IsChecked = _loginItem.Status == LoginItemStatus.Enabled;

            FillCameras();

            var saved = _controller.ActiveCalibration?.Saved;
            _calibration.Text = SettingsText.Calibration(
                saved?.Displays.Count ?? 0,
                saved?.CreatedAt,
                _controller.ActiveCalibration?.IsPartial ?? false,
                _controller.CalibrationRunning);
        }
        finally
        {
            _updating = false;
        }
    }

    private void FillCameras()
    {
        var devices = _controller.Devices;
        var selectedId = _controller.SelectedCameraId ?? _controller.RunningDevice?.Id ?? (devices.Count > 0 ? devices[0].Id : null);
        var same = _cameras.Items.Count == devices.Count;
        for (var i = 0; same && i < devices.Count; i++)
        {
            same = _cameras.Items[i] is ComboBoxItem { Tag: string id } && id == devices[i].Id;
        }

        if (!same)
        {
            _cameras.Items.Clear();
            foreach (var device in devices)
            {
                _cameras.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
            }
        }

        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].Id == selectedId)
            {
                _cameras.SelectedIndex = i;
            }
        }

        _cameras.IsEnabled = devices.Count > 0;
        _cameraHint.Text = SettingsText.CameraHint(devices.Count);
    }
}
