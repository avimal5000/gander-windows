using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core;
using Gander.Core.Controller;

namespace Gander.App.Windows;

/// <summary>Shared fade: fade in, hold, fade out, then hide; or visible and then gone when animations are off.</summary>
internal static class FeedbackFade
{
    /// <summary>True when Windows animations are on (Settings > Accessibility > Visual effects > Animation effects).</summary>
    public static bool Animated => AppTheme.Animate;

    /// <summary>Plays <paramref name="timing"/> on the window's opacity and hides the window at the end. <paramref name="generation"/> guards against a newer presentation.</summary>
    public static void Play(Window window, FeedbackTiming timing, bool animated, Func<bool> stillCurrent)
    {
        var frames = EdgeGlowLayout.Keyframes(timing, animated);
        var ease = GanderDesign.Motion.EaseInOut;
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        for (var i = 0; i < frames.Count; i++)
        {
            var (seconds, opacity) = frames[i];
            var time = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
            animation.KeyFrames.Add(animated && i > 0
                ? new SplineDoubleKeyFrame(opacity, time, new KeySpline(ease.X1, ease.Y1, ease.X2, ease.Y2))
                : new DiscreteDoubleKeyFrame(opacity, time));
        }

        if (!animated)
        {
            // Visible for the plain duration, then gone: one hold, nothing fades.
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(timing.PlainDuration + 0.001))));
        }

        animation.Completed += (_, _) =>
        {
            if (stillCurrent())
            {
                window.Hide();
            }
        };
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}

/// <summary>
/// A click-through, non-activating, topmost window over one display that draws the edge glow (Mac EdgeGlowView, SPEC F27):
/// a 4 px Gander-green border with a soft glow just inside it. Eight thin strips, so nothing covers the middle.
/// </summary>
public sealed class EdgeGlowWindow : Window
{
    private readonly Canvas _canvas = new();
    private int _generation;

    /// <summary>Creates the (hidden) glow window.</summary>
    public EdgeGlowWindow()
    {
        OverlayNative.Configure(this, "Gander glow");
        Opacity = 0;
        Content = _canvas;
        SizeChanged += (_, _) => LayOut();
    }

    /// <summary>Shows one glow over a display frame in physical pixels, in <paramref name="green"/>.</summary>
    public void Present(DisplayFrame frame, Rgb green, bool animated)
    {
        var generation = ++_generation;
        _canvas.Tag = green;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }

        OverlayNative.Place(this, frame.X, frame.Y, frame.Width, frame.Height);
        LayOut();
        FeedbackFade.Play(this, GanderDesign.Motion.Glow, animated, () => generation == _generation);
    }

    private void LayOut()
    {
        if (_canvas.Tag is not Rgb green || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _canvas.Children.Clear();
        var solid = GanderStyle.Brush(green);
        foreach (var strip in EdgeGlowLayout.Strips(ActualWidth, ActualHeight))
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(strip.GradientStart.X, strip.GradientStart.Y),
                EndPoint = new Point(strip.GradientEnd.X, strip.GradientEnd.Y),
            };
            foreach (var (offset, opacity) in GanderDesign.Glow.Stops())
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(opacity * 255), green.R, green.G, green.B), offset));
            }

            Add(strip.Glow, gradient);
            Add(strip.Border, solid);
        }
    }

    private void Add(MarkRect rect, Brush fill)
    {
        var shape = new System.Windows.Shapes.Rectangle { Width = rect.Width, Height = rect.Height, Fill = fill };
        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        _canvas.Children.Add(shape);
    }
}

/// <summary>A display frame in physical pixels.</summary>
public readonly record struct DisplayFrame(int X, int Y, int Width, int Height);

/// <summary>
/// The Focus Lock notice (Mac NoticePillView, SPEC F28): a rounded dark pill with a lock and "Focus Lock on" or
/// "Focus Lock off", near the top of the display that holds focus. Click-through and non-activating like the glow.
/// </summary>
public sealed class NoticeWindow : Window
{
    private readonly Border _pill = new();
    private readonly TextBlock _icon = new();
    private readonly TextBlock _text = new();
    private int _generation;

