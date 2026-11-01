using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core.Calibration;

namespace Gander.App.Windows;

/// <summary>
/// The "Set up Gander" window. Thin: the <see cref="SetupSession"/> decides what happens, <see cref="SetupViewBuilder"/>
/// decides what is shown, and this copies the view onto a fixed set of controls (so keyboard focus is not lost when the
/// state ticks ten times a second). Esc cancels a running calibration.
/// </summary>
public sealed class SetupWindow : Window
{
    private readonly SetupSession _session;
    private readonly GanderStyle _style = GanderStyle.Current();
    private readonly TextBlock _title;
    private readonly TextBlock _stepLine;
    private readonly StackPanel _lines = new();
    private readonly TextBlock _highlight;
    private readonly Border _card;
    private readonly TextBlock _message;
    private readonly ProgressBar _bar;
    private readonly TextBlock _caption;
    private readonly Button _primary;
    private readonly Button _secondary;
    private readonly StackPanel _header = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _content = new();
    private readonly OnboardingIntro _intro;
    private readonly Border _page;
    private readonly Brush _paper;
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _root = new();
    private SetupView? _view;
    private SetupStep? _shownStep;
    private bool _opened;

    /// <summary>Creates the window for <paramref name="session"/>; the session opens when the window shows.</summary>
    public SetupWindow(SetupSession session)
    {
        _session = session;
        Title = "Set up Gander";
        Width = GanderDesign.Size.SetupWidth;
        SizeToContent = SizeToContent.Height;
        MinHeight = GanderDesign.Size.SetupHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _style.ApplyTo(this);
        var s = _style;
        var theme = s.Theme;

        _title = s.Label(string.Empty, GanderDesign.Type.Title2);
        _stepLine = s.Label(string.Empty, GanderDesign.Type.Caption, secondary: true);
        _highlight = s.Label(string.Empty, GanderDesign.Type.Title3Strong);
        _highlight.Margin = new Thickness(0, 0, 0, 0);
        _message = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true);
        _bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(0, GanderDesign.Space.Xl, 0, GanderDesign.Space.Sm), Foreground = s.Accent };
        _caption = s.Label(string.Empty, GanderDesign.Type.Caption, secondary: true);
        _primary = s.MakeButton(string.Empty, prominent: true);
        _primary.IsDefault = true;
        _primary.MinWidth = 140;
        _secondary = s.MakeButton(string.Empty);
        _secondary.MinWidth = 120;
        _secondary.Margin = new Thickness(GanderDesign.Space.Xl, 0, 0, 0);
        _primary.Click += (_, _) => Run(_view?.Primary);
        _secondary.Click += (_, _) => Run(_view?.Secondary);
        AutomationProperties.SetName(_primary, "Primary action");
        AutomationProperties.SetName(_secondary, "Secondary action");
        AutomationProperties.SetName(_bar, "Calibration progress");
        AutomationProperties.SetName(_highlight, "Instruction");
        AutomationProperties.SetLiveSetting(_highlight, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);

        // Header of every numbered step: the mark in Gander green, "Step N of M", and the title.
        _header.Children.Add(GanderStyle.MakeMark(GanderDesign.Size.SetupMark, theme.Green, theme.Green));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        titles.Children.Add(_stepLine);
        titles.Children.Add(_title);
        _header.Children.Add(titles);

        var cardBody = new StackPanel();
        cardBody.Children.Add(_lines);
        cardBody.Children.Add(_highlight);
        _card = s.MakeCard(cardBody);

        _message.Margin = new Thickness(0, GanderDesign.Space.Xl, 0, 0);
        _content.Children.Add(_card);
        _content.Children.Add(_message);
        _content.Children.Add(_bar);
        _content.Children.Add(_caption);

        // The walkthrough sits on ivory paper (charcoal in dark); the numbered steps keep the window surface.
        _paper = GanderStyle.Brush(OnboardingArt.PaperColor(theme));
        _intro = new OnboardingIntro(s);
        _intro.Invoked += Run;

        _buttons.Margin = new Thickness(0, 18, 0, 0);
        _buttons.Children.Add(_primary);
        _buttons.Children.Add(_secondary);

        _header.Margin = new Thickness(0, 0, 0, 18);
        _root.Margin = new Thickness(GanderDesign.Space.SetupPadding);
        _root.Children.Add(_header);
        _root.Children.Add(_intro.Root);
        _root.Children.Add(_content);
        _root.Children.Add(_buttons);
        _page = new Border { Child = _root, Background = s.Surface };
        Content = _page;

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
            _session.Result ?? _session.Calibration.Result,
            _session.IntroPage));
        _view = view;

        var welcome = step == SetupStep.Welcome;
        _intro.Root.Visibility = welcome ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = welcome ? Visibility.Collapsed : Visibility.Visible;
        _content.Visibility = welcome ? Visibility.Collapsed : Visibility.Visible;
        _buttons.Visibility = welcome ? Visibility.Collapsed : Visibility.Visible;
        _primary.IsDefault = !welcome;
        _page.Background = welcome ? _paper : _style.Surface;
        Background = welcome ? _paper : _style.Surface;

        if (welcome)
        {
            _intro.Show(view);
        }
        else
        {
            _title.Text = view.Title;
            _stepLine.Text = view.StepLine ?? string.Empty;
            _stepLine.Visibility = view.StepLine is null ? Visibility.Collapsed : Visibility.Visible;
            _lines.Children.Clear();
            foreach (var line in view.Lines)
            {
                var text = _style.Label(line, GanderDesign.Type.Body);
                text.Margin = new Thickness(0, 0, 0, GanderDesign.Space.Lg);
                _lines.Children.Add(text);
            }

            _highlight.Text = view.Highlight ?? string.Empty;
            _highlight.Visibility = view.Highlight is null ? Visibility.Collapsed : Visibility.Visible;
            _card.Visibility = view.Lines.Count == 0 && view.Highlight is null ? Visibility.Collapsed : Visibility.Visible;
            _message.Text = view.Message ?? string.Empty;
            _message.Visibility = view.Message is null ? Visibility.Collapsed : Visibility.Visible;
            _message.Foreground = view.MessageIsError ? _style.Brick : _style.Secondary;
            _bar.Visibility = view.Progress is null ? Visibility.Collapsed : Visibility.Visible;
            _caption.Visibility = _bar.Visibility;
            if (view.Progress is { } progress)
            {
                _bar.Value = progress.Fraction;
                _caption.Text = progress.Caption;
            }
        }

        if (!welcome)
        {
            Show(_primary, view.Primary);
            Show(_secondary, view.Secondary);
        }

        if (_shownStep != step)
        {
            _shownStep = step;
            (welcome ? _intro.Next : _primary).Focus();
            FadeInStep();
        }
    }

    // One step at a time, cross-fading in about 0.2 s; none when Windows animations are off.
    private void FadeInStep()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        _root.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(GanderDesign.Motion.Fade)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
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
            case SetupAction.IntroNext:
                _session.IntroNext();
                break;
            case SetupAction.IntroBack:
                _session.IntroBack();
                break;
            case SetupAction.IntroSkip:
                _session.IntroSkip();
                break;
            case SetupAction.OpenCameraSettings:
                ShellLauncher.OpenCameraSettings();
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
}
