using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.App.Windows;

/// <summary>
/// The Pose Debug window (Mac <c>PoseDebugView</c>): live yaw, pitch, roll and confidence, a face-found indicator, the camera
/// and frame rate, two 10-second plots with the calibrated display bands, a numbers-only trace recorder and a summary to
/// copy. It follows <see cref="TrackingController.PoseObserved"/>, asks for the camera through <see cref="CameraDemand"/>
/// and gives it back on close. Never shows or stores a frame.
/// </summary>
public sealed class PoseDebugWindow : Window
{
    private const string DemandOwner = "pose-debug";

    private readonly TrackingController _controller;
    private readonly CameraDemand _demand;
    private readonly TraceStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly GanderStyle _style = GanderStyle.Current();
    private readonly PoseHistory _history = new();
    private readonly TraceRecorder _recorder = new();
    private readonly DispatcherTimer _timer;
    private LiveReadout _readout = new();
    private string? _lastFile;
    private int _lastCount;
    private string? _recordError;
    private bool _copiedSummary;

    private readonly StackPanel _statusHost = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _camera;
    private readonly TextBlock _fps;
    private readonly TextBlock _yaw;
    private readonly TextBlock _pitch;
    private readonly TextBlock _roll;
    private readonly TextBlock _landmarkYaw;
    private readonly TextBlock _landmarkPitch;
    private readonly TextBlock _confidence;
    private readonly TextBlock _face;
    private readonly Ellipse _faceDot = new() { Width = 10, Height = 10, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _look;
    private readonly TracePlot _yawPlot;
    private readonly TracePlot _pitchPlot;
    private readonly TextBlock _bandsNote;
    private readonly Button _record;
    private readonly Button _copySummary;
    private readonly Button _copyPath;
    private readonly TextBlock _recordStatus;
    private readonly TextBlock _recordResult;

    /// <summary>Creates the window.</summary>
    public PoseDebugWindow(TrackingController controller, CameraDemand demand, TraceStore store, Func<DateTimeOffset> now)
    {
        _controller = controller;
        _demand = demand;
        _store = store;
        _now = now;
        var s = _style;
        s.ApplyTo(this);
        Title = PoseDebugWording.Title;
        Width = 700;
        Height = 780;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _camera = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true, wrap: false);
        _fps = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true, wrap: false);
        _yaw = Value();
        _pitch = Value();
        _roll = Value();
        _landmarkYaw = Value();
        _landmarkPitch = Value();
        _confidence = Value();
        _face = Value();
        _look = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true);
        _yawPlot = new TracePlot(s);
        _pitchPlot = new TracePlot(s);
        _bandsNote = s.Label(string.Empty, GanderDesign.Type.Caption, secondary: true);
        _record = s.MakeButton("Record");
        _record.Click += (_, _) => ToggleRecording();
        _copySummary = s.MakeButton("Copy summary");
        _copySummary.Click += (_, _) => CopySummary();
        _copyPath = s.MakeButton("Copy path");
        _copyPath.Click += (_, _) => DebugWindowParts.CopyToClipboard(_lastFile ?? string.Empty);
        _recordStatus = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true);
        _recordResult = s.Label(string.Empty, GanderDesign.Type.Caption, secondary: true);

        var root = new StackPanel();
        root.Children.Add(Header());
        root.Children.Add(s.Section(null, Readout()));
        root.Children.Add(s.Section("Head pose", Plots()));
        root.Children.Add(s.Section("Trace recording", Recording()));
        var note = s.Label(PoseDebugWording.PrivacyNote, GanderDesign.Type.Caption, secondary: true);
        root.Children.Add(note);
        Content = DebugWindowParts.Page(s, root);

        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Refresh();

        Loaded += (_, _) =>
        {
            _controller.PoseObserved += OnPose;
            _controller.StateChanged += OnControllerChanged;
            _controller.SetPoseDebugVisible(WindowState != WindowState.Minimized);
            _ = AcquireAsync();
            _timer.Start();
            Refresh();
        };
        StateChanged += (_, _) => _controller.SetPoseDebugVisible(WindowState != WindowState.Minimized);
        Closed += (_, _) =>
        {
            _timer.Stop();
            _controller.PoseObserved -= OnPose;
            _controller.StateChanged -= OnControllerChanged;
            StopAndSave();
            _controller.SetPoseDebugVisible(false);
            _ = ReleaseAsync();
        };
    }

    private TextBlock Value()
    {
        var block = _style.Label(PoseFormat.Missing, GanderDesign.Type.Body, wrap: false);
        block.FontFamily = DesignFonts.Wpf(GanderDesign.Mono);
        return block;
    }

    private UIElement Header()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, GanderDesign.Space.Section) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_statusHost);
        _camera.Margin = new Thickness(GanderDesign.Space.Xl, 0, GanderDesign.Space.Xl, 0);
        Grid.SetColumn(_camera, 1);
        Grid.SetColumn(_fps, 2);
        grid.Children.Add(_camera);
        grid.Children.Add(_fps);
        return grid;
    }

    private UIElement Readout()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;

        void Add(string label, UIElement value, bool heading = false)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = heading
                ? _style.Label(label.ToUpperInvariant(), GanderDesign.Type.Caption, secondary: true, wrap: false)
                : _style.Label(label, GanderDesign.Type.Body, secondary: true, wrap: false);
            text.Margin = new Thickness(0, heading ? GanderDesign.Space.Md : GanderDesign.Space.Xs, 0, GanderDesign.Space.Xs);
            Grid.SetRow(text, row);
            grid.Children.Add(text);
            if (!heading)
            {
                ((FrameworkElement)value).Margin = new Thickness(0, GanderDesign.Space.Xs, 0, GanderDesign.Space.Xs);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
            }

            row++;
        }

        Add("Angles", new Grid(), heading: true);
        Add("Yaw", _yaw);
        Add("Pitch", _pitch);
        Add("Roll", _roll);
        Add("Landmark proxy", new Grid(), heading: true);
        Add("Yaw", _landmarkYaw);
        Add("Pitch", _landmarkPitch);
        Add("Detection", new Grid(), heading: true);
        Add("Confidence", _confidence);
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(_faceDot);
        face.Children.Add(_face);
        Add("Face", face);
        Add("Looking at", _look);
        return grid;
    }

    private UIElement Plots()
    {
        var panel = new StackPanel();
        var yawTitle = _style.Label(PoseDebugWording.YawHeading, GanderDesign.Type.Callout, secondary: true);
        yawTitle.Margin = new Thickness(0, 0, 0, GanderDesign.Space.Sm);
        var pitchTitle = _style.Label(PoseDebugWording.PitchHeading, GanderDesign.Type.Callout, secondary: true);
        pitchTitle.Margin = new Thickness(0, GanderDesign.Space.Xl, 0, GanderDesign.Space.Sm);
        _bandsNote.Margin = new Thickness(0, GanderDesign.Space.Lg, 0, 0);
        panel.Children.Add(yawTitle);
        panel.Children.Add(_yawPlot);
        panel.Children.Add(pitchTitle);
        panel.Children.Add(_pitchPlot);
        panel.Children.Add(_bandsNote);
        return panel;
    }

    private UIElement Recording()
    {
        var panel = new StackPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _copySummary.Margin = new Thickness(GanderDesign.Space.Lg, 0, 0, 0);
        _copyPath.Margin = new Thickness(GanderDesign.Space.Lg, 0, 0, 0);
        buttons.Children.Add(_record);
        buttons.Children.Add(_copySummary);
        buttons.Children.Add(_copyPath);
        _recordStatus.Margin = new Thickness(0, GanderDesign.Space.Lg, 0, 0);
        _recordResult.Margin = new Thickness(0, GanderDesign.Space.Md, 0, 0);
        panel.Children.Add(buttons);
        panel.Children.Add(_recordStatus);
        panel.Children.Add(_recordResult);
        return panel;
    }

    private async System.Threading.Tasks.Task AcquireAsync()
    {
        try
        {
            await _demand.AcquireAsync(DemandOwner);
        }
        catch (Exception)
        {
            // The controller reports camera trouble as state, never by throwing.
        }
    }

    private async System.Threading.Tasks.Task ReleaseAsync()
    {
        try
        {
            await _demand.ReleaseAsync(DemandOwner);
        }
        catch (Exception)
        {
            // See AcquireAsync.
        }
    }

    private void OnPose(TimedPose sample)
    {
        _history.Append(sample);
        _recorder.Append(sample);
        if (_readout.IsDue(sample))
        {
            _readout = LiveReadout.From(sample, _history.Fps);
        }

        if (_recorder.IsFull)
        {
            StopAndSave();
        }
    }

    private void OnControllerChanged()
    {
        if (_controller.Status is not CaptureStatus.Running)
        {
            _history.Reset();
            _readout = new LiveReadout();
            StopAndSave();
        }

        _ = _demand.ReevaluateAsync();
        Refresh();
    }

    private bool CameraRunning => _controller.Status is CaptureStatus.Running;

    private void ToggleRecording()
    {
        if (_recorder.IsRecording)
        {
            StopAndSave();
        }
        else if (CameraRunning)
        {
            _recordError = null;
            _recorder.Begin();
        }

        Refresh();
    }

    private void StopAndSave()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        var trace = _recorder.Finish();
        if (trace is null)
        {
            _recordError = "No samples were recorded.";
            return;
        }

        try
        {
            _lastFile = _store.Write(trace, _now());
            _lastCount = trace.Samples.Count;
            _recordError = null;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            _recordError = $"The trace could not be saved. {exception.Message}";
        }
    }

    private PlotData BuildPlot(PoseAxis axis)
    {
        var labels = DisplayLabeler.Labels(_controller.Layout);
        var order = new Dictionary<DisplayId, int>();
        for (var i = 0; i < _controller.Layout.Displays.Count; i++)
        {
            order[_controller.Layout.Displays[i].Id] = i;
        }

        var bands = PlotGeometry.Bands(
            _controller.ActiveCalibration?.Profile,
            axis,
            id => labels.GetValueOrDefault(id, id.RawValue),
            id => order.GetValueOrDefault(id));
        return PlotGeometry.Build(_history.Points, axis, bands);
    }

    private void CopySummary()
    {
        var yaw = BuildPlot(PoseAxis.Yaw);
        var pitch = BuildPlot(PoseAxis.Pitch);
        var text = PoseDebugSummary.Build(new PoseDebugSummaryInput(
            _controller.StatusLine,
            PoseDebugWording.CameraLine(_controller.Status, _controller.CurrentCameraName),
            _history.Fps,
            _readout.Pose,
            LookText(),
            yaw,
            pitch,
            _history.Points.Count,
            _lastFile,
            _lastCount,
            _now()));
        _copiedSummary = DebugWindowParts.CopyToClipboard(text);
        _copySummary.Content = _copiedSummary ? "Copied" : "Copy summary";
    }

    private string LookText() => LookSummary.Text(
        _controller.IsCameraActive,
        _controller.LastDecision?.Classification,
        _controller.DisplayName);

    private void Refresh()
    {
        var running = CameraRunning;
        var tone = _controller.TrackingStatus.Tone;
        _statusHost.Children.Clear();
        _statusHost.Children.Add(_style.StatusDot(tone));
        _statusHost.Children.Add(_style.Label(_controller.StatusLine, GanderDesign.Type.Headline, wrap: false));
        _camera.Text = PoseDebugWording.CameraLine(_controller.Status, _controller.CurrentCameraName);
        _fps.Text = PoseDebugWording.FpsText(running, _readout.Fps);

        var pose = running ? _readout.Pose : null;
        _yaw.Text = PoseFormat.Degrees(pose?.Yaw);
        _pitch.Text = PoseFormat.Degrees(pose?.Pitch);
        _roll.Text = PoseFormat.Degrees(pose?.Roll);
        _landmarkYaw.Text = PoseFormat.Degrees(pose?.LandmarkYaw);
        _landmarkPitch.Text = PoseFormat.Degrees(pose?.LandmarkPitch);
        _confidence.Text = pose is { } p ? PoseFormat.WholePercent(p.FaceConfidence) : PoseFormat.Missing;
        _face.Text = pose is null ? "Not detected" : "Detected";
        _faceDot.Fill = pose is null ? Brushes.Transparent : _style.Green;
        _faceDot.Stroke = pose is null ? _style.Secondary : _style.Green;
        _look.Text = LookText();

        var empty = PoseDebugWording.EmptyPlotText(running);
        var yaw = BuildPlot(PoseAxis.Yaw);
        _yawPlot.Show(yaw, empty);
        _pitchPlot.Show(BuildPlot(PoseAxis.Pitch), empty);
        _bandsNote.Text = yaw.Bands.Count > 0 ? PoseDebugWording.BandsNote : PoseDebugWording.NoBandsNote;

        _record.Content = _recorder.IsRecording ? "Stop and save" : "Record";
        _record.IsEnabled = _recorder.IsRecording || running;
        if (!_copySummary.IsMouseOver && _copiedSummary)
        {
            _copiedSummary = false;
            _copySummary.Content = "Copy summary";
        }

        _recordStatus.Text = _recorder.IsRecording
            ? $"Recording · {_recorder.SampleCount} samples · {PoseFormat.Seconds(_recorder.Duration)}"
            : running ? string.Empty : PoseDebugWording.CameraOffHint;
        _recordStatus.Foreground = _recorder.IsRecording ? _style.Brick : _style.Secondary;
        _copyPath.Visibility = _lastFile is null ? Visibility.Collapsed : Visibility.Visible;
        _recordResult.Text = _recordError ?? (_lastFile is null ? string.Empty : $"Saved {_lastFile} ({_lastCount} samples)");
        _recordResult.Foreground = _recordError is null ? _style.Secondary : _style.Brick;
    }
}
