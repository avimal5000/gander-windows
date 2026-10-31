using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Sway.Core;
using Sway.Core.Calibration;
using Sway.Core.Controller;

namespace Sway.App.Windows;

/// <summary>
/// The Head-Pose Gate Test window (Mac <c>GateTestView</c>): a setup form, the guided run (a marker on each display in
/// turn, the window shows the same steps), and the report with a Markdown summary to copy. The brain is
/// <see cref="GateRun"/> over the Core <see cref="GateTestSession"/>; the camera is held through
/// <see cref="CameraDemand"/> so no switch happens during the run. Numbers only: no frame is kept, and the trace is not saved.
/// </summary>
public sealed class GateTestWindow : Window
{
    private const string DemandOwner = "gate-test";

    private readonly TrackingController _controller;
    private readonly CameraDemand _demand;
    private readonly PromptOverlayHost _overlay;
    private readonly GateRun _run;
    private readonly SwayStyle _style = SwayStyle.Current();
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _setup = new();
    private readonly StackPanel _running = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _report = new();
    private readonly StackPanel _displays = new();
    private readonly StackPanel _setupMessages = new();
    private readonly TextBlock _roundsText;
    private readonly TextBlock _holdText;
    private readonly TextBlock _estimate;
    private readonly Button _start;
    private readonly Button _viewReport;
    private readonly TextBlock _stepHeading;
    private readonly TextBlock _instruction;
    private readonly TextBlock _phase;
    private readonly ProgressBar _bar;
    private readonly TextBlock _samples;
    private readonly TextBlock _stepText;
    private GateTestConfiguration _configuration = new();
    private GateRunState _shownState = GateRunState.Idle;
    private GateReport? _shownReport;
    private bool _showReport;
    private bool _starting;
    private Button? _copy;
    private ScrollViewer _setupPage = null!;
    private ScrollViewer _reportPage = null!;

