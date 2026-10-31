using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.Core.Calibration;

namespace Sway.App.Windows;

/// <summary>
/// The "Set up Sway" window. Thin: the <see cref="SetupSession"/> decides what happens, <see cref="SetupViewBuilder"/>
/// decides what is shown, and this copies the view onto a fixed set of controls (so keyboard focus is not lost when the
/// state ticks ten times a second). Esc cancels a running calibration.
/// </summary>
public sealed class SetupWindow : Window
{
    private static readonly Color Forest = Color.FromRgb(0x2F, 0x5D, 0x3A);

    private readonly SetupSession _session;
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _stepLine = new() { Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) };
    private readonly StackPanel _lines = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _highlight = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 16, 0, 4), Foreground = new SolidColorBrush(Forest) };
    private readonly TextBlock _caption = new() { Opacity = 0.7 };
    private readonly Button _primary = new() { MinWidth = 140, Padding = new Thickness(16, 7, 16, 7), IsDefault = true };
    private readonly Button _secondary = new() { MinWidth = 120, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
    private SetupView? _view;
    private SetupStep? _shownStep;
    private bool _opened;

    /// <summary>Creates the window for <paramref name="session"/>; the session opens when the window shows.</summary>
    public SetupWindow(SetupSession session)
    {
        _session = session;
        Title = "Set up Sway";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = SystemFonts.MessageFontFamily;
        FontSize = 14;

        var light = SystemInfo.IsLightApps();
        Background = new SolidColorBrush(light ? Color.FromRgb(0xF6, 0xF3, 0xEA) : Color.FromRgb(0x2B, 0x2B, 0x2B));
        Foreground = new SolidColorBrush(light ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF6, 0xF3, 0xEA));

        _primary.Background = new SolidColorBrush(Forest);
        _primary.Foreground = Brushes.White;
        _primary.BorderBrush = new SolidColorBrush(Forest);
        _primary.Click += (_, _) => Run(_view?.Primary);
        _secondary.Click += (_, _) => Run(_view?.Secondary);
        AutomationProperties.SetName(_primary, "Primary action");
        AutomationProperties.SetName(_secondary, "Secondary action");
        AutomationProperties.SetName(_bar, "Calibration progress");
        AutomationProperties.SetName(_highlight, "Instruction");
        AutomationProperties.SetLiveSetting(_highlight, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0),
        };
        buttons.Children.Add(_secondary);
        buttons.Children.Add(_primary);

        var stack = new StackPanel { Margin = new Thickness(32) };
        stack.Children.Add(_title);
        stack.Children.Add(_stepLine);
        stack.Children.Add(_lines);
        stack.Children.Add(_highlight);
        stack.Children.Add(_message);
        stack.Children.Add(_bar);
        stack.Children.Add(_caption);
        stack.Children.Add(buttons);
        Content = new Border { Child = stack, Background = Background };

        PreviewKeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            _session.Changed += Apply;
            _session.Open();
            _opened = true;
            Apply();
        };
        Closed += (_, _) =>
        {
            _session.Changed -= Apply;
            _session.Close();
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _session.Calibration.IsActive)
        {
            _session.CancelCalibration();
            e.Handled = true;
        }
    }

    private void Apply()
    {
        if (_opened && !_session.IsOpen)
        {
            // "Start tracking" closed the session; the window goes with it.
            _opened = false;
            Close();
            return;
        }

        if (_session.Step is not { } step)
        {
            return;
        }

        var view = SetupViewBuilder.Build(new SetupViewInput(
            step,
            _session.FramingText,
            _session.NeedsYouText,
            _session.CanStartCalibration,
            _session.Message,
            _session.Calibration.IsActive,
            _session.Calibration.Prompt,
            _session.Calibration.Progress,
            _session.Result ?? _session.Calibration.Result));
        _view = view;

        _title.Text = view.Title;
        _stepLine.Text = view.StepLine ?? string.Empty;
        _stepLine.Visibility = view.StepLine is null ? Visibility.Collapsed : Visibility.Visible;
        _lines.Children.Clear();
        foreach (var line in view.Lines)
        {
            _lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        }

        _highlight.Text = view.Highlight ?? string.Empty;
        _highlight.Visibility = view.Highlight is null ? Visibility.Collapsed : Visibility.Visible;
        _message.Text = view.Message ?? string.Empty;
        _message.Visibility = view.Message is null ? Visibility.Collapsed : Visibility.Visible;
        _message.Foreground = view.MessageIsError ? new SolidColorBrush(Color.FromRgb(0xC4, 0x3B, 0x2F)) : Foreground;
        _bar.Visibility = view.Progress is null ? Visibility.Collapsed : Visibility.Visible;
        _caption.Visibility = _bar.Visibility;
        if (view.Progress is { } progress)
        {
            _bar.Value = progress.Fraction;
            _caption.Text = progress.Caption;
        }

        Show(_primary, view.Primary);
        Show(_secondary, view.Secondary);
        if (_shownStep != step)
        {
            _shownStep = step;
            _primary.Focus();
        }
    }

    private static void Show(Button button, SetupButton? model)
    {
        button.Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
        if (model is null)
        {
            return;
        }

        button.Content = model.Label;
        button.IsEnabled = model.Enabled;
        AutomationProperties.SetName(button, model.Label);
    }

    private void Run(SetupButton? button)
    {
        switch (button?.Action)
        {
            case SetupAction.GetStarted:
                _session.GetStarted();
                break;
            case SetupAction.OpenCameraSettings:
                OpenSettings();
                break;
            case SetupAction.StartCalibration:
                _ = _session.StartCalibrationAsync();
                break;
            case SetupAction.CancelCalibration:
                _session.CancelCalibration();
                break;
            case SetupAction.CalibrateAgain:
                _session.CalibrateAgain();
                break;
            case SetupAction.StartTracking:
                _ = _session.StartTrackingAsync();
                break;
        }
    }

    private static void OpenSettings()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(SetupPresentation.CameraSettingsUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Nothing else to do: the message already says where the setting is.
        }
    }
}
