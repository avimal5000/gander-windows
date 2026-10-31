using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Sway.Core;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.App.Windows;

/// <summary>
/// The Settings window, laid out like the Mac's: Appearance, General, Switching, Focus Lock shortcut, Camera and
/// Calibrations, each in a card, then an About block that only Windows has. Thin: the controller owns the settings,
/// <see cref="SettingsWording"/> and <see cref="TimingPresets"/> decide words and numbers, <see cref="SwayStyle"/> decides
/// how things look, and this lays out controls and copies state onto them. The shortcut is captured by this window's
/// own key handler (never a keyboard hook) and only while "Record Shortcut" is active.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly TrackingController _controller;
    private readonly ILoginItem _loginItem;
    private readonly Action _calibrateAgain;
    private readonly AppPreferences _preferences;
    private SwayStyle _style = SwayStyle.Current();
    private bool _updating;
    private bool _capturing;
    private string _rowsKey = string.Empty;

    private RadioButton _accentSway = null!;
    private RadioButton _accentSystem = null!;
    private CheckBox _glow = null!;
    private CheckBox _notice = null!;
    private CheckBox _login = null!;
    private TextBlock _loginNote = null!;
    private Slider _hold = null!;
    private Slider _pause = null!;
    private Slider _confidence = null!;
    private TextBlock _holdValue = null!;
    private TextBlock _pauseValue = null!;
    private TextBlock _confidenceValue = null!;
    private Button _instant = null!;
    private Button _restore = null!;
    private TextBlock _shortcutValue = null!;
    private Button _record = null!;
    private Button _reset = null!;
    private Button _clear = null!;
    private TextBlock _shortcutHint = null!;
    private TextBlock _shortcutMessage = null!;
    private ComboBox _cameras = null!;
    private TextBlock _cameraHint = null!;
    private StackPanel _calibrationRows = null!;

    /// <summary>Creates the window. <paramref name="calibrateAgain"/> opens the Set up Gander window.</summary>
    public SettingsWindow(TrackingController controller, ILoginItem loginItem, Action calibrateAgain, AppPreferences preferences)
    {
        _controller = controller;
        _loginItem = loginItem;
        _calibrateAgain = calibrateAgain;
        _preferences = preferences;

        Title = "Gander Settings";
        Width = SwayDesign.Size.SettingsWidth;
        Height = Math.Min(640, Math.Max(400, SystemParameters.WorkArea.Height * 0.92));
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildContent();

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => SetCapturing(false);
        Loaded += (_, _) =>
        {
            _controller.StateChanged += Apply;
            Apply();
        };
        Closed += (_, _) => _controller.StateChanged -= Apply;
    }

    private void BuildContent()
    {
        _style = SwayStyle.Current();
        _style.ApplyTo(this);
        var s = _style;

        _accentSway = s.MakeSegment(AccentChoices.Title(AccentChoice.Gander), "accent");
        _accentSystem = s.MakeSegment(AccentChoices.Title(AccentChoice.System), "accent");
        _glow = s.MakeSwitch(SettingsWording.GlowToggle);
        _notice = s.MakeSwitch(SettingsWording.NoticeToggle);
        _login = s.MakeSwitch(SettingsWording.StartWithWindows);
        _loginNote = s.Label(SettingsWording.StartWithWindowsUnavailable, SwayDesign.Type.Callout, secondary: true);
        _hold = MakeSlider(SwitchSensitivity.DwellRange.Lower, SwitchSensitivity.DwellRange.Upper, 0.05);
        _pause = MakeSlider(SwitchSensitivity.CooldownRange.Lower, SwitchSensitivity.CooldownRange.Upper, 0.05);
        _confidence = MakeSlider(0, SwitchSensitivity.ConfidenceMargins.Count - 1, 1);
        _holdValue = Value();
        _pauseValue = Value();
        _confidenceValue = Value();
        _instant = s.MakeButton(SettingsWording.UseInstant);
        _instant.ToolTip = SettingsWording.InstantHelp;
        _restore = s.MakeButton(SettingsWording.RestoreDefaults);
        _shortcutValue = s.Label(string.Empty, SwayDesign.Type.Chip, wrap: false);
        _record = s.MakeButton(SettingsWording.RecordShortcut);
        _reset = s.MakeButton(SettingsWording.ResetTo(HotKeyShortcut.FocusLockDefault));
        _clear = s.MakeButton(SettingsWording.Clear);
        _shortcutHint = s.Label(string.Empty, SwayDesign.Type.Callout);
        _shortcutHint.Foreground = s.Amber;
        _shortcutMessage = s.Label(string.Empty, SwayDesign.Type.Callout);
        _shortcutMessage.Foreground = s.Brick;
        _cameras = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Background = s.Card, Foreground = s.Text };
        _cameraHint = s.Label(string.Empty, SwayDesign.Type.Caption, secondary: true);
        _calibrationRows = new StackPanel();

        AutomationProperties.SetName(_accentSway, AccentChoices.Title(AccentChoice.Gander));
        AutomationProperties.SetName(_accentSystem, AccentChoices.Title(AccentChoice.System));
        AutomationProperties.SetName(_hold, "Hold time in seconds");
        AutomationProperties.SetName(_pause, "Pause after a switch in seconds");
        AutomationProperties.SetName(_confidence, "Switch confidence");
        AutomationProperties.SetName(_shortcutValue, "Focus Lock shortcut");
        AutomationProperties.SetName(_record, SettingsWording.RecordShortcut);
        AutomationProperties.SetName(_cameras, "Camera");
        AutomationProperties.SetLiveSetting(_shortcutHint, AutomationLiveSetting.Polite);

        var stack = new StackPanel { Margin = new Thickness(SwayDesign.Space.SettingsPadding) };
        stack.Children.Add(Section(SettingsWording.Appearance, AppearanceBody()));
        stack.Children.Add(Section(SettingsWording.General, GeneralBody()));
        stack.Children.Add(Section(SettingsWording.Switching, SwitchingBody()));
        stack.Children.Add(Section(SettingsWording.ShortcutSection, ShortcutBody()));
        stack.Children.Add(Section(SettingsWording.CameraSection, CameraBody()));
        stack.Children.Add(Section(SettingsWording.Calibrations, CalibrationsBody()));
        stack.Children.Add(Section(SettingsWording.About, AboutBody()));

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = s.Surface,
            Focusable = false,
        };

        WireEvents();
    }

    private Slider MakeSlider(double min, double max, double step) => new()
    {
        Minimum = min,
        Maximum = max,
        TickFrequency = step,
        IsSnapToTickEnabled = true,
        SmallChange = step,
        LargeChange = step * 5,
        MinWidth = 240,
        Foreground = _style.Accent,
    };

    private TextBlock Value()
    {
        var block = _style.Label(string.Empty, SwayDesign.Type.Body, secondary: true, wrap: false);
        block.HorizontalAlignment = HorizontalAlignment.Right;
        return block;
    }

    private Border Section(string title, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(_style.Label(title, SwayDesign.Type.Headline));
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, SwayDesign.Space.Lg + 2, 0, 0));
        panel.Children.Add(body);
        var card = _style.MakeCard(panel);
        card.Margin = new Thickness(0, 0, 0, SwayDesign.Space.Section);
        return card;
    }

    private UIElement AppearanceBody()
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        segments.Children.Add(_accentSway);
        segments.Children.Add(_accentSystem);
        var panel = new StackPanel();
        panel.Children.Add(_style.Label(SettingsWording.AccentLabel, SwayDesign.Type.Body));
        var host = _style.MakeSegmentHost(segments);
        host.Margin = new Thickness(0, SwayDesign.Space.Md, 0, SwayDesign.Space.Xl);
        panel.Children.Add(host);
        _glow.Margin = new Thickness(0, 0, 0, SwayDesign.Space.Xl);
        panel.Children.Add(_glow);
        panel.Children.Add(_notice);
        return panel;
    }

    private UIElement GeneralBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(_login);
        _loginNote.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        panel.Children.Add(_loginNote);
        return panel;
    }

    private UIElement SwitchingBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(SliderBlock(SettingsWording.HoldTime, _holdValue, _hold, SettingsWording.HoldTimeHelp, null, null));
        panel.Children.Add(SliderBlock(SettingsWording.Pause, _pauseValue, _pause, SettingsWording.PauseHelp, null, null));
        panel.Children.Add(SliderBlock(SettingsWording.Confidence, _confidenceValue, _confidence, SettingsWording.ConfidenceHelp, SettingsWording.ConfidenceMin, SettingsWording.ConfidenceMax));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _instant.Margin = new Thickness(0, 0, SwayDesign.Space.Lg, 0);
        buttons.Children.Add(_instant);
        buttons.Children.Add(_restore);
        panel.Children.Add(buttons);
        return panel;
    }

    private UIElement SliderBlock(string label, TextBlock value, Slider slider, string help, string? minLabel, string? maxLabel)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, SwayDesign.Space.Xl) };
        var header = new Grid();
        header.Children.Add(_style.Label(label, SwayDesign.Type.Body, wrap: false));
        header.Children.Add(value);
        block.Children.Add(header);
        if (minLabel is null)
        {
            slider.Margin = new Thickness(0, SwayDesign.Space.Sm, 0, SwayDesign.Space.Sm);
            block.Children.Add(slider);
        }
        else
        {
            var row = new Grid { Margin = new Thickness(0, SwayDesign.Space.Sm, 0, SwayDesign.Space.Sm) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var min = _style.Label(minLabel, SwayDesign.Type.Caption, secondary: true, wrap: false);
            var max = _style.Label(maxLabel ?? string.Empty, SwayDesign.Type.Caption, secondary: true, wrap: false);
            min.VerticalAlignment = VerticalAlignment.Center;
            max.VerticalAlignment = VerticalAlignment.Center;
            slider.Margin = new Thickness(SwayDesign.Space.Lg, 0, SwayDesign.Space.Lg, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(max, 2);
            row.Children.Add(min);
            row.Children.Add(slider);
            row.Children.Add(max);
            block.Children.Add(row);
        }

        block.Children.Add(_style.Label(help, SwayDesign.Type.Caption, secondary: true));
        return block;
    }

    private UIElement ShortcutBody()
    {
        var panel = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = new Border
        {
            Child = _shortcutValue,
            Background = SwayStyle.Brush(_style.Theme.Line),
            CornerRadius = new CornerRadius(SwayDesign.Radius.Chip),
            Padding = new Thickness(10, 4, 10, 4),
        };
        Grid.SetColumn(_record, 2);
        row.Children.Add(chip);
        row.Children.Add(_record);
        panel.Children.Add(row);

        var second = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, SwayDesign.Space.Lg + 2, 0, 0) };
        _reset.Margin = new Thickness(0, 0, SwayDesign.Space.Lg, 0);
        second.Children.Add(_reset);
        second.Children.Add(_clear);
        panel.Children.Add(second);

        _shortcutHint.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        _shortcutMessage.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        panel.Children.Add(_shortcutHint);
        panel.Children.Add(_shortcutMessage);
        var help = _style.Label(SettingsWording.ShortcutHelp, SwayDesign.Type.Caption, secondary: true);
        help.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        panel.Children.Add(help);
        return panel;
    }

    private UIElement CameraBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(_cameras);
        _cameraHint.Margin = new Thickness(0, SwayDesign.Space.Md, 0, 0);
        panel.Children.Add(_cameraHint);
        return panel;
    }

    private UIElement CalibrationsBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(_calibrationRows);
        var calibrate = _style.MakeButton(SettingsWording.Calibrate);
        calibrate.HorizontalAlignment = HorizontalAlignment.Left;
        calibrate.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        calibrate.Click += (_, _) => _calibrateAgain();
        AutomationProperties.SetName(calibrate, SettingsWording.Calibrate);
        panel.Children.Add(calibrate);
        return panel;
    }

    private UIElement AboutBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(_style.Label($"Gander for Windows. {SettingsText.Version(SystemInfo.SwayVersion())}", SwayDesign.Type.Body));
        var privacy = _style.Label(SettingsText.PrivacyLine, SwayDesign.Type.Headline);
        privacy.Margin = new Thickness(0, SwayDesign.Space.Md, 0, 0);
        panel.Children.Add(privacy);
        var notices = _style.Label(SettingsText.NoticesSummary, SwayDesign.Type.Caption, secondary: true);
        notices.Margin = new Thickness(0, SwayDesign.Space.Md, 0, 0);
        panel.Children.Add(notices);
        return panel;
    }

    private void WireEvents()
    {
        _accentSway.Checked += (_, _) => ChooseAccent(AccentChoice.Gander);
        _accentSystem.Checked += (_, _) => ChooseAccent(AccentChoice.System);
        _glow.Click += (_, _) => _preferences.SetEdgeGlow(_glow.IsChecked == true);
        _notice.Click += (_, _) => _preferences.SetFocusLockNotice(_notice.IsChecked == true);
        _login.Click += (_, _) =>
        {
            _loginItem.SetEnabled(_login.IsChecked == true);
            Apply();
        };
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
        _confidence.ValueChanged += (_, e) =>
        {
            _confidenceValue.Text = SettingsWording.ConfidenceName((int)Math.Round(e.NewValue));
            if (!_updating)
            {
                _controller.SetSwitchConfidence((int)Math.Round(e.NewValue));
            }
        };
        _instant.Click += (_, _) => _controller.UseInstantTiming();
        _restore.Click += (_, _) => _controller.RestoreSensitivityDefaults();
        _record.Click += (_, _) => SetCapturing(!_capturing);
        _reset.Click += (_, _) =>
        {
            SetCapturing(false);
            _controller.ResetFocusLockShortcut();
            Apply();
        };
        _clear.Click += (_, _) =>
        {
            SetCapturing(false);
            _controller.SetFocusLockShortcut(null);
            Apply();
        };
        _cameras.SelectionChanged += (_, _) =>
        {
            if (!_updating && _cameras.SelectedItem is ComboBoxItem { Tag: string id })
            {
                _ = _controller.SelectCameraAsync(id.Length == 0 ? null : id);
            }
        };
    }

    private void ChooseAccent(AccentChoice choice)
    {
        if (_updating || choice == _preferences.Accent)
        {
            return;
        }

        _preferences.SetAccent(choice);
        // Every control takes its color from the accent, so build them again.
        BuildContent();
        _rowsKey = string.Empty;
        Apply();
    }

    private void SetCapturing(bool on)
    {
        _capturing = on;
        _record.Content = on ? SettingsWording.Cancel : SettingsWording.RecordShortcut;
        if (!on)
        {
            _shortcutHint.Text = string.Empty;
        }

        ShowState();
    }

    // Capture only happens here, in this window, while "Record Shortcut" is active. There is no keyboard hook.
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
                ShowState();
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

    private void ShowState()
    {
        _shortcutValue.Text = _capturing ? SettingsWording.TypeShortcut : SettingsText.ShortcutValue(_controller.FocusLockShortcut);
        _shortcutHint.Visibility = string.IsNullOrEmpty(_shortcutHint.Text) ? Visibility.Collapsed : Visibility.Visible;
        _shortcutMessage.Text = _controller.ShortcutMessage ?? string.Empty;
        _shortcutMessage.Visibility = string.IsNullOrEmpty(_shortcutMessage.Text) ? Visibility.Collapsed : Visibility.Visible;
        _reset.IsEnabled = !_capturing && _controller.FocusLockShortcut != HotKeyShortcut.FocusLockDefault;
        _clear.IsEnabled = !_capturing && _controller.FocusLockShortcut is not null;
    }

    private void Apply()
    {
        _updating = true;
        try
        {
            _accentSway.IsChecked = _preferences.Accent == AccentChoice.Gander;
            _accentSystem.IsChecked = _preferences.Accent == AccentChoice.System;
            _glow.IsChecked = _preferences.EdgeGlowEnabled;
            _notice.IsChecked = _preferences.FocusLockNoticeEnabled;

            var status = _loginItem.Status;
            _login.IsEnabled = status != LoginItemStatus.Unavailable;
            _login.IsChecked = status == LoginItemStatus.Enabled;
            _loginNote.Visibility = status == LoginItemStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed;

            var sensitivity = _controller.Sensitivity;
            _hold.Value = sensitivity.Dwell;
            _pause.Value = sensitivity.Cooldown;
            _confidence.Value = sensitivity.ConfidenceLevel;
            _holdValue.Text = TimingPresets.Seconds(sensitivity.Dwell);
            _pauseValue.Text = TimingPresets.Seconds(sensitivity.Cooldown);
            _confidenceValue.Text = SettingsWording.ConfidenceName(sensitivity.ConfidenceLevel);
            _instant.IsEnabled = sensitivity != sensitivity.ApplyingInstantTiming();
            _restore.IsEnabled = sensitivity != SwitchSensitivity.Default;

            ShowState();
            FillCameras();
            FillCalibrations();
        }
        finally
        {
            _updating = false;
        }
    }

    private void FillCameras()
    {
        var devices = _controller.Devices;
        var selected = _controller.SelectedCameraId;
        _cameras.Items.Clear();
        _cameras.Items.Add(new ComboBoxItem { Content = SettingsWording.DefaultCamera, Tag = string.Empty });
        foreach (var device in devices)
        {
            _cameras.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
        }

        var missing = selected is not null && devices.All(d => d.Id != selected);
        if (missing)
        {
            _cameras.Items.Add(new ComboBoxItem { Content = SettingsWording.UnavailableCamera, Tag = selected });
        }

        _cameras.SelectedIndex = selected is null
            ? 0
            : Math.Max(0, _cameras.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (string?)item.Tag == selected));
        _cameras.IsEnabled = !_controller.CalibrationRunning;
        _cameraHint.Text = SettingsText.CameraHint(devices.Count);
    }

    private void FillCalibrations()
    {
        var rows = SettingsWording.Rows(_controller.Library, _controller.ActiveCalibration?.Saved.Key, _controller.DisplayName);
        var key = string.Join("|", rows.Select(r => $"{r.Displays}/{r.Camera}/{r.Date:O}/{r.IsInUse}"));
        if (key == _rowsKey)
        {
            return;
        }

        _rowsKey = key;
        _calibrationRows.Children.Clear();
        if (rows.Count == 0)
        {
            _calibrationRows.Children.Add(_style.Label(SettingsWording.NoCalibrations, SwayDesign.Type.Callout, secondary: true));
            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                _calibrationRows.Children.Add(new Border { Height = SwayDesign.Space.Hairline, Background = _style.Line, Margin = new Thickness(0, SwayDesign.Space.Lg, 0, SwayDesign.Space.Lg) });
            }

            _calibrationRows.Children.Add(CalibrationRowView(rows[i]));
        }
    }

    private UIElement CalibrationRowView(CalibrationRow row)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(_style.Label(row.Displays, SwayDesign.Type.Body));
        if (row.IsInUse)
        {
            // A sage capsule with forest text, whatever the accent (Mac "In use").
            var pill = new Border
            {
                Child = new TextBlock
                {
                    Text = SettingsWording.InUse,
                    FontFamily = DesignFonts.Wpf(SwayDesign.Text),
                    FontSize = SwayDesign.Type.Caption.Size,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = SwayStyle.Brush(SwayDesign.Forest),
                },
                Background = SwayStyle.Brush(Rgba.Of(SwayDesign.Sage, 0.35)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(SwayDesign.Space.Md, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.Children.Add(pill);
        }

        text.Children.Add(title);
        var detail = _style.Label(row.Detail, SwayDesign.Type.Callout, secondary: true);
        detail.Margin = new Thickness(0, SwayDesign.Space.Xs, 0, 0);
        text.Children.Add(detail);

        var delete = _style.MakeButton(SettingsWording.Delete);
        delete.Foreground = _style.Brick;
        delete.VerticalAlignment = VerticalAlignment.Center;
        var key = row.Key;
        delete.Click += (_, _) => _controller.RemoveFromLibrary(key);
        AutomationProperties.SetName(delete, $"{SettingsWording.Delete} calibration for {row.Displays}");
        Grid.SetColumn(delete, 1);
        grid.Children.Add(text);
        grid.Children.Add(delete);
        return grid;
    }
}