    /// <summary>Creates the window.</summary>
    public GateTestWindow(TrackingController controller, CameraDemand demand, Func<double> clock, Func<DateTimeOffset> now)
    {
        _controller = controller;
        _demand = demand;
        _overlay = new PromptOverlayHost();
        _run = new GateRun(clock, now);
        var s = _style;
        s.ApplyTo(this);
        Title = GateTestWording.Title;
        Width = 640;
        Height = 700;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _roundsText = s.Label(string.Empty, SwayDesign.Type.Body, wrap: false);
        _holdText = s.Label(string.Empty, SwayDesign.Type.Body, wrap: false);
        _estimate = s.Label(string.Empty, SwayDesign.Type.Callout, secondary: true);
        _start = s.MakeButton(GateTestWording.StartButton, prominent: true);
        _start.IsDefault = true;
        _start.Click += (_, _) => _ = StartAsync();
        _viewReport = s.MakeButton("View last report");
        _viewReport.Click += (_, _) =>
        {
            _showReport = true;
            Apply();
        };

        _stepHeading = s.Label(string.Empty, SwayDesign.Type.Headline);
        _stepHeading.TextAlignment = TextAlignment.Center;
        _instruction = s.Label(string.Empty, SwayDesign.Type.Title2);
        _instruction.TextAlignment = TextAlignment.Center;
        _instruction.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        _phase = s.Label(string.Empty, SwayDesign.Type.Body, secondary: true);
        _phase.TextAlignment = TextAlignment.Center;
        _phase.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        _bar = new ProgressBar { Height = 8, Width = 320, Minimum = 0, Maximum = 1, Foreground = s.Accent, Margin = new Thickness(0, SwayDesign.Space.Xl, 0, 0) };
        AutomationProperties.SetName(_bar, "Gate test progress");
        _samples = s.Label(string.Empty, SwayDesign.Type.Callout, secondary: true);
        _samples.TextAlignment = TextAlignment.Center;
        _samples.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        _stepText = s.Label(string.Empty, SwayDesign.Type.Caption, secondary: true);
        _stepText.TextAlignment = TextAlignment.Center;
        _stepText.Margin = new Thickness(0, SwayDesign.Space.Md, 0, 0);

        BuildSetup();
        BuildRunning();

        var root = new Grid();
        _setupPage = DebugWindowParts.Page(s, _setup);
        _reportPage = DebugWindowParts.Page(s, _report);
        root.Children.Add(_setupPage);
        root.Children.Add(_running);
        root.Children.Add(_reportPage);
        Content = root;

        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            _run.Tick();
            Apply();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _run.State != GateRunState.Idle)
            {
                _run.Cancel();
                Apply();
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            _controller.PoseObserved += OnPose;
            _controller.StateChanged += OnControllerChanged;
            _timer.Start();
            Apply();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _controller.PoseObserved -= OnPose;
            _controller.StateChanged -= OnControllerChanged;
            _run.Cancel(null);
            _overlay.Dispose();
            _ = ReleaseAsync();
        };
    }

    private void BuildSetup()
    {
        var s = _style;
        var heading = s.Label(GateTestWording.Heading, SwayDesign.Type.Title2);
        var intro = s.Label(GateTestWording.Intro, SwayDesign.Type.Body, secondary: true);
        intro.Margin = new Thickness(0, SwayDesign.Space.Md, 0, SwayDesign.Space.Section);
        _setup.Children.Add(heading);
        _setup.Children.Add(intro);

        var displaysBody = new StackPanel();
        displaysBody.Children.Add(_displays);
        displaysBody.Children.Add(_setupMessages);
        _setup.Children.Add(s.Section(GateTestWording.DisplaysHeading, displaysBody));

        var settings = new StackPanel();
        settings.Children.Add(Stepper(_roundsText, () => Change(rounds: -1), () => Change(rounds: 1)));
        var hold = Stepper(_holdText, () => Change(hold: -1), () => Change(hold: 1));
        hold.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        settings.Children.Add(hold);
        _estimate.Margin = new Thickness(0, SwayDesign.Space.Xl, 0, 0);
        settings.Children.Add(_estimate);
        _setup.Children.Add(s.Section(GateTestWording.SettingsHeading, settings));

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _viewReport.Margin = new Thickness(SwayDesign.Space.Lg, 0, 0, 0);
        actions.Children.Add(_start);
        actions.Children.Add(_viewReport);
        _setup.Children.Add(actions);
    }

    private void BuildRunning()
    {
        var cancel = _style.MakeButton(GateTestWording.CancelButton);
        cancel.MinWidth = 120;
        cancel.Margin = new Thickness(0, 24, 0, 0);
        cancel.Click += (_, _) =>
        {
            _run.Cancel();
            Apply();
        };
        var hint = _style.Label(GateTestWording.EscHint, SwayDesign.Type.Callout, secondary: true);
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, SwayDesign.Space.Xl, 0, 0);
        _running.Children.Add(_stepHeading);
        _running.Children.Add(_instruction);
        _running.Children.Add(_phase);
        _running.Children.Add(_bar);
        _running.Children.Add(_samples);
        _running.Children.Add(_stepText);
        _running.Children.Add(cancel);
        _running.Children.Add(hint);
        _running.Visibility = Visibility.Collapsed;
    }

    private FrameworkElement Stepper(TextBlock label, Action minus, Action plus)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var less = _style.MakeButton("−");
        less.MinWidth = 32;
        less.Click += (_, _) => minus();
        AutomationProperties.SetName(less, "Less");
        var more = _style.MakeButton("+");
        more.MinWidth = 32;
        more.Margin = new Thickness(SwayDesign.Space.Md, 0, 0, 0);
        more.Click += (_, _) => plus();
        AutomationProperties.SetName(more, "More");
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(less, 1);
        Grid.SetColumn(more, 2);
        grid.Children.Add(label);
        grid.Children.Add(less);
        grid.Children.Add(more);
        return grid;
    }

    private void Change(int rounds = 0, double hold = 0)
    {
        _configuration = new GateTestConfiguration(_configuration.Rounds + rounds, _configuration.HoldDuration + hold);
        Apply();
    }

    private async Task StartAsync()
    {
        if (_starting || _run.State != GateRunState.Idle)
        {
            return;
        }

        var layout = _controller.Layout;
        _showReport = false;
        if (!GateRun.CanRun(layout))
        {
            _run.Begin(layout, null, _configuration, _controller.Status);
            Apply();
            return;
        }

        _starting = true;
        try
        {
            await _demand.AcquireAsync(DemandOwner, exclusive: true);
            _run.Begin(layout, _controller.CurrentCameraName, _configuration, _controller.Status);
        }
        catch (Exception exception)
        {
            _run.Fail($"The camera could not be started. {exception.Message}");
            _ = ReleaseAsync();
        }
        finally
        {
            _starting = false;
        }

        Apply();
    }

    private async Task ReleaseAsync()
    {
        try
        {
            await _demand.ReleaseAsync(DemandOwner);
        }
        catch (Exception)
        {
            // The controller reports camera trouble as state, never by throwing.
        }
    }

    private void OnPose(TimedPose sample) => _run.Ingest(sample);

    private void OnControllerChanged()
    {
        _run.OnStatus(_controller.Status);
        Apply();
    }

    private void Apply()
    {
        var state = _run.State;
        _overlay.Show(_run.Progress);
        if (state == GateRunState.Idle && _shownState != GateRunState.Idle)
        {
            _ = ReleaseAsync();
            if (_run.Report is not null)
            {
                _showReport = true;
            }
        }

        _shownState = state;

        var layout = _controller.Layout;
        if (_run.Report is { } report && !ReferenceEquals(report, _shownReport))
        {
            _shownReport = report;
            BuildReport(report);
        }

        var running = state != GateRunState.Idle;
        var showReport = !running && _showReport && _shownReport is not null && _run.Report is not null;
        _running.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _setupPage.Visibility = !running && !showReport ? Visibility.Visible : Visibility.Collapsed;
        _reportPage.Visibility = showReport ? Visibility.Visible : Visibility.Collapsed;

        if (running)
        {
            ApplyRunning();
        }
        else if (!showReport)
        {
            ApplySetup(layout);
        }
    }

    private void ApplyRunning()
    {
        if (_run.Progress is { } progress)
        {
            _stepHeading.Text = GateTestWording.StepHeading(progress);
            _instruction.Text = GateTestWording.Instruction(_controller.DisplayName(progress.Display.Id));
            _phase.Text = GateTestWording.PhaseText(progress.Phase);
            _bar.Visibility = Visibility.Visible;
            _bar.Value = progress.Fraction;
            _samples.Text = progress.Phase == GatePhase.Hold ? GateTestWording.SamplesText(progress) : string.Empty;
            _stepText.Text = GateTestWording.StepText(progress);
        }
        else
        {
            _stepHeading.Text = GateTestWording.StartingCamera;
            _instruction.Text = string.Empty;
            _phase.Text = string.Empty;
            _bar.Visibility = Visibility.Collapsed;
            _samples.Text = string.Empty;
            _stepText.Text = string.Empty;
        }
    }

    private void ApplySetup(DisplayLayout layout)
    {
        var s = _style;
        _displays.Children.Clear();
        var rows = GateTestWording.DisplayRows(layout);
        if (rows.Count == 0)
        {
            _displays.Children.Add(s.Label(GateTestWording.NoDisplays, SwayDesign.Type.Body, secondary: true));
        }

        foreach (var row in rows)
        {
            var line = new Grid { Margin = new Thickness(0, SwayDesign.Space.Sm, 0, SwayDesign.Space.Sm) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = SwayStyle.Brush(Rgba.Of(SwayDesign.SeriesColor(row.SeriesIndex), 0.25)),
                Margin = new Thickness(0, 0, SwayDesign.Space.Xl, 0),
                Child = new TextBlock
                {
                    Text = row.Number.ToString(CultureInfo.InvariantCulture),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = s.Text,
                    FontWeight = FontWeights.SemiBold,
                },
            };
            var name = s.Label(row.Name, SwayDesign.Type.Body, wrap: false);
            var detail = s.Label(row.Detail, SwayDesign.Type.Callout, secondary: true, wrap: false);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(detail, 2);
            line.Children.Add(badge);
            line.Children.Add(name);
            line.Children.Add(detail);
            _displays.Children.Add(line);
        }

        _setupMessages.Children.Clear();
        if (!GateRun.CanRun(layout))
        {
            AddMessage(GateTestWording.NeedsTwoDisplays, error: true);
        }
        else if (layout.Displays.Count > SwayPolicy.MaxTrackedDisplays)
        {
            AddMessage(GateTestWording.TrackedNote(layout.Displays.Count), error: false);
        }

        if (_run.Message is { } message)
        {
            AddMessage(message, _run.MessageIsError);
        }

        _roundsText.Text = GateTestWording.RoundsText(_configuration.Rounds);
        _holdText.Text = GateTestWording.HoldText(_configuration.HoldDuration);
        _estimate.Text = GateTestWording.EstimateText(_configuration, layout.Displays.Count);
        _start.IsEnabled = GateRun.CanRun(layout) && !_starting;
        _viewReport.Visibility = _run.Report is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddMessage(string text, bool error)
    {
        var label = _style.Label(text, SwayDesign.Type.Callout, secondary: !error);
        if (error)
        {
            label.Foreground = _style.Brick;
        }

        label.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        _setupMessages.Children.Add(label);
    }

    private void BuildReport(GateReport report)
    {
        var s = _style;
        _report.Children.Clear();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(s.Label(GateTestWording.ResultHeading, SwayDesign.Type.Title2));
        var badgeBrush = report.Result.Verdict switch
        {
            SeparabilityVerdict.SeparatesClearly => s.Green,
            SeparabilityVerdict.Marginal => s.Amber,
            SeparabilityVerdict.DoesNotSeparate => s.Brick,
            _ => s.Secondary,
        };
        var badgeText = s.Label(report.Title, SwayDesign.Type.Headline, wrap: false);
        badgeText.Foreground = Brushes.White;
        head.Children.Add(new Border
        {
            Background = badgeBrush,
            CornerRadius = new CornerRadius(SwayDesign.Radius.Chip),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(SwayDesign.Space.Xl, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeText,
        });
        _report.Children.Add(head);

        var explanation = s.Label(report.Explanation, SwayDesign.Type.Body);
        explanation.Margin = new Thickness(0, SwayDesign.Space.Lg, 0, 0);
        var source = s.Label(report.SourceLine, SwayDesign.Type.Callout, secondary: true);
        source.Margin = new Thickness(0, SwayDesign.Space.Sm, 0, SwayDesign.Space.Xl);
        _report.Children.Add(explanation);
        _report.Children.Add(source);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, SwayDesign.Space.Section) };
        _copy = s.MakeButton(GateTestWording.CopyButton, prominent: true);
        _copy.Click += (_, _) =>
        {
            var copied = DebugWindowParts.CopyToClipboard(report.Markdown());
            _copy.Content = copied ? GateTestWording.CopiedButton : GateTestWording.CopyButton;
        };
        var again = s.MakeButton(GateTestWording.RunAgainButton);
        again.Margin = new Thickness(SwayDesign.Space.Lg, 0, 0, 0);
        again.Click += (_, _) =>
        {
            _showReport = false;
            _run.ShowSetup();
            Apply();
        };
        actions.Children.Add(_copy);
        actions.Children.Add(again);
        _report.Children.Add(actions);

        var summary = new StackPanel();
        summary.Children.Add(s.Label($"Face found in {PoseFormat.WholePercent(report.Result.FaceRate)} of frames", SwayDesign.Type.Body));
        var accuracy = s.Label(
            report.Result.HeldOutAccuracy is { } held ? $"Held-out accuracy: {PoseFormat.WholePercent(held)}" : "Held-out accuracy: not enough data",
            SwayDesign.Type.Body);
        accuracy.Margin = new Thickness(0, SwayDesign.Space.Sm, 0, 0);
        summary.Children.Add(accuracy);
        _report.Children.Add(s.Section("Summary", summary));

        var table = new List<string[]> { new[] { "Display", "Face samples", "Yaw (mean, std)", "Pitch (mean, std)" } };
        foreach (var label in report.Result.Labels)
        {
            table.Add(
            [
                report.NameOf(label.Label),
                label.Count.ToString(CultureInfo.InvariantCulture),
                string.Create(CultureInfo.InvariantCulture, $"{label.MeanYaw:0.0}°, {label.StdYaw:0.0}°"),
                string.Create(CultureInfo.InvariantCulture, $"{label.MeanPitch:0.0}°, {label.StdPitch:0.0}°"),
            ]);
        }

        _report.Children.Add(s.Section("Displays", Table(table)));

        if (report.Result.Pairs.Count > 0)
        {
            var pairs = new List<string[]> { new[] { "Pair", "Distance", "Yaw difference", "d-prime" } };
            foreach (var pair in report.Result.Pairs)
            {
                pairs.Add(
                [
                    $"{report.NameOf(pair.First)} and {report.NameOf(pair.Second)}",
                    string.Create(CultureInfo.InvariantCulture, $"{pair.CenterDistanceDegrees:0.0}°"),
                    string.Create(CultureInfo.InvariantCulture, $"{pair.YawDifferenceDegrees:0.0}°"),
                    string.Create(CultureInfo.InvariantCulture, $"{pair.DPrime:0.00}"),
                ]);
            }

            _report.Children.Add(s.Section("Pairs", Table(pairs)));
        }
    }

    private UIElement Table(IReadOnlyList<string[]> rows)
    {
        var grid = new Grid();
        for (var column = 0; column < rows[0].Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 0 ? new GridLength(1.4, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        }

        for (var row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < rows[row].Length; column++)
            {
                var cell = _style.Label(rows[row][column], row == 0 ? SwayDesign.Type.Caption : SwayDesign.Type.Callout, secondary: row == 0);
                cell.Margin = new Thickness(0, SwayDesign.Space.Sm, SwayDesign.Space.Lg, SwayDesign.Space.Sm);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }
}
