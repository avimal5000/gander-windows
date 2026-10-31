using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.App.Windows;

/// <summary>
/// The Focus Test window (Mac <c>FocusTestView</c>): one row per display with its last-used window (the app name only,
/// never a title), a Focus button and a Focus in 3 s button for each, and a log of the last results. It calls
/// <see cref="IFocusController.FocusAsync"/> the way switching does, so it raises the window and gives it keyboard focus and
/// never touches the pointer. While it is open the controller is told the Focus Test is visible (the pose demand).
/// </summary>
public sealed class FocusTestWindow : Window
{
    private readonly TrackingController _controller;
    private readonly IFocusController _focus;
    private readonly Func<double> _clock;
    private readonly Func<DateTimeOffset> _now;
    private readonly GanderStyle _style = GanderStyle.Current();
    private readonly FocusTestState _state = new();
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _rowsHost = new();
    private readonly StackPanel _logHost = new();
    private readonly CancellationTokenSource _closed = new();
    private IReadOnlyDictionary<DisplayId, WindowSummary> _windows = new Dictionary<DisplayId, WindowSummary>();
    private DisplayId? _focused;
    private double _nextPoll;
    private bool _polling;
    private string _shownKey = string.Empty;
    private string _shownLog = string.Empty;

    /// <summary>Creates the window.</summary>
    public FocusTestWindow(TrackingController controller, IFocusController focus, Func<double> clock, Func<DateTimeOffset> now)
    {
        _controller = controller;
        _focus = focus;
        _clock = clock;
        _now = now;
        var s = _style;
        s.ApplyTo(this);
        Title = FocusTestWording.Title;
        Width = 600;
        Height = 560;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel();
        root.Children.Add(s.Label(FocusTestWording.Heading, GanderDesign.Type.Title2));
        root.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, GanderDesign.Space.Section));

        var displays = new StackPanel();
        displays.Children.Add(_rowsHost);
        var explanation = s.Label(FocusTestWording.Explanation, GanderDesign.Type.Caption, secondary: true);
        explanation.Margin = new Thickness(0, GanderDesign.Space.Xl, 0, 0);
        displays.Children.Add(explanation);
        root.Children.Add(s.Section(FocusTestWording.DisplaysHeading, displays));
        root.Children.Add(s.Section(FocusTestWording.ResultsHeading, _logHost));
        Content = DebugWindowParts.Page(s, root);

        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            _controller.SetFocusTestVisible(true);
            _controller.StateChanged += Rebuild;
            _timer.Start();
            _nextPoll = 0;
            Tick();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _closed.Cancel();
            _controller.StateChanged -= Rebuild;
            _controller.SetFocusTestVisible(false);
            _state.Reset();
        };
    }

    private void Tick()
    {
        var now = _clock();
        if (_state.Tick(now) is { } due)
        {
            _ = FocusAsync(due);
        }

        if (now >= _nextPoll && !_polling)
        {
            _nextPoll = now + 1;
            _ = RefreshAsync();
        }

        Rebuild();
    }

    private async Task RefreshAsync()
    {
        _polling = true;
        try
        {
            var layout = _controller.Layout;
            var windows = await _focus.LastUsedWindowsAsync(layout);
            var focused = await _focus.FocusedDisplayAsync(layout);
            if (_closed.IsCancellationRequested)
            {
                return;
            }

            _windows = windows;
            _focused = focused;
        }
        catch (Exception)
        {
            // A poll that fails leaves the last answer on screen; the next one tries again.
        }
        finally
        {
            _polling = false;
        }

        Rebuild();
    }

    private async Task FocusAsync(DisplayId display)
    {
        if (!_state.BeginFocus(display))
        {
            return;
        }

        Rebuild();
        var layout = _controller.Layout;
        string text;
        try
        {
            var result = await _focus.FocusAsync(display, layout, _closed.Token);
            text = FocusTestWording.LogText(result, _controller.DisplayName);
        }
        catch (OperationCanceledException)
        {
            text = FocusTestWording.CancelledText;
        }
        catch (Exception exception)
        {
            text = $"Failed: {exception.Message}";
        }

        _state.Finish(text, _now());
        _nextPoll = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        var rows = FocusTestWording.Rows(_controller.Layout, _windows, _focused);
        var key = string.Join(
            "|",
            rows.Select(r => $"{r.Id.RawValue}/{r.Title}/{r.WindowText}"))
            + $"#{_state.Activity}/{_state.Target?.RawValue}/{_state.CountdownSeconds}";
        if (key != _shownKey)
        {
            _shownKey = key;
            BuildRows(rows);
        }

        var logKey = string.Join("|", _state.Log.Select(e => e.Id));
        if (logKey != _shownLog)
        {
            _shownLog = logKey;
            BuildLog();
        }
    }

    private void BuildRows(IReadOnlyList<FocusRow> rows)
    {
        var s = _style;
        _rowsHost.Children.Clear();
        if (rows.Count == 0)
        {
            _rowsHost.Children.Add(s.Label(FocusTestWording.NoDisplays, GanderDesign.Type.Body, secondary: true));
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var grid = new Grid { Margin = new Thickness(0, GanderDesign.Space.Lg, 0, GanderDesign.Space.Lg) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(s.Label(row.Title, GanderDesign.Type.Headline, wrap: false));
            var window = s.Label(row.WindowText, GanderDesign.Type.Callout, secondary: !row.HasWindow, wrap: false);
            window.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(window);
            grid.Children.Add(text);

            if (_state.Activity == FocusActivityKind.Focusing && _state.Target == row.Id)
            {
                var busy = s.Label("Focusing…", GanderDesign.Type.Callout, secondary: true, wrap: false);
                busy.VerticalAlignment = VerticalAlignment.Center;
                busy.Margin = new Thickness(0, 0, GanderDesign.Space.Xl, 0);
                Grid.SetColumn(busy, 1);
                grid.Children.Add(busy);
            }

            var now = s.MakeButton(FocusTestWording.FocusButton);
            now.IsEnabled = _state.CanRequest;
            var id = row.Id;
            now.Click += (_, _) => _ = FocusAsync(id);
            now.Margin = new Thickness(0, 0, GanderDesign.Space.Lg, 0);
            Grid.SetColumn(now, 2);
            grid.Children.Add(now);

            Button delayed;
            if (_state.Activity == FocusActivityKind.CountingDown && _state.Target == row.Id)
            {
                delayed = s.MakeButton(FocusTestWording.CancelButton(_state.CountdownSeconds));
                delayed.Click += (_, _) =>
                {
                    _state.CancelCountdown();
                    Rebuild();
                };
            }
            else
            {
                delayed = s.MakeButton(FocusTestWording.DelayedButton);
                delayed.IsEnabled = _state.CanRequest;
                delayed.Click += (_, _) =>
                {
                    _state.BeginCountdown(id, _clock());
                    Rebuild();
                };
            }

            Grid.SetColumn(delayed, 3);
            grid.Children.Add(delayed);
            _rowsHost.Children.Add(grid);
            if (i < rows.Count - 1)
            {
                _rowsHost.Children.Add(new Border { Height = GanderDesign.Space.Hairline * 2, Background = s.Line });
            }
        }
    }

    private void BuildLog()
    {
        var s = _style;
        _logHost.Children.Clear();
        if (_state.Log.Count == 0)
        {
            _logHost.Children.Add(s.Label(FocusTestWording.NoResults, GanderDesign.Type.Callout, secondary: true));
        }

        foreach (var entry in _state.Log)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, GanderDesign.Space.Xs, 0, GanderDesign.Space.Xs) };
            var time = s.Label(entry.Time.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), GanderDesign.Type.Callout, secondary: true, wrap: false);
            time.Margin = new Thickness(0, 0, GanderDesign.Space.Xl, 0);
            line.Children.Add(time);
            line.Children.Add(s.Label(entry.Text, GanderDesign.Type.Callout));
            _logHost.Children.Add(line);
        }
    }
}