    /// <summary>Creates the (hidden) notice window.</summary>
    public NoticeWindow()
    {
        OverlayNative.Configure(this, "Gander notice");
        Opacity = 0;

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_icon);
        row.Children.Add(_text);
        _pill.Child = row;
        Content = _pill;
        SizeChanged += (_, _) => _pill.CornerRadius = new CornerRadius(ActualHeight / 2);
    }

    /// <summary>Shows the notice at the top center of a display frame in physical pixels; <paramref name="scale"/> is that display's DPI scale.</summary>
    public void Present(DisplayFrame frame, double scale, bool isOn, bool animated)
    {
        var generation = ++_generation;
        // The HUD is dark in either appearance, so its text is light (Mac: vibrantDark).
        var surface = Rgba.Of(GanderDesign.Charcoal, 0.92);
        _pill.Background = GanderStyle.Brush(surface);
        _pill.Padding = new Thickness(GanderDesign.Notice.PaddingX, GanderDesign.Notice.PaddingY, GanderDesign.Notice.PaddingX, GanderDesign.Notice.PaddingY);
        var ink = GanderStyle.Brush(GanderDesign.Ivory);
        _icon.Text = isOn ? "" : "";
        _icon.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        _icon.FontSize = GanderDesign.Type.Notice.Size;
        _icon.Foreground = ink;
        _icon.Margin = new Thickness(0, 0, 7, 0);
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _text.Text = SettingsWording.NoticeText(isOn);
        _text.FontFamily = DesignFonts.Wpf(GanderDesign.Type.Notice);
        _text.FontSize = GanderDesign.Type.Notice.Size;
        _text.FontWeight = DesignFonts.WpfWeight(GanderDesign.Type.Notice);
        _text.Foreground = ink;
        _text.VerticalAlignment = VerticalAlignment.Center;

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _pill.DesiredSize;
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);
        if (!IsVisible)
        {
            Show();
        }

        var x = frame.X + (frame.Width - width) / 2;
        var y = frame.Y + (int)Math.Round(GanderDesign.Notice.TopOffset * scale);
        OverlayNative.Place(this, x, y, width, height);
        FeedbackFade.Play(this, GanderDesign.Motion.Notice, animated, () => generation == _generation);
    }
}

/// <summary>
/// Shows the edge glow on the display that just received focus, and the Focus Lock notice when the shortcut toggles it.
/// One reusable window per display for the glow, one for the notice; all hidden between uses. Honors Windows' animation
/// setting the way the Mac honors Reduce Motion.
/// </summary>
public sealed class FeedbackHost : IDisposable
{
    private readonly TrackingController _controller;
    private readonly AppPreferences _preferences;
    private readonly Dictionary<DisplayId, EdgeGlowWindow> _glows = [];
    private NoticeWindow? _notice;

    /// <summary>Starts following the controller.</summary>
    public FeedbackHost(TrackingController controller, AppPreferences preferences)
    {
        _controller = controller;
        _preferences = preferences;
        _controller.FocusSwitched += OnFocusSwitched;
        _controller.FocusLockToggledByShortcut += OnFocusLockToggled;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _controller.FocusSwitched -= OnFocusSwitched;
        _controller.FocusLockToggledByShortcut -= OnFocusLockToggled;
        foreach (var window in _glows.Values)
        {
            window.Close();
        }

        _glows.Clear();
        _notice?.Close();
    }

    private void OnFocusSwitched(DisplayId id)
    {
        if (!_preferences.EdgeGlowEnabled)
        {
            return;
        }

        var display = FindDisplay(id);
        if (display is null)
        {
            return;
        }

        if (!_glows.TryGetValue(id, out var window))
        {
            window = new EdgeGlowWindow();
            _glows[id] = window;
        }

        window.Present(FrameOf(display), AppTheme.Current.Green, FeedbackFade.Animated);
    }

    private void OnFocusLockToggled(bool locked)
    {
        if (!_preferences.FocusLockNoticeEnabled)
        {
            return;
        }

        var display = (_controller.TrackingFocus is { } focus ? FindDisplay(focus) : null)
            ?? _controller.Layout.Displays.FirstOrDefault(d => d.IsMain)
            ?? _controller.Layout.Displays.FirstOrDefault();
        if (display is null)
        {
            return;
        }

        var frame = FrameOf(display);
        var scale = PromptOverlayMath.ScaleFromDpi(NativeShell.ScalePercentAt(frame.X + frame.Width / 2, frame.Y + frame.Height / 2));
        _notice ??= new NoticeWindow();
        _notice.Present(frame, scale, locked, FeedbackFade.Animated);
    }

    private DisplayInfo? FindDisplay(DisplayId id) =>
        _controller.Layout.Displays.FirstOrDefault(d => d.Id == id);

    private static DisplayFrame FrameOf(DisplayInfo display) => new(
        (int)Math.Round(display.Frame.X),
        (int)Math.Round(display.Frame.Y),
        (int)Math.Round(display.Frame.Width),
        (int)Math.Round(display.Frame.Height));
}
